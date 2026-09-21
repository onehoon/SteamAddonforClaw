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
            return Task.FromResult(ShutdownResult);
        }
    }

    private sealed class FakeProcess : IClawHudProcessHandle
    {
        private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HasExited { get; set; }
        internal int ExitCode { get; set; }
        internal bool KillCalled { get; private set; }

        bool IClawHudProcessHandle.HasExited => HasExited;
        int IClawHudProcessHandle.ExitCode => ExitCode;
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
            HasExited ? Task.CompletedTask : _exited.Task.WaitAsync(cancellationToken);
        public void Kill(bool entireProcessTree) { KillCalled = true; HasExited = true; _exited.TrySetResult(true); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
