using System.Collections.Concurrent;
using System.IO.Pipes;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

public sealed class NamedPipeAddonFrontendServer : IAsyncDisposable
{
    private readonly string _pipeName; private readonly IAddonFrontendControl _inner; private readonly Func<NamedPipeServerStream> _pipeFactory; private Func<Task>? _afterResponse; private readonly CancellationTokenSource _lifetime = new(); private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously); private NamedPipeServerStream? _activePipe; private Task? _acceptLoop; private int _started; private int _disposed; private volatile ServedConnection? _servedConnection;

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
    public void SetAfterResponse(Func<Task> afterResponse) => _afterResponse = afterResponse ?? throw new ArgumentNullException(nameof(afterResponse));
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
    { using var connection = CancellationTokenSource.CreateLinkedTokenSource(token); using var gate = new SemaphoreSlim(1, 1); var requests = new ConcurrentDictionary<long, CancellationTokenSource>(); var activeRequests = new ConcurrentDictionary<long, Task>(); var operationGate = new SemaphoreSlim(1, 1); var notificationGate = new object(); Task? notificationTask = null; var notificationDirty = false; var notificationSending = false; var probeSessionMayBeOpen = false; var rumbleLoopMayBeRunning = false; var connectionClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    else if (message.Method.Value == FrontendRpcMethod.StartXbox360RumbleLoopDiagnostic)
                        rumbleLoopMayBeRunning = FrontendWireCodec.Decode<FrontendXbox360RumbleLoopSnapshot>(payload).State == FrontendXbox360RumbleLoopState.Running;
                    else if (message.Method.Value == FrontendRpcMethod.StopXbox360RumbleLoopDiagnostic)
                        rumbleLoopMayBeRunning = false;
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, message.Method, Payload: payload)).ConfigureAwait(false);
                    if (_afterResponse is not null)
                        await _afterResponse().ConfigureAwait(false);
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
        ServedConnection? served = null;
        try { var hello = await FrontendWireCodec.ReadAsync(pipe, connection.Token).ConfigureAwait(false); if (hello.Kind != FrontendWireMessageKind.Handshake || hello.ProtocolVersion != FrontendTransportProtocol.CurrentVersion) { await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.ProtocolError, Error: new(FrontendRemoteErrorCode.ProtocolMismatch, "Protocol version mismatch."))).ConfigureAwait(false); return; } await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted)).ConfigureAwait(false); _inner.StateInvalidated += Invalidated;
            served = new ServedConnection
            {
                SendCloseRequestedAsync = () => Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Notification, Notification: FrontendNotificationKind.CloseRequested)),
                Completion = connectionClosed.Task
            };
            _servedConnection = served;
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
                if (message.Payload is not null && message.Method.Value is FrontendRpcMethod.GetBootstrap or FrontendRpcMethod.CaptureStatus or FrontendRpcMethod.CaptureAppUpdate or FrontendRpcMethod.CheckAndDownloadAppUpdate or FrontendRpcMethod.InstallAppUpdate or FrontendRpcMethod.CaptureGamingHome or FrontendRpcMethod.CaptureClawHud or FrontendRpcMethod.CaptureShortcutEditor or FrontendRpcMethod.SuppressDeveloperMenuWarning or FrontendRpcMethod.CaptureTdp or FrontendRpcMethod.RunPrerequisiteSetup or FrontendRpcMethod.GenerateEnvironmentReport or FrontendRpcMethod.OpenClawSensorProbe or FrontendRpcMethod.CaptureClawSensorProbe or FrontendRpcMethod.NextClawSensorProbePhase or FrontendRpcMethod.PreviousClawSensorProbePhase or FrontendRpcMethod.StopClawSensorProbe or FrontendRpcMethod.CloseClawSensorProbe or FrontendRpcMethod.OpenFanProbe or FrontendRpcMethod.ScanProfileGames or FrontendRpcMethod.ScanXboxGames or FrontendRpcMethod.CaptureActiveGameProfile or FrontendRpcMethod.CaptureCenterMStartup or FrontendRpcMethod.CaptureDeviceQuickSettings or FrontendRpcMethod.CaptureAddonQuickSettingsShell or FrontendRpcMethod.CaptureAddonQuickSettingsTabOrder or FrontendRpcMethod.CaptureBatteryChargeLimitTest or FrontendRpcMethod.CaptureBatteryChargeLimit or FrontendRpcMethod.CaptureControllerVibrationStrength or FrontendRpcMethod.CaptureXbox360RumbleLoopDiagnostic or FrontendRpcMethod.StartXbox360RumbleLoopDiagnostic or FrontendRpcMethod.StopXbox360RumbleLoopDiagnostic or FrontendRpcMethod.RunPid1902InputCadenceDiagnostic or FrontendRpcMethod.CaptureGameInputSystemButtonProbe or FrontendRpcMethod.StartGameInputSystemButtonProbe or FrontendRpcMethod.StopGameInputSystemButtonProbe)
                {
                    requests.TryRemove(id, out var invalidPayloadCts); invalidPayloadCts?.Dispose();
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.InvalidMessage, "Unexpected payload."))).ConfigureAwait(false);
                    continue;
                }
                if (!IsControllerVibrationPayloadValid(message.Method.Value, message.Payload))
                {
                    requests.TryRemove(id, out var invalidVibrationPayloadCts); invalidVibrationPayloadCts?.Dispose();
                    await Send(new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, id, Error: new(FrontendRemoteErrorCode.InvalidMessage, "Invalid controller vibration payload."))).ConfigureAwait(false);
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
            if (rumbleLoopMayBeRunning)
                try { await _inner.StopXbox360RumbleLoopDiagnosticAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            if (probeSessionMayBeOpen)
                try { await _inner.CloseClawSensorProbeAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            operationGate.Dispose(); }
    }
    // SF-V2-04/PR3: dispatch typed Quick Settings RPCs onto the existing IAddonFrontendControl
    // seam only. Product validation (page/row/value shape, TDP group, enum values) stays in the
    // SF-V2-03 QuickSettingsMutationAdapter behind MutateQuickSettingAsync -- no feature switch here.
    private async Task<System.Text.Json.JsonElement> InvokeQuickSettingsCaptureAsync(System.Text.Json.JsonElement? p, CancellationToken t)
    {
        var request = FrontendWireCodec.Decode<CaptureQuickSettingsPageRequest>(p);
        return FrontendWireCodec.Payload(await _inner.CaptureQuickSettingsPageAsync(request.PageId, request.ProfileTarget, t).ConfigureAwait(false));
    }

    private static bool IsControllerVibrationPayloadValid(FrontendRpcMethod method, System.Text.Json.JsonElement? payload)
    {
        try
        {
            return method switch
            {
                FrontendRpcMethod.SetControllerVibrationStrength =>
                    FrontendWireCodec.Decode<SetControllerVibrationStrengthRequest>(payload) is { } pair
                    && pair.LeftPercent is >= 0 and <= 100
                    && pair.RightPercent is >= 0 and <= 100,
                FrontendRpcMethod.TestControllerVibrationMotor =>
                    Enum.IsDefined(FrontendWireCodec.Decode<TestControllerVibrationMotorRequest>(payload).Motor),
                FrontendRpcMethod.RunControllerVibrationProfileWriteProbe =>
                    Enum.IsDefined(FrontendWireCodec.Decode<RunControllerVibrationProfileWriteProbeRequest>(payload).Mode),
                _ => true,
            };
        }
        catch (FrontendProtocolException)
        {
            return false;
        }
    }

    private async Task<System.Text.Json.JsonElement> InvokeAsync(FrontendRpcMethod m, System.Text.Json.JsonElement? p, CancellationToken t) => m == FrontendRpcMethod.CaptureAppUpdate
        ? FrontendWireCodec.Payload(await _inner.CaptureAppUpdateAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CheckAndDownloadAppUpdate
        ? FrontendWireCodec.Payload(await _inner.CheckAndDownloadAppUpdateAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.InstallAppUpdate
        ? FrontendWireCodec.Payload(await _inner.InstallAppUpdateAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureGamingHome
        ? FrontendWireCodec.Payload(await _inner.CaptureGamingHomeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureClawHud
        ? FrontendWireCodec.Payload(await _inner.CaptureClawHudAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetClawHudEnabled
        ? FrontendWireCodec.Payload(await _inner.SetClawHudEnabledAsync(FrontendWireCodec.Decode<SetClawHudEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.MutateClawHudSetting
        ? FrontendWireCodec.Payload(await _inner.MutateClawHudSettingAsync(FrontendWireCodec.Decode<FrontendClawHudMutationIntent>(p), t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureShortcutEditor
        ? FrontendWireCodec.Payload(await _inner.CaptureShortcutEditorAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.MutateShortcut
        ? FrontendWireCodec.Payload(await _inner.MutateShortcutAsync(FrontendWireCodec.Decode<FrontendShortcutMutationIntent>(p), t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGamingHomeSelection
        ? FrontendWireCodec.Payload(await _inner.SetGamingHomeSelectionAsync(FrontendWireCodec.Decode<SetGamingHomeSelectionRequest>(p).Selection, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGamingHomeStartup
        ? FrontendWireCodec.Payload(await _inner.SetGamingHomeStartupAsync(FrontendWireCodec.Decode<SetGamingHomeStartupRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetQuickSettingsCurrentPowerSourceOnly
        ? FrontendWireCodec.Payload(await _inner.SetQuickSettingsCurrentPowerSourceOnlyAsync(FrontendWireCodec.Decode<SetQuickSettingsCurrentPowerSourceOnlyRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureAddonQuickSettingsShell
        ? FrontendWireCodec.Payload(await _inner.CaptureAddonQuickSettingsShellAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureAddonQuickSettingsTabOrder
        ? FrontendWireCodec.Payload(await _inner.CaptureAddonQuickSettingsTabOrderAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.MoveAddonQuickSettingsTab
        ? FrontendWireCodec.Payload(await _inner.MoveAddonQuickSettingsTabAsync(FrontendWireCodec.Decode<AddonQuickSettingsTabOrderMoveIntent>(p), t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureQuickSettingsPage
        ? await InvokeQuickSettingsCaptureAsync(p, t).ConfigureAwait(false)
        : m == FrontendRpcMethod.MutateQuickSetting
        ? FrontendWireCodec.Payload(await _inner.MutateQuickSettingAsync(FrontendWireCodec.Decode<QuickSettingsMutationIntent>(p), t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileFavorite
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileFavoriteAsync(FrontendWireCodec.Decode<SetGameProfileFavoriteRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileFavoriteRequest>(p).Favorite, FrontendWireCodec.Decode<SetGameProfileFavoriteRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileResolution
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileResolutionAsync(FrontendWireCodec.Decode<SetGameProfileResolutionRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileResolutionRequest>(p).Resolution, FrontendWireCodec.Decode<SetGameProfileResolutionRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileCpuBoostEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileCpuBoostEnabledAsync(FrontendWireCodec.Decode<SetGameProfileCpuBoostEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileCpuBoostEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileTdpEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileTdpEnabledAsync(FrontendWireCodec.Decode<SetGameProfileTdpEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileTdpEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetFrontButtonMapping
        ? FrontendWireCodec.Payload(await _inner.SetFrontButtonMappingAsync(FrontendWireCodec.Decode<SetFrontButtonMappingRequest>(p).Mapping, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetBackButtonMapping
        ? FrontendWireCodec.Payload(await _inner.SetBackButtonMappingAsync(FrontendWireCodec.Decode<SetBackButtonMappingRequest>(p).Mapping, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetControllerLedSettings
        ? FrontendWireCodec.Payload(await _inner.SetControllerLedSettingsAsync(FrontendWireCodec.Decode<SetControllerLedSettingsRequest>(p).Settings, t).ConfigureAwait(false))
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
        : m == FrontendRpcMethod.SetGameProfileGpuMinimumClockEnabled
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileGpuMinimumClockEnabledAsync(FrontendWireCodec.Decode<SetGameProfileGpuMinimumClockEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileGpuMinimumClockEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileGpuMinimumClockAc
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileGpuMinimumClockAcAsync(FrontendWireCodec.Decode<SetGameProfileGpuMinimumClockIndexRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileGpuMinimumClockIndexRequest>(p).SelectableClockIndex, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetGameProfileGpuMinimumClockDc
        ? FrontendWireCodec.Payload(await _inner.SetGameProfileGpuMinimumClockDcAsync(FrontendWireCodec.Decode<SetGameProfileGpuMinimumClockIndexRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileGpuMinimumClockIndexRequest>(p).SelectableClockIndex, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureCenterMStartup
        ? FrontendWireCodec.Payload(await _inner.CaptureCenterMStartupAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.RequestCenterMAuthorityTransition
        ? FrontendWireCodec.Payload(await _inner.RequestCenterMAuthorityTransitionAsync(FrontendWireCodec.Decode<RequestCenterMAuthorityTransitionRequest>(p).CenterMEnabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureDeviceQuickSettings
        ? FrontendWireCodec.Payload(await _inner.CaptureDeviceQuickSettingsAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.OpenFanProbe
        ? FrontendWireCodec.Payload(await _inner.OpenFanProbeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.RunFanProbe
        ? FrontendWireCodec.Payload(await _inner.RunFanProbeAsync(FrontendWireCodec.Decode<RunFanProbeRequest>(p).Operation, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureBatteryChargeLimitTest
        ? FrontendWireCodec.Payload(await _inner.CaptureBatteryChargeLimitTestAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureBatteryChargeLimit
        ? FrontendWireCodec.Payload(await _inner.CaptureBatteryChargeLimitAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureControllerVibrationStrength
        ? FrontendWireCodec.Payload(await _inner.CaptureControllerVibrationStrengthAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetControllerVibrationStrength
        ? FrontendWireCodec.Payload(await _inner.SetControllerVibrationStrengthAsync(
            FrontendWireCodec.Decode<SetControllerVibrationStrengthRequest>(p).LeftPercent,
            FrontendWireCodec.Decode<SetControllerVibrationStrengthRequest>(p).RightPercent,
            t).ConfigureAwait(false))
        : m == FrontendRpcMethod.TestControllerVibrationMotor
        ? FrontendWireCodec.Payload(await _inner.TestControllerVibrationMotorAsync(
            FrontendWireCodec.Decode<TestControllerVibrationMotorRequest>(p).Motor,
            t).ConfigureAwait(false))
        : m == FrontendRpcMethod.RunControllerVibrationProfileWriteProbe
        ? FrontendWireCodec.Payload(await _inner.RunControllerVibrationProfileWriteProbeAsync(
            FrontendWireCodec.Decode<RunControllerVibrationProfileWriteProbeRequest>(p).Mode,
            t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetDeviceBatteryChargeLimitEnabled
        ? FrontendWireCodec.Payload(await _inner.SetDeviceBatteryChargeLimitEnabledAsync(FrontendWireCodec.Decode<SetDeviceBatteryChargeLimitEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetDeviceBatteryChargeLimitPercent
        ? FrontendWireCodec.Payload(await _inner.SetDeviceBatteryChargeLimitPercentAsync(FrontendWireCodec.Decode<SetDeviceBatteryChargeLimitPercentRequest>(p).Percent, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetBatteryChargeLimitTestEnabled
        ? FrontendWireCodec.Payload(await _inner.SetBatteryChargeLimitTestEnabledAsync(FrontendWireCodec.Decode<SetBatteryChargeLimitTestEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetBatteryChargeLimitTestPercent
        ? FrontendWireCodec.Payload(await _inner.SetBatteryChargeLimitTestPercentAsync(FrontendWireCodec.Decode<SetBatteryChargeLimitTestPercentRequest>(p).Percent, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureXbox360RumbleLoopDiagnostic
        ? FrontendWireCodec.Payload(await _inner.CaptureXbox360RumbleLoopDiagnosticAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.StartXbox360RumbleLoopDiagnostic
        ? FrontendWireCodec.Payload(await _inner.StartXbox360RumbleLoopDiagnosticAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.StopXbox360RumbleLoopDiagnostic
        ? FrontendWireCodec.Payload(await _inner.StopXbox360RumbleLoopDiagnosticAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.RunPid1902InputCadenceDiagnostic
        ? FrontendWireCodec.Payload(await _inner.RunPid1902InputCadenceDiagnosticAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureGameInputSystemButtonProbe
        ? FrontendWireCodec.Payload(await _inner.CaptureGameInputSystemButtonProbeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.StartGameInputSystemButtonProbe
        ? FrontendWireCodec.Payload(await _inner.StartGameInputSystemButtonProbeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.StopGameInputSystemButtonProbe
        ? FrontendWireCodec.Payload(await _inner.StopGameInputSystemButtonProbeAsync(t).ConfigureAwait(false))
        : m == FrontendRpcMethod.CaptureXboxGameProfile
        ? FrontendWireCodec.Payload(await _inner.CaptureXboxGameProfileAsync(FrontendWireCodec.Decode<CaptureXboxGameProfileRequest>(p).Key, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileFavorite
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileFavoriteAsync(FrontendWireCodec.Decode<SetXboxGameProfileFavoriteRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileFavoriteRequest>(p).Favorite, FrontendWireCodec.Decode<SetXboxGameProfileFavoriteRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileEnabled
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileEnabledAsync(FrontendWireCodec.Decode<SetXboxGameProfileEnabledRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileEnabledRequest>(p).Enabled, FrontendWireCodec.Decode<SetXboxGameProfileEnabledRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileCpuBoostEnabled
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileCpuBoostEnabledAsync(FrontendWireCodec.Decode<SetXboxGameProfileCpuBoostEnabledRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileCpuBoostEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileCpuBoostAc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileCpuBoostAcAsync(FrontendWireCodec.Decode<SetXboxGameProfileCpuBoostAcRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileCpuBoostAcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileCpuBoostDc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileCpuBoostDcAsync(FrontendWireCodec.Decode<SetXboxGameProfileCpuBoostDcRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileCpuBoostDcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileTdpEnabled
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileTdpEnabledAsync(FrontendWireCodec.Decode<SetXboxGameProfileTdpEnabledRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileTdpEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileTdp
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileTdpAsync(FrontendWireCodec.Decode<SetXboxGameProfileTdpRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileTdpRequest>(p).Configuration, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfilePowerModeEnabled
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfilePowerModeEnabledAsync(FrontendWireCodec.Decode<SetXboxGameProfilePowerModeEnabledRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfilePowerModeEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfilePowerModeAc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfilePowerModeAcAsync(FrontendWireCodec.Decode<SetXboxGameProfilePowerModeAcRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfilePowerModeAcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfilePowerModeDc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfilePowerModeDcAsync(FrontendWireCodec.Decode<SetXboxGameProfilePowerModeDcRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfilePowerModeDcRequest>(p).Mode, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileFpsLimitEnabled
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileFpsLimitEnabledAsync(FrontendWireCodec.Decode<SetXboxGameProfileFpsLimitEnabledRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileFpsLimitEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileFpsLimitAc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileFpsLimitAcAsync(FrontendWireCodec.Decode<SetXboxGameProfileFpsLimitAcRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileFpsLimitAcRequest>(p).Fps, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileFpsLimitDc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileFpsLimitDcAsync(FrontendWireCodec.Decode<SetXboxGameProfileFpsLimitDcRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileFpsLimitDcRequest>(p).Fps, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileGpuMinimumClockEnabled
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileGpuMinimumClockEnabledAsync(FrontendWireCodec.Decode<SetXboxGameProfileGpuMinimumClockEnabledRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileGpuMinimumClockEnabledRequest>(p).Enabled, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileGpuMinimumClockAc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileGpuMinimumClockAcAsync(FrontendWireCodec.Decode<SetXboxGameProfileGpuMinimumClockIndexRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileGpuMinimumClockIndexRequest>(p).SelectableClockIndex, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileGpuMinimumClockDc
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileGpuMinimumClockDcAsync(FrontendWireCodec.Decode<SetXboxGameProfileGpuMinimumClockIndexRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileGpuMinimumClockIndexRequest>(p).SelectableClockIndex, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileResolution
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileResolutionAsync(FrontendWireCodec.Decode<SetXboxGameProfileResolutionRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileResolutionRequest>(p).Resolution, FrontendWireCodec.Decode<SetXboxGameProfileResolutionRequest>(p).DisplayName, t).ConfigureAwait(false))
        : m == FrontendRpcMethod.SetXboxGameProfileBackButtonMapping
        ? FrontendWireCodec.Payload(await _inner.SetXboxGameProfileBackButtonMappingAsync(FrontendWireCodec.Decode<SetXboxGameProfileBackButtonMappingRequest>(p).Key, FrontendWireCodec.Decode<SetXboxGameProfileBackButtonMappingRequest>(p).Mapping, t).ConfigureAwait(false))
        : m switch
    { FrontendRpcMethod.GetBootstrap => FrontendWireCodec.Payload(await _inner.GetBootstrapAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CaptureStatus => FrontendWireCodec.Payload(await _inner.CaptureStatusAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetLogLevel => FrontendWireCodec.Payload(await _inner.SetLogLevelAsync(FrontendWireCodec.Decode<SetLogLevelRequest>(p).Level, t).ConfigureAwait(false)), FrontendRpcMethod.SetFrontButtonMapping => FrontendWireCodec.Payload(await _inner.SetFrontButtonMappingAsync(FrontendWireCodec.Decode<SetFrontButtonMappingRequest>(p).Mapping, t).ConfigureAwait(false)), FrontendRpcMethod.SuppressDeveloperMenuWarning => FrontendWireCodec.Payload(await _inner.SuppressDeveloperMenuWarningAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CaptureCpuBoost => FrontendWireCodec.Payload(await _inner.CaptureCpuBoostAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceCpuBoostAc => FrontendWireCodec.Payload(await _inner.SetDeviceCpuBoostAcAsync(FrontendWireCodec.Decode<SetDeviceCpuBoostAcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceCpuBoostDc => FrontendWireCodec.Payload(await _inner.SetDeviceCpuBoostDcAsync(FrontendWireCodec.Decode<SetDeviceCpuBoostDcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceCpuBoostEnabled => FrontendWireCodec.Payload(await _inner.SetDeviceCpuBoostEnabledAsync(FrontendWireCodec.Decode<SetDeviceCpuBoostEnabledRequest>(p).Enabled, t).ConfigureAwait(false)), FrontendRpcMethod.CaptureTdp => FrontendWireCodec.Payload(await _inner.CaptureTdpAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceTdp => FrontendWireCodec.Payload(await _inner.SetDeviceTdpAsync(FrontendWireCodec.Decode<SetDeviceTdpRequest>(p).Configuration, t).ConfigureAwait(false)), FrontendRpcMethod.SetDeviceTdpEnabled => FrontendWireCodec.Payload(await _inner.SetDeviceTdpEnabledAsync(FrontendWireCodec.Decode<SetDeviceTdpEnabledRequest>(p).Enabled, t).ConfigureAwait(false)), FrontendRpcMethod.ScanProfileGames => FrontendWireCodec.Payload(await _inner.ScanProfileGamesAsync(t).ConfigureAwait(false)), FrontendRpcMethod.ScanXboxGames => FrontendWireCodec.Payload(await _inner.ScanXboxGamesAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CaptureGameProfile => FrontendWireCodec.Payload(await _inner.CaptureGameProfileAsync(FrontendWireCodec.Decode<CaptureGameProfileRequest>(p).AppId, t).ConfigureAwait(false)), FrontendRpcMethod.CaptureActiveGameProfile => FrontendWireCodec.Payload(await _inner.CaptureActiveGameProfileAsync(t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileEnabled => FrontendWireCodec.Payload(await _inner.SetGameProfileEnabledAsync(FrontendWireCodec.Decode<SetGameProfileEnabledRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileEnabledRequest>(p).Enabled, FrontendWireCodec.Decode<SetGameProfileEnabledRequest>(p).DisplayName, t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileCpuBoostAc => FrontendWireCodec.Payload(await _inner.SetGameProfileCpuBoostAcAsync(FrontendWireCodec.Decode<SetGameProfileCpuBoostAcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileCpuBoostAcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileCpuBoostDc => FrontendWireCodec.Payload(await _inner.SetGameProfileCpuBoostDcAsync(FrontendWireCodec.Decode<SetGameProfileCpuBoostDcRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileCpuBoostDcRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.SetGameProfileTdp => FrontendWireCodec.Payload(await _inner.SetGameProfileTdpAsync(FrontendWireCodec.Decode<SetGameProfileTdpRequest>(p).AppId, FrontendWireCodec.Decode<SetGameProfileTdpRequest>(p).Configuration, t).ConfigureAwait(false)), FrontendRpcMethod.RunPrerequisiteSetup => FrontendWireCodec.Payload(await _inner.RunPrerequisiteSetupAsync(t).ConfigureAwait(false)), FrontendRpcMethod.GenerateEnvironmentReport => FrontendWireCodec.Payload(await _inner.GenerateEnvironmentReportAsync(t).ConfigureAwait(false)), FrontendRpcMethod.OpenClawSensorProbe => FrontendWireCodec.Payload(await _inner.OpenClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.StartClawSensorProbe => FrontendWireCodec.Payload(await _inner.StartClawSensorProbeAsync(FrontendWireCodec.Decode<StartClawSensorProbeRequest>(p).Mode, t).ConfigureAwait(false)), FrontendRpcMethod.CaptureClawSensorProbe => FrontendWireCodec.Payload(await _inner.CaptureClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.NextClawSensorProbePhase => FrontendWireCodec.Payload(await _inner.NextClawSensorProbePhaseAsync(t).ConfigureAwait(false)), FrontendRpcMethod.PreviousClawSensorProbePhase => FrontendWireCodec.Payload(await _inner.PreviousClawSensorProbePhaseAsync(t).ConfigureAwait(false)), FrontendRpcMethod.StopClawSensorProbe => FrontendWireCodec.Payload(await _inner.StopClawSensorProbeAsync(t).ConfigureAwait(false)), FrontendRpcMethod.CloseClawSensorProbe => FrontendWireCodec.Payload(await _inner.CloseClawSensorProbeAsync(t).ConfigureAwait(false)), _ => throw new FrontendProtocolException("Unsupported method.") };
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _lifetime.Cancel(); _ready.TrySetCanceled(_lifetime.Token); Interlocked.Exchange(ref _activePipe, null)?.Dispose(); if (_acceptLoop is not null) try { await _acceptLoop.ConfigureAwait(false); } catch { } _lifetime.Dispose(); }
}
