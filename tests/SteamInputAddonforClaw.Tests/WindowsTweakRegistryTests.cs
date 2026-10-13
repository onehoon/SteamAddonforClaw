using Microsoft.Win32;
using SteamInputAddonforClaw.WindowsTweaks;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsTweakRegistryTests
{
    [Fact]
    public void Read_distinguishes_missing_key_missing_value_and_typed_value()
    {
        var path = CreateTestPath();
        try
        {
            var missingKey = WindowsTweakRegistry.Read(Registry.CurrentUser, path, "Enabled");
            Assert.False(missingKey.KeyExists);
            Assert.False(missingKey.ValueExists);

            using (Registry.CurrentUser.CreateSubKey(path, writable: true)) { }
            var missingValue = WindowsTweakRegistry.Read(Registry.CurrentUser, path, "Enabled");
            Assert.True(missingValue.KeyExists);
            Assert.False(missingValue.ValueExists);

            WindowsTweakRegistry.Write(Registry.CurrentUser, path, "Enabled", 1, RegistryValueKind.DWord);
            var value = WindowsTweakRegistry.Read(Registry.CurrentUser, path, "Enabled");
            Assert.True(value.KeyExists);
            Assert.True(value.ValueExists);
            Assert.Equal(1, value.Value);
            Assert.Equal(RegistryValueKind.DWord, value.Kind);

            WindowsTweakRegistry.Write(Registry.CurrentUser, path, "Custom", "0", RegistryValueKind.String);
            var customValue = WindowsTweakRegistry.Read(Registry.CurrentUser, path, "Custom");
            Assert.True(customValue.ValueExists);
            Assert.Equal("0", customValue.Value);
            Assert.Equal(RegistryValueKind.String, customValue.Kind);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Default_value_write_and_value_delete_do_not_remove_sibling_values_or_the_key()
    {
        var path = CreateTestPath();
        try
        {
            WindowsTweakRegistry.Write(Registry.CurrentUser, path, string.Empty, string.Empty, RegistryValueKind.String);
            WindowsTweakRegistry.Write(Registry.CurrentUser, path, "Sibling", "preserved", RegistryValueKind.String);

            var defaultValue = WindowsTweakRegistry.Read(Registry.CurrentUser, path, string.Empty);
            Assert.True(defaultValue.KeyExists);
            Assert.True(defaultValue.ValueExists);
            Assert.Equal(string.Empty, defaultValue.Value);
            Assert.Equal(RegistryValueKind.String, defaultValue.Kind);

            WindowsTweakRegistry.DeleteValue(Registry.CurrentUser, path, string.Empty);

            var deletedValue = WindowsTweakRegistry.Read(Registry.CurrentUser, path, string.Empty);
            Assert.True(deletedValue.KeyExists);
            Assert.False(deletedValue.ValueExists);
            var sibling = WindowsTweakRegistry.Read(Registry.CurrentUser, path, "Sibling");
            Assert.True(sibling.ValueExists);
            Assert.Equal("preserved", sibling.Value);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    private static string CreateTestPath() =>
        $@"Software\SteamInputAddonforClaw.Tests\WindowsTweakRegistry\{Guid.NewGuid():N}";
}
