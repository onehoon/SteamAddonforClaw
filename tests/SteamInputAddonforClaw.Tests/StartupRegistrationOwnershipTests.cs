using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-B: background startup is installed-app lifecycle infrastructure, not a user
/// preference. <see cref="StartupSettingsCoordinator"/> exposes exactly two explicit lifecycle
/// operations -- ensure (Runtime startup / before Addon controller authority) and uninstall removal --
/// with nothing persisted and no OFF request.</summary>
public sealed class StartupRegistrationOwnershipTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private StartupSettingsCoordinator Coordinator(FakeStartupManager manager)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        return new StartupSettingsCoordinator(new AppSettings(), store, manager);
    }

    [Fact]
    public void EnsureStartupRegistration_synchronizes_the_owned_task_on_and_returns_the_result()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(manager);

        var result = coordinator.EnsureStartupRegistration();

        Assert.True(result.Success);
        Assert.Equal([true], manager.Synchronized);
    }

    [Fact]
    public void EnsureStartupRegistration_failure_is_returned_and_nothing_is_persisted()
    {
        var manager = new FakeStartupManager { Result = StartupRegistrationResult.Failed() };
        var coordinator = Coordinator(manager);

        var result = coordinator.EnsureStartupRegistration();

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void RemoveStartupRegistrationForUninstall_synchronizes_the_owned_task_off()
    {
        var manager = new FakeStartupManager();
        var coordinator = Coordinator(manager);

        var result = coordinator.RemoveStartupRegistrationForUninstall();

        Assert.True(result.Success);
        Assert.Equal([false], manager.Synchronized);
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
