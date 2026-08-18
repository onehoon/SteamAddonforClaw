using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiRoutingRetirementTests
{
    private const int Pid = 4242;
    private const string ExpectedPath = @"C:\Program Files\WindowsApps\9426MICRO-STARINTERNATION.64797CC12EF8E_3.0.60630.0_x64__kzh8wxbdkxb8p\MSI Center M\MSI Center M.exe";

    [Fact]
    public async Task No_real_mainui_present_skips_retirement()
    {
        var (retirement, invoker, windowController) = Create(new QueueProcessSnapshotSource([]));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.NoMainUiPresent, result);
        Assert.Equal(0, invoker.TerminateCallCount);
        Assert.Equal(0, windowController.CallCount);
    }

    [Fact]
    public async Task Process_enumeration_uncertain_fails_closed()
    {
        var (retirement, invoker, _) = Create(new QueueProcessSnapshotSource([null]));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityUncertain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Multiple_same_name_candidates_terminates_none()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [
                new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath),
                new ProcessSnapshotEntry(Pid + 1, "MSI Center M", ExpectedPath)
            ]
        ]);
        var (retirement, invoker, _) = Create(snapshots);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MultipleCandidates, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Candidate_package_path_mismatch_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", @"C:\evil\MSI Center M.exe")]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Candidate_retained_handle_identity_uncertain_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Uncertain, null, null, null)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityUncertain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_tray_resident_mainui_with_xinput_is_retired_without_minimizing()
    {
        // The primary regression test for the hardware failure: a real MainUI that has lived only
        // in the tray (never observed visible by this process) must still be a valid routing
        // retirement candidate.
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator's fresh same-name recheck
            [] // fresh absence after termination
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.Retired, result);
        Assert.Equal(0, windowController.CallCount);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_with_directinput_native_state_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.NotXInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.XInputNotConfirmed, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_with_uncertain_native_state_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.Uncertain));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.XInputNotConfirmed, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_without_terminate_rights_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            handleOpener: new FakeHandleOpener(grantTerminateRights: false));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.AccessDenied, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_exits_naturally_before_termination_cleanly_continues()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [] // fresh absence check
        ]);
        // The upfront identity check observes it still alive; the terminator's own fresh capture
        // (immediately before termination) is what discovers the natural exit.
        var identity = new QueueIdentityInspector(
        [
            new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath),
            new LiveProcessIdentity(LiveProcessProbeStatus.Exited, null, null, null)
        ]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.Retired, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_minimizes_waits_hidden_confirms_xinput_then_terminates()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator recheck
            [] // fresh absence
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 1), // upfront: visible
            new MainUiWindowSnapshot(true, 1, 0), // minimize-wait loop observes hidden
            new MainUiWindowSnapshot(true, 1, 0) // terminator's fresh recheck
        ]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.Retired, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_minimize_command_failure_does_not_terminate()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 1)]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            minimizeResult: CenterMMainUiMinimizeResult.AmbiguousVisibleWindows);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MinimizeFailed, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_that_never_becomes_hidden_times_out_without_terminating()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 1)]); // stays visible forever
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            minimizeWaitTimeout: TimeSpan.FromMilliseconds(60), minimizeWaitPollInterval: TimeSpan.FromMilliseconds(15));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MinimizeTimedOut, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Window_visible_again_immediately_before_termination_blocks_the_kill()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 0), // upfront: tray/hidden
            new MainUiWindowSnapshot(true, 1, 1) // terminator's fresh recheck: visible again (race C)
        ]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Termination_wait_timeout_does_not_report_retired()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput), waitForExitSucceeds: false);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.TerminationTimedOut, result);
    }

    [Fact]
    public async Task Another_mainui_present_after_termination_rejects_stale_success()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator recheck
            [new ProcessSnapshotEntry(Pid + 1, "MSI Center M", ExpectedPath)] // a NEW real MainUI after retirement
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MultipleCandidates, result);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    private static (CenterMMainUiRoutingRetirement Retirement, RecordingInvoker Invoker, FixedWindowController WindowController) Create(
        QueueProcessSnapshotSource snapshotSource,
        QueueIdentityInspector? identityInspector = null,
        QueueWindowSnapshotProvider? windowSnapshotProvider = null,
        ICenterMNativeModeProbe? nativeModeProbe = null,
        CenterMMainUiMinimizeResult minimizeResult = CenterMMainUiMinimizeResult.Requested,
        IProcessHandleOpener? handleOpener = null,
        TimeSpan? minimizeWaitTimeout = null,
        TimeSpan? minimizeWaitPollInterval = null,
        TimeSpan? xInputWaitTimeout = null,
        TimeSpan? xInputWaitPollInterval = null,
        bool waitForExitSucceeds = true)
    {
        var identity = identityInspector ?? new QueueIdentityInspector([]);
        var window = windowSnapshotProvider ?? new QueueWindowSnapshotProvider([]);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: waitForExitSucceeds);
        var terminator = new CenterMMainUiRoutingTerminator(invoker, identity, window, snapshotSource);
        var windowController = new FixedWindowController(minimizeResult);
        var retirement = new CenterMMainUiRoutingRetirement(
            nativeModeProbe ?? new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            processSnapshotSource: snapshotSource,
            handleOpener: handleOpener ?? new FakeHandleOpener(),
            identityInspector: identity,
            windowSnapshotProvider: window,
            windowController: windowController,
            terminator: terminator,
            minimizeWaitTimeout: minimizeWaitTimeout ?? TimeSpan.FromMilliseconds(200),
            minimizeWaitPollInterval: minimizeWaitPollInterval ?? TimeSpan.FromMilliseconds(20),
            xInputWaitTimeout: xInputWaitTimeout ?? TimeSpan.FromMilliseconds(200),
            xInputWaitPollInterval: xInputWaitPollInterval ?? TimeSpan.FromMilliseconds(20),
            terminateWaitTimeout: TimeSpan.FromMilliseconds(50));
        return (retirement, invoker, windowController);
    }

    private sealed class QueueProcessSnapshotSource(IEnumerable<IReadOnlyList<ProcessSnapshotEntry>?> responses) : IProcessSnapshotSource
    {
        private readonly Queue<IReadOnlyList<ProcessSnapshotEntry>?> _queue = new(responses);
        private IReadOnlyList<ProcessSnapshotEntry>? _last = [];

        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class QueueIdentityInspector(IEnumerable<LiveProcessIdentity> responses) : IProcessIdentityInspector
    {
        private readonly Queue<LiveProcessIdentity> _queue = new(responses);
        private LiveProcessIdentity _last;

        public LiveProcessIdentity Inspect(SafeProcessHandle handle)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class QueueWindowSnapshotProvider(IEnumerable<MainUiWindowSnapshot?> responses) : IMainUiWindowSnapshotProvider
    {
        private readonly Queue<MainUiWindowSnapshot?> _queue = new(responses);
        private MainUiWindowSnapshot? _last;

        public MainUiWindowSnapshot? Capture(int processId)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class FixedNativeModeProbe(CenterMNativeModeProbeResult result) : ICenterMNativeModeProbe
    {
        public Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FixedWindowController(CenterMMainUiMinimizeResult result) : ICenterMMainUiWindowController
    {
        internal int CallCount { get; private set; }
        public CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class RecordingInvoker(bool terminateSucceeds, bool waitSucceeds) : ITerminateProcessInvoker
    {
        internal int TerminateCallCount { get; private set; }

        public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
        {
            TerminateCallCount++;
            win32Error = terminateSucceeds ? 0 : 5;
            return terminateSucceeds;
        }

        public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout) => waitSucceeds;
    }

    /// <summary>Opens a REAL (non-pseudo) handle to this test process itself, via
    /// <c>OpenProcess</c> against the actual current PID -- the <c>GetCurrentProcess()</c>
    /// pseudo-handle (-1) trips <see cref="SafeProcessHandle"/>'s own IsInvalid check when routed
    /// through the production <see cref="TrackedCenterMMainUi.Create"/> path (unlike
    /// <see cref="TrackedCenterMMainUi.CreateForTesting"/>, which bypasses that check entirely).</summary>
    private sealed class FakeHandleOpener(bool grantTerminateRights = true) : IProcessHandleOpener
    {
        private const uint PROCESS_TERMINATE = 0x0001;

        public SafeProcessHandle Open(int processId, uint desiredAccess)
        {
            if (!grantTerminateRights && (desiredAccess & PROCESS_TERMINATE) != 0)
                return new SafeProcessHandle(IntPtr.Zero, false);
            return OpenProcess(desiredAccess, false, Environment.ProcessId);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
    }
}
