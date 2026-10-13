using Microsoft.Win32;
using Xunit;
using System.Text.RegularExpressions;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.WindowsTweaks;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsTweaksOperatingSystemContractTests
{
    private static readonly string Source = ReadOperatingSystemSource();

    [Fact]
    public void Memory_integrity_uses_DeviceGuard_configured_and_running_id_two_without_setting_a_firmware_lock()
    {
        var capture = Method("CaptureMemoryIntegrity");
        var mutation = Method("SetMemoryIntegrity");

        Assert.Contains("WindowsTweakRegistry.Read(Registry.LocalMachine, HvciRegistryPath, \"Enabled\")", capture, StringComparison.Ordinal);
        Assert.Contains("deviceGuard.ConfiguredServices.Contains(2)", capture, StringComparison.Ordinal);
        Assert.Contains("deviceGuard.RunningServices.Contains(2)", capture, StringComparison.Ordinal);
        Assert.Contains("var managed = IsMemoryIntegrityPolicyManaged()", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("CodeIntegrityPolicyEnforcementStatus", capture, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, \"Enabled\"", mutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, \"Locked\"", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue(\"Locked\", 1", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("Mandatory", mutation, StringComparison.Ordinal);

        var policy = Method("IsMemoryIntegrityPolicyManaged");
        Assert.Contains("ReadDword(Registry.LocalMachine, DeviceGuardRegistryPath, \"Locked\") == 1", policy, StringComparison.Ordinal);
        Assert.Contains("ReadDword(Registry.LocalMachine, HvciRegistryPath, \"Locked\") == 1", policy, StringComparison.Ordinal);
        var readDword = Method("ReadDword");
        Assert.Contains("WindowsTweakRegistry.Read(root, path, name)", readDword, StringComparison.Ordinal);
        Assert.Contains("read.Kind != RegistryValueKind.DWord", readDword, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", readDword, StringComparison.Ordinal);
    }

    [Fact]
    public void HVCI_registry_configuration_wins_over_stale_WMI_configuration()
    {
        var configured = WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(true, true, 0, RegistryValueKind.DWord), wmiConfigured: true);
        var snapshot = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.MemoryIntegrity,
            new(true, false, configured, EffectiveEnabled: true));

        Assert.False(snapshot.ConfiguredEnabled);
        Assert.Equal(FrontendWindowsTweakState.DisablePendingRestart, snapshot.State);
        Assert.True(snapshot.CanChange);
    }

    [Fact]
    public void HVCI_configuration_requires_an_exact_dword_and_only_uses_WMI_when_the_value_is_absent()
    {
        Assert.False(WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(true, true, 0, RegistryValueKind.DWord), wmiConfigured: true));
        Assert.True(WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(true, true, 1, RegistryValueKind.DWord), wmiConfigured: false));
        Assert.Null(WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(true, true, 2, RegistryValueKind.DWord), wmiConfigured: true));
        Assert.Null(WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(true, true, "0", RegistryValueKind.String), wmiConfigured: true));
        Assert.True(WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(true, false, null, null), wmiConfigured: true));
        Assert.False(WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
            new(false, false, null, null), wmiConfigured: false));

        var customValue = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.MemoryIntegrity,
            new(true, false,
                WindowsTweaksOperatingSystem.ResolveMemoryIntegrityConfiguredState(
                    new(true, true, 2, RegistryValueKind.DWord), wmiConfigured: true),
                EffectiveEnabled: null,
                Detail: "Memory Integrity has an unrecognized registry configuration value."));
        Assert.Equal(FrontendWindowsTweakState.Unknown, customValue.State);
        Assert.False(customValue.CanChange);
    }

    [Fact]
    public void Virtual_machine_platform_changes_only_the_named_feature_without_reboot_or_dependency_install()
    {
        var mutation = Method("SetVirtualMachinePlatform");
        Assert.Contains("private const string DismFeatureName = \"VirtualMachinePlatform\"", Source, StringComparison.Ordinal);
        Assert.Contains("/NoRestart", mutation, StringComparison.Ordinal);
        var enableArguments = WindowsTweaksOperatingSystem.BuildDismFeatureMutationArguments(enabled: true);
        var disableArguments = WindowsTweaksOperatingSystem.BuildDismFeatureMutationArguments(enabled: false);
        Assert.Contains("/NoRestart", enableArguments);
        Assert.Contains("/NoRestart", disableArguments);
        Assert.Contains("/FeatureName:VirtualMachinePlatform", enableArguments);
        Assert.DoesNotContain("/All", enableArguments);
        Assert.DoesNotContain("/All", disableArguments);
        Assert.Contains("/LimitAccess", enableArguments);
        Assert.DoesNotContain("/LimitAccess", disableArguments);
        Assert.Contains("/Disable-Feature", disableArguments);
        Assert.Contains("/Enable-Feature", enableArguments);
        Assert.Contains("BuildDismFeatureMutationArguments(enabled)", mutation, StringComparison.Ordinal);
        Assert.Contains("\"Enable Pending\"", Method("CaptureVirtualMachinePlatform"), StringComparison.Ordinal);
        Assert.Contains("\"Disable Pending\"", Method("CaptureVirtualMachinePlatform"), StringComparison.Ordinal);
    }

    [Fact]
    public void Faster_menus_tweak_reads_and_writes_animation_and_submenu_delay()
    {
        var capture = Method("CaptureInstantContextMenus");
        var mutation = Method("SetInstantContextMenus");
        Assert.Contains("SpiGetMenuAnimation", capture, StringComparison.Ordinal);
        Assert.Contains("SpiGetMenuShowDelay", capture, StringComparison.Ordinal);
        Assert.Contains("SystemParametersInfoW(SpiGetMenuShowDelay, 0, ref delayMs, 0)", capture, StringComparison.Ordinal);
        Assert.Contains("SpiSetMenuAnimation", mutation, StringComparison.Ordinal);
        Assert.Contains("SpiSetMenuShowDelay", mutation, StringComparison.Ordinal);
        Assert.Contains("SystemParametersInfoW(SpiSetMenuShowDelay, targetDelay, ref ignored, SpifUpdateIniFile | SpifSendChange)", mutation, StringComparison.Ordinal);
        Assert.Contains("0x006A", Source, StringComparison.Ordinal);
        Assert.Contains("0x006B", Source, StringComparison.Ordinal);
        Assert.Contains("enabled ? 0 : 1", mutation, StringComparison.Ordinal);
        Assert.Contains("enabled ? 10u : 400u", mutation, StringComparison.Ordinal);
        Assert.Contains("SpifUpdateIniFile | SpifSendChange", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsTweakRegistry.Write", mutation, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(capture, "throw new Win32Exception\\(Marshal.GetLastWin32Error\\(\\)\\)").Count);
        Assert.True(mutation.IndexOf("SpiSetMenuAnimation", StringComparison.Ordinal)
            < mutation.IndexOf("SpiSetMenuShowDelay", StringComparison.Ordinal));
    }

    [Fact]
    public void Faster_menus_capture_maps_only_the_exact_on_and_off_pairs()
    {
        var enabled = WindowsTweaksOperatingSystem.ResolveInstantContextMenus(animationEnabled: false, delayMs: 10);
        var disabled = WindowsTweaksOperatingSystem.ResolveInstantContextMenus(animationEnabled: true, delayMs: 400);

        Assert.True(enabled.ConfiguredEnabled);
        Assert.True(enabled.EffectiveEnabled);
        Assert.False(enabled.IsMixed);
        Assert.False(disabled.ConfiguredEnabled);
        Assert.False(disabled.EffectiveEnabled);
        Assert.False(disabled.IsMixed);
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 10)]
    [InlineData(false, 200)]
    [InlineData(true, 200)]
    public void Faster_menus_custom_pairs_are_explicitly_mixed_and_remain_changeable(bool animationEnabled, int delayMs)
    {
        var observation = WindowsTweaksOperatingSystem.ResolveInstantContextMenus(animationEnabled, delayMs);
        var snapshot = WindowsTweaksStateMapper.Map(FrontendWindowsTweakId.InstantContextMenus, observation);

        Assert.True(observation.IsMixed);
        Assert.Equal(FrontendWindowsTweakState.Mixed, snapshot.State);
        Assert.False(snapshot.ConfiguredEnabled);
        Assert.Null(snapshot.EffectiveEnabled);
        Assert.True(snapshot.CanChange);
        Assert.Contains("do not match", snapshot.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classic_context_menu_removes_only_the_exact_owned_child_key()
    {
        var mutation = Method("SetClassicContextMenu");
        Assert.Contains("Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32", Source, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.CurrentUser, ClassicContextMenuPath, string.Empty, string.Empty, RegistryValueKind.String)", mutation, StringComparison.Ordinal);
        Assert.Contains("DeleteSubKey(\"InprocServer32\", throwOnMissingSubKey: false)", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSubKeyTree", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSubKey(\"{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", mutation, StringComparison.Ordinal);
        var capture = Method("CaptureClassicContextMenu");
        Assert.Contains("WindowsTweakRegistry.Read(Registry.CurrentUser, ClassicContextMenuPath, string.Empty)", capture, StringComparison.Ordinal);
        Assert.Contains("if (!registration.ValueExists)", capture, StringComparison.Ordinal);
        Assert.Contains("registration.Kind != RegistryValueKind.String", capture, StringComparison.Ordinal);
        Assert.Contains("value.Length != 0", capture, StringComparison.Ordinal);
    }

    [Fact]
    public void Wake_sign_in_reads_and_writes_both_ac_and_dc_for_the_exact_console_lock_setting()
    {
        var capture = Method("CapturePasswordOnWake");
        var mutation = Method("SetPasswordOnWake");
        Assert.Contains("0e796bdb-100d-47d6-a2d5-f7d2daa51f51", Source, StringComparison.Ordinal);
        Assert.Contains("fea3413e-7e05-4911-9a71-700331f1c294", Source, StringComparison.Ordinal);
        Assert.Contains("ReadPowerValue", capture, StringComparison.Ordinal);
        Assert.Contains("NoSubgroup", mutation, StringComparison.Ordinal);
        Assert.Contains("NoSubgroup", Method("ReadPowerValue"), StringComparison.Ordinal);
        Assert.DoesNotContain("SleepSubgroup", Source, StringComparison.Ordinal);
        Assert.Contains("ReadPowerValue(scheme, ac: true)", capture, StringComparison.Ordinal);
        Assert.Contains("ReadPowerValue(scheme, ac: false)", capture, StringComparison.Ordinal);
        Assert.Contains("if (ac != dc)", capture, StringComparison.Ordinal);
        Assert.Contains("var value = enabled ? 0u : 1u", mutation, StringComparison.Ordinal);
        Assert.Contains("PowerWriteACValueIndex", mutation, StringComparison.Ordinal);
        Assert.Contains("PowerWriteDCValueIndex", mutation, StringComparison.Ordinal);
        Assert.Contains("PowerSetActiveScheme", mutation, StringComparison.Ordinal);
        var policy = Method("IsPasswordOnWakePolicyManaged");
        Assert.Contains("PowerSettings\\{ConsoleLockSetting:B}", policy, StringComparison.Ordinal);
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
