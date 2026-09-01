using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR2.5: while MSI Center M startup config is exactly Disabled, <c>LaunchAtWindowsStartup</c>
/// is a mandatory-ON policy the coordinator enforces -- a saved <c>false</c> is converged on repair
/// and a user OFF request is rejected without persisting <c>false</c> or deleting the owned task.</summary>
public sealed class StartupSettingsCoordinatorMandatoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private StartupSettingsCoordinator Coordinator(bool savedLaunchAtStartup, bool mandatory, FakeStartupManager manager)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(new AppSettings { LaunchAtWindowsStartup = savedLaunchAtStartup });
        return new StartupSettingsCoordinator(store.Load(), store, manager, () => mandatory);
    }

    [Fact]
    public void Enabled_mode_repair_synchronizes_the_saved_preference_and_leaves_it_optional()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(savedLaunchAtStartup: false, mandatory: false, manager);

        coordinator.Repair();

        Assert.Equal([false], manager.Synchronized);
        Assert.False(coordinator.Settings.LaunchAtWindowsStartup);
        Assert.False(coordinator.IsLaunchAtWindowsStartupRequired);
        Assert.True(coordinator.ChangeLaunchAtWindowsStartup(false).Success); // user may keep it off
    }

    [Fact]
    public void Disabled_mode_repair_converges_a_saved_false_to_true()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(savedLaunchAtStartup: false, mandatory: true, manager);

        var result = coordinator.Repair();

        Assert.True(result.Success);
        Assert.Equal([true], manager.Synchronized);
        Assert.True(coordinator.Settings.LaunchAtWindowsStartup);
        Assert.True(coordinator.IsLaunchAtWindowsStartupRequired);
    }

    [Fact]
    public void Disabled_mode_repair_keeps_a_saved_true_and_synchronizes_true()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(savedLaunchAtStartup: true, mandatory: true, manager);

        coordinator.Repair();

        Assert.Equal([true], manager.Synchronized);
        Assert.True(coordinator.Settings.LaunchAtWindowsStartup);
    }

    [Fact]
    public void Disabled_mode_rejects_a_user_off_request_without_persisting_false_or_deleting_the_task()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(savedLaunchAtStartup: true, mandatory: true, manager);

        var result = coordinator.ChangeLaunchAtWindowsStartup(false);

        Assert.True(result.Success);
        Assert.Equal("Required while MSI Center M is disabled.", result.Message);
        Assert.True(coordinator.Settings.LaunchAtWindowsStartup);
        Assert.DoesNotContain(false, manager.Synchronized); // never Synchronize(false) => task not deleted
        Assert.Contains(true, manager.Synchronized);        // repairs/proves the required task instead
    }

    [Fact]
    public void Disabled_mode_off_request_when_saved_false_converges_to_true_first()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(savedLaunchAtStartup: false, mandatory: true, manager);

        coordinator.ChangeLaunchAtWindowsStartup(false);

        Assert.True(coordinator.Settings.LaunchAtWindowsStartup); // not persisted false then discovered
        Assert.DoesNotContain(false, manager.Synchronized);
    }

    [Fact]
    public void Disabled_mode_repair_failure_is_surfaced_and_the_desired_preference_stays_true()
    {
        var manager = new FakeStartupManager { Result = StartupRegistrationResult.Failed() };
        var coordinator = Coordinator(savedLaunchAtStartup: false, mandatory: true, manager);

        var result = coordinator.Repair();

        Assert.False(result.Success);
        Assert.True(coordinator.Settings.LaunchAtWindowsStartup); // no rollback that re-enables Center M
    }

    [Fact]
    public void Disabled_mode_off_request_surfaces_a_repair_failure()
    {
        var manager = new FakeStartupManager { Result = StartupRegistrationResult.Failed() };
        var coordinator = Coordinator(savedLaunchAtStartup: true, mandatory: true, manager);

        var result = coordinator.ChangeLaunchAtWindowsStartup(false);

        Assert.False(result.Success);
        Assert.True(coordinator.Settings.LaunchAtWindowsStartup);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public List<bool> Synchronized { get; } = [];
        public StartupRegistrationResult? Result { get; init; }

        public StartupRegistrationResult Synchronize(bool enabled)
        {
            Synchronized.Add(enabled);
            return Result ?? (enabled ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Disabled());
        }
    }
}
