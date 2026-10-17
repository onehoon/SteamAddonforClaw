using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-B: background startup is installed-app lifecycle infrastructure, not a user
/// preference. <see cref="StartupSettingsCoordinator"/> exposes exactly two explicit lifecycle
/// operations -- ensure (Runtime startup / before Addon controller authority) and uninstall removal --
/// with nothing persisted and no OFF request.</summary>
[Collection("AppLog")]
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
        Assert.Same(result, coordinator.LatestStartupRegistration);
        Assert.Equal([true], manager.Synchronized);
    }

    [Fact]
    public void EnsureStartupRegistration_failure_is_returned_and_nothing_is_persisted()
    {
        var manager = new FakeStartupManager { Result = StartupRegistrationResult.Failed() };
        var coordinator = Coordinator(manager);

        var result = coordinator.EnsureStartupRegistration();

        Assert.False(result.Success);
        Assert.Same(result, coordinator.LatestStartupRegistration);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
    }

    [Fact]
    public void EnsureStartupRegistration_catches_scheduler_exception_and_retains_failure()
    {
        AppLog.DirectoryOverride = _dir;
        var manager = new FakeStartupManager { Failure = new InvalidOperationException("Scheduler unavailable.") };
        var coordinator = Coordinator(manager);

        var result = coordinator.EnsureStartupRegistration();

        Assert.False(result.Success);
        Assert.Equal(StartupTaskFailureReason.RegistrationFailed, result.FailureReason);
        Assert.Same(result, coordinator.LatestStartupRegistration);
        Assert.Equal([true], manager.Synchronized);
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
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public List<bool> Synchronized { get; } = [];
        public StartupRegistrationResult? Result { get; set; }
        public Exception? Failure { get; init; }

        public StartupRegistrationResult Synchronize(bool enabled)
        {
            Synchronized.Add(enabled);
            if (Failure is not null) throw Failure;
            return Result ?? (enabled ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Disabled());
        }
    }
}
