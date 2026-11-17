using System.Xml.Linq;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;
using SteamInputAddonforClaw.Views;
using SteamInputAddonforClaw.Windowing;
using Xunit;

namespace SteamInputAddonforClaw.UiTests;

public sealed class WindowsTweaksUiContractTests
{

    [Fact]
    public void Windows_tweak_cards_are_bound_to_the_shared_mutation_handler()
    {
        var root = FindRepositoryRoot();
        var xaml = XDocument.Load(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml"));
        var cards = xaml.Descendants().Where(element => element.Name.LocalName == "SettingsCard").ToArray();
        Assert.NotEmpty(cards);
        foreach (var card in cards)
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)card.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))));
            var toggle = Assert.Single(card.Descendants(), element => element.Name.LocalName == "ToggleSwitch");
            Assert.Equal("TweakSwitch_Toggled", (string?)toggle.Attribute("Toggled"));
        }
    }
    [Fact]
    public void Omitted_model_specific_tweak_hides_the_entire_card()
    {
        var modelSpecific = new FrontendWindowsTweakDefinition(
            FrontendWindowsTweakId.VirtualMachinePlatform,
            "Synthetic model-specific tweak",
            "Test only",
            FrontendWindowsTweakApplicability.A2vmOnly);
        // Runtime applicability filtering omits this A2VM-only row on non-A2VM devices.
        var runtimeFilteredSnapshot = new FrontendWindowsTweaksSnapshot(
            Array.Empty<FrontendWindowsTweakSnapshot>());

        Assert.Equal(
            Visibility.Collapsed,
            WindowsTweaksPage.GetCardVisibility(runtimeFilteredSnapshot, modelSpecific.Id));

        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml.cs"));
        Assert.Contains("card.Visibility = GetCardVisibility(snapshot, definition.Id)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_or_pending_warning_state_never_becomes_an_off_default()
    {
        var day = new DateOnly(2026, 10, 13);

        Assert.True(WindowsTweaksPage.ShouldShowWarning(day, persistedDate: null, acceptedThisProcess: null));
        Assert.True(WindowsTweaksPage.ShouldShowWarning(day, new DateOnly(2026, 10, 12), acceptedThisProcess: null));
        Assert.False(WindowsTweaksPage.ShouldShowWarning(day, day, acceptedThisProcess: null));
        Assert.False(WindowsTweaksPage.ShouldShowWarning(day, persistedDate: null, acceptedThisProcess: day));
        Assert.True(WindowsTweaksPage.ShouldShowWarning(day, persistedDate: null, acceptedThisProcess: new DateOnly(2026, 10, 12)));
    }

    [Fact]
    public void Mixed_and_exception_states_use_their_semantic_resource_keys()
    {
        var mixed = new FrontendWindowsTweakSnapshot(FrontendWindowsTweakId.DisableEdgeBackground,
            FrontendWindowsTweakState.Mixed, false, null, true, "Pair differs.");
        var enabled = new FrontendWindowsTweakSnapshot(FrontendWindowsTweakId.DisableEdgeBackground,
            FrontendWindowsTweakState.Enabled, true, true, true, null);
        var pending = new FrontendWindowsTweakSnapshot(FrontendWindowsTweakId.VirtualMachinePlatform,
            FrontendWindowsTweakState.EnablePendingRestart, true, false, true, null);
        Assert.Equal(MainUiStrings.Get("Tweaks_State_MixedApply", string.Empty), WindowsTweaksPage.FormatState(mixed));
        Assert.Equal(MainUiStrings.Get("Tweaks_State_Disabled", string.Empty), WindowsTweaksPage.FormatState(enabled));
        Assert.Equal(MainUiStrings.Get("Tweaks_State_RestartRequired", string.Empty), WindowsTweaksPage.FormatState(pending));
        var configured = new FrontendWindowsTweakSnapshot(FrontendWindowsTweakId.MemoryIntegrity,
            FrontendWindowsTweakState.EnablePendingRestart, true, false, true, null);
        var explorer = new FrontendWindowsTweakSnapshot(FrontendWindowsTweakId.DisableEdgeBackground,
            FrontendWindowsTweakState.ExplorerRestartPending, true, false, true, null);
        Assert.Equal(MainUiStrings.Get("Tweaks_State_ConfiguredNotRunning", string.Empty), WindowsTweaksPage.FormatState(configured));
        Assert.Equal(MainUiStrings.Get("Tweaks_State_ExplorerRestartRequired", string.Empty), WindowsTweaksPage.FormatState(explorer));
    }
    [Fact]
    public void Status_semantics_invert_disable_features_and_omit_os_feature_status()
    {
        var cases = new[]
        {
            (FrontendWindowsTweakId.InstantContextMenus, FrontendWindowsTweakState.Enabled, "Tweaks_State_Enabled"),
            (FrontendWindowsTweakId.InstantContextMenus, FrontendWindowsTweakState.Disabled, "Tweaks_State_Disabled"),
            (FrontendWindowsTweakId.ClassicContextMenu, FrontendWindowsTweakState.Enabled, "Tweaks_State_Enabled"),
            (FrontendWindowsTweakId.ClassicContextMenu, FrontendWindowsTweakState.Disabled, "Tweaks_State_Disabled"),
            (FrontendWindowsTweakId.DisablePasswordOnWake, FrontendWindowsTweakState.Enabled, "Tweaks_State_Disabled"),
            (FrontendWindowsTweakId.DisablePasswordOnWake, FrontendWindowsTweakState.Disabled, "Tweaks_State_Enabled"),
            (FrontendWindowsTweakId.DisableEdgeBackground, FrontendWindowsTweakState.Enabled, "Tweaks_State_Disabled"),
            (FrontendWindowsTweakId.DisableEdgeBackground, FrontendWindowsTweakState.Disabled, "Tweaks_State_Enabled"),
            (FrontendWindowsTweakId.DisableStartupAppDelay, FrontendWindowsTweakState.Enabled, "Tweaks_State_Disabled"),
            (FrontendWindowsTweakId.DisableStartupAppDelay, FrontendWindowsTweakState.Disabled, "Tweaks_State_Enabled"),
        };
        foreach (var (id, state, resourceKey) in cases)
        {
            var row = new FrontendWindowsTweakSnapshot(id, state,
                state == FrontendWindowsTweakState.Enabled, state == FrontendWindowsTweakState.Enabled, true, null);
            Assert.Equal(MainUiStrings.Get(resourceKey, string.Empty), WindowsTweaksPage.FormatState(row));
        }
        foreach (var id in new[] { FrontendWindowsTweakId.MemoryIntegrity, FrontendWindowsTweakId.VirtualMachinePlatform })
        foreach (var state in new[] { FrontendWindowsTweakState.Enabled, FrontendWindowsTweakState.Disabled })
        {
            var row = new FrontendWindowsTweakSnapshot(id, state,
                state == FrontendWindowsTweakState.Enabled, state == FrontendWindowsTweakState.Enabled, true, null);
            Assert.Null(WindowsTweaksPage.FormatState(row));
        }
    }
    [Fact]
    public void Updating_one_tweak_row_preserves_all_other_rows_and_warning_date()
    {
        var original = new FrontendWindowsTweaksSnapshot(
            FrontendWindowsTweakCatalog.All.Select(item => new FrontendWindowsTweakSnapshot(
                item.Id, FrontendWindowsTweakState.Disabled, false, false, true, null)).ToArray(),
            new DateOnly(2026, 10, 13));
        var updated = new FrontendWindowsTweakSnapshot(
            FrontendWindowsTweakId.DisableEdgeBackground, FrontendWindowsTweakState.Enabled, true, true, true, null);

        var result = WindowsTweaksPage.ReplaceTweak(original, updated);

        Assert.Equal(original.WarningAcknowledgedLocalDate, result.WarningAcknowledgedLocalDate);
        Assert.Equal(updated, Assert.Single(result.Tweaks, row => row.Id == updated.Id));
        Assert.Equal(original.Tweaks.Where(row => row.Id != updated.Id), result.Tweaks.Where(row => row.Id != updated.Id));
    }

    [Fact]
    public void Safety_notice_gates_controls_and_os_mutation_is_read_back_not_optimistic()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "Views", "WindowsTweaksPage.xaml.cs"));
        var warningAccepted = page.IndexOf("if (!await ShowDailyWarningAsync()) return;", StringComparison.Ordinal);
        var acknowledge = page.IndexOf("AcknowledgeWindowsTweaksWarningAsync(today)", StringComparison.Ordinal);
        var controlsShown = page.IndexOf("TweaksPanel.Visibility = Visibility.Visible;", StringComparison.Ordinal);

        Assert.True(warningAccepted >= 0 && acknowledge > warningAccepted && controlsShown > acknowledge);
        Assert.Contains("toggle.IsOn = configured", page, StringComparison.Ordinal);
        Assert.Contains("toggle.IsOn = current.ConfiguredEnabled.Value", page, StringComparison.Ordinal);
        Assert.Contains("ReplaceTweak(_snapshot, result.UpdatedTweak)", page, StringComparison.Ordinal);
        Assert.Contains("ApplyTweakRow(result.UpdatedTweak)", page, StringComparison.Ordinal);
        Assert.Contains("SetOperationMessage(result.Succeeded ? null : result.FailureMessage)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception.Message", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCaptureAfterMutationFailureAsync", page, StringComparison.Ordinal);
        var handlerStart = page.IndexOf("private async void TweakSwitch_Toggled", StringComparison.Ordinal);
        var handlerEnd = page.IndexOf("internal static string? FormatState", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var mutationHandler = page[handlerStart..handlerEnd];
        Assert.DoesNotContain("CaptureWindowsTweaksAsync", mutationHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySnapshot(result", mutationHandler, StringComparison.Ordinal);
        Assert.Contains("HashSet<FrontendWindowsTweakId> _mutatingIds", page, StringComparison.Ordinal);
        Assert.Contains("!_mutatingIds.Contains(row.Id)", page, StringComparison.Ordinal);
        Assert.Contains("_mutatingIds.Count > 0", page, StringComparison.Ordinal);
        var dialogStart = page.IndexOf("private async Task<bool> ShowDailyWarningAsync()", StringComparison.Ordinal);
        var dialogEnd = page.IndexOf("private async Task<FrontendWindowsTweaksSnapshot> CaptureAsync()", dialogStart, StringComparison.Ordinal);
        Assert.True(dialogStart >= 0 && dialogEnd > dialogStart);
        var dialogFactory = page[dialogStart..dialogEnd];
        Assert.Contains("RequestedTheme = ActualTheme", dialogFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme = ElementTheme.Dark", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("SetStateLabel(controls.State, MainUiStrings.Get(\"Tweaks_Updating\"", page, StringComparison.Ordinal);
        Assert.Contains("stateText.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed", page, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(stateText", page, StringComparison.Ordinal);
        var frontendControl = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw", "Frontend", "InProcessAddonFrontendControl.cs"));
        var mutationStart = frontendControl.IndexOf("public async Task<FrontendWindowsTweakMutationResult> SetWindowsTweakAsync", StringComparison.Ordinal);
        var mutationEnd = frontendControl.IndexOf("public Task<bool> AcknowledgeWindowsTweaksWarningAsync", mutationStart, StringComparison.Ordinal);
        Assert.True(mutationStart >= 0 && mutationEnd > mutationStart);
        Assert.DoesNotContain("StateInvalidated?.Invoke", frontendControl[mutationStart..mutationEnd], StringComparison.Ordinal);
        Assert.Contains("return result;", frontendControl[mutationStart..mutationEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Restart-Computer", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Opening_tweaks_does_not_skip_shortcut_page_deactivation()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        mainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "if (page == MainNavigationPage.Shortcut) _shortcutContent!.Activate();\n        else if (wasShortcut) _shortcutContent?.Deactivate();\n        if (page == MainNavigationPage.Tweaks) _tweaksContent!.Activate();",
            mainWindow,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
