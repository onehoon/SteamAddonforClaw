using SteamInputAddonforClaw.ClawHud;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawHudProcessControllerTests
{
    [Fact]
    public async Task EnsureRunning_LaunchesExactManagedRuntime_AndConvergesHudEnabled()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess();
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(false),
            EnabledSnapshot = Snapshot(true),
        };
        ClawHudProcessLaunchRequest? launchRequest = null;
        var controller = new ClawHudProcessController(control, request =>
        {
            launchRequest = request;
            return process;
        });

        var state = await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        Assert.True(state.ActualState == ClawHudFeatureState.Ready, state.Failure);
        Assert.True(control.SetHudEnabledCalled);
        Assert.NotNull(launchRequest);
        Assert.Equal(Path.Combine(runtime.RuntimeDirectory!, "ClawHUD.exe"), launchRequest!.ExecutablePath);
        Assert.Equal(runtime.RuntimeDirectory, launchRequest.WorkingDirectory);
        Assert.Equal(["--managed"], launchRequest.Arguments);
        Assert.False(launchRequest.UseShellExecute);
    }

    [Fact]
    public async Task EnsureRunning_AdoptsSurvivorWhenTheLosingChildExitsDuringSingleInstanceSettle()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess
        {
            ExitOnFirstWait = true,
            ExitCodeAfterWait = (int)ClawHudManagedStartupExitCode.AlreadyRunning,
        };
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
        };
        var controller = new ClawHudProcessController(control, _ => process);

        var state = await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Ready, state.ActualState);
        Assert.Equal(ClawHudFeatureState.Ready, controller.State.ActualState);
        Assert.True(process.DisposeCalled);
        Assert.False(process.KillCalled);
        Assert.Equal(0, control.RequestShutdownCalls);
    }

    [Fact]
    public async Task EnsureRunning_AdoptsExactManagedSurvivorAfterAlreadyRunning()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess { HasExited = true, ExitCode = 20 };
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
        };
        var controller = new ClawHudProcessController(control, _ => process);

        var state = await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        Assert.True(state.ActualState == ClawHudFeatureState.Ready, state.Failure);
        Assert.Equal(0, control.RequestShutdownCalls);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task EnsureRunning_IsIdempotentForAnAlreadyReadyProvenChild()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess();
        var launches = 0;
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
            OnRequestShutdown = process.CompleteExitForTest,
        };
        var controller = new ClawHudProcessController(control, _ =>
        {
            launches++;
            return process;
        });

        var first = await controller.EnsureRunningAsync(runtime, CancellationToken.None);
        var second = await controller.EnsureRunningAsync(runtime, CancellationToken.None);
        var stopped = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Ready, first.ActualState);
        Assert.Equal(ClawHudFeatureState.Ready, second.ActualState);
        Assert.Equal(1, launches);
        Assert.Equal(ClawHudFeatureState.Disabled, stopped.ActualState);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public async Task Stop_DoesNotShutdownStandaloneThatReplacedAnAdoptedManagedRuntime()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess { HasExited = true, ExitCode = (int)ClawHudManagedStartupExitCode.AlreadyRunning };
        var control = new FakeControl
        {
            RuntimeInfos = new Queue<ClawHudRuntimeInfo?>([
                new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
                new("9.9.9", 1, 1, ClawHudWireLaunchMode.Standalone, ClawHudWireRuntimeState.Ready),
            ]),
            Snapshot = Snapshot(true),
        };
        var controller = new ClawHudProcessController(control, _ => process);

        var ready = await controller.EnsureRunningAsync(runtime, CancellationToken.None);
        var stopped = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Ready, ready.ActualState);
        Assert.Equal(ClawHudFeatureState.StandaloneConflict, stopped.ActualState);
        Assert.Equal(0, control.RequestShutdownCalls);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task EnsureRunning_DoesNotTouchStandaloneConflict()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess { HasExited = true, ExitCode = 20 };
        var control = new FakeControl
        {
            RuntimeInfo = new("9.9.9", 1, 1, ClawHudWireLaunchMode.Standalone, ClawHudWireRuntimeState.Ready),
        };
        var controller = new ClawHudProcessController(control, _ => process);

        var state = await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.StandaloneConflict, state.ActualState);
        Assert.Equal(0, control.RequestShutdownCalls);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task EnsureRunning_ReplacesCompatibleManagedVersionMismatch()
    {
        var runtime = Runtime("1.0.1");
        var first = new FakeProcess { HasExited = true, ExitCode = 20 };
        var second = new FakeProcess();
        var control = new FakeControl
        {
            RuntimeInfos = new Queue<ClawHudRuntimeInfo?>([
                new("1.0.0", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
                null,
                new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            ]),
            Snapshot = Snapshot(true),
        };
        var launches = 0;
        var controller = new ClawHudProcessController(control, _ => ++launches == 1 ? first : second,
            (_, _) => Task.CompletedTask);

        var state = await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        Assert.True(state.ActualState == ClawHudFeatureState.Ready, state.Failure);
        Assert.Equal(2, launches);
        Assert.Equal(1, control.RequestShutdownCalls);
    }

    [Theory]
    [InlineData((int)ClawHudManagedStartupExitCode.UnsupportedHardware, "StartupExit:UnsupportedHardware")]
    [InlineData((int)ClawHudManagedStartupExitCode.HardwareIndeterminate, "StartupExit:HardwareIndeterminate")]
    [InlineData((int)ClawHudManagedStartupExitCode.PresentMonRebootRequired, "StartupExit:PresentMonRebootRequired")]
    [InlineData((int)ClawHudManagedStartupExitCode.PresentMonElevationCancelled, "StartupExit:PresentMonElevationCancelled")]
    [InlineData((int)ClawHudManagedStartupExitCode.PresentMonMsiMissing, "StartupExit:PresentMonMsiMissing")]
    [InlineData((int)ClawHudManagedStartupExitCode.PresentMonInstallTimedOut, "StartupExit:PresentMonInstallTimedOut")]
    [InlineData((int)ClawHudManagedStartupExitCode.PresentMonInstallFailed, "StartupExit:PresentMonInstallFailed")]
    [InlineData((int)ClawHudManagedStartupExitCode.PresentMonValidationFailed, "StartupExit:PresentMonValidationFailed")]
    [InlineData((int)ClawHudManagedStartupExitCode.RuntimeInitializationFailed, "StartupExit:RuntimeInitializationFailed")]
    [InlineData((int)ClawHudManagedStartupExitCode.ControlIpcUnavailable, "StartupExit:ControlIpcUnavailable")]
    public async Task EnsureRunning_ManagedStartupExitCodesRemainFeatureLocal(int exitCode, string expectedFailure)
    {
        var process = new FakeProcess { HasExited = true, ExitCode = exitCode };
        var control = new FakeControl();
        var controller = new ClawHudProcessController(control, _ => process);

        var state = await controller.EnsureRunningAsync(Runtime("1.0.1"), CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Unavailable, state.ActualState);
        Assert.True(state.DesiredEnabled);
        Assert.Equal(expectedFailure, state.Failure);
        Assert.Equal(0, control.RequestShutdownCalls);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task EnsureRunning_DisposesExitedStartupChildBeforeReturningFailure()
    {
        var process = new FakeProcess
        {
            HasExited = true,
            ExitCode = (int)ClawHudManagedStartupExitCode.PresentMonElevationCancelled,
        };
        var controller = new ClawHudProcessController(new FakeControl(), _ => process);

        var state = await controller.EnsureRunningAsync(Runtime("1.0.1"), CancellationToken.None);
        var stopped = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Unavailable, state.ActualState);
        Assert.Equal("StartupExit:PresentMonElevationCancelled", state.Failure);
        Assert.True(process.DisposeCalled);
        Assert.Equal(ClawHudFeatureState.Disabled, stopped.ActualState);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task Stop_AdoptedManagedRuntimeDoesNotForceKillWhenGracefulShutdownCannotBeConfirmed()
    {
        var process = new FakeProcess { HasExited = true, ExitCode = 20 };
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
            ShutdownResult = ClawHudControlResult<ClawHudUnit>.Timeout,
        };
        var controller = new ClawHudProcessController(control, _ => process);

        var ready = await controller.EnsureRunningAsync(Runtime("1.0.1"), CancellationToken.None);
        var stopped = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Ready, ready.ActualState);
        Assert.Equal(ClawHudFeatureState.Unavailable, stopped.ActualState);
        Assert.False(process.KillCalled);
    }

    [Fact]
    public async Task Stop_OnlyKillsProvenChildAfterGracefulShutdownFailure()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess();
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
            ShutdownResult = ClawHudControlResult<ClawHudUnit>.Timeout,
        };
        var controller = new ClawHudProcessController(control, _ => process);
        await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        var state = await controller.StopAsync(false, CancellationToken.None);

        Assert.True(state.ActualState == ClawHudFeatureState.Unavailable, state.Failure);
        Assert.True(process.KillCalled);
        Assert.True(process.DisposeCalled);
    }

    [Fact]
    public async Task Stop_DisposesProvenChildAfterGracefulShutdown()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess { ExitOnThirdWait = true };
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
        };
        var controller = new ClawHudProcessController(control, _ => process);
        await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        var state = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Disabled, state.ActualState);
        Assert.False(process.KillCalled);
        Assert.True(process.DisposeCalled);
    }

    [Fact]
    public async Task Stop_OwnsRetirementWhenShutdownCausesObservedChildExit()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess();
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
            OnRequestShutdown = process.CompleteExitForTest,
        };
        var controller = new ClawHudProcessController(control, _ => process);
        await controller.EnsureRunningAsync(runtime, CancellationToken.None);

        var state = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Disabled, state.ActualState);
        Assert.Equal(1, process.DisposeCalls);
    }

    [Fact]
    public async Task UnexpectedManagedChildExitDisposesProvenChildAfterRecordingUnavailableState()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess { ExitOnSecondWait = true, ExitCodeAfterWait = 123 };
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = Snapshot(true),
        };
        var controller = new ClawHudProcessController(control, _ => process);

        await controller.EnsureRunningAsync(runtime, CancellationToken.None);
        await process.DisposedTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(ClawHudFeatureState.Unavailable, controller.State.ActualState);
        Assert.Equal("ManagedChildExited:123", controller.State.Failure);
        Assert.True(process.DisposeCalled);
    }

    [Fact]
    public async Task Stop_RequestsGracefulShutdownAfterManagedClassificationWhenReadinessFails()
    {
        var runtime = Runtime("1.0.1");
        var process = new FakeProcess { ExitOnSecondWait = true };
        var control = new FakeControl
        {
            RuntimeInfo = new("1.0.1", 1, 1, ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            Snapshot = null,
        };
        var controller = new ClawHudProcessController(control, _ => process);

        var startup = await controller.EnsureRunningAsync(runtime, CancellationToken.None);
        var stopped = await controller.StopAsync(false, CancellationToken.None);

        Assert.Equal(ClawHudFeatureState.Unavailable, startup.ActualState);
        Assert.Equal(1, control.RequestShutdownCalls);
        Assert.Equal(ClawHudFeatureState.Disabled, stopped.ActualState);
        Assert.False(process.KillCalled);
    }

    private static ClawHudRuntimeAcquisitionResult Runtime(string version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClawHud.Tests", Guid.NewGuid().ToString("N"));
        return new(true, version, directory, Path.Combine(directory, "ClawHUD.exe"), ClawHudRuntimeAcquisitionFailure.None, null);
    }

    private static ClawHudSettingsSnapshot Snapshot(bool enabled) => new(
        false, enabled, 0, ClawHudWireFont.Unispace, ClawHudWireVisibilityMode.Always,
        ClawHudWireAlignment.Left, ClawHudWireBackgroundMode.FullWidth, 100, false, null);

    private sealed class FakeControl : IClawHudControlClient
    {
        internal ClawHudRuntimeInfo? RuntimeInfo { get; init; }
        internal Queue<ClawHudRuntimeInfo?>? RuntimeInfos { get; init; }
        internal ClawHudSettingsSnapshot? Snapshot { get; init; }
        internal ClawHudSettingsSnapshot? EnabledSnapshot { get; init; }
        internal ClawHudControlResult<ClawHudUnit> ShutdownResult { get; init; } = ClawHudControlResult<ClawHudUnit>.Success(new());
        internal Action? OnRequestShutdown { get; init; }
        internal int RequestShutdownCalls { get; private set; }
        internal bool SetHudEnabledCalled { get; private set; }

        public Task<ClawHudControlResult<ClawHudRuntimeInfo>> GetRuntimeInfoAsync(CancellationToken cancellationToken = default)
        {
            if (RuntimeInfos is { Count: > 0 })
            {
                var value = RuntimeInfos.Dequeue();
                return Task.FromResult(value is null
                    ? ClawHudControlResult<ClawHudRuntimeInfo>.Transport
                    : ClawHudControlResult<ClawHudRuntimeInfo>.Success(value));
            }
            return Task.FromResult(RuntimeInfo is null
                ? ClawHudControlResult<ClawHudRuntimeInfo>.Transport
                : ClawHudControlResult<ClawHudRuntimeInfo>.Success(RuntimeInfo));
        }

        public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot is null ? ClawHudControlResult<ClawHudSettingsSnapshot>.Transport : ClawHudControlResult<ClawHudSettingsSnapshot>.Success(Snapshot));

        public Task<ClawHudControlResult<ClawHudSettingsSnapshot>> SetHudEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            SetHudEnabledCalled = true;
            return Task.FromResult(EnabledSnapshot is null ? ClawHudControlResult<ClawHudSettingsSnapshot>.Transport : ClawHudControlResult<ClawHudSettingsSnapshot>.Success(EnabledSnapshot));
        }

        public Task<ClawHudControlResult<ClawHudUnit>> RequestShutdownAsync(CancellationToken cancellationToken = default)
        {
            RequestShutdownCalls++;
            OnRequestShutdown?.Invoke();
            return Task.FromResult(ShutdownResult);
        }
    }

    private sealed class FakeProcess : IClawHudProcessHandle
    {
        private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waitCalls;
        internal bool HasExited { get; set; }
        internal int ExitCode { get; set; }
        internal bool ExitOnFirstWait { get; init; }
        internal int ExitCodeAfterWait { get; init; }
        internal bool ExitOnSecondWait { get; init; }
        internal bool ExitOnThirdWait { get; init; }
        internal bool KillCalled { get; private set; }
        internal bool DisposeCalled { get; private set; }
        internal int DisposeCalls { get; private set; }
        internal Task DisposedTask => _disposed.Task;

        private readonly TaskCompletionSource<bool> _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        bool IClawHudProcessHandle.HasExited => HasExited;
        int IClawHudProcessHandle.ExitCode => ExitCode;
        public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            if (HasExited) return Task.CompletedTask;
            var waitCall = Interlocked.Increment(ref _waitCalls);
            if (waitCall == 1 && ExitOnFirstWait)
                CompleteExit(ExitCodeAfterWait);
            else if (waitCall == 2 && ExitOnSecondWait)
                CompleteExit(ExitCodeAfterWait);
            else if (waitCall == 3 && ExitOnThirdWait)
                CompleteExit(ExitCodeAfterWait);
            return _exited.Task.WaitAsync(cancellationToken);
        }
        public void Kill(bool entireProcessTree) { KillCalled = true; CompleteExit(0); }
        public ValueTask DisposeAsync() { DisposeCalled = true; DisposeCalls++; _disposed.TrySetResult(true); return ValueTask.CompletedTask; }

        internal void CompleteExitForTest() => CompleteExit(0);

        private void CompleteExit(int exitCode)
        {
            ExitCode = exitCode;
            HasExited = true;
            _exited.TrySetResult(true);
        }
    }
}
