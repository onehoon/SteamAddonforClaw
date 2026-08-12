using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

public sealed class MsiClawInputSource : IMsiClawInputDiagnostic, IControllerStateSnapshotSource
{
    private static readonly int M1AuxiliaryIndex = MsiClawControls.Catalog.GetIndex(MsiClawControls.M1);
    private static readonly int M2AuxiliaryIndex = MsiClawControls.Catalog.GetIndex(MsiClawControls.M2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(8);
    private readonly Func<IDirectInputDeviceEnumerator> _enumeratorFactory;
    private readonly Lock _sync = new();
    private InputSession? _currentSession;
    private int _testSession;
    private bool _disposed;

    public MsiClawInputSource(Func<IDirectInputDeviceEnumerator> enumeratorFactory)
    {
        _enumeratorFactory = enumeratorFactory ?? throw new ArgumentNullException(nameof(enumeratorFactory));
    }

    public MsiClawInputSource(IDirectInputDeviceEnumerator enumerator)
        : this(() => enumerator)
    {
    }

    public event EventHandler<ControllerState>? StateChanged;
    public event EventHandler? IndependentVerified;
    public event EventHandler<MsiClawInputTestSummary>? TestCompleted;

    private sealed class StateBox(ControllerState value) { internal ControllerState Value { get; } = value; }
    private static ControllerState NeutralState() => new(new AuxiliaryButtonState(Enumerable.Repeat(false, MsiClawControls.Catalog.Count).ToArray()));
    private StateBox _latestState = new(NeutralState());
    public ControllerState LatestState => Volatile.Read(ref _latestState).Value;

    internal static bool IsM1Pressed(ControllerState state) => state.Auxiliary[M1AuxiliaryIndex];
    internal static bool IsM2Pressed(ControllerState state) => state.Auxiliary[M2AuxiliaryIndex];

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _currentSession is not null && !_currentSession.Cancellation.IsCancellationRequested;
            }
        }
    }

    public MsiClawInputStartResult Start()
    {
        AppLog.Info("Diagnostics", "M1/M2 input diagnostic requested.");

        lock (_sync)
        {
            if (_disposed)
            {
                return new MsiClawInputStartResult(MsiClawInputStartStatus.InitializationFailed, "DirectInput diagnostics are unavailable because the window is closing.");
            }

            if (_currentSession is not null)
            {
                AppLog.Info("Diagnostics", "M1/M2 input diagnostic request ignored.", ("Reason", "DiagnosticAlreadyRunning"), ("Action", "Ignore"));
                return new MsiClawInputStartResult(MsiClawInputStartStatus.AlreadyRunning, "M1/M2 DirectInput test is already running.");
            }

            IDirectInputDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = _enumeratorFactory();
            }
            catch (Exception exception)
            {
                AppLog.Warn("DirectInput", "DirectInput initialization failed.", exception, ("Reason", "DirectInputInitializationFailed"), ("Action", "AbortDiagnostic"), ("NoChangesMade", true));
                return new MsiClawInputStartResult(MsiClawInputStartStatus.InitializationFailed, "DirectInput initialization failed. No controller settings were changed.");
            }

            var nextSession = _testSession + 1;
            IReadOnlyList<DirectInputDeviceDescriptor> candidates;
            try
            {
                candidates = enumerator.EnumerateGameControllers();
            }
            catch (Exception exception)
            {
                AppLog.Warn("MsiInput", "DirectInput enumeration failed.", exception, ("TestSession", nextSession), ("Reason", "EnumerationFailed"), ("Action", "AbortDiagnostic"), ("NoChangesMade", true));
                TryDisposeEnumerator(enumerator, nextSession);
                return new(MsiClawInputStartStatus.EnumerationFailed, "DirectInput device enumeration failed. No controller settings were changed.");
            }

            var selection = MsiClawDirectInputDeviceSelector.Select(candidates);
            LogCandidates(candidates, nextSession);
            if (!selection.IsSelected)
            {
                TryDisposeEnumerator(enumerator, nextSession);
                return MapSelectionFailure(selection);
            }

            AppLog.Info("MsiInput", "MSI Claw DirectInput device selected.",
                ("TestSession", nextSession),
                ("CandidateCount", selection.CandidateCount),
                ("VID", MsiClawHardware.FormatVendorId()),
                ("PID", MsiClawHardware.FormatDirectInputProductId()),
                ("InstanceGuid", selection.Descriptor!.InstanceGuid),
                ("PnpInstanceId", selection.Descriptor.PnpInstanceId),
                ("PhysicalIdentity", selection.Descriptor.PhysicalIdentity),
                ("SelectionReason", selection.Reason));
            return StartCoreLocked(enumerator, selection.Descriptor!, nextSession, "Diagnostics");
        }
    }

    public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
    {
        lock (_sync)
        {
            if (_disposed) return new(MsiClawInputStartStatus.InitializationFailed, "DirectInput is unavailable because the input source is disposed.");
            if (_currentSession is not null) return new(MsiClawInputStartStatus.AlreadyRunning, "M1/M2 DirectInput test is already running.");
            if (!MsiClawDirectInputDeviceSelector.Select([descriptor]).IsSelected)
                return new(MsiClawInputStartStatus.Indeterminate, "The prepared DirectInput descriptor could not be verified. No changes were made.");

            IDirectInputDeviceEnumerator enumerator;
            try
            {
                enumerator = _enumeratorFactory();
            }
            catch (Exception exception)
            {
                AppLog.Warn("DirectInput", "DirectInput initialization failed.", exception, ("Reason", "DirectInputInitializationFailed"), ("Action", "AbortInput"), ("NoChangesMade", true));
                return new(MsiClawInputStartStatus.InitializationFailed, "DirectInput initialization failed. No controller settings were changed.");
            }

            return StartCoreLocked(enumerator, descriptor, _testSession + 1, "Routing");
        }
    }

    private MsiClawInputStartResult StartCoreLocked(IDirectInputDeviceEnumerator enumerator, DirectInputDeviceDescriptor descriptor, int sessionId, string logCategory)
    {
        IDirectInputDevice? device = null;
        try
        {
            device = enumerator.CreateDevice(descriptor);
        }
        catch (Exception exception)
        {
            AppLog.Warn("DirectInput", "DirectInput device creation failed.", exception, ("Reason", "CreateDeviceFailed"), ("Action", "AbortInput"), ("NoChangesMade", true));
            TryDisposeEnumerator(enumerator, sessionId);
            return new(MsiClawInputStartStatus.CreateDeviceFailed, "DirectInput device creation failed. No controller settings were changed.");
        }

        var session = new InputSession(++_testSession, enumerator, device, new CancellationTokenSource());
        try
        {
            AppLog.Info("DirectInput", "Device acquire started.", ("TestSession", session.Id), ("InstanceGuid", descriptor.InstanceGuid));
            var stopwatch = Stopwatch.StartNew();
            device.Acquire();
            session.AcquiredAt = Stopwatch.GetTimestamp();
            session.AcquireDurationMs = stopwatch.ElapsedMilliseconds;
            AppLog.Info("DirectInput", "Device acquire succeeded.", ("TestSession", session.Id), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception)
        {
            AppLog.Warn("DirectInput", "Device acquire failed.", exception, ("TestSession", session.Id), ("Reason", "AcquireFailed"), ("Action", "AbortInput"), ("NoChangesMade", true));
            CleanupBeforePolling(session);
            return new(MsiClawInputStartStatus.AcquireFailed, "DirectInput device acquisition failed. No controller settings were changed.");
        }

        _currentSession = session;
        session.PollingTask = PollAsync(session);
        AppLog.Info(logCategory, "M1/M2 input source started.", ("TestSession", session.Id), ("VID", MsiClawHardware.FormatVendorId()), ("PID", MsiClawHardware.FormatDirectInputProductId()), ("InstanceGuid", descriptor.InstanceGuid));
        return new(MsiClawInputStartStatus.Started, "M1/M2 DirectInput test is running.");
    }

    public async Task StopAsync()
    {
        InputSession? session;
        lock (_sync)
        {
            session = _currentSession;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            session.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        var pollingTask = session.PollingTask;
        if (pollingTask is not null)
        {
            await pollingTask.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
    }

    private static void LogCandidates(IReadOnlyList<DirectInputDeviceDescriptor> candidates, int testSession)
    {
        foreach (var candidate in candidates)
        {
            var matches = MsiClawHardware.IsDirectInputController(candidate.VendorId, candidate.ProductId);
            AppLog.Debug("MsiInput", matches ? "DirectInput device candidate." : "DirectInput device ignored.", ("TestSession", testSession), ("InstanceGuid", candidate.InstanceGuid), ("ProductGuid", candidate.ProductGuid), ("ProductName", candidate.ProductName), ("VID", $"0x{candidate.VendorId:X4}"), ("PID", $"0x{candidate.ProductId:X4}"), ("DevicePath", candidate.DevicePath), ("PnpInstanceId", candidate.PnpInstanceId), ("PhysicalIdentity", candidate.PhysicalIdentity), ("UsagePage", candidate.UsagePage), ("Usage", candidate.Usage), ("ButtonCount", candidate.ButtonCount), ("AxisCount", candidate.AxisCount), ("MatchReason", matches ? "KnownMsiClawDirectInput" : "NotMsiClawPid1902"), ("SelectionReason", candidate.TopologyReason));
        }

        AppLog.Debug("MsiInput", "DirectInput enumeration completed.", ("TestSession", testSession), ("DeviceCount", candidates.Count));
    }

    private static MsiClawInputStartResult MapSelectionFailure(MsiClawDirectInputSelectionResult selection)
    {
        if (selection.Status == MsiClawDirectInputSelectionStatus.NotFound)
        {
            AppLog.Info("Diagnostics", "M1/M2 input diagnostic was not started.", ("Reason", selection.Reason), ("Action", "NoChangesMade"));
            return new(MsiClawInputStartStatus.Pid1902NotFound, "DirectInput PID_1902 device not found. No changes were made.");
        }

        AppLog.Warn("MsiInput", "MSI Claw DirectInput candidate selection is indeterminate.", null, ("CandidateCount", selection.CandidateCount), ("Reason", selection.Reason), ("Action", "DoNotAcquire"));
        return new(MsiClawInputStartStatus.Indeterminate, "MSI Claw PID_1902 DirectInput identity could not be verified. No changes were made.");
    }

    private async Task PollAsync(InputSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        var previous = NeutralState();
        var hasPrevious = false;
        var m1Observed = false;
        var m2Observed = false;
        var m1OnlyObserved = false;
        var m2OnlyObserved = false;
        var independent = false;
        var readFailures = 0;
        var cleanupSucceeded = true;
        var stopReason = MsiClawInputStopReason.Stopped;
        var firstReadLogged = false;

        try
        {
            while (!session.Cancellation.IsCancellationRequested)
            {
                DirectInputState input;
                try
                {
                    input = session.Device.ReadState();
                }
                catch (Exception exception)
                {
                    readFailures++;
                    stopReason = MsiClawInputStopReason.ReadStateFailed;
                    AppLog.Warn("DirectInput", "Controller state read failed.", exception, ("TestSession", session.Id), ("Attempt", readFailures), ("Reason", "ReadStateFailed"), ("Action", "StopDiagnostic"));
                    AppLog.Debug("RoutingTrace", "Physical input read failed.", ("Event", "PhysicalInputReadFailed"), ("RoutingExecution", (object?)RoutingTraceContext.Current), ("TestSession", session.Id), ("SessionAgeMs", Elapsed(session.StartedAt)), ("LastSuccessfulReadAgeMs", session.LastSuccessfulReadAt is { } last ? Elapsed(last) : -1), ("SuccessfulReadCount", session.SuccessfulReadCount), ("ReadFailures", readFailures), ("ExceptionType", exception.GetType().Name));
                    break;
                }

                if (!TryMapState(input, out var current))
                {
                    stopReason = MsiClawInputStopReason.InvalidButtonLayout;
                    AppLog.Warn("MsiInput", "DirectInput state layout is invalid.", null, ("TestSession", session.Id), ("ButtonCount", input.Buttons.Count), ("RequiredButtonCount", MsiClawHardware.RequiredDirectInputButtonCount), ("Action", "StopDiagnostic"), ("Reason", "InsufficientButtonCount"));
                    break;
                }

                var successfulReadAt = Stopwatch.GetTimestamp();
                Volatile.Write(ref _latestState, new StateBox(current));
                session.SuccessfulReadCount++;
                session.LastSuccessfulReadAt = successfulReadAt;
                if (!firstReadLogged)
                {
                    firstReadLogged = true;
                    AppLog.Debug("RoutingTrace", "Physical input first read succeeded.", ("Event", "PhysicalInputFirstRead"), ("RoutingExecution", (object?)RoutingTraceContext.Current), ("TestSession", session.Id), ("AcquireElapsedMs", session.AcquireDurationMs), ("FirstReadAfterAcquireMs", ElapsedBetween(session.AcquiredAt, successfulReadAt)), ("SessionAgeMs", Elapsed(session.StartedAt)));
                }

                if (!hasPrevious)
                {
                    AppLog.Debug("MsiInput", "Initial ControllerState.", ("TestSession", session.Id), ("M1", IsM1Pressed(current)), ("M2", IsM2Pressed(current)));
                    StateChanged?.Invoke(this, current);
                    previous = current;
                    hasPrevious = true;
                }
                else if (current != previous)
                {
                    LogStateChange(session.Id, previous, current);
                    ControllerStateDiagnostics.LogChanges(previous, current, session.Id);
                    StateChanged?.Invoke(this, current);
                    previous = current;
                }

                if (IsM1Pressed(current) && !m1Observed)
                {
                    m1Observed = true;
                    AppLog.Info("Diagnostics", "M1 input verified.", ("TestSession", session.Id), ("ButtonIndex", MsiClawHardware.M1DirectInputButtonIndex));
                }

                if (IsM2Pressed(current) && !m2Observed)
                {
                    m2Observed = true;
                    AppLog.Info("Diagnostics", "M2 input verified.", ("TestSession", session.Id), ("ButtonIndex", MsiClawHardware.M2DirectInputButtonIndex));
                }

                if (IsM1Pressed(current) && !IsM2Pressed(current)) m1OnlyObserved = true;
                if (!IsM1Pressed(current) && IsM2Pressed(current)) m2OnlyObserved = true;
                if (!independent && m1OnlyObserved && m2OnlyObserved)
                {
                    independent = true;
                    AppLog.Info("Diagnostics", "Independent M1/M2 input verified.", ("TestSession", session.Id), ("M1OnlyObserved", true), ("M2OnlyObserved", true), ("M1ButtonIndex", MsiClawHardware.M1DirectInputButtonIndex), ("M2ButtonIndex", MsiClawHardware.M2DirectInputButtonIndex));
                    IndependentVerified?.Invoke(this, EventArgs.Empty);
                }

                await Task.Delay(PollInterval, session.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            Volatile.Write(ref _latestState, new StateBox(NeutralState()));
            cleanupSucceeded = CleanupSession(session);
            var summary = new MsiClawInputTestSummary(session.Id, stopwatch.ElapsedMilliseconds, m1Observed, m2Observed, independent, readFailures, cleanupSucceeded, stopReason);
            lock (_sync)
            {
                if (ReferenceEquals(_currentSession, session))
                {
                    _currentSession = null;
                }
            }
            session.Cancellation.Dispose();
            AppLog.Info("Diagnostics", "M1/M2 input diagnostic completed.", ("TestSession", summary.TestSession), ("DurationMs", summary.DurationMs), ("M1Observed", summary.M1Observed), ("M2Observed", summary.M2Observed), ("Independent", summary.Independent), ("ReadFailures", summary.ReadFailures), ("CleanupSucceeded", summary.CleanupSucceeded), ("StopReason", summary.StopReason));
            TestCompleted?.Invoke(this, summary);
        }
    }

    private static bool TryMapState(DirectInputState input, out ControllerState state)
    {
        if (input.Buttons.Count < MsiClawHardware.RequiredDirectInputButtonCount)
        {
            state = default;
            return false;
        }

        return MsiClawControllerStateMapper.TryMap(input, out state);
    }

    private static bool CleanupSession(InputSession session)
    {
        var cleanupSucceeded = true;
        try
        {
            AppLog.Info("DirectInput", "Device unacquire started.", ("TestSession", session.Id));
            session.Device.Unacquire();
            AppLog.Info("DirectInput", "Device unacquire completed.", ("TestSession", session.Id), ("Success", true));
        }
        catch (Exception exception)
        {
            cleanupSucceeded = false;
            AppLog.Error("DirectInput", "Device cleanup failed.", exception, ("TestSession", session.Id), ("Operation", "Unacquire"));
        }

        try
        {
            session.Device.Dispose();
            AppLog.Info("DirectInput", "Device disposed.", ("TestSession", session.Id));
        }
        catch (Exception exception)
        {
            cleanupSucceeded = false;
            AppLog.Error("DirectInput", "Device cleanup failed.", exception, ("TestSession", session.Id), ("Operation", "Dispose"));
        }

        cleanupSucceeded &= TryDisposeEnumerator(session.Enumerator, session.Id);

        return cleanupSucceeded;
    }

    private static void CleanupBeforePolling(InputSession session)
    {
        CleanupSession(session);
        session.Cancellation.Dispose();
    }

    private static bool TryDisposeEnumerator(IDirectInputDeviceEnumerator enumerator, int testSession)
    {
        try
        {
            enumerator.Dispose();
            AppLog.Info("DirectInput", "DirectInput enumerator disposed.", ("TestSession", testSession));
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("DirectInput", "DirectInput enumerator cleanup failed.", exception, ("TestSession", testSession), ("Operation", "EnumeratorDispose"));
            return false;
        }
    }

    private static void LogStateChange(int session, ControllerState previous, ControllerState current)
    {
        if (IsM1Pressed(previous) != IsM1Pressed(current))
        {
            AppLog.Debug("MsiInput", "M1 state changed.", ("TestSession", session), ("ButtonIndex", MsiClawHardware.M1DirectInputButtonIndex), ("Previous", IsM1Pressed(previous)), ("Current", IsM1Pressed(current)));
        }
        if (IsM2Pressed(previous) != IsM2Pressed(current))
        {
            AppLog.Debug("MsiInput", "M2 state changed.", ("TestSession", session), ("ButtonIndex", MsiClawHardware.M2DirectInputButtonIndex), ("Previous", IsM2Pressed(previous)), ("Current", IsM2Pressed(current)));
        }
        AppLog.Debug("MsiInput", "ControllerState changed.", ("TestSession", session), ("M1", $"{IsM1Pressed(previous)}->{IsM1Pressed(current)}"), ("M2", $"{IsM2Pressed(previous)}->{IsM2Pressed(current)}"));
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private static long ElapsedBetween(long started, long ended) => (long)Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds;

    private sealed class InputSession(int id, IDirectInputDeviceEnumerator enumerator, IDirectInputDevice device, CancellationTokenSource cancellation)
    {
        public int Id { get; } = id;
        public IDirectInputDeviceEnumerator Enumerator { get; } = enumerator;
        public IDirectInputDevice Device { get; } = device;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? PollingTask { get; set; }
        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public long AcquiredAt { get; set; }
        public long AcquireDurationMs { get; set; }
        public long? LastSuccessfulReadAt { get; set; }
        public int SuccessfulReadCount { get; set; }
    }

}
