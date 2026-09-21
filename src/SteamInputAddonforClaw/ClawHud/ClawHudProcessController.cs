using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.ClawHud;

internal enum ClawHudFeatureState
{
    Disabled,
    Starting,
    Ready,
    Unavailable,
    StandaloneConflict,
}

internal sealed record ClawHudState(
    bool DesiredEnabled,
    ClawHudFeatureState ActualState,
    string? Failure = null,
    string? RuntimeVersion = null,
    string? ApplicationVersion = null);

internal sealed record ClawHudProcessLaunchRequest(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    bool UseShellExecute);

internal interface IClawHudProcessHandle : IAsyncDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
    void Kill(bool entireProcessTree);
}

internal sealed class ClawHudProcessController : IAsyncDisposable
{
    private const string Category = "ClawHUD.Process";
    private const int AlreadyRunningExitCode = (int)ClawHudManagedStartupExitCode.AlreadyRunning;
    private static readonly TimeSpan ReadinessBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadinessRetryInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(5);

    private readonly IClawHudControlClient _controlClient;
    private readonly Func<ClawHudProcessLaunchRequest, IClawHudProcessHandle> _launch;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IClawHudProcessHandle? _ownedChild;
    private int _stopping;
    private int _disposed;
    private bool _managedReady;

    internal ClawHudProcessController(
        IClawHudControlClient? controlClient = null,
        Func<ClawHudProcessLaunchRequest, IClawHudProcessHandle>? launch = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _controlClient = controlClient ?? new ClawHudControlClient();
        _launch = launch ?? LaunchProcess;
        _delay = delay ?? Task.Delay;
        State = new(false, ClawHudFeatureState.Disabled);
    }

    internal ClawHudState State { get; private set; }

