using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostDisplayScaleNotificationContractTests
{
    [Fact]
    public void Display_scale_owner_is_the_single_nonblocking_notification_source()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));
        var controllerCreated = source.IndexOf("_overlayController = new OverlayProcessController", StringComparison.Ordinal);
        var subscribed = source.IndexOf("_displayResolutionRuntime.DisplayScaleCommitted += OnDisplayScaleCommitted", StringComparison.Ordinal);
        Assert.True(controllerCreated >= 0 && subscribed > controllerCreated);
        Assert.Equal(1, Count(source, "NotifyDisplayScaleChangedAsync("));

        var callback = ExtractMethod(source, "private void OnDisplayScaleCommitted(");
        Assert.Contains("_ = NotifyOverlayDisplayScaleChangedAsync(change)", callback);
        Assert.DoesNotContain("await ", callback, StringComparison.Ordinal);
        var notification = ExtractMethod(source, "private async Task NotifyOverlayDisplayScaleChangedAsync(");
        Assert.Contains("_overlayController.NotifyDisplayScaleChangedAsync(change)", notification);
        Assert.Contains("catch (Exception exception)", notification);

        var shutdown = ExtractMethod(source, "private void PrepareRuntimeForShutdown()");
        var unsubscribe = shutdown.IndexOf("_displayResolutionRuntime.DisplayScaleCommitted -= OnDisplayScaleCommitted", StringComparison.Ordinal);
        var restore = shutdown.IndexOf("_displayResolutionRuntime.Shutdown()", StringComparison.Ordinal);
        Assert.True(unsubscribe >= 0 && restore > unsubscribe);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method was not found: {signature}");
        var openingBrace = source.IndexOf('{', start);
        Assert.True(openingBrace > start, $"Method body was not found: {signature}");
        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }
        throw new InvalidOperationException($"Method body was not closed: {signature}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
