using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;

namespace SteamInputAddonforClaw.FrontendTransport;

internal sealed class FrontendRequestTerminalState
{
    internal int Value;
    internal bool TryStart() => Interlocked.CompareExchange(ref Value, 1, 0) == 0;
    internal bool TryCancelBeforeStart() => Interlocked.CompareExchange(ref Value, 2, 0) == 0;
    internal bool TryCancelStarted() => Interlocked.CompareExchange(ref Value, 4, 1) == 1;
    internal bool TryCompleteResponse() => Interlocked.CompareExchange(ref Value, 3, 1) == 1;
}

public sealed class NamedPipeAddonFrontendClient : IAddonFrontendControl, IAsyncDisposable
{
    private readonly string _pipeName; private readonly int _version; private readonly ConcurrentDictionary<long, TaskCompletionSource<FrontendWireEnvelope>> _pending = new(); private readonly ConcurrentDictionary<long, FrontendRequestTerminalState> _requestStates = new(); private readonly ConcurrentDictionary<long, byte> _cancelled = new();
    private readonly CancellationTokenSource _lifetime = new(); private readonly SemaphoreSlim _writeGate = new(1, 1); private readonly object _connectionGate = new(); private NamedPipeClientStream? _pipe; private Exception? _disconnectReason; private Task? _readLoop; private long _nextRequestId; private int _disposed; private int _connecting; private int _disconnectedRaised;
    public event EventHandler? StateInvalidated;
    public event EventHandler? Disconnected;
    // OQ3-A: the Runtime asks the connected Main UI to run its normal close path before the Addon
    // Overlay is shown. Narrow notification only -- not a general command bus.
    public event EventHandler? CloseRequested;
    public NamedPipeAddonFrontendClient(string pipeName) : this(pipeName, FrontendTransportProtocol.CurrentVersion) { }
    internal NamedPipeAddonFrontendClient(string pipeName, int version) { _pipeName = pipeName; _version = version; }
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pipe is not null) throw new InvalidOperationException("Client is already connected.");
        if (Interlocked.Exchange(ref _connecting, 1) != 0) throw new InvalidOperationException("Client connection attempt is already in progress.");
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false); await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.Handshake), _writeGate, timeout.Token).ConfigureAwait(false); var reply = await FrontendWireCodec.ReadAsync(pipe, timeout.Token).ConfigureAwait(false); if (reply.Kind == FrontendWireMessageKind.ProtocolError) throw new FrontendProtocolException(reply.Error?.Message ?? "Protocol rejected."); if (reply.Kind != FrontendWireMessageKind.HandshakeAccepted || reply.ProtocolVersion != _version) throw new FrontendProtocolException("Protocol handshake failed."); lock (_connectionGate) { ObjectDisposedException.ThrowIf(_disposed != 0, this); _pipe = pipe; _readLoop = ReadLoopAsync(); } }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
        finally { Volatile.Write(ref _connecting, 0); }
    }
    public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => SendAsync<FrontendBootstrapSnapshot>(FrontendRpcMethod.GetBootstrap, null, t);
    public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => SendAsync<FrontendStatusSnapshot>(FrontendRpcMethod.CaptureStatus, null, t);
    public Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendLaunchAtStartupResult>(FrontendRpcMethod.SetLaunchAtWindowsStartup, FrontendWireCodec.Payload(new SetLaunchAtWindowsStartupRequest(enabled)), t);
    public Task<FrontendSteamInputRoutingMutationResult> SetSteamInputRoutingEnabledAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendSteamInputRoutingMutationResult>(FrontendRpcMethod.SetSteamInputRoutingEnabled, FrontendWireCodec.Payload(new SetSteamInputRoutingEnabledRequest(enabled)), t);
    public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SetLogLevel, FrontendWireCodec.Payload(new SetLogLevelRequest(level)), t);
    public Task<FrontendSettingsSnapshot> SetOem1MappingAsync(SteamInputAddonforClaw.Contracts.Oem1.Oem1MappingSettings mapping, CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SetOem1Mapping, FrontendWireCodec.Payload(new SetOem1MappingRequest(mapping)), t);
    public Task<FrontendSettingsSnapshot> SetWingMappingAsync(SteamInputAddonforClaw.Contracts.Wing.WingMappingSettings mapping, CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SetWingMapping, FrontendWireCodec.Payload(new SetWingMappingRequest(mapping)), t);
    public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => SendAsync<FrontendSettingsSnapshot>(FrontendRpcMethod.SuppressDeveloperMenuWarning, null, t);
    public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendDeveloperSnapshot>(FrontendRpcMethod.SetDeveloperTestMode, FrontendWireCodec.Payload(new SetDeveloperTestModeRequest(enabled)), t);
    public Task<FrontendVibrationTestResult> RunVibrationTestAsync(FrontendVibrationTestCommand command, CancellationToken t = default) => SendAsync<FrontendVibrationTestResult>(FrontendRpcMethod.RunVibrationTest, FrontendWireCodec.Payload(new RunVibrationTestRequest(command)), t);
    public Task<FrontendVibrationTestResult> OpenVibrationTestSessionAsync(CancellationToken t = default) => SendAsync<FrontendVibrationTestResult>(FrontendRpcMethod.OpenVibrationTestSession, null, t);
    public Task<FrontendVibrationTestResult> CloseVibrationTestSessionAsync(CancellationToken t = default) => SendAsync<FrontendVibrationTestResult>(FrontendRpcMethod.CloseVibrationTestSession, null, t);
    public Task<FrontendCpuBoostSnapshot> CaptureCpuBoostAsync(CancellationToken t = default) => SendAsync<FrontendCpuBoostSnapshot>(FrontendRpcMethod.CaptureCpuBoost, null, t);
    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostAcAsync(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode mode, CancellationToken t = default) => SendAsync<FrontendCpuBoostMutationResult>(FrontendRpcMethod.SetDeviceCpuBoostAc, FrontendWireCodec.Payload(new SetDeviceCpuBoostAcRequest(mode)), t);
    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostDcAsync(SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode mode, CancellationToken t = default) => SendAsync<FrontendCpuBoostMutationResult>(FrontendRpcMethod.SetDeviceCpuBoostDc, FrontendWireCodec.Payload(new SetDeviceCpuBoostDcRequest(mode)), t);
    public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendCpuBoostMutationResult>(FrontendRpcMethod.SetDeviceCpuBoostEnabled, FrontendWireCodec.Payload(new SetDeviceCpuBoostEnabledRequest(enabled)), t);
    public Task<FrontendPowerModeSnapshot> CapturePowerModeAsync(CancellationToken t = default) => SendAsync<FrontendPowerModeSnapshot>(FrontendRpcMethod.CapturePowerMode, null, t);
    public Task<FrontendPowerModeMutationResult> SetDevicePowerModeAcAsync(WindowsPowerMode mode, CancellationToken t = default) => SendAsync<FrontendPowerModeMutationResult>(FrontendRpcMethod.SetDevicePowerModeAc, FrontendWireCodec.Payload(new SetDevicePowerModeAcRequest(mode)), t);
    public Task<FrontendPowerModeMutationResult> SetDevicePowerModeDcAsync(WindowsPowerMode mode, CancellationToken t = default) => SendAsync<FrontendPowerModeMutationResult>(FrontendRpcMethod.SetDevicePowerModeDc, FrontendWireCodec.Payload(new SetDevicePowerModeDcRequest(mode)), t);
    public Task<FrontendPowerModeMutationResult> SetDevicePowerModeEnabledAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendPowerModeMutationResult>(FrontendRpcMethod.SetDevicePowerModeEnabled, FrontendWireCodec.Payload(new SetDevicePowerModeEnabledRequest(enabled)), t);
    public Task<FrontendTdpSnapshot> CaptureTdpAsync(CancellationToken t = default) => SendAsync<FrontendTdpSnapshot>(FrontendRpcMethod.CaptureTdp, null, t);
    public Task<FrontendTdpMutationResult> SetDeviceTdpAsync(FrontendTdpConfiguration configuration, CancellationToken t = default) => SendAsync<FrontendTdpMutationResult>(FrontendRpcMethod.SetDeviceTdp, FrontendWireCodec.Payload(new SetDeviceTdpRequest(configuration)), t);
    public Task<FrontendTdpMutationResult> SetDeviceTdpEnabledAsync(bool enabled, CancellationToken t = default) => SendAsync<FrontendTdpMutationResult>(FrontendRpcMethod.SetDeviceTdpEnabled, FrontendWireCodec.Payload(new SetDeviceTdpEnabledRequest(enabled)), t);
    public Task<FrontendCenterMStartupSnapshot> CaptureCenterMStartupAsync(CancellationToken t = default) => SendAsync<FrontendCenterMStartupSnapshot>(FrontendRpcMethod.CaptureCenterMStartup, null, t);
    public Task<FrontendCenterMStartupMutationResult> RequestCenterMAuthorityTransitionAsync(bool centerMEnabled, CancellationToken t = default) => SendAsync<FrontendCenterMStartupMutationResult>(FrontendRpcMethod.RequestCenterMAuthorityTransition, FrontendWireCodec.Payload(new RequestCenterMAuthorityTransitionRequest(centerMEnabled)), t);
    public Task<IReadOnlyList<FrontendProfileGameCatalogEntry>> ScanProfileGamesAsync(CancellationToken t = default) => SendAsync<IReadOnlyList<FrontendProfileGameCatalogEntry>>(FrontendRpcMethod.ScanProfileGames, null, t);
    public Task<FrontendGameProfileSnapshot> CaptureGameProfileAsync(uint appId, CancellationToken t = default) => SendAsync<FrontendGameProfileSnapshot>(FrontendRpcMethod.CaptureGameProfile, FrontendWireCodec.Payload(new CaptureGameProfileRequest(appId)), t);
    public Task<FrontendGameProfileSnapshot> CaptureActiveGameProfileAsync(CancellationToken t = default) => SendAsync<FrontendGameProfileSnapshot>(FrontendRpcMethod.CaptureActiveGameProfile, null, t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileEnabledAsync(uint appId, bool enabled, string? displayName, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileEnabled, FrontendWireCodec.Payload(new SetGameProfileEnabledRequest(appId, enabled, displayName)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostEnabledAsync(uint appId, bool enabled, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileCpuBoostEnabled, FrontendWireCodec.Payload(new SetGameProfileCpuBoostEnabledRequest(appId, enabled)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileTdpEnabledAsync(uint appId, bool enabled, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileTdpEnabled, FrontendWireCodec.Payload(new SetGameProfileTdpEnabledRequest(appId, enabled)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeEnabledAsync(uint appId, bool enabled, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfilePowerModeEnabled, FrontendWireCodec.Payload(new SetGameProfilePowerModeEnabledRequest(appId, enabled)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostAcAsync(uint appId, SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode mode, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileCpuBoostAc, FrontendWireCodec.Payload(new SetGameProfileCpuBoostAcRequest(appId, mode)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostDcAsync(uint appId, SteamInputAddonforClaw.Contracts.DeviceProfiles.CpuBoostMode mode, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileCpuBoostDc, FrontendWireCodec.Payload(new SetGameProfileCpuBoostDcRequest(appId, mode)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeAcAsync(uint appId, WindowsPowerMode mode, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfilePowerModeAc, FrontendWireCodec.Payload(new SetGameProfilePowerModeAcRequest(appId, mode)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeDcAsync(uint appId, WindowsPowerMode mode, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfilePowerModeDc, FrontendWireCodec.Payload(new SetGameProfilePowerModeDcRequest(appId, mode)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitEnabledAsync(uint appId, bool enabled, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileFpsLimitEnabled, FrontendWireCodec.Payload(new SetGameProfileFpsLimitEnabledRequest(appId, enabled)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitAcAsync(uint appId, int fps, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileFpsLimitAc, FrontendWireCodec.Payload(new SetGameProfileFpsLimitAcRequest(appId, fps)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitDcAsync(uint appId, int fps, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileFpsLimitDc, FrontendWireCodec.Payload(new SetGameProfileFpsLimitDcRequest(appId, fps)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileTdpAsync(uint appId, FrontendGameTdpConfiguration configuration, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileTdp, FrontendWireCodec.Payload(new SetGameProfileTdpRequest(appId, configuration)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileFavoriteAsync(uint appId, bool favorite, string? displayName, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileFavorite, FrontendWireCodec.Payload(new SetGameProfileFavoriteRequest(appId, favorite, displayName)), t);
    public Task<FrontendGameProfileMutationResult> SetGameProfileResolutionAsync(uint appId, FrontendGameResolution? resolution, string? displayName, CancellationToken t = default) => SendAsync<FrontendGameProfileMutationResult>(FrontendRpcMethod.SetGameProfileResolution, FrontendWireCodec.Payload(new SetGameProfileResolutionRequest(appId, resolution, displayName)), t);
    public Task<FrontendClawSensorProbeSnapshot> OpenClawSensorProbeAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.OpenClawSensorProbe, null, t);
    public Task<FrontendClawSensorProbeSnapshot> StartClawSensorProbeAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.StartClawSensorProbe, null, t);
    public Task<FrontendClawSensorProbeSnapshot> CaptureClawSensorProbeAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.CaptureClawSensorProbe, null, t);
    public Task<FrontendClawSensorProbeSnapshot> NextClawSensorProbePhaseAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.NextClawSensorProbePhase, null, t);
    public Task<FrontendClawSensorProbeSnapshot> PreviousClawSensorProbePhaseAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.PreviousClawSensorProbePhase, null, t);
    public Task<FrontendClawSensorProbeSnapshot> StopClawSensorProbeAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.StopClawSensorProbe, null, t);
    public Task<FrontendClawSensorProbeSnapshot> CloseClawSensorProbeAsync(CancellationToken t = default) => SendAsync<FrontendClawSensorProbeSnapshot>(FrontendRpcMethod.CloseClawSensorProbe, null, t);
    public Task<FrontendFanProbeSnapshot> OpenFanProbeAsync(CancellationToken t = default) => SendAsync<FrontendFanProbeSnapshot>(FrontendRpcMethod.OpenFanProbe, null, t);
    public Task<FrontendFanProbeSnapshot> RunFanProbeAsync(FrontendFanProbeOperation operation, CancellationToken t = default) => SendAsync<FrontendFanProbeSnapshot>(FrontendRpcMethod.RunFanProbe, FrontendWireCodec.Payload(new RunFanProbeRequest(operation)), t);
    public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => SendAsync<FrontendPrerequisiteSetupResult>(FrontendRpcMethod.RunPrerequisiteSetup, null, t);
    public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => SendAsync<FrontendEnvironmentReportResult>(FrontendRpcMethod.GenerateEnvironmentReport, null, t);
    private async Task<T> SendAsync<T>(FrontendRpcMethod method, JsonElement? payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var pipe = _pipe ?? throw new FrontendTransportException("Client is not connected.", _disconnectReason); var id = Interlocked.Increment(ref _nextRequestId); var tcs = new TaskCompletionSource<FrontendWireEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously); if (!_pending.TryAdd(id, tcs)) throw new FrontendTransportException("Duplicate request id.");
        var requestState = new FrontendRequestTerminalState();
        _requestStates.TryAdd(id, requestState);
        using var registration = token.Register(() =>
        {
            if (requestState.TryCancelBeforeStart())
            {
                _pending.TryRemove(id, out _);
            }
            else if (Volatile.Read(ref requestState.Value) == 1)
            {
                _cancelled.TryAdd(id, 0);
                if (requestState.TryCancelStarted())
                {
                    _pending.TryRemove(id, out _);
                    _ = SendCancelSafelyAsync(id);
                }
                else
                {
                    _cancelled.TryRemove(id, out _);
                }
            }
            if (Volatile.Read(ref requestState.Value) is 2 or 4) tcs.TrySetCanceled(token);
        });
        try
        {
            try { await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.Request, id, method, Payload: payload), _writeGate, token, _lifetime.Token, () => { if (!requestState.TryStart()) throw new OperationCanceledException(token); }).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (_disposed != 0 || _lifetime.IsCancellationRequested || exception is IOException || exception is ObjectDisposedException)
            { throw new FrontendTransportException(_disposed != 0 ? "Client disposed." : "Pipe connection closed.", exception); }
            var response = await tcs.Task.ConfigureAwait(false); if (response.Error is { } error) { if (error.Code == FrontendRemoteErrorCode.Cancelled) throw new OperationCanceledException(token); throw new FrontendRemoteException(error.Code, error.Message); } try { return FrontendWireCodec.Decode<T>(response.Payload); } catch (FrontendProtocolException exception) { MarkDisconnected(exception); throw; }
        }
        finally { _pending.TryRemove(id, out _); _requestStates.TryRemove(id, out _); }
    }
    private async Task SendCancelSafelyAsync(long id) { try { if (_pipe is { } pipe) await FrontendWireCodec.WriteAsync(pipe, new(_version, FrontendWireMessageKind.CancelRequest, id), _writeGate, _lifetime.Token).ConfigureAwait(false); } catch { } }
    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested && _pipe is { } pipe)
            {
                var message = await FrontendWireCodec.ReadAsync(pipe, _lifetime.Token).ConfigureAwait(false);
                if (message.ProtocolVersion != FrontendTransportProtocol.CurrentVersion)
                    throw new FrontendProtocolException("Protocol version mismatch.");
                if (message.Kind == FrontendWireMessageKind.Notification && message.Notification == FrontendNotificationKind.StateInvalidated)
                {
                    StateInvalidated?.Invoke(this, EventArgs.Empty);
                    continue;
                }
                if (message.Kind == FrontendWireMessageKind.Notification && message.Notification == FrontendNotificationKind.CloseRequested)
                {
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    continue;
                }
                if (message.Kind != FrontendWireMessageKind.Response || message.RequestId is not > 0)
                    throw new FrontendProtocolException("Unexpected wire message.");
                var id = message.RequestId.Value;
                if (_pending.TryGetValue(id, out var tcs))
                {
                    var responseWon = true;
                    if (_requestStates.TryGetValue(id, out var state))
                    {
                        responseWon = state.TryCompleteResponse();
                        if (!responseWon && Volatile.Read(ref state.Value) == 4)
                            _cancelled.TryRemove(id, out _);
                    }
                    if (responseWon) { _cancelled.TryRemove(id, out _); tcs.TrySetResult(message); }
                }
                else if (!_cancelled.TryRemove(id, out _))
                {
                    throw new FrontendProtocolException("Unexpected response correlation id.");
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { MarkDisconnected(e); }
    }
    private void MarkDisconnected(Exception exception)
    {
        if (Volatile.Read(ref _disposed) != 0 || _lifetime.IsCancellationRequested)
            return;
        _disconnectReason = exception;
        Interlocked.Exchange(ref _pipe, null)?.Dispose();
        FailPending(new FrontendTransportException("Pipe connection closed.", exception));
        if (Interlocked.Exchange(ref _disconnectedRaised, 1) == 0)
            Disconnected?.Invoke(this, EventArgs.Empty);
    }
    private void FailPending(Exception e) { foreach (var item in _pending) item.Value.TrySetException(e); _pending.Clear(); _cancelled.Clear(); }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _lifetime.Cancel(); NamedPipeClientStream? pipe; lock (_connectionGate) pipe = Interlocked.Exchange(ref _pipe, null); pipe?.Dispose(); if (_readLoop is not null) try { await _readLoop.ConfigureAwait(false); } catch { } FailPending(new FrontendTransportException("Client disposed.")); _writeGate.Dispose(); _lifetime.Dispose(); }
}
