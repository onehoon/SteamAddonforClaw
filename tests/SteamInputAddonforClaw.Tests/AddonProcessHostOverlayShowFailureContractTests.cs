using System;
using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostOverlayShowFailureContractTests
{
    [Fact]
    public void Failed_show_acknowledgement_retires_overlay_before_any_capture_or_presentation_pause()
    {
        var hostSource = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");
        var coordinateShow = ExtractMethod(hostSource, "private async Task CoordinateOverlayToggleAsync");
        var showFailure = ExtractBlockAfter(coordinateShow, "if (!await _overlayController.ShowAsync().ConfigureAwait(false))");

        Assert.Contains("Overlay Show did not acknowledge Visible; controller stays live.", showFailure, StringComparison.Ordinal);
        Assert.Contains("return;", showFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("PauseForOverlayAsync", showFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayControllerInputRouter", showFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("_overlayCaptureActive = true", showFailure, StringComparison.Ordinal);

        var showAcknowledgement = coordinateShow.IndexOf("_overlayController.ShowAsync()", StringComparison.Ordinal);
        var pause = coordinateShow.IndexOf("presentation.PauseForOverlayAsync(", StringComparison.Ordinal);
        var routerStart = coordinateShow.IndexOf("router.Start();", StringComparison.Ordinal);
        var captureCommit = coordinateShow.IndexOf("_overlayCaptureActive = true;", StringComparison.Ordinal);
        Assert.True(showAcknowledgement >= 0 && showAcknowledgement < pause);
        Assert.True(pause < routerStart && routerStart < captureCommit);

        var controllerSource = ReadSource("src", "SteamInputAddonforClaw", "Lifecycle", "OverlayProcessController.cs");
        var setVisibility = ExtractMethod(controllerSource, "private async Task<bool> SetVisibilityAsync");
        var failedAcknowledgement = ExtractBlockAfter(setVisibility, "if (!await server.SendCommandAsync(command).ConfigureAwait(false))");

        Assert.Contains("retiring the current Overlay session.", failedAcknowledgement, StringComparison.Ordinal);
        Assert.Contains("await StopCurrentAsync().ConfigureAwait(false);", failedAcknowledgement, StringComparison.Ordinal);
        Assert.Contains("return false;", failedAcknowledgement, StringComparison.Ordinal);
        Assert.True(setVisibility.IndexOf("server.SendCommandAsync(command)", StringComparison.Ordinal)
            < setVisibility.IndexOf("lock (_sync) _visible = show;", StringComparison.Ordinal));
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

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
