using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// The OEM1 mapping rides on the Addon's existing settings file -- there is no second store. These
/// cover the persistence guarantees the work order names: locked first-install defaults, survival
/// across a restart, and "remapping off never erases the bindings".
/// </summary>
public sealed class Oem1MappingSettingsPersistenceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_testDirectory, "settings.json");

    [Fact]
    public void No_persisted_value_yields_the_locked_defaults()
    {
        var mapping = new SettingsStore(SettingsPath).Load().Oem1Mapping;

        Assert.True(mapping.RemappingEnabled);
        Assert.Equal(Oem1Action.SteamBigPicture, mapping.NormalSingle.Action);
        Assert.Equal(Oem1Action.None, mapping.NormalDouble.Action);
        Assert.Equal(Oem1Action.SteamQuickAccess, mapping.RoutingSingle.Action);
        Assert.Equal(Oem1Action.None, mapping.RoutingDouble.Action);
    }

    [Fact]
    public void A_settings_file_predating_the_feature_yields_the_locked_defaults()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(SettingsPath, "{\"LaunchAtWindowsStartup\":false}");

        Assert.Equal(Oem1MappingSettings.Default, new SettingsStore(SettingsPath).Load().Oem1Mapping);
    }

    [Fact]
    public void A_malformed_mapping_falls_back_to_the_defaults_rather_than_failing_to_load()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(SettingsPath, "{\"LaunchAtWindowsStartup\":false,\"Oem1Mapping\":{\"NormalSingle\":{\"Action\":\"NotARealAction\"}}}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.False(settings.LaunchAtWindowsStartup);
        Assert.Equal(Oem1MappingSettings.Default, settings.Oem1Mapping);
    }

    [Fact]
    public void Every_slot_and_its_action_specific_configuration_survives_a_restart()
    {
        var store = new SettingsStore(SettingsPath);
        var mapping = new Oem1MappingSettings
        {
            RemappingEnabled = true,
            NormalSingle = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with
            {
                Hotkey = new(Oem1HotkeyModifiers.Control | Oem1HotkeyModifiers.Shift, Oem1HotkeyKey.S)
            },
            NormalDouble = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with
            {
                Launch = new(@"C:\fake\app.exe", "--windowed")
            },
            RoutingSingle = Oem1SlotBinding.Of(Oem1Action.SteamQuickAccess),
            RoutingDouble = Oem1SlotBinding.Of(Oem1Action.None)
        };

        store.Save(new AppSettings() with { Oem1Mapping = mapping });

        // A brand-new store instance over the same path is exactly what an app restart does.
        Assert.Equal(mapping, new SettingsStore(SettingsPath).Load().Oem1Mapping);
    }

    [Fact]
    public void Turning_remapping_off_persists_the_switch_without_touching_the_bindings()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var configured = Oem1MappingSettings.Default with
        {
            NormalDouble = Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey) with { Hotkey = new(Key: Oem1HotkeyKey.F12) }
        };
        coordinator.ChangeOem1Mapping(configured);

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });

        var persisted = new SettingsStore(SettingsPath).Load().Oem1Mapping;
        Assert.False(persisted.RemappingEnabled);
        Assert.Equal(configured.NormalSingle, persisted.NormalSingle);
        Assert.Equal(configured.NormalDouble, persisted.NormalDouble);
        Assert.Equal(configured.RoutingSingle, persisted.RoutingSingle);
        Assert.Equal(configured.RoutingDouble, persisted.RoutingDouble);
    }

    [Fact]
    public void Turning_remapping_back_on_restores_the_previously_saved_mappings()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var configured = Oem1MappingSettings.Default with
        {
            RoutingDouble = Oem1SlotBinding.Of(Oem1Action.LaunchApplication) with { Launch = new(@"C:\fake\app.exe") }
        };
        coordinator.ChangeOem1Mapping(configured);

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });
        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = true });

        Assert.Equal(configured, new SettingsStore(SettingsPath).Load().Oem1Mapping);
    }

    [Fact]
    public void Changing_the_mapping_raises_exactly_one_change_notification_and_no_op_writes_raise_none()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());
        var changes = 0;
        coordinator.Oem1MappingChanged += (_, _) => changes++;

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });
        Assert.Equal(1, changes);

        // Value equality, not reference equality: re-saving an identical mapping must not churn the
        // runtime's suppression lifecycle.
        coordinator.ChangeOem1Mapping(Oem1MappingSettings.Default with { RemappingEnabled = false });
        Assert.Equal(1, changes);
    }

    [Fact]
    public void The_steam_input_routing_switch_and_the_remapping_switch_are_independent()
    {
        var store = new SettingsStore(SettingsPath);
        var coordinator = new StartupSettingsCoordinator(store.Load(), store, new NoOpStartupManager());

        coordinator.ChangeSteamInputRoutingEnabled(false);
        Assert.True(coordinator.Oem1Mapping.RemappingEnabled);

        coordinator.ChangeOem1Mapping(coordinator.Oem1Mapping with { RemappingEnabled = false });
        Assert.False(coordinator.SteamInputRoutingEnabled);
        Assert.False(new SettingsStore(SettingsPath).Load().Oem1Mapping.RemappingEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => new(true, "No-op");
    }
}
