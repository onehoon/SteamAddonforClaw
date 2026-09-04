using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostResumeTests
{
    private static string HostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
    }

    [Fact] // Full1902 Suspend/Resume section 16.8: controller reconcile is requested immediately on resume.
    public void OnPowerResumeObserved_requests_the_controller_presentation_reconcile_before_the_profile_settle()
    {
        var host = HostSource();
        var body = host[host.IndexOf("private void OnPowerResumeObserved()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("internal static async Task ReconcilePerformanceAfterResumeAsync", StringComparison.Ordinal)];

        Assert.Contains("RequestControllerPresentationReconcile(\"PowerResume\")", body);
        // The controller reconcile call precedes the delayed CPU Boost / Power Mode profile path.
        Assert.True(
            body.IndexOf("RequestControllerPresentationReconcile(\"PowerResume\")", StringComparison.Ordinal)
            < body.IndexOf("ReconcilePerformanceAfterResumeAsync", StringComparison.Ordinal));
    }

    [Fact] // Full1902 Suspend/Resume review addendum A: host-local adapter reads current ownership; B: guard order.
    public void Suspend_participant_is_a_host_local_adapter_and_the_release_step_runs_after_the_existing_guards()
    {
        var host = HostSource();

        // A.1 / A.3: one fixed host-local adapter passed through the composition, not AddonProcessHost itself.
        Assert.Contains("full1902SuspendParticipant: new Full1902SuspendParticipant(QuiesceFull1902PresentationForSuspendAsync)", host);
        Assert.Contains("private sealed class Full1902SuspendParticipant(Func<CancellationToken, Task<bool>> quiesce)", host);
        Assert.DoesNotContain("class AddonProcessHost : IAsyncDisposable, ", host); // host does not itself implement the participant

        // A.2: the quiesce callback null-guards the CURRENT presentation field at execution time.
        var quiesce = host[host.IndexOf("private Task<bool> QuiesceFull1902PresentationForSuspendAsync", StringComparison.Ordinal)..];
        quiesce = quiesce[..quiesce.IndexOf("private sealed class Full1902SuspendParticipant", StringComparison.Ordinal)];
        Assert.Contains("var presentation = _presentationOwnership;", quiesce);
        Assert.Contains("if (presentation is null)", quiesce);
        Assert.Contains("PauseForSuspendAsync", quiesce);

        // B.1: in the reconcile entrypoint the suspend-release pre-step comes AFTER the source and
        // Win+G suppression guards, and ResetLatestStateToNeutral only runs inside that block.
        var reconcile = host[host.IndexOf("private async Task ReconcileControllerPresentationAsync", StringComparison.Ordinal)..];
        reconcile = reconcile[..reconcile.IndexOf("private", reconcile.IndexOf("ReconcileDesiredPresentationAsync", StringComparison.Ordinal))];
        Assert.True(
            reconcile.IndexOf("_winGSuppressionGuard.IsArmed", StringComparison.Ordinal)
            < reconcile.IndexOf("presentation.IsSuspendPaused", StringComparison.Ordinal));
        Assert.True(
            reconcile.IndexOf("presentation.IsSuspendPaused", StringComparison.Ordinal)
            < reconcile.IndexOf("source.ResetLatestStateToNeutral()", StringComparison.Ordinal));
        Assert.True(
            reconcile.IndexOf("source.ResetLatestStateToNeutral()", StringComparison.Ordinal)
            < reconcile.IndexOf("ResumeAfterSuspendAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resume_reconcile_waits_then_uses_latest_app_id_for_both_runtimes()
    {
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appId = 111u;
        var calls = new List<(string Runtime, uint AppId)>();

        var reconcile = AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            CancellationToken.None,
            async (delay, _) =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(2500), delay);
                delayStarted.SetResult();
                await releaseDelay.Task;
            },
            () => appId,
            currentAppId => calls.Add(("CPU", currentAppId)),
            currentAppId => calls.Add(("Power", currentAppId)));

        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Empty(calls);
        appId = 222;
        releaseDelay.SetResult();
        await reconcile;

        Assert.Equal(new[] { ("CPU", 222u), ("Power", 222u) }, calls);
    }

    [Fact]
    public async Task Resume_reconcile_keeps_power_mode_independent_when_cpu_boost_fails()
    {
        var calls = new List<string>();

        await AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            CancellationToken.None,
            static (_, _) => Task.CompletedTask,
            static () => 123u,
            _ =>
            {
                calls.Add("CPU");
                throw new InvalidOperationException("CPU failure");
            },
            _ => calls.Add("Power"));

        Assert.Equal(new[] { "CPU", "Power" }, calls);
    }

    [Fact]
    public async Task Resume_reconcile_keeps_cpu_boost_independent_when_power_mode_fails()
    {
        var calls = new List<string>();

        await AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            CancellationToken.None,
            static (_, _) => Task.CompletedTask,
            static () => 123u,
            _ => calls.Add("CPU"),
            _ =>
            {
                calls.Add("Power");
                throw new InvalidOperationException("Power failure");
            });

        Assert.Equal(new[] { "CPU", "Power" }, calls);
    }

    [Fact]
    public async Task Resume_reconcile_cancellation_during_settle_skips_both_runtimes()
    {
        using var cancellation = new CancellationTokenSource();
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var reconcile = AddonProcessHost.ReconcilePerformanceAfterResumeAsync(
            cancellation.Token,
            async (_, token) =>
            {
                delayStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            static () => 123u,
            _ => calls++,
            _ => calls++);

        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await reconcile;

        Assert.Equal(0, calls);
    }
}
