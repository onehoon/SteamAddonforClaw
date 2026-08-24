using SteamInputAddonforClaw.Contracts.Wing;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WingMappingSettingsPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Wing.{Guid.NewGuid():N}");
    private string PathName => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Missing_wing_property_uses_default_mapping()
    {
        var mapping = new SettingsStore(PathName).Load().WingMapping;
        Assert.Equal(WingAction.SteamButton, mapping.Single.Action);
        Assert.Equal(WingAction.None, mapping.Double.Action);
    }

    [Fact]
    public void Mapping_round_trips_without_touching_routing_or_oem1()
    {
        var store = new SettingsStore(PathName);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var mapping = WingMappingSettings.Default with
        {
            Single = WingSlotBinding.Of(WingAction.KeyboardHotkey) with
            {
                Hotkey = new(WingHotkeyModifiers.Control, WingHotkeyKey.R)
            },
            Double = WingSlotBinding.Of(WingAction.LaunchApplication) with
            {
                Launch = new(@"C:\fake\wing.exe", "--test")
            }
        };
        var changes = 0;
        coordinator.WingMappingChanged += (_, _) => changes++;
        coordinator.ChangeWingMapping(mapping);

        var persisted = new SettingsStore(PathName).Load();
        Assert.Equal(mapping, persisted.WingMapping);
        Assert.False(persisted.SteamInputRoutingEnabled);
        Assert.Equal(Oem1MappingSettings.Default, persisted.Oem1Mapping);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Routing_toggle_does_not_erase_wing_mapping()
    {
        var store = new SettingsStore(PathName);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var mapping = WingMappingSettings.Default with { Double = WingSlotBinding.Of(WingAction.SteamButton) };
        coordinator.ChangeWingMapping(mapping);
        coordinator.ChangeSteamInputRoutingEnabled(false);
        Assert.Equal(mapping, new SettingsStore(PathName).Load().WingMapping);
    }

    [Theory]
    [InlineData("null", "{}")]
    [InlineData("{}", "null")]
    public void Null_nested_binding_falls_back_to_defaults(string hotkey, string launch)
    {
        Directory.CreateDirectory(_directory);
        var json = "{\"WingMapping\":{\"Single\":{\"Action\":\"SteamButton\",\"Hotkey\":" + hotkey + ",\"Launch\":" + launch + "},\"Double\":{\"Action\":\"None\",\"Hotkey\":{},\"Launch\":{}}}}";
        File.WriteAllText(PathName, json);

        var loaded = new SettingsStore(PathName).Load();

        Assert.Equal(WingMappingSettings.Default, loaded.WingMapping);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class NoOpStartupManager : IWindowsStartupManager { public StartupRegistrationResult Synchronize(bool enabled) => new(true, "No-op"); }
}
