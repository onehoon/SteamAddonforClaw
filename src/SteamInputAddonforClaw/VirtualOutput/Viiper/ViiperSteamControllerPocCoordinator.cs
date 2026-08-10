using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Power;
using System.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum ViiperSteamControllerPocState { Stopped, Starting, Running, Stopping, Failed }
internal sealed record ViiperSteamControllerPocResult(bool Succeeded, ViiperSteamControllerPocState State, string Reason);
internal sealed record ViiperNativeTeardownResult(bool Success, bool HadApi, bool HadDevice, bool HadBus, bool WasInitialized, bool FinalNeutralSucceeded, bool RemoveDeviceSucceeded, bool RemoveBusSucceeded, bool ShutdownSucceeded, bool DisposeSucceeded);

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
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _nativeOperationLock = new(1, 1);
    private readonly byte[] _report = new byte[ClassicSteamControllerReportBuilder.ReportLength];
    private IViiperNativeApi? _api;
    private ControllerDeviceInfo? _trackedDevice;
    private ViiperSteamControllerPocState _state;
    private uint _deviceId;
    private uint _frame;
    private long _reportsSent;
    private long _reportSendFailures;
    private uint? _firstPumpFrame;
    private uint? _lastPumpFrame;
    private DateTimeOffset? _reportPumpStartedUtc;
    private DateTimeOffset? _lastSuccessfulReportUtc;
    private DateTimeOffset? _reportPumpStopRequestedUtc;
    private CancellationTokenSource? _runningLifetime;
    private Task? _reportPump;
    private readonly Lock _reportSync = new();
    private int _syntheticButtons;
    private readonly PowerMutationGate? _powerGate;
    private readonly ViiperVirtualDeviceIdentityPolicy _identityPolicy;
    private readonly Action? _beforeRunningCommit;
    private CancellationTokenSource _lifecycleCancellation = new();
    private bool _nativeInitialized;
    private bool _busCreated;
    private bool _deviceCreated;
    private int _disposed;

    internal ViiperSteamControllerPocCoordinator(ISystemStatusProvider statusProvider, IControllerDeviceEnumerator deviceEnumerator, AddonOwnedVirtualDeviceTracker tracker, string payloadPath, Func<string, IViiperNativeApi>? nativeLoader = null, PowerMutationGate? powerGate = null, ViiperVirtualDeviceIdentityPolicy? identityPolicy = null, Action? beforeRunningCommit = null)
    {
        _statusProvider = statusProvider;
        _deviceEnumerator = deviceEnumerator;
        _tracker = tracker;
        _payloadPath = payloadPath;
        _nativeLoader = nativeLoader ?? ViiperNativeApi.Load;
        _powerGate = powerGate;
        _identityPolicy = identityPolicy ?? new ViiperVirtualDeviceIdentityPolicy();
        _beforeRunningCommit = beforeRunningCommit;
    }

    internal ViiperSteamControllerPocState State => _state;
    public string Name => "VIIPER";

    internal async Task<ViiperSteamControllerPocResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            await ExecuteNativeAsync(token, "Load", () => { _api = _nativeLoader(Path.GetFullPath(_payloadPath)); return 0; }, operationToken).ConfigureAwait(false);
            if (!await ExecuteNativeAsync(token, "GetDeviceTypes", () => _api!.GetDeviceTypes().Contains("steamcontroller", StringComparer.Ordinal), operationToken).ConfigureAwait(false)) return await FailAsync("SteamControllerTypeUnavailable").ConfigureAwait(false);
            if (await ExecuteNativeAsync(token, "Initialize", () => { var result = _api!.Initialize("127.0.0.1:3241"); if (result == 0) _nativeInitialized = true; return result; }, operationToken).ConfigureAwait(false) != 0) return await FailAsync("ViiperInitFailed").ConfigureAwait(false);
            if (await ExecuteNativeAsync(token, "CreateBus", () => { var result = _api!.CreateBus(BusId); if (result == 0) _busCreated = true; return result; }, operationToken).ConfigureAwait(false) != 0) return await FailAsync("ViiperBusCreateFailed").ConfigureAwait(false);
            var addResult = await ExecuteNativeAsync(token, "AddDevice", () => { var result = _api!.AddDevice(BusId, "steamcontroller", VendorId, ProductId, out var deviceId); if (result == 0) { _deviceId = deviceId; _deviceCreated = true; } return result; }, operationToken).ConfigureAwait(false);
            if (addResult != 0) return await FailAsync("ViiperDeviceAddFailed").ConfigureAwait(false);
            if (await ExecuteNativeAsync(token, "SetFeedbackCallback", () => _api!.SetFeedbackCallback(BusId, _deviceId, feedback => AppLog.Debug("ViiperPoc", "Steam Controller feedback received.", ("Length", feedback.Length))), operationToken).ConfigureAwait(false) != 0) return await FailAsync("ViiperFeedbackCallbackFailed", ownershipUncertain: true).ConfigureAwait(false);
            await ExecuteNativeAsync(token, "InitialNeutral", () => { Send(new ClassicSteamControllerInput(false, false)); return 0; }, operationToken).ConfigureAwait(false);

            _trackedDevice = await WaitForCreatedDeviceAsync(before, operationToken).ConfigureAwait(false);
            EnsureCurrent(token);
            var trackedDevice = _trackedDevice;
            if (trackedDevice is null) return await FailAsync("ViiperDeviceIdentityUnverified", ownershipUncertain: true).ConfigureAwait(false);
            _beforeRunningCommit?.Invoke();
            if (!TryCommitRunning(token, trackedDevice, out var monitorToken))
            {
                AppLog.Warn("ViiperPoc.Power", "VIIPER start invalidated by power transition.", null, ("CapturedEpoch", token.Epoch), ("CurrentEpoch", _powerGate?.Epoch), ("LastCompletedStep", "PnPOwnershipResolution"));
                return new(false, _state, "PowerTransitionInvalidated");
            }
            _ = MonitorSafetyAsync(monitorToken);
            AppLog.Info("ViiperPoc", "Classic Steam Controller PoC started.", ("BusId", BusId), ("DeviceId", _deviceId), ("InstanceId", trackedDevice.InstanceId));
            return new(true, _state, "Started");
        }
        catch (OperationCanceledException) when (_powerGate is not null && !_powerGate.IsOpen)
        {
            AppLog.Warn("ViiperPoc.Power", "VIIPER start invalidated by power transition.", null, ("CurrentEpoch", _powerGate.Epoch));
            return new(false, _state, "PowerTransitionInvalidated");
        }
        catch (Exception exception)
        {
            if (_powerGate is not null && !_powerGate.IsOpen)
            {
                AppLog.Warn("ViiperPoc.Power", "VIIPER start invalidated by power transition.", null, ("CurrentEpoch", _powerGate.Epoch));
                return new(false, _state, "PowerTransitionInvalidated");
            }
            AppLog.Error("ViiperPoc", "Classic Steam Controller PoC start failed.", exception);
            return await FailAsync("StartException", ownershipUncertain: _deviceId != 0).ConfigureAwait(false);
        }
        finally { _lifecycleLock.Release(); }
    }

    private void EnsureCurrent(PowerMutationToken token)
    {
        if (_powerGate is not null && !_powerGate.IsCurrent(token)) throw new OperationCanceledException("Power mutation token is stale.");
    }
    private bool TryCommitRunning(PowerMutationToken token, ControllerDeviceInfo trackedDevice, out CancellationToken monitorToken)
    {
        CancellationTokenSource? committedLifetime = null;
        void Commit()
        {
            _tracker.Publish(trackedDevice);
            _state = ViiperSteamControllerPocState.Running;
            committedLifetime = new CancellationTokenSource();
            _runningLifetime = committedLifetime;
            _reportPump = RunReportPumpAsync(committedLifetime.Token);
        }
        var committed = _powerGate is null ? CommitWithoutGate() : _powerGate.TryCommitMutation(token, Commit);
        monitorToken = committedLifetime?.Token ?? default;
        return committed;

        bool CommitWithoutGate() { Commit(); return true; }
    }

    internal async Task<ViiperSteamControllerPocResult> StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == ViiperSteamControllerPocState.Stopped) return new(true, _state, "AlreadyStopped");
            if (_api is null) return await FailAsync("NativeApiUnavailable").ConfigureAwait(false);
            _state = ViiperSteamControllerPocState.Stopping;
            Volatile.Write(ref _syntheticButtons, 0);
            _reportPumpStopRequestedUtc = DateTimeOffset.UtcNow;
            _runningLifetime?.Cancel();
            if (_reportPump is not null) await _reportPump.ConfigureAwait(false);
            await _nativeOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            ViiperNativeTeardownResult teardown;
            try { teardown = CleanupNativeSessionUnderLock("NormalStop", null, null); }
            finally { _nativeOperationLock.Release(); }
            LogReportPumpSummary("NormalStop", teardown.FinalNeutralSucceeded);
            if (!teardown.Success) { _tracker.MarkOwnershipUncertain(); _state = ViiperSteamControllerPocState.Failed; return new(false, _state, "ViiperNativeTeardownFailed"); }
            if (_trackedDevice is not null && !await WaitForDisappearanceAsync(_trackedDevice.InstanceId, cancellationToken).ConfigureAwait(false)) return await FailAsync("PnPDisappearanceTimedOut", ownershipUncertain: true).ConfigureAwait(false);
            if (_trackedDevice is not null) _tracker.Remove(_trackedDevice);
            _trackedDevice = null;
            _state = ViiperSteamControllerPocState.Stopped;
            AppLog.Info("ViiperPoc", "Classic Steam Controller PoC stopped.");
            return new(true, _state, "Stopped");
        }
        catch (Exception exception)
        {
            AppLog.Error("ViiperPoc", "Classic Steam Controller PoC stop failed.", exception);
            return await FailAsync("StopException").ConfigureAwait(false);
        }
        finally { _lifecycleLock.Release(); }
    }

    internal async Task<ViiperSteamControllerPocResult> PulseAsync(bool leftGrip, bool rightGrip, CancellationToken cancellationToken = default)
    {
        if (_state != ViiperSteamControllerPocState.Running || _api is null) return new(false, _state, "NotRunning");
        var powerToken = new PowerMutationToken(0);
        if (_powerGate is not null && !_powerGate.TryAcquire(out powerToken)) return new(false, _state, "PowerGateClosed");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifecycleCancellation.Token);
        EnsureCurrent(powerToken);
        Volatile.Write(ref _syntheticButtons, (leftGrip ? 1 : 0) | (rightGrip ? 2 : 0));
        try { await Task.Delay(TimeSpan.FromMilliseconds(150), linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_powerGate is not null && !_powerGate.IsCurrent(powerToken))
        {
            Volatile.Write(ref _syntheticButtons, 0);
            return new(false, _state, "PowerTransitionInvalidated");
        }
        EnsureCurrent(powerToken);
        Volatile.Write(ref _syntheticButtons, 0);
        return new(true, _state, leftGrip ? "LeftGripPulse" : "RightGripPulse");
    }

    private void Send(ClassicSteamControllerInput input, bool reportPump = false)
    {
        lock (_reportSync)
        {
            if (_api is null) return;
            var frame = _frame++;
            ClassicSteamControllerReportBuilder.Write(_report, frame, input);
            if (_api.SetInput(BusId, _deviceId, _report) != 0)
            {
                if (reportPump) _reportSendFailures++;
                throw new InvalidOperationException($"VIIPER input failed: {_api.GetLastError()}");
            }
            if (reportPump)
            {
                _reportsSent++;
                _firstPumpFrame ??= frame;
                _lastPumpFrame = frame;
                _lastSuccessfulReportUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    private async Task RunReportPumpAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        _reportPumpStartedUtc = DateTimeOffset.UtcNow;
        try { while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) Send(CurrentInput(), reportPump: true); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("ViiperPoc", "VIIPER report pump failed.", exception);
            _ = Task.Run(HandleReportPumpFailureAsync);
        }
    }

    private async Task HandleReportPumpFailureAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == ViiperSteamControllerPocState.Running)
                await FailAsync("ReportPumpFailure", ownershipUncertain: true).ConfigureAwait(false);
        }
        finally { _lifecycleLock.Release(); }
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

    private async Task<T> ExecuteNativeAsync<T>(PowerMutationToken token, string operation, Func<T> action, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        AppLog.Trace("ViiperPoc.Lifecycle", "VIIPER native operation waiting.", ("Operation", operation), ("CapturedEpoch", token.Epoch));
        await _nativeOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try { EnsureCurrent(token); }
            catch { AppLog.Warn("ViiperPoc.Lifecycle", "VIIPER native operation rejected by stale power epoch.", null, ("Operation", operation), ("CapturedEpoch", token.Epoch), ("CurrentEpoch", _powerGate?.Epoch)); throw; }
            AppLog.Debug("ViiperPoc.Lifecycle", "VIIPER native operation admitted.", ("Operation", operation), ("CapturedEpoch", token.Epoch));
            return action();
        }
        finally { _nativeOperationLock.Release(); AppLog.Trace("ViiperPoc.Lifecycle", "VIIPER native operation completed.", ("Operation", operation), ("ElapsedMs", Stopwatch.GetElapsedTime(started).TotalMilliseconds)); }
    }
    private async Task<ViiperSteamControllerPocResult> FailAsync(string reason, bool ownershipUncertain = false)
    {
        _reportPumpStopRequestedUtc = DateTimeOffset.UtcNow;
        _runningLifetime?.Cancel(); if (ownershipUncertain) _tracker.MarkOwnershipUncertain();
        await _nativeOperationLock.WaitAsync().ConfigureAwait(false);
        try { _ = CleanupNativeSessionUnderLock(reason, null, null); }
        finally { _nativeOperationLock.Release(); }
        _trackedDevice = null; _state = ViiperSteamControllerPocState.Failed;
        AppLog.Warn("ViiperPoc", "Classic Steam Controller PoC entered failed state.", null, ("Reason", reason)); return new(false, _state, reason);
    }
    private ViiperNativeTeardownResult CleanupNativeSessionUnderLock(string reason, long? cycle, long? epoch)
    {
        var api = _api; var hadApi = api is not null; var hadDevice = _deviceCreated; var hadBus = _busCreated; var initialized = _nativeInitialized;
        var neutral = !hadDevice; var removeDevice = !hadDevice; var removeBus = !hadBus; var shutdown = !initialized; var dispose = !hadApi;
        if (api is not null)
        {
            try { if (hadDevice) { AppLog.Debug("ViiperPoc.Power", "VIIPER native teardown step started.", ("Operation", "FinalNeutral"), ("Cycle", cycle), ("Epoch", epoch)); Send(new ClassicSteamControllerInput(false, false)); neutral = true; } } catch { neutral = false; }
            LogTeardownStep("FinalNeutral", hadDevice, neutral, cycle, epoch);
            try { if (hadDevice) { AppLog.Debug("ViiperPoc.Power", "VIIPER native teardown step started.", ("Operation", "RemoveDevice"), ("Cycle", cycle), ("Epoch", epoch), ("DeviceId", _deviceId)); removeDevice = api.RemoveDevice(BusId, _deviceId) == 0; } } catch { removeDevice = false; }
            LogTeardownStep("RemoveDevice", hadDevice, removeDevice, cycle, epoch);
            try { if (hadBus) { AppLog.Debug("ViiperPoc.Power", "VIIPER native teardown step started.", ("Operation", "RemoveBus"), ("Cycle", cycle), ("Epoch", epoch)); removeBus = api.RemoveBus(BusId) == 0; } } catch { removeBus = false; }
            LogTeardownStep("RemoveBus", hadBus, removeBus, cycle, epoch);
            try { if (initialized) { AppLog.Debug("ViiperPoc.Power", "VIIPER native teardown step started.", ("Operation", "Shutdown"), ("Cycle", cycle), ("Epoch", epoch)); api.Shutdown(); shutdown = true; } } catch { shutdown = false; }
            LogTeardownStep("Shutdown", initialized, shutdown, cycle, epoch);
            try { AppLog.Debug("ViiperPoc.Power", "VIIPER native teardown step started.", ("Operation", "Dispose"), ("Cycle", cycle), ("Epoch", epoch)); api.Dispose(); dispose = true; } catch { dispose = false; }
            LogTeardownStep("Dispose", true, dispose, cycle, epoch);
        }
        _api = null; _deviceId = 0; _deviceCreated = _busCreated = _nativeInitialized = false;
        var result = new ViiperNativeTeardownResult(neutral && removeDevice && removeBus && shutdown && dispose, hadApi, hadDevice, hadBus, initialized, neutral, removeDevice, removeBus, shutdown, dispose);
        AppLog.Info("ViiperPoc.Power", "VIIPER native teardown completed.", ("Reason", reason), ("Cycle", cycle), ("Epoch", epoch), ("FinalNeutralSucceeded", neutral), ("RemoveDeviceSucceeded", removeDevice), ("RemoveBusSucceeded", removeBus), ("ShutdownSucceeded", shutdown), ("DisposeSucceeded", dispose));
        return result;
    }
    private static void LogTeardownStep(string operation, bool attempted, bool succeeded, long? cycle, long? epoch) => AppLog.Debug("ViiperPoc.Power", "VIIPER native teardown step completed.", ("Operation", operation), ("Cycle", cycle), ("Epoch", epoch), ("Outcome", attempted ? succeeded ? "Succeeded" : "Failed" : "Skipped"));

    private static bool AllowsStart(SystemStatusSnapshot snapshot) => snapshot.HardwareCompatibility.Status == SteamInputAddonforClaw.Devices.HardwareCompatibilityStatus.Supported && snapshot.Compatibility.AllowsMutation && snapshot.RecoverySafe && snapshot.ExternalController.Status == ExternalControllerAssessmentStatus.Clear && snapshot.Steam.IsActive && snapshot.Prerequisites.HidHide.Status == SteamInputAddonforClaw.Prerequisites.PrerequisiteStatus.Ready && snapshot.Prerequisites.UsbIpWin2.Status == SteamInputAddonforClaw.Prerequisites.PrerequisiteStatus.Ready && snapshot.Prerequisites.Viiper.Status == SteamInputAddonforClaw.Prerequisites.PrerequisiteStatus.Present;
    internal void CancelLifecycle() { try { _lifecycleCancellation.Cancel(); } catch { } }
    public async Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var previousState = _state;
        var reportPumpStopped = _reportPump is null;
        var nativeLockAcquired = false;
        AppLog.Info("ViiperPoc.Power", "Suspend quiesce started.", ("Cycle", cycle), ("Epoch", epoch), ("ViiperState", _state), ("HasApi", _api is not null), ("Initialized", _nativeInitialized), ("BusCreated", _busCreated), ("DeviceCreated", _deviceCreated), ("DeviceId", _deviceId), ("BudgetRemainingMs", (deadline - started).TotalMilliseconds));
        CancelLifecycle();
        Volatile.Write(ref _syntheticButtons, 0);
        _reportPumpStopRequestedUtc = DateTimeOffset.UtcNow;
        _runningLifetime?.Cancel();
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) { _tracker.MarkOwnershipUncertain(); return CompleteSuspendQuiesce(false, "DeadlineExpiredBeforeReportPumpStop", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, null, "DeadlineExpired"); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(remaining);
        try { if (_reportPump is not null) await _reportPump.WaitAsync(timeout.Token).ConfigureAwait(false); reportPumpStopped = true; }
        catch (OperationCanceledException) { _tracker.MarkOwnershipUncertain(); AppLog.Warn("ViiperPoc.Power", "VIIPER suspend teardown could not stop the report pump before deadline.", null, ("Cycle", cycle), ("Epoch", epoch)); return CompleteSuspendQuiesce(false, "ReportPumpStopTimedOut", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, null, "NotChecked"); }
        catch (Exception exception) { _tracker.MarkOwnershipUncertain(); AppLog.Error("ViiperPoc.Power", "VIIPER suspend teardown report pump stop failed.", exception, ("Cycle", cycle), ("Epoch", epoch)); return CompleteSuspendQuiesce(false, "ReportPumpStopFailed", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, null, "NotChecked"); }
        try { await _nativeOperationLock.WaitAsync(timeout.Token).ConfigureAwait(false); nativeLockAcquired = true; }
        catch (OperationCanceledException) { _tracker.MarkOwnershipUncertain(); AppLog.Warn("ViiperPoc.Power", "VIIPER suspend teardown could not acquire native-operation lock before deadline.", null, ("Cycle", cycle), ("Epoch", epoch), ("ElapsedMs", (DateTimeOffset.UtcNow - started).TotalMilliseconds)); return CompleteSuspendQuiesce(false, "NativeLockTimedOut", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, null, "NotChecked"); }
        catch (Exception exception) { _tracker.MarkOwnershipUncertain(); AppLog.Error("ViiperPoc.Power", "VIIPER suspend teardown native-operation lock acquisition failed.", exception, ("Cycle", cycle), ("Epoch", epoch)); return CompleteSuspendQuiesce(false, "NativeLockFailed", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, null, "NotChecked"); }
        ControllerDeviceInfo? tracked;
        ViiperNativeTeardownResult teardown;
        try
        {
            teardown = CleanupNativeSessionUnderLock("Suspend", cycle, epoch);
            tracked = _trackedDevice; _trackedDevice = null;
        }
        finally { _nativeOperationLock.Release(); }
        LogReportPumpSummary("Suspend", teardown.FinalNeutralSucceeded, cycle, epoch);
        _state = ViiperSteamControllerPocState.Stopped;
        if (!teardown.Success) { _tracker.MarkOwnershipUncertain(); AppLog.Warn("ViiperPoc.Power", "Suspend native teardown completed with failures.", null, ("Cycle", cycle), ("Epoch", epoch), ("ElapsedMs", (DateTimeOffset.UtcNow - started).TotalMilliseconds), ("OwnershipUncertain", true)); return CompleteSuspendQuiesce(false, "NativeTeardownFailed", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, teardown, "NotChecked"); }
        if (tracked is null)
        {
            if (teardown.HadDevice) { _tracker.MarkOwnershipUncertain(); AppLog.Warn("ViiperPoc.Power", "Suspend teardown could not verify an untracked created device.", null, ("Cycle", cycle), ("Epoch", epoch), ("OwnershipUncertain", true)); return CompleteSuspendQuiesce(false, "UntrackedNativeDevice", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, teardown, "NotChecked"); }
            return CompleteSuspendQuiesce(true, "NoNativeDevice", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, teardown, "NotRequired");
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (DateTimeOffset.UtcNow >= deadline) break;
            try
            {
                var present = _deviceEnumerator.EnumeratePresentDevices();
                var stillPresent = present.Any(device => string.Equals(device.InstanceId, tracked.InstanceId, StringComparison.OrdinalIgnoreCase));
                var matchingCandidateCount = present.Count(_identityPolicy.IsMatchingCandidate);
                AppLog.Trace("ViiperPoc.PnP", "Suspend PnP quick verification poll.", ("Cycle", cycle), ("Epoch", epoch), ("Attempt", attempt + 1), ("TrackedInstancePresent", stillPresent), ("CandidatePresent", matchingCandidateCount > 0), ("MatchingCandidateCount", matchingCandidateCount), ("RemainingBudgetMs", (deadline - DateTimeOffset.UtcNow).TotalMilliseconds));
                if (!stillPresent && matchingCandidateCount == 0)
                {
                    _tracker.Remove(tracked); _state = ViiperSteamControllerPocState.Stopped;
                    AppLog.Info("ViiperPoc.Power", "Suspend PnP quick verification completed.", ("Cycle", cycle), ("Epoch", epoch), ("Outcome", "VerifiedAbsent"), ("Attempt", attempt + 1));
                    return CompleteSuspendQuiesce(true, "Succeeded", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, teardown, "VerifiedAbsent");
                }
            }
            catch (Exception exception) { AppLog.Warn("ViiperPoc.PnP", "Suspend PnP quick verification failed.", exception, ("Cycle", cycle), ("Epoch", epoch), ("Attempt", attempt + 1), ("Outcome", "EnumerationFailed")); break; }
            var delay = TimeSpan.FromMilliseconds(Math.Min(25, Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds)));
            try { if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { _tracker.MarkOwnershipUncertain(); return CompleteSuspendQuiesce(false, "PnPVerificationTimedOut", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, teardown, "DeadlineExpired"); }
        }
        _tracker.MarkOwnershipUncertain();
        var pnpOutcome = DateTimeOffset.UtcNow >= deadline ? "DeadlineExpired" : "StillPresentOrReplacementCandidate";
        AppLog.Warn("ViiperPoc.Power", "Suspend PnP quick verification did not confirm disappearance.", null, ("Cycle", cycle), ("Epoch", epoch), ("Outcome", pnpOutcome), ("OwnershipUncertain", true), ("TotalElapsedMs", (DateTimeOffset.UtcNow - started).TotalMilliseconds));
        return CompleteSuspendQuiesce(false, "PnPVerificationFailed", cycle, epoch, previousState, started, reportPumpStopped, nativeLockAcquired, teardown, pnpOutcome);
    }
    private bool CompleteSuspendQuiesce(bool success, string outcome, long cycle, long epoch, ViiperSteamControllerPocState previousState, DateTimeOffset started, bool reportPumpStopped, bool nativeLockAcquired, ViiperNativeTeardownResult? teardown, string pnpOutcome)
    {
        AppLog.Info("ViiperPoc.Power", "VIIPER suspend quiesce completed.", ("Cycle", cycle), ("Epoch", epoch), ("Outcome", outcome), ("PreviousViiperState", previousState), ("ReportPumpStopped", reportPumpStopped), ("NativeLockAcquired", nativeLockAcquired), ("FinalNeutralSucceeded", teardown?.FinalNeutralSucceeded), ("RemoveDeviceSucceeded", teardown?.RemoveDeviceSucceeded), ("RemoveBusSucceeded", teardown?.RemoveBusSucceeded), ("ShutdownSucceeded", teardown?.ShutdownSucceeded), ("DisposeSucceeded", teardown?.DisposeSucceeded), ("PnpOutcome", pnpOutcome), ("OwnershipUncertain", _tracker.HasUncertainOwnership), ("TotalElapsedMs", (DateTimeOffset.UtcNow - started).TotalMilliseconds), ("BudgetRemainingMs", 0d), ("Succeeded", success));
        return success;
    }
    private void LogReportPumpSummary(string reason, bool finalNeutralSucceeded, long? cycle = null, long? epoch = null)
    {
        if (_reportPumpStartedUtc is null) return;
        AppLog.Info("ViiperPoc.ReportPump", "Report pump stopped.", ("Reason", reason), ("Cycle", cycle), ("Epoch", epoch), ("ReportsSent", _reportsSent), ("FirstFrame", _firstPumpFrame), ("LastFrame", _lastPumpFrame), ("SendFailures", _reportSendFailures), ("PumpStartedUtc", _reportPumpStartedUtc), ("LastSuccessfulSendUtc", _lastSuccessfulReportUtc), ("PumpStopRequestedUtc", _reportPumpStopRequestedUtc), ("FinalNeutralSucceeded", finalNeutralSucceeded));
    }
    public Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken)
    {
        if (!_tracker.HasUncertainOwnership) return Task.FromResult(true);
        try { return Task.FromResult(_tracker.ClearUncertaintyAfterVerifiedAbsence(_deviceEnumerator.EnumeratePresentDevices(), _identityPolicy)); }
        catch { return Task.FromResult(false); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_state is ViiperSteamControllerPocState.Running or ViiperSteamControllerPocState.Starting) await StopAsync().ConfigureAwait(false);
        _lifecycleCancellation.Dispose();
    }
}
