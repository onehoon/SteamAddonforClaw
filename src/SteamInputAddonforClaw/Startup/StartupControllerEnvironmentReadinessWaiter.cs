using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Startup;

internal interface IStartupControllerEnvironmentReadinessWaiter
{
    Task<ControllerEnvironmentAssessmentSnapshot> WaitForReadyAssessmentAsync(bool isBackgroundStartup, CancellationToken cancellationToken);
}

/// <summary>
/// Bounded, read-only wait for Stock MSI Center M software/process readiness during background
/// (Task Scheduler, --background) startup only. This exists to replace the implicit protection
/// the fixed three-minute logon delay currently provides -- it never mutates hardware, HidHide,
/// VIIPER, or routing state; it only re-captures <see cref="IControllerEnvironmentAssessmentProvider"/>
/// until the environment is no longer boot-transient or the bounded window elapses. Physical
/// controller topology readiness remains <see cref="ControllerEnvironmentWaiter"/>'s job.
/// </summary>
internal sealed class StartupControllerEnvironmentReadinessWaiter(
    IControllerEnvironmentAssessmentProvider assessmentProvider,
    TimeSpan? pollInterval = null,
    TimeSpan? timeout = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IStartupControllerEnvironmentReadinessWaiter
{
    private readonly IControllerEnvironmentAssessmentProvider _assessmentProvider = assessmentProvider ?? throw new ArgumentNullException(nameof(assessmentProvider));
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<ControllerEnvironmentAssessmentSnapshot> WaitForReadyAssessmentAsync(bool isBackgroundStartup, CancellationToken cancellationToken)
    {
        var assessment = _assessmentProvider.Capture();
        if (!isBackgroundStartup)
        {
            AppLog.Debug("Startup", "Startup controller software readiness wait skipped.", ("LaunchMode", "Manual"), ("Action", "Continue"));
            return assessment;
        }

        if (!IsBootTransient(assessment))
        {
            AppLog.Info("Startup", "Startup controller software readiness satisfied.", ("LaunchMode", "Background"), ("Attempts", 1), ("ElapsedMs", 0),
                ("ControllerManager", assessment.Manager.Kind), ("CompatibilityStatus", assessment.Compatibility.Status), ("CompatibilityReason", assessment.Compatibility.Reason), ("Action", "Continue"));
            return assessment;
        }

        var stopwatch = Stopwatch.StartNew();
        var attempts = 1;
        AppLog.Info("Startup", "Startup controller software readiness wait started.",
            ("LaunchMode", "Background"), ("PollIntervalMs", _pollInterval.TotalMilliseconds), ("TimeoutMs", _timeout.TotalMilliseconds),
            ("ControllerManager", assessment.Manager.Kind), ("CompatibilityStatus", assessment.Compatibility.Status), ("CompatibilityReason", assessment.Compatibility.Reason));

        try
        {
            while (IsBootTransient(assessment))
            {
                if (stopwatch.Elapsed >= _timeout)
                {
                    AppLog.Warn("Startup", "Startup controller software readiness timed out.", null,
                        ("LaunchMode", "Background"), ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds),
                        ("ControllerManager", assessment.Manager.Kind), ("CompatibilityStatus", assessment.Compatibility.Status), ("CompatibilityReason", assessment.Compatibility.Reason), ("Action", "Passive"));
                    return assessment;
                }

                await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                attempts++;
                assessment = _assessmentProvider.Capture();
                AppLog.Debug("Startup", "Startup controller software readiness poll.", ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds),
                    ("ControllerManager", assessment.Manager.Kind), ("CompatibilityStatus", assessment.Compatibility.Status));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Debug("Startup", "Startup controller software readiness wait cancelled.", ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            throw;
        }

        AppLog.Info("Startup", "Startup controller software readiness satisfied.", ("LaunchMode", "Background"), ("Attempts", attempts), ("ElapsedMs", stopwatch.ElapsedMilliseconds),
            ("ControllerManager", assessment.Manager.Kind), ("CompatibilityStatus", assessment.Compatibility.Status), ("CompatibilityReason", assessment.Compatibility.Reason), ("Action", "Continue"));
        return assessment;
    }

    /// <summary>
    /// True only for the specific boot-transient shapes a background launch may observe before
    /// Stock Center M has finished starting: a temporarily indeterminate assessment (covers
    /// Center M "Starting" and any other indeterminate software/manager state), or a definite
    /// stock-path read where Center M is installed but not yet running ("NotRunning" surfaces
    /// here as Unsupported/MsiCenterMNotOperational, since a background launch cannot yet tell
    /// "not started yet" apart from "won't start"). Every other shape -- a third-party manager,
    /// an already-supported stock environment, or Center M definitively not installed -- is
    /// terminal and must not wait.
    /// </summary>
    private static bool IsBootTransient(ControllerEnvironmentAssessmentSnapshot assessment)
    {
        if (assessment.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Indeterminate) return true;
        return assessment.Manager.Kind == ControllerManagerKind.None
            && assessment.Compatibility.Status == ControllerEnvironmentCompatibilityStatus.Unsupported
            && assessment.Compatibility.Reason == ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational;
    }
}
