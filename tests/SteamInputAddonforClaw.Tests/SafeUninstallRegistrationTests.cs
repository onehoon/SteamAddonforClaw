using SteamInputAddonforClaw.Install;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SafeUninstallRegistrationTests
{
    [Fact]
    public void EnsureCurrentInstallation_RepairsOnlyTheExactAddonEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var registry = new FakeRegistry(new(root, $"\"{Path.Combine(root, "Update.exe")}\" --uninstall", $"\"{Path.Combine(root, "Update.exe")}\" --uninstall --silent"));

        var result = SafeUninstallRegistration.EnsureCurrentInstallation(root, registry, _ => true);

        Assert.True(result.Success, result.Reason);
        Assert.Equal(1, registry.WriteCalls);
        Assert.Equal($"\"{Path.Combine(root, VelopackAppPaths.StableLauncherName)}\" --safe-uninstall", registry.Entry!.UninstallString);
        Assert.Equal($"\"{Path.Combine(root, VelopackAppPaths.StableLauncherName)}\" --safe-uninstall --silent", registry.Entry.QuietUninstallString);
    }

    [Fact]
    public void EnsureCurrentInstallation_DoesNotRewriteWrongInstallLocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var registry = new FakeRegistry(new("C:\\OtherAddon", $"\"{Path.Combine(root, "Update.exe")}\" --uninstall", null));

        var result = SafeUninstallRegistration.EnsureCurrentInstallation(root, registry, _ => true);

        Assert.False(result.Success);
        Assert.Equal(0, registry.WriteCalls);
    }

    [Fact]
    public void EnsureCurrentInstallation_DoesNotRewriteUninstallExecutableOutsideCurrentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var registry = new FakeRegistry(new(root, "\"C:\\OtherAddon\\Update.exe\" --uninstall", null));

        var result = SafeUninstallRegistration.EnsureCurrentInstallation(root, registry, _ => true);

        Assert.False(result.Success);
        Assert.Equal(0, registry.WriteCalls);
    }

    [Fact]
    public void TryValidateCurrentInstallation_AcceptsForwardedCurrentBinaryAndRejectsStableLauncher()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var updater = Path.Combine(root, "Update.exe");
        var currentDirectory = Path.Combine(root, "current");
        var currentExecutable = Path.Combine(currentDirectory, VelopackAppPaths.MainExecutableName);
        var registry = new FakeRegistry(new(root, $"\"{updater}\" --uninstall", null));

        var expectedCurrentExecutable = VelopackAppPaths.ResolveCurrentExecutablePath(currentDirectory);
        Assert.True(SafeUninstallRegistration.TryValidateCurrentInstallation(root, currentExecutable, expectedCurrentExecutable, out var resolvedUpdater, registry, _ => true));
        Assert.Equal(updater, resolvedUpdater);
        Assert.False(SafeUninstallRegistration.TryValidateCurrentInstallation(root, Path.Combine(root, VelopackAppPaths.StableLauncherName), expectedCurrentExecutable, out _, registry, _ => true));
        Assert.False(SafeUninstallRegistration.TryValidateCurrentInstallation(root, Path.Combine(root, "Other.exe"), expectedCurrentExecutable, out _, registry, _ => true));
        Assert.False(SafeUninstallRegistration.TryValidateCurrentInstallation(root, null, expectedCurrentExecutable, out _, registry, _ => true));
    }

    [Fact]
    public void InstalledVeloPackLayout_RegistersStableLauncherAndValidatesForwardedCurrentBinary()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var currentDirectory = Path.Combine(root, "current");
        var stableLauncher = Path.Combine(root, VelopackAppPaths.StableLauncherName);
        var updater = Path.Combine(root, VelopackAppPaths.UpdaterExecutableName);
        var currentExecutable = Path.Combine(currentDirectory, VelopackAppPaths.MainExecutableName);
        Directory.CreateDirectory(currentDirectory);
        File.WriteAllText(stableLauncher, "stub");
        File.WriteAllText(updater, "updater");
        File.WriteAllText(currentExecutable, "main executable");
        var registry = new FakeRegistry(new(root, $"\"{updater}\" --uninstall", null));

        try
        {
            var registration = SafeUninstallRegistration.EnsureCurrentInstallation(root, registry);
            Assert.True(registration.Success, registration.Reason);
            Assert.Equal($"\"{stableLauncher}\" --safe-uninstall", registry.Entry!.UninstallString);
            Assert.Equal($"\"{stableLauncher}\" --safe-uninstall --silent", registry.Entry.QuietUninstallString);

            var expectedCurrentExecutable = VelopackAppPaths.ResolveCurrentExecutablePath(currentDirectory);
            Assert.True(SafeUninstallRegistration.TryValidateCurrentInstallation(
                root, currentExecutable, expectedCurrentExecutable, out var resolvedUpdater, registry));
            Assert.Equal(updater, resolvedUpdater);
            Assert.True(VelopackAppPaths.TryResolveCurrentExecutablePath(
                currentExecutable, expectedCurrentExecutable, out var elevatedHelperExecutable));
            Assert.Equal(currentExecutable, elevatedHelperExecutable);
            Assert.False(VelopackAppPaths.TryResolveCurrentExecutablePath(
                stableLauncher, expectedCurrentExecutable, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeRegistry(SafeUninstallRegistryEntry entry) : ISafeUninstallRegistry
    {
        internal SafeUninstallRegistryEntry? Entry { get; private set; } = entry;
        internal int WriteCalls { get; private set; }
        public SafeUninstallRegistryEntry? ReadAddonEntry() => Entry;
        public bool WriteAddonUninstallStrings(string uninstallString, string quietUninstallString)
        {
            WriteCalls++;
            Entry = Entry! with { UninstallString = uninstallString, QuietUninstallString = quietUninstallString };
            return true;
        }
    }
}
