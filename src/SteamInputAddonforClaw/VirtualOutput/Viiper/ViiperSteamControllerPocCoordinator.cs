using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Power;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum ViiperSteamControllerPocState { Stopped, Starting, Running, Stopping, Failed }
internal sealed record ViiperSteamControllerPocResult(bool Succeeded, ViiperSteamControllerPocState State, string Reason);

internal sealed class ViiperSteamControllerPocCoordinator : IAsyncDisposable, IPowerTransitionParticipant
{
    private const uint BusId = 1;
    private const ushort VendorId = 0x28DE;
    private const ushort ProductId = 0x1102;
    private readonly ISystemStatusProvider _statusProvider;
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly AddonOwnedVirtualDeviceTracker _tracker;
    private readonly string _payloadPath;
    private readonly Func<string, IViiperNativeApi> _nativeLoader;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly byte[] _report = new byte[ClassicSteamControllerReportBuilder.ReportLength];
    private IViiperNativeApi? _api;
    private ControllerDeviceInfo? _trackedDevice;
    private ViiperSteamControllerPocState _state;
    private uint _deviceId;
    private uint _frame;
    private CancellationTokenSource? _runningLifetime;
    private Task? _reportPump;
    private readonly Lock _reportSync = new();
    private int _syntheticButtons;
    private readonly PowerMutationGate? _powerGate;
    private readonly ViiperVirtualDeviceIdentityPolicy _identityPolicy;
    private CancellationTokenSource _lifecycleCancellation = new();

    internal ViiperSteamControllerPocCoordinator(ISystemStatusProvider statusProvider, IControllerDeviceEnumerator deviceEnumerator, AddonOwnedVirtualDeviceTracker tracker, string payloadPath, Func<string, IViiperNativeApi>? nativeLoader = null, PowerMutationGate? powerGate = null, ViiperVirtualDeviceIdentityPolicy? identityPolicy = null)
    {
        _statusProvider = statusProvider;
        _deviceEnumerator = deviceEnumerator;
        _tracker = tracker;
        _payloadPath = payloadPath;
        _nativeLoader = nativeLoader ?? ViiperNativeApi.Load;
        _powerGate = powerGate;
        _identityPolicy = identityPolicy ?? new ViiperVirtualDeviceIdentityPolicy();
    }

    internal ViiperSteamControllerPocState State => _state;
    public string Name => "VIIPER";

    internal async Task<ViiperSteamControllerPocResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var token = new PowerMutationToken(0);
            if (_powerGate is not null && !_powerGate.TryAcquire(out token)) return new(false, _state, "PowerGateClosed");
            if (_lifecycleCancellation.IsCancellationRequested) { _lifecycleCancellation.Dispose(); _lifecycleCancellation = new CancellationTokenSource(); }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifecycleCancellation.Token);
            var operationToken = linked.Token;
            if (_state == ViiperSteamControllerPocState.Running) return new(true, _state, "AlreadyRunning");
            if (_state == ViiperSteamControllerPocState.Failed) return new(false, _state, "FailedStateRequiresRestart");
            var status = await _statusProvider.CaptureAsync(operationToken).ConfigureAwait(false);
            EnsureCurrent(token);
            if (!AllowsStart(status)) return new(false, _state, "SafetyGateDenied");
            if (!File.Exists(_payloadPath)) return new(false, _state, "ViiperPayloadMissing");

            _state = ViiperSteamControllerPocState.Starting;
            var before = _deviceEnumerator.EnumeratePresentDevices();
            EnsureCurrent(token);
            _api = _nativeLoader(Path.GetFullPath(_payloadPath));
            EnsureCurrent(token);
            if (!_api.GetDeviceTypes().Contains("steamcontroller", StringComparer.Ordinal)) return Fail("SteamControllerTypeUnavailable");
            EnsureCurrent(token);
            if (_api.Initialize("127.0.0.1:3241") != 0) return Fail("ViiperInitFailed");
            EnsureCurrent(token);
            if (_api.CreateBus(BusId) != 0) return Fail("ViiperBusCreateFailed");
            EnsureCurrent(token);
            if (_api.AddDevice(BusId, "steamcontroller", VendorId, ProductId, out _deviceId) != 0) return Fail("ViiperDeviceAddFailed");
            EnsureCurrent(token);
            if (_api.SetFeedbackCallback(BusId, _deviceId, feedback => AppLog.Debug("ViiperPoc", "Steam Controller feedback received.", ("Length", feedback.Length))) != 0) return Fail("ViiperFeedbackCallbackFailed", ownershipUncertain: true);
            EnsureCurrent(token);
            Send(new ClassicSteamControllerInput(false, false));

            _trackedDevice = await WaitForCreatedDeviceAsync(before, operationToken).ConfigureAwait(false);
            EnsureCurrent(token);
            if (_trackedDevice is null) return Fail("ViiperDeviceIdentityUnverified", ownershipUncertain: true);
            EnsureCurrent(token);
            _tracker.Publish(_trackedDevice);
            EnsureCurrent(token);
            _state = ViiperSteamControllerPocState.Running;
            _runningLifetime = new CancellationTokenSource();
            _reportPump = RunReportPumpAsync(_runningLifetime.Token);
            _ = MonitorSafetyAsync(_runningLifetime.Token);
            AppLog.Info("ViiperPoc", "Classic Steam Controller PoC started.", ("BusId", BusId), ("DeviceId", _deviceId), ("InstanceId", _trackedDevice.InstanceId));
            return new(true, _state, "Started");
        }
        catch (Exception exception)
        {
            if (_powerGate is not null && !_powerGate.IsOpen) AppLog.Warn("ViiperPoc.Power", "VIIPER start invalidated by power transition.", null, ("CurrentEpoch", _powerGate.Epoch));
            AppLog.Error("ViiperPoc", "Classic Steam Controller PoC start failed.", exception);
            return Fail("StartException", ownershipUncertain: _deviceId != 0);
        }
        finally { _mutex.Release(); }
    }

    private void EnsureCurrent(PowerMutationToken token)
    {
        if (_powerGate is not null && !_powerGate.IsCurrent(token)) throw new OperationCanceledException("Power mutation token is stale.");
    }

    internal async Task<ViiperSteamControllerPocResult> StopAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == ViiperSteamControllerPocState.Stopped) return new(true, _state, "AlreadyStopped");
            if (_api is null) return Fail("NativeApiUnavailable");
            _state = ViiperSteamControllerPocState.Stopping;
            Volatile.Write(ref _syntheticButtons, 0);
            _runningLifetime?.Cancel();
            if (_reportPump is not null) await _reportPump.ConfigureAwait(false);
            Send(new ClassicSteamControllerInput(false, false));
            if (_api.RemoveDevice(BusId, _deviceId) != 0) return Fail("ViiperDeviceRemoveFailed", ownershipUncertain: true);
            if (_trackedDevice is not null && !await WaitForDisappearanceAsync(_trackedDevice.InstanceId, cancellationToken).ConfigureAwait(false)) return Fail("PnPDisappearanceTimedOut", ownershipUncertain: true);
            if (_trackedDevice is not null) _tracker.Remove(_trackedDevice);
            _api.RemoveBus(BusId);
            _api.Shutdown();
            _api.Dispose();
            _api = null;
            _trackedDevice = null;
            _deviceId = 0;
            _state = ViiperSteamControllerPocState.Stopped;
            AppLog.Info("ViiperPoc", "Classic Steam Controller PoC stopped.");
            return new(true, _state, "Stopped");
        }
        catch (Exception exception)
        {
            AppLog.Error("ViiperPoc", "Classic Steam Controller PoC stop failed.", exception);
            return Fail("StopException");
        }
        finally { _mutex.Release(); }
    }

    internal async Task<ViiperSteamControllerPocResult> PulseAsync(bool leftGrip, bool rightGrip, CancellationToken cancellationToken = default)
    {
        if (_state != ViiperSteamControllerPocState.Running || _api is null) return new(false, _state, "NotRunning");
        Volatile.Write(ref _syntheticButtons, (leftGrip ? 1 : 0) | (rightGrip ? 2 : 0));
        await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _syntheticButtons, 0);
        return new(true, _state, leftGrip ? "LeftGripPulse" : "RightGripPulse");
    }

    private void Send(ClassicSteamControllerInput input)
    {
        lock (_reportSync)
        {
            if (_api is null) return;
            ClassicSteamControllerReportBuilder.Write(_report, _frame++, input);
            if (_api.SetInput(BusId, _deviceId, _report) != 0) throw new InvalidOperationException($"VIIPER input failed: {_api.GetLastError()}");
        }
    }

    private async Task RunReportPumpAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        try { while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) Send(CurrentInput()); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("ViiperPoc", "VIIPER report pump failed.", exception);
            _ = Task.Run(HandleReportPumpFailureAsync);
        }
    }

    private async Task HandleReportPumpFailureAsync()
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == ViiperSteamControllerPocState.Running)
                Fail("ReportPumpFailure", ownershipUncertain: true);
        }
        finally { _mutex.Release(); }
    }

    private ClassicSteamControllerInput CurrentInput()
    {
        var buttons = Volatile.Read(ref _syntheticButtons);
        return new((buttons & 1) != 0, (buttons & 2) != 0);
    }

    private async Task MonitorSafetyAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                var status = await _statusProvider.CaptureAsync(cancellationToken).ConfigureAwait(false);
                if (!status.Steam.IsActive || status.ExternalController.Status != ExternalControllerAssessmentStatus.Clear)
                {
                    AppLog.Warn("ViiperPoc", "Safety state changed while PoC was active.", null, ("SteamActive", status.Steam.IsActive), ("External", status.ExternalController.Status));
                    _ = StopAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("ViiperPoc", "Safety monitor failed.", exception); _ = StopAsync(); }
    }

    private async Task<ControllerDeviceInfo?> WaitForCreatedDeviceAsync(IReadOnlyList<ControllerDeviceInfo> before, CancellationToken token)
    {
        var known = before.Select(device => device.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidates = _deviceEnumerator.EnumeratePresentDevices().Where(device => !known.Contains(device.InstanceId) && _identityPolicy.IsMatchingCandidate(device)).ToArray();
            if (candidates.Length == 1) return candidates[0];
            if (candidates.Length > 1) return null;
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<bool> WaitForDisappearanceAsync(string instanceId, CancellationToken token)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (_deviceEnumerator.EnumeratePresentDevices().All(device => !string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))) return true;
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        return false;
    }

    private ViiperSteamControllerPocResult Fail(string reason, bool ownershipUncertain = false)
    {
        _runningLifetime?.Cancel();
        if (ownershipUncertain) _tracker.MarkOwnershipUncertain();
        try { if (_api is not null) { if (_deviceId != 0) _api.RemoveDevice(BusId, _deviceId); _api.RemoveBus(BusId); _api.Shutdown(); _api.Dispose(); } } catch { }
        _api = null;
        _trackedDevice = null;
        _deviceId = 0;
        _state = ViiperSteamControllerPocState.Failed;
        AppLog.Warn("ViiperPoc", "Classic Steam Controller PoC entered failed state.", null, ("Reason", reason));
        return new(false, _state, reason);
    }

    private static bool AllowsStart(SystemStatusSnapshot snapshot) => snapshot.HardwareCompatibility.Status == SteamInputAddonforClaw.Devices.HardwareCompatibilityStatus.Supported && snapshot.Compatibility.AllowsMutation && snapshot.RecoverySafe && snapshot.ExternalController.Status == ExternalControllerAssessmentStatus.Clear && snapshot.Steam.IsActive && snapshot.Prerequisites.HidHide.Status == SteamInputAddonforClaw.Prerequisites.PrerequisiteStatus.Ready && snapshot.Prerequisites.UsbIpWin2.Status == SteamInputAddonforClaw.Prerequisites.PrerequisiteStatus.Ready && snapshot.Prerequisites.Viiper.Status == SteamInputAddonforClaw.Prerequisites.PrerequisiteStatus.Present;
    internal void CancelLifecycle() { try { _lifecycleCancellation.Cancel(); } catch { } }
    public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        CancelLifecycle();
        Volatile.Write(ref _syntheticButtons, 0);
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) { _tracker.MarkOwnershipUncertain(); return false; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(remaining);
        var result = await StopAsync(timeout.Token).ConfigureAwait(false);
        if (!result.Succeeded) _tracker.MarkOwnershipUncertain();
        return result.Succeeded;
    }
    public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken)
    {
        if (!_tracker.HasUncertainOwnership) return Task.FromResult(true);
        try { return Task.FromResult(_tracker.ClearUncertaintyAfterVerifiedAbsence(_deviceEnumerator.EnumeratePresentDevices(), _identityPolicy)); }
        catch { return Task.FromResult(false); }
    }
    public async ValueTask DisposeAsync() { if (_state is ViiperSteamControllerPocState.Running or ViiperSteamControllerPocState.Starting) await StopAsync(); _mutex.Dispose(); }
}
