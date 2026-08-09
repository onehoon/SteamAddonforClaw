using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input.DirectInput;

namespace SteamInputAddonforClaw.Input;

public sealed class MsiClawInputSource : IAsyncDisposable
{
    private const ushort MsiVendorId = 0x0DB0;
    private const ushort DirectInputProductId = 0x1902;
    private const int M1ButtonIndex = 15;
    private const int M2ButtonIndex = 16;
    private const int RequiredButtonCount = M2ButtonIndex + 1;
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
            IDirectInputDevice? device = null;
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
            var selection = SelectDevice(enumerator, nextSession);
            if (selection.Status != MsiClawInputStartStatus.Started)
            {
                TryDisposeEnumerator(enumerator, nextSession);
                return new MsiClawInputStartResult(selection.Status, selection.Message);
            }

            try
            {
                device = enumerator.CreateDevice(selection.Descriptor!);
            }
            catch (Exception exception)
            {
                AppLog.Warn("DirectInput", "DirectInput device creation failed.", exception, ("Reason", "CreateDeviceFailed"), ("Action", "AbortDiagnostic"), ("NoChangesMade", true));
                TryDisposeEnumerator(enumerator, nextSession);
                return new MsiClawInputStartResult(MsiClawInputStartStatus.CreateDeviceFailed, "DirectInput device creation failed. No controller settings were changed.");
            }

            var session = new InputSession(++_testSession, enumerator, device, new CancellationTokenSource());
            try
            {
                AppLog.Info("DirectInput", "Device acquire started.", ("TestSession", session.Id), ("InstanceGuid", selection.Descriptor!.InstanceGuid));
                var stopwatch = Stopwatch.StartNew();
                device.Acquire();
                AppLog.Info("DirectInput", "Device acquire succeeded.", ("TestSession", session.Id), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            }
            catch (Exception exception)
            {
                AppLog.Warn("DirectInput", "Device acquire failed.", exception, ("TestSession", session.Id), ("Reason", "AcquireFailed"), ("Action", "AbortDiagnostic"), ("NoChangesMade", true));
                CleanupBeforePolling(session);
                return new MsiClawInputStartResult(MsiClawInputStartStatus.AcquireFailed, "DirectInput device acquisition failed. No controller settings were changed.");
            }

            _currentSession = session;
            session.PollingTask = PollAsync(session);
            AppLog.Info("Diagnostics", "M1/M2 input diagnostic started.", ("TestSession", session.Id), ("VID", "0x0DB0"), ("PID", "0x1902"), ("InstanceGuid", selection.Descriptor!.InstanceGuid));
            return new MsiClawInputStartResult(MsiClawInputStartStatus.Started, "M1/M2 DirectInput test is running.");
        }
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

