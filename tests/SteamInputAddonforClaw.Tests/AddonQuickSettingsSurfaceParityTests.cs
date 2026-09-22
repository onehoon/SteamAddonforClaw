using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonQuickSettingsSurfaceParityTests
{
    [Fact]
    public void Shell_and_setting_share_one_custom_order_and_move_boundaries()
    {
        var order = new[]
        {
            AddonQuickSettingsTabId.Setting,
            AddonQuickSettingsTabId.Shortcut,
            AddonQuickSettingsTabId.Device,
            AddonQuickSettingsTabId.Controller,
            AddonQuickSettingsTabId.Profile,
        };

        var shell = AddonQuickSettingsShellContract.Create(order);
        var setting = AddonQuickSettingsTabOrderProduct.Create(order);

        Assert.Equal(order, shell.Tabs.Select(tab => tab.TabId));
        Assert.Equal(order, setting.Rows.Select(row => row.TabId));
        Assert.Equal(order.Select(AddonQuickSettingsShellContract.LabelFor), shell.Tabs.Select(tab => tab.Label));
        Assert.Equal(shell.Tabs.Select(tab => tab.Label), setting.Rows.Select(row => row.Label));
        Assert.Equal(
            setting.Rows.Select(row => (row.TabId, row.CanMoveEarlier, row.CanMoveLater)),
            shell.Tabs.Select((tab, index) => (tab.TabId, index > 0, index < shell.Tabs.Count - 1)));
    }

    [Fact]
    public void Device_and_profile_pages_keep_shared_row_metadata_as_the_product_authority()
    {
        var numeric = new QuickSettingsRow(
            QuickSettingsRowId.DeviceTdpAcPl1,
            "AC PL1",
            QuickSettingsControlKind.Slider,
            true,
            true,
            QuickSettingsValue.Integer(20),
            new(QuickSettingsSliderKind.Numeric, Minimum: 5, Maximum: 30, Step: 1, Suffix: " W"),
            QuickSettingsCommitPolicy.TrailingDebounce2000,
            QuickSettingsCommitGroupId.DeviceTdpConfiguration);
        var discrete = new QuickSettingsRow(
            QuickSettingsRowId.DevicePowerModeAc,
            "AC Power Mode",
            QuickSettingsControlKind.Slider,
            true,
            true,
            QuickSettingsValue.Integer(1),
            new(QuickSettingsSliderKind.Discrete, Options: [new(0, "Best power efficiency"), new(1, "Balanced")]),
            QuickSettingsCommitPolicy.TrailingDebounce2000);
        var upper = numeric with
        {
            RowId = QuickSettingsRowId.DeviceTdpAcPl2,
            Label = "AC PL2",
            Value = QuickSettingsValue.Integer(25),
        };
        var toggle = new QuickSettingsRow(
            QuickSettingsRowId.DeviceTdpEnabled,
            "TDP Control",
            QuickSettingsControlKind.Toggle,
            true,
            true,
            QuickSettingsValue.Boolean(true),
            null,
            QuickSettingsCommitPolicy.Immediate);
        var page = new QuickSettingsPageSnapshot(
            QuickSettingsPageId.Device,
            null,
            true,
            null,
            [new(QuickSettingsSectionId.DeviceTdp, "TDP Control", [toggle, numeric, upper, discrete])],
            [new(
                QuickSettingsRowId.DeviceTdpAcPl1,
                QuickSettingsRowId.DeviceTdpAcPl2,
                5)]);

        var rows = page.Sections.Single().Rows;
        Assert.Equal([QuickSettingsControlKind.Toggle, QuickSettingsControlKind.Slider, QuickSettingsControlKind.Slider, QuickSettingsControlKind.Slider], rows.Select(row => row.ControlKind));
        Assert.Equal(["TDP Control", "AC PL1", "AC PL2", "AC Power Mode"], rows.Select(row => row.Label));
        Assert.Equal(QuickSettingsSliderKind.Numeric, rows[1].SliderSpec?.Kind);
        Assert.Equal(QuickSettingsSliderKind.Numeric, rows[2].SliderSpec?.Kind);
        Assert.Equal(QuickSettingsSliderKind.Discrete, rows[3].SliderSpec?.Kind);
        Assert.Equal(["Best power efficiency", "Balanced"], rows[3].SliderSpec?.Options?.Select(option => option.Label));
        Assert.All(rows.Where(row => row.ControlKind == QuickSettingsControlKind.Slider), row =>
            Assert.Equal(QuickSettingsCommitPolicy.TrailingDebounce2000, row.CommitPolicy));
        Assert.Single(page.LinkedSliderConstraints);

        var overlay = ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayQuickSettingsPageBinding.cs");
        Assert.Contains("policy.DelayMilliseconds", overlay);
        Assert.DoesNotContain("ProductionDelay", overlay);
    }

    [Fact]
    public void Shortcut_and_controller_remain_narrow_surface_specific_contracts()
    {
        var shortcut = AddonQuickSettingsShortcutContract.Create();
        Assert.Equal(
            [AddonQuickSettingsShortcutSlotId.Slot1, AddonQuickSettingsShortcutSlotId.Slot2, AddonQuickSettingsShortcutSlotId.Slot3, AddonQuickSettingsShortcutSlotId.Slot4],
            shortcut.Slots.Select(slot => slot.SlotId));
        Assert.All(shortcut.Slots, slot => Assert.Equal("Unassigned", slot.StatusLabel));

        var shell = AddonQuickSettingsShellContract.Create(AddonQuickSettingsTabOrderContract.DefaultOrder);
        Assert.Contains(shell.Tabs, tab => tab.TabId == AddonQuickSettingsTabId.Controller);
        Assert.DoesNotContain(Enum.GetNames<QuickSettingsPageId>(), name => name == "Controller");

        var overlay = ReadOverlayWindowSources();
        Assert.Contains("_ => CreatePlaceholderPage(id)", overlay);
        Assert.Contains("AddonQuickSettingsShortcutContract.Create()", overlay);
    }

    [Fact]
    public void Overlay_consumes_shared_shell_and_special_page_contracts_without_new_wire_or_polling()
    {
        var overlay = ReadOverlayWindowSources();
        var frontendWire = ReadSource("src", "SteamInputAddonforClaw.FrontendTransport", "FrontendWire.cs");
        var overlayWire = ReadSource("src", "SteamInputAddonforClaw.FrontendTransport", "OverlayWire.cs");

        Assert.Contains("AddonQuickSettingsShellContract.LabelFor(id)", overlay);
        Assert.Contains("AddonQuickSettingsTabOrderContract", overlay);
        Assert.Contains("index / 2", overlay);
        Assert.Contains("index % 2", overlay);
        Assert.DoesNotContain("setInterval", overlay);
        Assert.Contains("CurrentVersion = 40", frontendWire);
        Assert.Contains("CurrentVersion = 10", overlayWire);
        Assert.DoesNotContain("CurrentVersion = 35", frontendWire);
        Assert.DoesNotContain("CurrentVersion = 8", overlayWire);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts])).ReplaceLineEndings("\n");
    }

    private static string ReadOverlayWindowSources() => string.Join(
        Environment.NewLine,
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Shell.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Navigation.cs"),
        ReadSource("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.QuickSettings.cs"));
}
