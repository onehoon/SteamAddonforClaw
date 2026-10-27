using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayClawHudHiddenRefreshContractTests
{
    [Fact]
    public void ClawHud_capture_and_wire_send_are_ready_hidden_or_visible_while_other_refreshes_stay_visible_only()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Lifecycle", "OverlayProcessController.cs"));
        var server = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.FrontendTransport", "NamedPipeOverlayServer.cs"));
        var host = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));

        var clawHudRefresh = Method(controller, "internal async Task RefreshClawHudAsync()", "internal async Task RefreshShortcutAsync()");
        var quickSettingsRefresh = Method(controller, "internal async Task RefreshQuickSettingsAsync(", "internal async Task RefreshTabOrderAsync()");
        var sendClawHud = Method(server, "internal async Task<bool> SendClawHudStateAsync(", "internal async Task<bool> SendShortcutStateAsync(");
        var sendQuickSettings = Method(server, "internal async Task<bool> SendQuickSettingsPageStateAsync(", "internal async Task<bool> SendClawHudStateAsync(");
        var setClawHud = Method(host, "internal async Task<FrontendClawHudSnapshot> SetClawHudFrontendAsync(", "internal async Task<FrontendClawHudMutationResult> MutateClawHudFrontendAsync(");
        var mutateClawHud = Method(host, "internal async Task<FrontendClawHudMutationResult> MutateClawHudFrontendAsync(", "internal void TestOnly_SetStartupForInitialization(");
        var clawHudStateChanged = Method(host, "private void OnClawHudStateChanged(", "private void StartBackgroundUpdate()");
        var genericInvalidation = Method(host, "private void OnFrontendStateInvalidatedForOverlay(", "private async Task<OverlayShortcutExecutionOutcome> HandleOverlayShortcutExecutionAsync(");

        Assert.Contains("OverlayState.Hidden", clawHudRefresh, StringComparison.Ordinal);
        Assert.Contains("OverlayState.Visible", clawHudRefresh, StringComparison.Ordinal);
        Assert.Contains("OverlayState.Hidden", sendClawHud, StringComparison.Ordinal);
        Assert.Contains("OverlayState.Visible", sendClawHud, StringComparison.Ordinal);
        Assert.Contains("RefreshClawHudAsync()", setClawHud, StringComparison.Ordinal);
        Assert.Contains("RefreshClawHudAsync()", mutateClawHud, StringComparison.Ordinal);
        Assert.Contains("OverlayState.Visible", quickSettingsRefresh, StringComparison.Ordinal);
        Assert.Contains("OverlayState.Visible", sendQuickSettings, StringComparison.Ordinal);
        Assert.Contains("RefreshClawHudAsync()", clawHudStateChanged, StringComparison.Ordinal);
        Assert.Contains("!_overlayController.IsVisible", clawHudStateChanged, StringComparison.Ordinal);
        Assert.Contains("if (!_overlayCaptureActive) return;", genericInvalidation, StringComparison.Ordinal);
        Assert.Contains("if (!_overlayController.IsVisible) return;", genericInvalidation, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static string Method(string source, string signature, string nextSignature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method was not found: {signature}");
        var end = source.IndexOf(nextSignature, start + signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Method end was not found: {nextSignature}");
        return source[start..end];
    }
}
