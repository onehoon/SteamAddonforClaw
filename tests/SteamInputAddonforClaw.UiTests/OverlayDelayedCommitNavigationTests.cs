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
        var navigationEnd = app.IndexOf("private void OnProfileSelectedDetailBackRequested", navigationStart, StringComparison.Ordinal);
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
    public void Profile_selected_detail_back_and_tab_leave_flush_before_releasing_the_app_id_context()
    {
        var profile = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs");
        var backStart = profile.IndexOf("internal bool TryHandleBack()", StringComparison.Ordinal);
        var backEnd = profile.IndexOf("internal void CompleteProfileSelectedDetailBack()", backStart, StringComparison.Ordinal);
        Assert.True(backStart >= 0 && backEnd > backStart);
        var back = profile[backStart..backEnd];
        Assert.Contains("ProfileSelectedDetailBackRequested?.Invoke()", back, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelUnsubmittedDrafts", back, StringComparison.Ordinal);

        var tabStart = profile.IndexOf("private void OnProfileTabSelectionChanged", StringComparison.Ordinal);
        var tabEnd = profile.IndexOf("private void ApplyProfileDetailPage", tabStart, StringComparison.Ordinal);
        Assert.True(tabStart >= 0 && tabEnd > tabStart);
        var tabChange = profile[tabStart..tabEnd];
        Assert.Contains("ProfileSelectedDetailTabLeaveRequested?.Invoke()", tabChange, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelUnsubmittedDrafts", tabChange, StringComparison.Ordinal);
        Assert.Contains("_profileDetailNavigationInProgress", tabChange, StringComparison.Ordinal);

        var app = ReadSource("src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs");
        var flushStart = app.IndexOf("private async Task FlushProfileEditsThenCompleteNavigationAsync", StringComparison.Ordinal);
        var flushEnd = app.IndexOf("private void OnProfileCatalogRequestRequested", flushStart, StringComparison.Ordinal);
        Assert.True(flushStart >= 0 && flushEnd > flushStart);
        var flush = app[flushStart..flushEnd];
        Assert.True(flush.IndexOf("FlushProfilePendingUserEditsAsync()", StringComparison.Ordinal)
            < flush.IndexOf("CompleteProfileSelectedDetailTabLeave()", StringComparison.Ordinal));
        Assert.True(flush.IndexOf("FlushProfilePendingUserEditsAsync()", StringComparison.Ordinal)
            < flush.IndexOf("CompleteProfileSelectedDetailBack()", StringComparison.Ordinal));
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
