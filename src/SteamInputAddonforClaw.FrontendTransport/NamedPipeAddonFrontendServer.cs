using System.Collections.Concurrent;
using System.IO.Pipes;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

public sealed class NamedPipeAddonFrontendServer : IAsyncDisposable
{
    private readonly string _pipeName; private readonly IAddonFrontendControl _inner; private readonly Func<NamedPipeServerStream> _pipeFactory; private readonly CancellationTokenSource _lifetime = new(); private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously); private NamedPipeServerStream? _activePipe; private Task? _acceptLoop; private int _started; private int _disposed; private volatile ServedConnection? _servedConnection;

    // OQ3-A: a handle to the currently served frontend connection so the Runtime can ask the Main UI
    // to run its normal close path and then positively observe THIS connection disconnecting. The
    // server already permits only one connected frontend -- this is not a multi-client model.
    private sealed class ServedConnection
    {
        internal required Func<Task> SendCloseRequestedAsync { get; init; }
        internal required Task Completion { get; init; }
    }
    public NamedPipeAddonFrontendServer(string pipeName, IAddonFrontendControl inner) : this(pipeName, inner, () => new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)) { }
    internal NamedPipeAddonFrontendServer(string pipeName, IAddonFrontendControl inner, Func<NamedPipeServerStream> pipeFactory) { _pipeName = pipeName; _inner = inner; _pipeFactory = pipeFactory; }
    public Task StartAsync() { ObjectDisposedException.ThrowIf(_disposed != 0, this); if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Server already started."); _acceptLoop = AcceptLoopAsync(); return _ready.Task; }

    /// <summary>OQ3-A: ask the connected Main UI to run its normal close path, then wait for THIS
    /// current frontend connection to disconnect. Returns true when no client is connected, or when
    /// the connection is positively observed gone. Returns false when the send fails while the client
    /// is still connected, or the wait times out with the client still connected. One attempt only --
    /// no retries, no forced kill.</summary>
    public async Task<bool> RequestClientCloseAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var served = _servedConnection;
        if (served is null) return true;
        try
        {
            await served.SendCloseRequestedAsync().ConfigureAwait(false);
        }
        catch
        {
            // Send failed: only a connection that is already gone counts as successfully retired.
            return served.Completion.IsCompleted;
        }
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await served.Completion.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return served.Completion.IsCompleted;
        }
    }
    private async Task AcceptLoopAsync()
    { while (!_lifetime.IsCancellationRequested) { try { await using var pipe = _pipeFactory(); Interlocked.Exchange(ref _activePipe, pipe); _ready.TrySetResult(); await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false); await ServeAsync(pipe, _lifetime.Token).ConfigureAwait(false); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { _ready.TrySetCanceled(_lifetime.Token); } catch (Exception exception) { if (_ready.TrySetException(exception)) return; try { await Task.Delay(100, _lifetime.Token).ConfigureAwait(false); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { } } finally { Interlocked.Exchange(ref _activePipe, null)?.Dispose(); } } }
    private async Task ServeAsync(Stream pipe, CancellationToken token)
    { using var connection = CancellationTokenSource.CreateLinkedTokenSource(token); using var gate = new SemaphoreSlim(1, 1); var requests = new ConcurrentDictionary<long, CancellationTokenSource>(); var activeRequests = new ConcurrentDictionary<long, Task>(); var operationGate = new SemaphoreSlim(1, 1); var notificationGate = new object(); Task? notificationTask = null; var notificationDirty = false; var notificationSending = false; var probeSessionMayBeOpen = false; var connectionClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Send(FrontendWireEnvelope e) => await FrontendWireCodec.WriteAsync(pipe, e, gate, connection.Token).ConfigureAwait(false);
        async Task Notify()
        {
            while (true)
            {
                lock (notificationGate)
                {
                    if (!notificationDirty) { notificationSending = false; notificationTask = null; return; }
                    notificationDirty = false;
                }
                try { await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Notification, Notification: FrontendNotificationKind.StateInvalidated)); } catch { return; }
            }
        }
        void Invalidated(object? _, EventArgs __)
        {
            lock (notificationGate) { notificationDirty = true; if (!notificationSending) { notificationSending = true; notificationTask = Notify(); } }
        }
        async Task ExecuteRequestAsync(long id, FrontendWireEnvelope message, CancellationTokenSource requestCts, Task startSignal)
        {
            try
            {
                await startSignal.ConfigureAwait(false);
                await operationGate.WaitAsync(requestCts.Token).ConfigureAwait(false);
                try
                {
                    var payload = await InvokeAsync(message.Method!.Value, message.Payload, requestCts.Token).ConfigureAwait(false);
                    if (message.Method.Value == FrontendRpcMethod.OpenClawSensorProbe) probeSessionMayBeOpen = true;
                    else if (message.Method.Value == FrontendRpcMethod.CloseClawSensorProbe) probeSessionMayBeOpen = false;
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, message.Method, Payload: payload)).ConfigureAwait(false);
                }
                finally { operationGate.Release(); }
            }
            catch (OperationCanceledException) when (requestCts.IsCancellationRequested || connection.IsCancellationRequested)
            {
                await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.Cancelled, "Operation cancelled."))).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.OperationFailed, exception.Message))).ConfigureAwait(false);
            }
            catch (FrontendProtocolException exception)
            {
                await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.InvalidMessage, exception.Message))).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.OperationFailed, exception.Message))).ConfigureAwait(false);
            }
            finally
            {
                requests.TryRemove(id, out _);
                activeRequests.TryRemove(id, out _);
                requestCts.Dispose();
            }
        }
        try { var hello = await FrontendWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false); if (hello.Kind != FrontendWireMessageKind.Handshake || hello.ProtocolVersion != FrontendTransportProtocol.CurrentVersion) { await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.ProtocolError, Error: new(FrontendRemoteErrorCode.ProtocolMismatch, "Protocol version mismatch."))).ConfigureAwait(false); return; } await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted)).ConfigureAwait(false); _inner.StateInvalidated += Invalidated;
            _servedConnection = new ServedConnection
            {
                SendCloseRequestedAsync = () => Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Notification, Notification: FrontendNotificationKind.CloseRequested)),
                Completion = connectionClosed.Task
            };
            while (!connection.IsCancellationRequested)
            {
                FrontendWireEnvelope message;
                try { message = await FrontendWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false); }
                catch (FrontendProtocolException exception)
                {
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.ProtocolError, Error: new(FrontendRemoteErrorCode.InvalidMessage, exception.Message))).ConfigureAwait(false);
                    break;
                }
                if (message.ProtocolVersion != FrontendTransportProtocol.CurrentVersion)
                {
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.ProtocolError, Error: new(FrontendRemoteErrorCode.ProtocolMismatch, "Protocol version mismatch."))).ConfigureAwait(false);
                    break;
                }
                if (message.Kind == FrontendWireMessageKind.CancelRequest && message.RequestId is > 0 and var cancelId)
                {
                    if (requests.TryGetValue(cancelId, out var cancellation)) cancellation.Cancel();
                    continue;
                }
                if (message.Kind != FrontendWireMessageKind.Request || message.RequestId is not > 0 || message.Method is null)
                {
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.ProtocolError, Error: new(FrontendRemoteErrorCode.InvalidMessage, "Invalid request."))).ConfigureAwait(false);
                    throw new FrontendProtocolException("Invalid request.");
                }
                var id = message.RequestId.Value;
                var requestCts = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
                if (!requests.TryAdd(id, requestCts))
                {
                    requestCts.Dispose();
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.ProtocolError, Error: new(FrontendRemoteErrorCode.InvalidMessage, "Duplicate request id."))).ConfigureAwait(false);
                    throw new FrontendProtocolException("Duplicate request id.");
                }
                if (message.Method.Value == FrontendRpcMethod.Unknown || !Enum.IsDefined(message.Method.Value))
                {
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.UnsupportedMethod, "Unsupported method."))).ConfigureAwait(false);
                    requests.TryRemove(id, out var unsupportedCts); unsupportedCts?.Dispose();
                    continue;
                }
                if (message.Payload is not null && message.Method.Value is FrontendRpcMethod.GetBootstrap or FrontendRpcMethod.CaptureStatus or FrontendRpcMethod.SuppressDeveloperMenuWarning or FrontendRpcMethod.CaptureTdp or FrontendRpcMethod.RunPrerequisiteSetup or FrontendRpcMethod.GenerateEnvironmentReport or FrontendRpcMethod.OpenClawSensorProbe or FrontendRpcMethod.StartClawSensorProbe or FrontendRpcMethod.CaptureClawSensorProbe or FrontendRpcMethod.NextClawSensorProbePhase or FrontendRpcMethod.PreviousClawSensorProbePhase or FrontendRpcMethod.StopClawSensorProbe or FrontendRpcMethod.CloseClawSensorProbe or FrontendRpcMethod.OpenFanProbe or FrontendRpcMethod.ScanProfileGames or FrontendRpcMethod.CaptureActiveGameProfile or FrontendRpcMethod.CaptureCenterMStartup)
                {
                    requests.TryRemove(id, out var invalidPayloadCts); invalidPayloadCts?.Dispose();
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.InvalidMessage, "Unexpected payload."))).ConfigureAwait(false);
                    continue;
                }
                var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var requestTask = ExecuteRequestAsync(id, message, requestCts, startSignal.Task);
                activeRequests.TryAdd(id, requestTask);
                startSignal.TrySetResult();
            } }
        finally { _servedConnection = null; connectionClosed.TrySetResult(); _inner.StateInvalidated -= Invalidated; connection.Cancel(); foreach (var item in requests.Values) item.Cancel(); try { await Task.WhenAll(activeRequests.Values).ConfigureAwait(false); } catch { } Task? pendingNotification; lock (notificationGate) pendingNotification = notificationTask; if (pendingNotification is not null) try { await pendingNotification.ConfigureAwait(false); } catch { }
            // Frontend disconnect (crash/kill, or the pipe otherwise dropping without an orderly
            // Close call) must still retire a Runtime-owned Claw Sensor Probe session: unlike the
            // old in-process page, this diagnostic keeps actively reading sensors in the headless
            // Runtime after the WinUI process is gone, so a missed Close would leak it indefinitely
            // (PR #290 review). Best-effort only -- this is the concrete single-frontend disconnect
            // boundary, not a general session/connection framework.
            if (probeSessionMayBeOpen)
                try { await _inner.CloseClawSensorProbeAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            operationGate.Dispose(); }
    }
    private async Task<System.Text.Json.JsonElement> InvokeAsync(FrontendRpcMethod m, System.Text.Json.JsonElement? p, CancellationToken t) => m == FrontendRpcMethod.SetGameProfileFavorite
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileFavoriteAsync(FrontendWireCodec.Decode<SetGameProfileFavoriteRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileFavoriteRequest>(p).Favorite, FrontendWireCodec.Decode<SetGameProfileFavoriteRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileResolution
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileResolutionAsync(FrontendWireCodec.Decode<SetGameProfileResolutionRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileResolutionRequest>(p).Resolution, FrontendWireCodec.Decode<SetGameProfileResolutionRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileCpuBoostEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileCpuBoostEnabledAsync(FrontendWireCodec.Decode<SetGameProfileCpuBoostEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileCpuBoostEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileTdpEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileTdpEnabledAsync(FrontendWireCodec.Decode<SetGameProfileTdpEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileTdpEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetFrontButtonMapping
        ? FrontendWireCodec.Payload(await _inner.SetFrontButtonMappingAsync(FrontendWireCodec.Decode<SetFrontButtonMappingRequest>(p).Mapping, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CapturePowerMode
        ? FrontendWireCodec.Payload(await _inner.CapturePowerModeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetDevicePowerModeAc
        ? FrontendWireCodec.Payload(await _inner.SetDevicePowerModeAcAsync(FrontendWireCodec.Decode<SetDevicePowerModeAcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetDevicePowerModeDc
        ? FrontendWireCodec.Payload(await _inner.SetDevicePowerModeDcAsync(FrontendWireCodec.Decode<SetDevicePowerModeDcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetDevicePowerModeEnabled
        ? FrontendWireCodec.Payload(await _inner.SetDevicePowerModeEnabledAsync(FrontendWireCodec.Decode<SetDevicePowerModeEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfilePowerModeEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfilePowerModeEnabledAsync(FrontendWireCodec.Decode<SetGameProfilePowerModeEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfilePowerModeEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfilePowerModeAc
        ? FrontendWireCodec.Payload(await _inner.SetGameProfilePowerModeAcAsync(FrontendWireCodec.Decode<SetGameProfilePowerModeAcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfilePowerModeAcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfilePowerModeDc
        ? FrontendWireCodec.Payload(await _inner.SetGameProfilePowerModeDcAsync(FrontendWireCodec.Decode<SetGameProfilePowerModeDcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfilePowerModeDcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileFpsLimitEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileFpsLimitEnabledAsync(FrontendWireCodec.Decode<SetGameProfileFpsLimitEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileFpsLimitEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileFpsLimitAc
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileFpsLimitAcAsync(FrontendWireCodec.Decode<SetGameProfileFpsLimitAcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileFpsLimitAcRequest>(p).Fps, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileFpsLimitDc
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileFpsLimitDcAsync(FrontendWireCodec.Decode<SetGameProfileFpsLimitDcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileFpsLimitDcRequest>(p).Fps, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureCenterMStartup
        ? FrontendWireCodec.Payload(await _inner.CaptureCenterMStartupAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.RequestCenterMAuthorityTransition
        ? FrontendWireCodec.Payload(await _inner.RequestCenterMAuthorityTransitionAsync(FrontendWireCodec.Decode<RequestCenterMAuthorityTransitionRequest>(p).CenterMEnabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.OpenFanProbe
        ? FrontendWireCodec.Payload(await _inner.OpenFanProbeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.RunFanProbe
        ? FrontendWireCodec.Payload(await _inner.RunFanProbeAsync(FrontendWireCodec.Decode<RunFanProbeRequest>(p).Operation, t).ConfigureAwait(false))
        : m switch
    { FrontendRpcMethod.GetBootstrap => FrontendWireCodec.Payload(await _inner.GetBootstrapAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CaptureStatus => FrontendWireCodec.Payload(await _inner.CaptureStatusAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetLogLevel =>FrontendWireCodec.Payload(await _inner.SetLogLevelAsync(FrontendWireCodec.Decode<SetLogLevelRequest>(p).Level, t).ConfigureAwait(false)), FrontendRpcMethod.SetFrontButtonMapping => FrontendWireCodec.Payload(await _inner.SetFrontButtonMappingAsync(FrontendWireCodec.Decode<SetFrontButtonMappingRequest>(p).Mapping, t).ConfigureAwait(false)), FrontendRpcMethod.SuppressDeveloperMenuWarning => FrontendWireCodec.Payload(await _inner.SuppressDeveloperMenuWarningAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetDeveloperTestMode => FrontendWireCodec.Payload(await _inner.SetDeveloperTestModeAsync(FrontendWireCodec.Decode<SetDeveloperTestModeRequest>(p).Enabled, t).ConfigureAwait(false)), FrontendRpcMethod.CaptureCpuBoost => FrontendWireCodec.Payload(await _inner.CaptureCpuBoostAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceCpuBoostAc => FrontendWireCodec.Payload(await _inner.SetDeviceCpuBoostAcAsync(FrontendWireCodec.Decode<SetDeviceCpuBoostAcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceCpuBoostDc => FrontendWireCodec.Payload(await _inner.SetDeviceCpuBoostDcAsync(FrontendWireCodec.Decode<SetDeviceCpuBoostDcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceCpuBoostEnabled => FrontendWireCodec.Payload(await _inner.SetDeviceCpuBoostEnabledAsync(FrontendWireCodec.Decode<SetDeviceCpuBoostEnabledRequest>(p).Enabled, t).ConfigureAwait(false)), FrontendRpcMethod.CaptureTdp => FrontendWireCodec.Payload(await _inner.CaptureTdpAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceTdp => FrontendWireCodec.Payload(await _inner.SetDeviceTdpAsync(FrontendWireCodec.Decode<SetDeviceTdpRequest>(p).Configuration, t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceTdpEnabled => FrontendWireCodec.Payload(await _inner.SetDeviceTdpEnabledAsync(FrontendWireCodec.Decode<SetDeviceTdpEnabledRequest>(p).Enabled, t).ConfigureAwait(false)), FrontendRpcMethod.ScanProfileGames => FrontendWireCodec.Payload(await _inner.ScanProfileGamesAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CaptureGameProfile => FrontendWireCodec.Payload(await _inner.CaptureGameProfileAsync(FrontendWireCodec.Decode<CaptureGameProfileRequest>(p).AppId, t).ConfigureAwait(false)), FrontendRpcMethod.CaptureActiveGameProfile => FrontendWireCodec.Payload(await _inner.CaptureActiveGameProfileAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileEnabled => FrontendWireCodec.Payload(await _inner.SetGameProfileEnabledAsync(FrontendWireCodec.Decode<SetGameProfileEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileEnabledRequest>(p).Enabled, FrontendWireCodec.Decode<SetGameProfileEnabledRequest>(p).DisplayName, t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileCpuBoostAc => FrontendWireCodec.Payload(await _inner.SetGameProfileCpuBoostAcAsync(FrontendWireCodec.Decode<SetGameProfileCpuBoostAcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileCpuBoostAcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileCpuBoostDc => FrontendWireCodec.Payload(await _inner.SetGameProfileCpuBoostDcAsync(FrontendWireCodec.Decode<SetGameProfileCpuBoostDcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileCpuBoostDcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileTdp => FrontendWireCodec.Payload(await _inner.SetGameProfileTdpAsync(FrontendWireCodec.Decode<SetGameProfileTdpRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileTdpRequest>(p).Configuration, t).ConfigureAwait(false)), FrontendRpcMethod.RunPrerequisiteSetup => FrontendWireCodec.Payload(await _inner.RunPrerequisiteSetupAsync(t).ConfigureAwait(false)), FrontendRpcMethod.GenerateEnvironmentReport => FrontendWireCodec.Payload(await _inner.GenerateEnvironmentReportAsync(t).ConfigureAwait(false)), FrontendRpcMethod.OpenClawSensorProbe => FrontendWireCodec.Payload(await _inner.OpenClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.StartClawSensorProbe => FrontendWireCodec.Payload(await _inner.StartClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CaptureClawSensorProbe => FrontendWireCodec.Payload(await _inner.CaptureClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.NextClawSensorProbePhase => FrontendWireCodec.Payload(await _inner.NextClawSensorProbePhaseAsync(t).ConfigureAwait(false)), FrontendRpcMethod.PreviousClawSensorProbePhase => FrontendWireCodec.Payload(await _inner.PreviousClawSensorProbePhaseAsync(t).ConfigureAwait(false)), FrontendRpcMethod.StopClawSensorProbe => FrontendWireCodec.Payload(await _inner.StopClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CloseClawSensorProbe => FrontendWireCodec.Payload(await _inner.CloseClawSensorProbeAsync(t).ConfigureAwait(false)), _ => throw new FrontendProtocolException("Unsupported method.") };
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _lifetime.Cancel(); _ready.TrySetCanceled(_lifetime.Token); Interlocked.Exchange(ref _activePipe, null)?.Dispose(); if (_acceptLoop is not null) try { await _acceptLoop.ConfigureAwait(false); } catch { } _lifetime.Dispose(); }
}
