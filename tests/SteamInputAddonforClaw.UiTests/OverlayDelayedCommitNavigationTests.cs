using System.IO;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayDelayedCommitNavigationTests
{
    [Fact]
    public void Root_back_and_outside_click_share_flush_then_dismiss_without_blocking_the_pipe_reader()
    {
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var navigationStart = app.IndexOf("private Task HandleNavigationAsync", StringComparison.Ordinal);
        var navigationEnd = app.IndexOf("private Task HandleCommandAsync", navigationStart, StringComparison.Ordinal);
        Assert.True(navigationStart >= 0 && navigationEnd > navigationStart);
        var navigation = app[navigationStart..navigationEnd];

        Assert.Contains("return Task.CompletedTask;", navigation, StringComparison.Ordinal);
        Assert.Contains("BeginUserDismiss(outsideClick: null)", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("await ", navigation, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue(() => BeginUserDismiss(outsideClick))", app, StringComparison.Ordinal);

        var dismissStart = app.IndexOf("private async Task SendUserDismissAsync", StringComparison.Ordinal);
        var dismissEnd = app.IndexOf("private async Task ConnectAndRunAsync", dismissStart, StringComparison.Ordinal);
        Assert.True(dismissStart >= 0 && dismissEnd > dismissStart);
        var dismiss = app[dismissStart..dismissEnd];
        Assert.Contains("_window?.FlushPendingControllerVibrationEdit();", dismiss, StringComparison.Ordinal);
        Assert.Contains("_window?.FlushPendingUserEditsAsync()", dismiss, StringComparison.Ordinal);
        Assert.True(dismiss.IndexOf("await Task.WhenAll(quickSettingsFlush, vibrationMutation)", StringComparison.Ordinal)
            < dismiss.IndexOf("await _client.SendDismissRequestedAsync()", StringComparison.Ordinal));
        Assert.Contains("Interlocked.CompareExchange(ref _userDismissInProgress, 1, 0)", app, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _userDismissInProgress, 0)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_has_no_selected_detail_navigation_and_uses_shared_page_leave_callbacks()
    {
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var shell = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs");
        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");

        Assert.Contains("internal bool TryHandleBack() => TryHandleSettingBack();", profile, StringComparison.Ordinal);
        Assert.Contains("private void OnProfileTabSelectionChanged(bool selected)", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileSelectedDetail", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileSelectedDetail", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileCatalog", app, StringComparison.Ordinal);
        Assert.Contains("OnProfileTabSelectionChanged(selected == AddonQuickSettingsTabId.Profile);", shell, StringComparison.Ordinal);
        Assert.Contains("FlushPendingUserEditsAsync()", ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Forced_hide_cancels_local_drafts_and_vibration_flush_is_not_duplicated()
    {
        var presentation = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs");
        var hideStart = presentation.IndexOf("internal async Task HideForPocAsync()", StringComparison.Ordinal);
        var hideEnd = presentation.IndexOf("private void ConfigureWindow()", hideStart, StringComparison.Ordinal);
        Assert.True(hideStart >= 0 && hideEnd > hideStart);
        var hide = presentation[hideStart..hideEnd];
        Assert.Contains("CancelUnsubmittedDrafts()", hide, StringComparison.Ordinal);
        Assert.Contains("CancelPendingControllerVibrationDraft()", hide, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(300", hide, StringComparison.Ordinal);
        Assert.DoesNotContain("FlushPendingUserEditsAsync", hide, StringComparison.Ordinal);
        var window = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs");
        var closedStart = window.IndexOf("Closed += (_, _) =>", StringComparison.Ordinal);
        var closedEnd = window.IndexOf("};", closedStart, StringComparison.Ordinal);
        Assert.True(closedStart >= 0 && closedEnd > closedStart);
        Assert.Contains("CancelPendingControllerVibrationDraft()", window[closedStart..closedEnd], StringComparison.Ordinal);

        var controller = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Controller.cs");
        Assert.Contains("await Task.Delay(300, token)", controller, StringComparison.Ordinal);
        Assert.Contains("if (!_vibrationDraftDirty || _controllerVibrationMutationInFlight) return;", controller, StringComparison.Ordinal);
        var submitStart = controller.IndexOf("private void SubmitControllerVibrationPair", StringComparison.Ordinal);
        var submitEnd = controller.IndexOf("internal void ApplyControllerVibrationState", submitStart, StringComparison.Ordinal);
        Assert.True(submitStart >= 0 && submitEnd > submitStart);
        var submit = controller[submitStart..submitEnd];
        Assert.True(submit.IndexOf("_vibrationDraftDirty = false", StringComparison.Ordinal)
            < submit.IndexOf("ControllerVibrationStrengthEditRequested?.Invoke", StringComparison.Ordinal));

        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        Assert.Contains("_controllerVibrationMutationTask = SendControllerVibrationMutationAsync", app, StringComparison.Ordinal);
        Assert.Contains("var vibrationMutation = _controllerVibrationMutationTask ?? Task.CompletedTask", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TestControllerVibrationMotorAsync", controller, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts])).ReplaceLineEndings("\n");
    }
}
