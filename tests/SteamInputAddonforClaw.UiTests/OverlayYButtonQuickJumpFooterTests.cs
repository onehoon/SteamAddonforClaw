using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class OverlayYButtonQuickJumpFooterTests
{
    [Theory]
    [InlineData(QuickOverlayYButtonTarget.Profile, "Profile")]
    [InlineData(QuickOverlayYButtonTarget.Shortcut, "Shortcut")]
    [InlineData(QuickOverlayYButtonTarget.Device, "Device")]
    [InlineData(QuickOverlayYButtonTarget.Controller, "Controller")]
    [InlineData(QuickOverlayYButtonTarget.Setting, "Setting")]
    [InlineData(QuickOverlayYButtonTarget.Off, "Off")]
    public void Formats_each_available_runtime_target(QuickOverlayYButtonTarget target, string expected)
    {
        Assert.Equal(expected, OverlayWindow.FormatYButtonQuickJumpFooterTarget(target, available: true));
    }

    [Theory]
    [InlineData(QuickOverlayYButtonTarget.Profile, false)]
    [InlineData(QuickOverlayYButtonTarget.Off, false)]
    [InlineData((QuickOverlayYButtonTarget)99, true)]
    [InlineData((QuickOverlayYButtonTarget)99, false)]
    public void Unavailable_or_invalid_runtime_state_uses_the_unavailable_mark(
        QuickOverlayYButtonTarget target,
        bool available)
    {
        Assert.Equal("—", OverlayWindow.FormatYButtonQuickJumpFooterTarget(target, available));
    }

    [Fact]
    public void Footer_projects_only_authoritative_settings_and_not_the_pending_choice()
    {
        var root = RepoRoot();
        var controller = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs"));
        var clawHud = File.ReadAllText(Path.Combine(
            root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs"));
        var stateApply = SliceMethod(controller,
            "internal void ApplyFrontendSettingsState(", "internal void ApplyFrontendSettingsMutationResult(");
        var mutationResult = SliceMethod(controller,
            "internal void ApplyFrontendSettingsMutationResult(", "internal void ApplyFrontendSettingsMutationFailure(");
        var mutationFailure = SliceMethod(controller,
            "internal void ApplyFrontendSettingsMutationFailure(", "private void RestoreControllerRowSelection(");
        var pendingMutation = SliceMethod(clawHud,
            "private void RequestYButtonQuickJumpChange(", "private void SetYButtonQuickJumpMutationInFlight(");

        Assert.Contains("ApplyYButtonQuickJumpFooterTarget(settings.YButtonQuickJumpTarget, settingsAvailable);", stateApply);
        Assert.DoesNotContain("_pendingYButtonQuickJumpTarget", stateApply);
        Assert.True(
            stateApply.IndexOf("_frontendSettingsSnapshot = settings;", StringComparison.Ordinal)
            < stateApply.IndexOf("ApplyYButtonQuickJumpFooterTarget(", StringComparison.Ordinal));
        Assert.Contains("ApplyFrontendSettingsState(response.Settings", mutationResult);
        Assert.Contains("SetYButtonQuickJumpMutationInFlight(false);", mutationFailure);
        Assert.DoesNotContain("ApplyYButtonQuickJumpFooterTarget", mutationFailure);
        Assert.DoesNotContain("QuickJumpFooter", pendingMutation);
    }

    private static string SliceMethod(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
