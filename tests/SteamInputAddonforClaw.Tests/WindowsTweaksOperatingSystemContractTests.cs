using Xunit;
using System.Text.RegularExpressions;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsTweaksOperatingSystemContractTests
{
    private static readonly string Source = ReadOperatingSystemSource();

    [Fact]
    public void Memory_integrity_uses_DeviceGuard_configured_and_running_id_two_without_setting_a_firmware_lock()
    {
        var capture = Method("CaptureMemoryIntegrity");
        var mutation = Method("SetMemoryIntegrity");

        Assert.Contains("deviceGuard.ConfiguredServices.Contains(2)", capture, StringComparison.Ordinal);
        Assert.Contains("deviceGuard.RunningServices.Contains(2)", capture, StringComparison.Ordinal);
        Assert.Contains("key.SetValue(\"Enabled\", enabled ? 1 : 0", mutation, StringComparison.Ordinal);
        Assert.Contains("key.SetValue(\"Locked\", 0", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue(\"Locked\", 1", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("Mandatory", mutation, StringComparison.Ordinal);
    }

    [Fact]
    public void Virtual_machine_platform_changes_only_the_named_feature_without_reboot_or_dependency_install()
    {
        var mutation = Method("SetVirtualMachinePlatform");
        Assert.Contains("private const string DismFeatureName = \"VirtualMachinePlatform\"", Source, StringComparison.Ordinal);
        Assert.Contains("/NoRestart", mutation, StringComparison.Ordinal);
        Assert.Contains("/LimitAccess", mutation, StringComparison.Ordinal);
        Assert.Contains("$\"/FeatureName:{DismFeatureName}\"", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("/All", mutation, StringComparison.Ordinal);
        Assert.Contains("\"Enable Pending\"", Method("CaptureVirtualMachinePlatform"), StringComparison.Ordinal);
        Assert.Contains("\"Disable Pending\"", Method("CaptureVirtualMachinePlatform"), StringComparison.Ordinal);
    }

    [Fact]
    public void Instant_context_menu_tweak_uses_only_menu_animation_system_parameters()
    {
        var capture = Method("CaptureInstantContextMenus");
        var mutation = Method("SetInstantContextMenus");
        Assert.Contains("SpiGetMenuAnimation", capture, StringComparison.Ordinal);
        Assert.Contains("SpiSetMenuAnimation", mutation, StringComparison.Ordinal);
        Assert.Contains("SpifUpdateIniFile | SpifSendChange", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuShowDelay", Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classic_context_menu_removes_only_the_exact_owned_child_key()
    {
        var mutation = Method("SetClassicContextMenu");
        Assert.Contains("Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32", Source, StringComparison.Ordinal);
        Assert.Contains("key.SetValue(string.Empty, string.Empty, RegistryValueKind.String)", mutation, StringComparison.Ordinal);
        Assert.Contains("DeleteSubKey(\"InprocServer32\", throwOnMissingSubKey: false)", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSubKeyTree", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSubKey(\"{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", mutation, StringComparison.Ordinal);
    }

    [Fact]
    public void Wake_sign_in_reads_and_writes_both_ac_and_dc_for_the_exact_console_lock_setting()
    {
        var capture = Method("CapturePasswordOnWake");
        var mutation = Method("SetPasswordOnWake");
        Assert.Contains("0e796bdb-100d-47d6-a2d5-f7d2daa51f51", Source, StringComparison.Ordinal);
        Assert.Contains("ReadPowerValue(scheme, ac: true)", capture, StringComparison.Ordinal);
        Assert.Contains("ReadPowerValue(scheme, ac: false)", capture, StringComparison.Ordinal);
        Assert.Contains("if (ac != dc)", capture, StringComparison.Ordinal);
        Assert.Contains("var value = enabled ? 0u : 1u", mutation, StringComparison.Ordinal);
        Assert.Contains("PowerWriteACValueIndex", mutation, StringComparison.Ordinal);
        Assert.Contains("PowerWriteDCValueIndex", mutation, StringComparison.Ordinal);
        Assert.Contains("PowerSetActiveScheme", mutation, StringComparison.Ordinal);
        var policy = Method("IsPasswordOnWakePolicyManaged");
        Assert.Contains("Software\\Policies\\Microsoft\\Windows\\System\\Power", policy, StringComparison.Ordinal);
        Assert.Contains("PromptPasswordOnResume", policy, StringComparison.Ordinal);
    }

    private static string Method(string name)
    {
        var match = Regex.Match(Source, $@"private static [^\r\n]*\b{Regex.Escape(name)}\(");
        Assert.True(match.Success, $"Method {name} was not found.");
        var start = match.Index;
        var next = Source.IndexOf("\n    private static ", start + 1, StringComparison.Ordinal);
        return next < 0 ? Source[start..] : Source[start..next];
    }

    private static string ReadOperatingSystemSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "SteamInputAddonforClaw", "WindowsTweaks", "WindowsTweaksOperatingSystem.cs");
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new DirectoryNotFoundException("WindowsTweaksOperatingSystem.cs was not found.");
    }
}
