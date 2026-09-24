using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostScreenshotContractTests
{
    [Fact]
    public void Screenshot_reuses_overlay_retirement_and_holds_the_visible_surface_gate_through_capture()
    {
        var source = ReadHostSource();
        var method = ExtractMethod(source, "private async Task<ShortcutExecutionResult> ExecuteFullscreenScreenshotShortcutAsync");

        var gateAcquire = method.IndexOf("_visibleSurfaceTransition.WaitAsync", StringComparison.Ordinal);
        var retirement = method.IndexOf("RetireOverlayCaptureUnderTransitionAsync(", StringComparison.Ordinal);
        var capture = method.IndexOf("_nircmdScreenshotCapture.CaptureAsync(", StringComparison.Ordinal);
        var gateRelease = method.LastIndexOf("_visibleSurfaceTransition.Release()", StringComparison.Ordinal);

        Assert.True(gateAcquire >= 0 && gateAcquire < retirement);
        Assert.True(retirement < capture && capture < gateRelease);
        Assert.Contains("if (!retired)", method, StringComparison.Ordinal);
        Assert.True(method.IndexOf("if (!retired)", StringComparison.Ordinal) < capture);
        var blockedBranch = ExtractBlockAfter(method, "if (!retired)");
        Assert.Contains("return new(ShortcutExecutionOutcome.Unavailable", blockedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("_nircmdScreenshotCapture.CaptureAsync", blockedBranch, StringComparison.Ordinal);
        Assert.Contains("_overlayCaptureActive || _overlayController.IsVisible", method, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureHiddenAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_callback_is_composed_on_shortcut_runtime_without_a_second_lifecycle_owner()
    {
        var source = ReadHostSource();

        Assert.Contains("_shortcutRuntime = new(_shortcutStore, screenshotAction: ExecuteFullscreenScreenshotShortcutAsync);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenshotManager", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScreenshotCoordinator", source, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }
        return source[start..(index + 1)];
    }

    private static string ExtractBlockAfter(string source, string statement)
    {
        var start = source.IndexOf(statement, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Statement not found: {statement}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }
        return source[openBrace..(index + 1)];
    }

    private static string ReadHostSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));
    }
}