    private SelectionResult SelectDevice(IDirectInputDeviceEnumerator enumerator, int testSession)
    {
        AppLog.Info("MsiInput", "DirectInput enumeration started.", ("TestSession", testSession));
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<DirectInputDeviceDescriptor> candidates;
        try
        {
            candidates = enumerator.EnumerateGameControllers();
        }
        catch (Exception exception)
        {
            AppLog.Warn("MsiInput", "DirectInput enumeration failed.", exception, ("TestSession", testSession), ("Reason", "EnumerationFailed"), ("Action", "AbortDiagnostic"), ("NoChangesMade", true));
            return new SelectionResult(MsiClawInputStartStatus.EnumerationFailed, "DirectInput device enumeration failed. No controller settings were changed.", null);
        }

        AppLog.Debug("MsiInput", "DirectInput enumeration completed.", ("TestSession", testSession), ("DeviceCount", candidates.Count), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        foreach (var candidate in candidates)
        {
            var matches = candidate.VendorId == MsiVendorId && candidate.ProductId == DirectInputProductId;
            AppLog.Trace("MsiInput", matches ? "DirectInput device candidate." : "DirectInput device ignored.", ("TestSession", testSession), ("InstanceGuid", candidate.InstanceGuid), ("ProductGuid", candidate.ProductGuid), ("ProductName", candidate.ProductName), ("VID", $"0x{candidate.VendorId:X4}"), ("PID", $"0x{candidate.ProductId:X4}"), ("DevicePath", candidate.DevicePath), ("PnpInstanceId", candidate.PnpInstanceId), ("PhysicalIdentity", candidate.PhysicalIdentity), ("UsagePage", candidate.UsagePage), ("Usage", candidate.Usage), ("ButtonCount", candidate.ButtonCount), ("AxisCount", candidate.AxisCount), ("MatchReason", matches ? "KnownMsiClawDirectInput" : "NotMsiClawPid1902"), ("SelectionReason", candidate.TopologyReason));
        }

        var selectedCandidates = candidates.Where(candidate => candidate.VendorId == MsiVendorId && candidate.ProductId == DirectInputProductId).ToArray();
        if (selectedCandidates.Length == 0) return NoCandidate(testSession);
        if (selectedCandidates.Any(candidate => !HasVerifiedIdentity(candidate))) return Indeterminate(selectedCandidates.Length, testSession, string.Join(',', selectedCandidates.Select(candidate => candidate.TopologyReason ?? "PhysicalIdentityUnverified").Distinct()));
        if (selectedCandidates.Any(candidate => candidate.ButtonCount is null or < RequiredButtonCount)) return Indeterminate(selectedCandidates.Length, testSession, "InsufficientButtonCount");
        var identities = selectedCandidates.Select(candidate => candidate.PhysicalIdentity!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (identities.Length != 1) return Indeterminate(selectedCandidates.Length, testSession, "MultiplePhysicalIdentities");
        var pnpInstanceIds = selectedCandidates.Select(candidate => candidate.PnpInstanceId!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (pnpInstanceIds.Length != 1) return Indeterminate(selectedCandidates.Length, testSession, "MultiplePnpInstanceIdentities");

        return Select(selectedCandidates.OrderBy(candidate => candidate.InstanceGuid).First(), testSession, selectedCandidates.Length == 1 ? "VerifiedMsiPhysicalRoot" : "VerifiedMsiPhysicalRootAlias");
    }

    private static bool HasVerifiedIdentity(DirectInputDeviceDescriptor descriptor) =>
        !string.IsNullOrWhiteSpace(descriptor.DevicePath) && !string.IsNullOrWhiteSpace(descriptor.PnpInstanceId) && !string.IsNullOrWhiteSpace(descriptor.PhysicalIdentity) && descriptor.UsagePage == 0x0001 && descriptor.Usage == 0x0005;

    private static SelectionResult Select(DirectInputDeviceDescriptor descriptor, int testSession, string reason)
    {
        AppLog.Info("MsiInput", "MSI Claw DirectInput device selected.", ("TestSession", testSession), ("VID", "0x0DB0"), ("PID", "0x1902"), ("InstanceGuid", descriptor.InstanceGuid), ("PnpInstanceId", descriptor.PnpInstanceId), ("PhysicalIdentity", descriptor.PhysicalIdentity), ("SelectionReason", reason));
        return new SelectionResult(MsiClawInputStartStatus.Started, "M1/M2 DirectInput test is running.", descriptor);
    }

    private static SelectionResult NoCandidate(int testSession)
    {
        AppLog.Info("Diagnostics", "M1/M2 input diagnostic was not started.", ("TestSession", testSession), ("Reason", "Pid1902NotFound"), ("Action", "NoChangesMade"));
        return new SelectionResult(MsiClawInputStartStatus.Pid1902NotFound, "DirectInput PID_1902 device not found. No changes were made.", null);
    }

    private static SelectionResult Indeterminate(int candidateCount, int testSession, string reason)
    {
        AppLog.Warn("MsiInput", "MSI Claw DirectInput candidate selection is indeterminate.", null, ("TestSession", testSession), ("CandidateCount", candidateCount), ("Reason", reason), ("Action", "DoNotAcquire"));
        return new SelectionResult(MsiClawInputStartStatus.Indeterminate, "MSI Claw PID_1902 DirectInput identity could not be verified. No changes were made.", null);
    }

    private async Task PollAsync(InputSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        var previous = new ControllerState(false, false);
        var hasPrevious = false;
        var m1Observed = false;
        var m2Observed = false;
        var m1OnlyObserved = false;
        var m2OnlyObserved = false;
        var independent = false;
        var readFailures = 0;
        var cleanupSucceeded = true;
        var stopReason = MsiClawInputStopReason.Stopped;

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
                    break;
                }

                if (!TryMapState(input, out var current))
                {
                    stopReason = MsiClawInputStopReason.InvalidButtonLayout;
                    AppLog.Warn("MsiInput", "DirectInput state layout is invalid.", null, ("TestSession", session.Id), ("ButtonCount", input.Buttons.Count), ("RequiredButtonCount", RequiredButtonCount), ("Action", "StopDiagnostic"), ("Reason", "InsufficientButtonCount"));
                    break;
                }

                if (!hasPrevious)
                {
                    AppLog.Trace("MsiInput", "Initial ControllerState.", ("TestSession", session.Id), ("M1", current.M1), ("M2", current.M2));
                    StateChanged?.Invoke(this, current);
                    previous = current;
                    hasPrevious = true;
                }
                else if (current != previous)
                {
                    LogStateChange(session.Id, previous, current);
                    StateChanged?.Invoke(this, current);
                    previous = current;
                }

                if (current.M1 && !m1Observed)
                {
                    m1Observed = true;
                    AppLog.Info("Diagnostics", "M1 input verified.", ("TestSession", session.Id), ("ButtonIndex", M1ButtonIndex));
                }

                if (current.M2 && !m2Observed)
                {
                    m2Observed = true;
                    AppLog.Info("Diagnostics", "M2 input verified.", ("TestSession", session.Id), ("ButtonIndex", M2ButtonIndex));
                }

                if (current.M1 && !current.M2) m1OnlyObserved = true;
                if (!current.M1 && current.M2) m2OnlyObserved = true;
                if (!independent && m1OnlyObserved && m2OnlyObserved)
                {
                    independent = true;
                    AppLog.Info("Diagnostics", "Independent M1/M2 input verified.", ("TestSession", session.Id), ("M1OnlyObserved", true), ("M2OnlyObserved", true), ("M1ButtonIndex", M1ButtonIndex), ("M2ButtonIndex", M2ButtonIndex));
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
        if (input.Buttons.Count < RequiredButtonCount)
        {
            state = default;
            return false;
        }

        state = new ControllerState(input.Buttons[M1ButtonIndex], input.Buttons[M2ButtonIndex]);
        return true;
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
        if (previous.M1 != current.M1)
        {
            AppLog.Trace("MsiInput", "M1 state changed.", ("TestSession", session), ("ButtonIndex", M1ButtonIndex), ("Previous", previous.M1), ("Current", current.M1));
        }
        if (previous.M2 != current.M2)
        {
            AppLog.Trace("MsiInput", "M2 state changed.", ("TestSession", session), ("ButtonIndex", M2ButtonIndex), ("Previous", previous.M2), ("Current", current.M2));
        }
        AppLog.Trace("MsiInput", "ControllerState changed.", ("TestSession", session), ("M1", $"{previous.M1}->{current.M1}"), ("M2", $"{previous.M2}->{current.M2}"));
    }

    private sealed class InputSession(int id, IDirectInputDeviceEnumerator enumerator, IDirectInputDevice device, CancellationTokenSource cancellation)
    {
        public int Id { get; } = id;
        public IDirectInputDeviceEnumerator Enumerator { get; } = enumerator;
        public IDirectInputDevice Device { get; } = device;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task? PollingTask { get; set; }
    }

    private sealed record SelectionResult(MsiClawInputStartStatus Status, string Message, DirectInputDeviceDescriptor? Descriptor);
}