    internal async Task<ClawHudState> EnsureRunningAsync(
        ClawHudRuntimeAcquisitionResult runtime,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Volatile.Write(ref _stopping, 0);
            State = new(true, ClawHudFeatureState.Starting, RuntimeVersion: runtime.RuntimeVersion);
            if (!runtime.IsReady || runtime.RuntimeDirectory is null || runtime.ExecutablePath is null)
                return Unavailable(runtime.RuntimeVersion, runtime.Failure.ToString());

            return await EnsureRunningCoreAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Unavailable(runtime.RuntimeVersion, "Canceled");
        }
        catch (Exception exception)
        {
            AppLog.Warn(Category, "Managed ClawHUD startup failed; controller remains feature-local.", exception,
                ("RuntimeVersion", runtime.RuntimeVersion), ("Failure", exception.GetType().Name));
            return Unavailable(runtime.RuntimeVersion, exception.GetType().Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal ClawHudState RecordAcquisitionFailure(ClawHudRuntimeAcquisitionResult runtime)
    {
        State = new(true, ClawHudFeatureState.Unavailable,
            runtime.FailureMessage ?? runtime.Failure.ToString(), runtime.RuntimeVersion);
        return State;
    }

    internal async Task<ClawHudState> StopAsync(bool desiredEnabled, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Volatile.Write(ref _stopping, 1);
            var child = _ownedChild;
            var failure = (string?)null;

            if (_managedReady)
            {
                var shutdown = await _controlClient.RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
                if (!shutdown.Succeeded)
                {
                    failure = Describe(shutdown);
                }
                else if (child is null)
                {
                    if (!await WaitForEndpointDisappearanceAsync(cancellationToken).ConfigureAwait(false))
                        failure = "ManagedShutdownNotConfirmed";
                }
            }

            if (child is not null && !child.HasExited)
            {
                if (failure is null)
                {
                    using var shutdownSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    shutdownSource.CancelAfter(ShutdownBudget);
                    try { await child.WaitForExitAsync(shutdownSource.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { failure ??= "ManagedShutdownTimedOut"; }
                }

                if (!child.HasExited)
                {
                    try
                    {
                        child.Kill(entireProcessTree: true);
                        AppLog.Warn(Category, "Managed ClawHUD graceful shutdown failed; proven child was terminated.", null,
                            ("Event", "ManagedShutdownFallbackKill"));
                    }
                    catch (Exception exception)
                    {
                        failure ??= $"ManagedShutdownKillFailed:{exception.GetType().Name}";
                    }
                }
            }

            _ownedChild = null;
            _managedReady = false;
            State = failure is null
                ? new(desiredEnabled, ClawHudFeatureState.Disabled)
                : new(desiredEnabled, ClawHudFeatureState.Unavailable, failure);
            return State;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = new(desiredEnabled, ClawHudFeatureState.Unavailable, "Canceled");
            return State;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var child = _ownedChild;
        _ownedChild = null;
        if (child is not null)
        {
            try { await child.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { AppLog.Warn(Category, "Managed child handle disposal failed.", exception); }
        }
        _gate.Dispose();
    }

    private async Task<ClawHudState> EnsureRunningCoreAsync(
        ClawHudRuntimeAcquisitionResult runtime,
        CancellationToken cancellationToken)
    {
        var child = Start(runtime);
        _ownedChild = child;
        var info = await WaitForRuntimeInfoAsync(child, cancellationToken).ConfigureAwait(false);
        if (info is not null)
        {
            if (child.HasExited)
            {
                await child.DisposeAsync().ConfigureAwait(false);
                _ownedChild = null;
                return await HandleClassifiedRuntimeInfoAsync(runtime, info, cancellationToken).ConfigureAwait(false);
            }

            if (info.LaunchMode == ClawHudWireLaunchMode.Standalone)
            {
                await child.DisposeAsync().ConfigureAwait(false);
                _ownedChild = null;
                return new(true, ClawHudFeatureState.StandaloneConflict, "StandaloneConflict", runtime.RuntimeVersion, info.ApplicationVersion);
            }

            if (!string.Equals(info.ApplicationVersion, runtime.RuntimeVersion, StringComparison.Ordinal))
            {
                await child.DisposeAsync().ConfigureAwait(false);
                _ownedChild = null;
                return await ReplaceMismatchedManagedRuntimeAsync(runtime, info, cancellationToken).ConfigureAwait(false);
            }

            return await CompleteReadyAsync(runtime, info, child, cancellationToken).ConfigureAwait(false);
        }

        var exitCode = child.HasExited ? child.ExitCode : (int?)null;
        if (exitCode != AlreadyRunningExitCode)
            return Unavailable(runtime.RuntimeVersion, exitCode is { } code ? DescribeStartupExit(code) : "ControlIpcUnavailable");

        await child.DisposeAsync().ConfigureAwait(false);
        _ownedChild = null;
        return await ClassifyExistingInstanceAsync(runtime, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClawHudRuntimeInfo?> WaitForRuntimeInfoAsync(IClawHudProcessHandle child, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ReadinessBudget;
        while (DateTime.UtcNow < deadline)
        {
            var result = await _controlClient.GetRuntimeInfoAsync(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && result.Value is { } info
                && (info.LaunchMode == ClawHudWireLaunchMode.Standalone
                    || info.RuntimeState == ClawHudWireRuntimeState.Ready))
                return info;
            if (child.HasExited) return null;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            await _delay(remaining < ReadinessRetryInterval ? remaining : ReadinessRetryInterval, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private async Task<ClawHudState> ClassifyExistingInstanceAsync(
        ClawHudRuntimeAcquisitionResult runtime,
        CancellationToken cancellationToken)
    {
        var result = await _controlClient.GetRuntimeInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
            return Unavailable(runtime.RuntimeVersion, Describe(result));

        var info = result.Value;
        AppLog.Info(Category, "Existing ClawHUD instance classified.",
            ("Event", "ExistingInstanceClassified"), ("LaunchMode", info.LaunchMode), ("RuntimeState", info.RuntimeState),
            ("ApplicationVersion", info.ApplicationVersion));
        return await HandleClassifiedRuntimeInfoAsync(runtime, info, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClawHudState> HandleClassifiedRuntimeInfoAsync(
        ClawHudRuntimeAcquisitionResult runtime,
        ClawHudRuntimeInfo info,
        CancellationToken cancellationToken)
    {
        if (info.LaunchMode == ClawHudWireLaunchMode.Standalone)
            return new(true, ClawHudFeatureState.StandaloneConflict, "StandaloneConflict", runtime.RuntimeVersion, info.ApplicationVersion);
        if (!IsProtocolCompatible(info) || info.RuntimeState != ClawHudWireRuntimeState.Ready)
            return Unavailable(runtime.RuntimeVersion, "ExistingManagedRuntimeNotReady", info.ApplicationVersion);
        if (!string.Equals(info.ApplicationVersion, runtime.RuntimeVersion, StringComparison.Ordinal))
            return await ReplaceMismatchedManagedRuntimeAsync(runtime, info, cancellationToken).ConfigureAwait(false);
        return await CompleteReadyAsync(runtime, info, child: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClawHudState> ReplaceMismatchedManagedRuntimeAsync(
        ClawHudRuntimeAcquisitionResult runtime,
        ClawHudRuntimeInfo info,
        CancellationToken cancellationToken)
    {
        var shutdown = await _controlClient.RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
        if (!shutdown.Succeeded)
            return Unavailable(runtime.RuntimeVersion, $"ManagedVersionMismatchShutdown:{Describe(shutdown)}", info.ApplicationVersion);
        if (!await WaitForEndpointDisappearanceAsync(cancellationToken).ConfigureAwait(false))
            return Unavailable(runtime.RuntimeVersion, "ManagedVersionMismatchShutdownNotConfirmed", info.ApplicationVersion);
        return await EnsureRunningCoreAsync(runtime, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClawHudState> CompleteReadyAsync(
        ClawHudRuntimeAcquisitionResult runtime,
        ClawHudRuntimeInfo info,
        IClawHudProcessHandle? child,
        CancellationToken cancellationToken)
    {
        if (info.LaunchMode != ClawHudWireLaunchMode.Managed || info.RuntimeState != ClawHudWireRuntimeState.Ready
            || !IsProtocolCompatible(info) || !string.Equals(info.ApplicationVersion, runtime.RuntimeVersion, StringComparison.Ordinal))
            return Unavailable(runtime.RuntimeVersion, "ManagedReadinessContractFailed", info.ApplicationVersion);

        var snapshot = await _controlClient.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Succeeded || snapshot.Value is null)
            return Unavailable(runtime.RuntimeVersion, $"SettingsSnapshot:{Describe(snapshot)}", info.ApplicationVersion);
        if (!snapshot.Value.HudEnabled)
        {
            var enabled = await _controlClient.SetHudEnabledAsync(true, cancellationToken).ConfigureAwait(false);
            if (!enabled.Succeeded || enabled.Value?.HudEnabled != true)
                return Unavailable(runtime.RuntimeVersion, $"HudEnable:{Describe(enabled)}", info.ApplicationVersion);
        }

        _ownedChild = child;
        _managedReady = true;
        State = new(true, ClawHudFeatureState.Ready, RuntimeVersion: runtime.RuntimeVersion, ApplicationVersion: info.ApplicationVersion);
        AppLog.Info(Category, "Managed ClawHUD control is ready.",
            ("Event", "ControlReady"), ("RuntimeVersion", runtime.RuntimeVersion), ("ApplicationVersion", info.ApplicationVersion),
            ("PID", child is null ? null : "ProvenChild"));
        if (child is not null)
            _ = ObserveChildExitAsync(child, runtime.RuntimeVersion);
        return State;
    }

    private async Task ObserveChildExitAsync(IClawHudProcessHandle child, string runtimeVersion)
    {
        try { await child.WaitForExitAsync().ConfigureAwait(false); }
        catch (Exception exception)
        {
            AppLog.Warn(Category, "Managed child exit observation failed.", exception);
            return;
        }
        if (ReferenceEquals(_ownedChild, child))
        {
            _ownedChild = null;
            _managedReady = false;
            if (Volatile.Read(ref _stopping) == 0)
            {
                State = new(true, ClawHudFeatureState.Unavailable, $"ManagedChildExited:{child.ExitCode}", runtimeVersion);
                AppLog.Warn(Category, "Managed ClawHUD child exited unexpectedly.", null,
                    ("Event", "ManagedRuntimeUnavailable"), ("ExitCode", child.ExitCode), ("RuntimeVersion", runtimeVersion));
            }
        }
    }

    private async Task<bool> WaitForEndpointDisappearanceAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ShutdownBudget;
        while (DateTime.UtcNow < deadline)
        {
            var probe = await _controlClient.GetRuntimeInfoAsync(cancellationToken).ConfigureAwait(false);
            if (!probe.Succeeded) return true;
            var remaining = deadline - DateTime.UtcNow;
            await _delay(remaining < ReadinessRetryInterval ? remaining : ReadinessRetryInterval, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private IClawHudProcessHandle Start(ClawHudRuntimeAcquisitionResult runtime)
    {
        if (!string.Equals(Path.GetFileName(runtime.ExecutablePath), "ClawHUD.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The acquired ClawHUD executable is not ClawHUD.exe.");
        return _launch(new(runtime.ExecutablePath!, runtime.RuntimeDirectory!, ["--managed"], UseShellExecute: false));
    }

    private ClawHudState Unavailable(string runtimeVersion, string? failure, string? applicationVersion = null)
    {
        State = new(true, ClawHudFeatureState.Unavailable, failure, runtimeVersion, applicationVersion);
        AppLog.Warn(Category, "Managed ClawHUD is unavailable.", null,
            ("Event", "ManagedRuntimeUnavailable"), ("RuntimeVersion", runtimeVersion), ("Failure", failure ?? "Unknown"));
        return State;
    }

    private static bool IsProtocolCompatible(ClawHudRuntimeInfo info) =>
        info.MinimumProtocolVersion <= ClawHudControlProtocol.ProtocolVersion
        && info.MaximumProtocolVersion >= ClawHudControlProtocol.ProtocolVersion;

    private static string Describe<T>(ClawHudControlResult<T> result) =>
        result.Kind == ClawHudControlResultKind.ProtocolError ? $"Protocol:{result.Status}" : result.Kind.ToString();

    private static string DescribeStartupExit(int exitCode) => exitCode switch
    {
        (int)ClawHudManagedStartupExitCode.UnsupportedHardware => "StartupExit:UnsupportedHardware",
        (int)ClawHudManagedStartupExitCode.HardwareIndeterminate => "StartupExit:HardwareIndeterminate",
        (int)ClawHudManagedStartupExitCode.PresentMonRebootRequired => "StartupExit:PresentMonRebootRequired",
        (int)ClawHudManagedStartupExitCode.PresentMonElevationCancelled => "StartupExit:PresentMonElevationCancelled",
        (int)ClawHudManagedStartupExitCode.PresentMonMsiMissing => "StartupExit:PresentMonMsiMissing",
        (int)ClawHudManagedStartupExitCode.PresentMonInstallTimedOut => "StartupExit:PresentMonInstallTimedOut",
        (int)ClawHudManagedStartupExitCode.PresentMonInstallFailed => "StartupExit:PresentMonInstallFailed",
        (int)ClawHudManagedStartupExitCode.PresentMonValidationFailed => "StartupExit:PresentMonValidationFailed",
        (int)ClawHudManagedStartupExitCode.RuntimeInitializationFailed => "StartupExit:RuntimeInitializationFailed",
        (int)ClawHudManagedStartupExitCode.ControlIpcUnavailable => "StartupExit:ControlIpcUnavailable",
        _ => $"StartupExit:Unknown:{exitCode}",
    };

    private static IClawHudProcessHandle LaunchProcess(ClawHudProcessLaunchRequest request)
    {
        var info = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = request.UseShellExecute,
            WorkingDirectory = request.WorkingDirectory,
        };
        foreach (var argument in request.Arguments)
            info.ArgumentList.Add(argument);
        var process = Process.Start(info) ?? throw new InvalidOperationException("ClawHUD process could not be started.");
        return new SystemClawHudProcessHandle(process);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ClawHudProcessController));
    }

    private sealed class SystemClawHudProcessHandle(Process process) : IClawHudProcessHandle
    {
        public bool HasExited => process.HasExited;
        public int ExitCode => process.HasExited ? process.ExitCode : 0;
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => process.WaitForExitAsync(cancellationToken);
        public void Kill(bool entireProcessTree) { if (!process.HasExited) process.Kill(entireProcessTree); }
        public ValueTask DisposeAsync() { process.Dispose(); return ValueTask.CompletedTask; }
    }
}
