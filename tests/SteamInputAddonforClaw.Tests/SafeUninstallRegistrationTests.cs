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
        Assert.Equal($"\"{Path.Combine(root, "SteamInputAddonforClaw.exe")}\" --safe-uninstall", registry.Entry!.UninstallString);
        Assert.Equal($"\"{Path.Combine(root, "SteamInputAddonforClaw.exe")}\" --safe-uninstall --silent", registry.Entry.QuietUninstallString);
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
    public void TryValidateCurrentInstallation_RequiresStableStubFromExactRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeUninstall", Guid.NewGuid().ToString("N"));
        var updater = Path.Combine(root, "Update.exe");
        var stub = Path.Combine(root, "SteamInputAddonforClaw.exe");
        var registry = new FakeRegistry(new(root, $"\"{updater}\" --uninstall", null));

        Assert.True(SafeUninstallRegistration.TryValidateCurrentInstallation(root, stub, out var resolvedUpdater, registry, _ => true));
        Assert.Equal(updater, resolvedUpdater);
        Assert.False(SafeUninstallRegistration.TryValidateCurrentInstallation(root, Path.Combine(root, "Other.exe"), out _, registry, _ => true));
        Assert.False(SafeUninstallRegistration.TryValidateCurrentInstallation(root, null, out _, registry, _ => true));
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
