using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerBadgeLifecycleNotificationTests
{
    [Fact]
    public async Task Presentation_reconcile_invalidates_only_after_final_badge_changes()
    {
        var currentBadge = FrontendControllerBadgeState.Xbox360Active;
        var reconcileGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconcile = reconcileGate.Task;
        var invalidations = 0;

        var notification = AddonProcessHost.NotifyControllerBadgeIfChangedAfterAsync(
            reconcile,
            currentBadge,
            () => currentBadge,
            () => invalidations++);

        Assert.Equal(0, invalidations);
        currentBadge = FrontendControllerBadgeState.SteamDeckActive;
        reconcileGate.SetResult();
        await notification;

        Assert.Equal(1, invalidations);
    }

    [Fact]
    public async Task Unchanged_presentation_reconcile_does_not_invalidate()
    {
        var currentBadge = FrontendControllerBadgeState.Xbox360Active;
        var invalidations = 0;

        await AddonProcessHost.NotifyControllerBadgeIfChangedAfterAsync(
            Task.CompletedTask,
            currentBadge,
            () => currentBadge,
            () => invalidations++);

        Assert.Equal(0, invalidations);
    }

    [Fact]
    public void Presentation_reconcile_owner_schedules_notification_after_its_task_is_published()
    {
        var root = FindRepositoryRoot();
        var host = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        var request = ExtractMethod(host, "private void RequestControllerPresentationReconcile(");

        var publishTask = request.IndexOf("_presentationReconcile = reconcile", StringComparison.Ordinal);
        var scheduleNotification = request.IndexOf("NotifyControllerBadgeIfChangedAfterAsync(", StringComparison.Ordinal);

        Assert.True(publishTask >= 0);
        Assert.True(scheduleNotification > publishTask);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method {signature}.");
        var openingBrace = source.IndexOf('{', start);
        Assert.True(openingBrace >= 0);
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Method body for {signature} is not balanced.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
