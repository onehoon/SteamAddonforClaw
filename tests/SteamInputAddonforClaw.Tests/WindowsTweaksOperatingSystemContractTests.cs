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

        Assert.Contains("private const string HvciRegistryPath = @\"SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity\"", Source, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Read(Registry.LocalMachine, HvciRegistryPath, \"Enabled\")", capture, StringComparison.Ordinal);
        Assert.Contains("deviceGuard.ConfiguredServices.Contains(2)", capture, StringComparison.Ordinal);
        Assert.Contains("deviceGuard.RunningServices.Contains(2)", capture, StringComparison.Ordinal);
        Assert.Contains("var managed = IsMemoryIntegrityPolicyManaged()", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("CodeIntegrityPolicyEnforcementStatus", capture, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, \"Enabled\"", mutation, StringComparison.Ordinal);
        Assert.Contains("enabled ? 1 : 0, RegistryValueKind.DWord", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureMemoryIntegrity", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDeviceGuard", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, \"Locked\"", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue(\"Locked\", 1", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("Mandatory", mutation, StringComparison.Ordinal);

        var policy = Method("IsMemoryIntegrityPolicyManaged");
        Assert.Contains("HasMemoryIntegrityPolicy() || IsMemoryIntegrityLocked()", policy, StringComparison.Ordinal);
        var locked = Method("IsMemoryIntegrityLocked");
        Assert.Contains("ReadDword(Registry.LocalMachine, DeviceGuardRegistryPath, \"Locked\") == 1", locked, StringComparison.Ordinal);
        Assert.Contains("ReadDword(Registry.LocalMachine, HvciRegistryPath, \"Locked\") == 1", locked, StringComparison.Ordinal);
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
    public void Virtual_machine_platform_uses_windows_optional_feature_commands_without_reboot()
    {
        var capture = WindowsTweaksOperatingSystem.BuildVmpCaptureScript();
        var enable = WindowsTweaksOperatingSystem.BuildVmpMutationScript(enabled: true);
        var disable = WindowsTweaksOperatingSystem.BuildVmpMutationScript(enabled: false);

        Assert.Contains("Get-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -ErrorAction Stop", capture, StringComparison.Ordinal);
        Assert.Contains("Enable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -All -NoRestart -ErrorAction Stop", enable, StringComparison.Ordinal);
        Assert.Contains("Disable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform' -NoRestart -ErrorAction Stop", disable, StringComparison.Ordinal);
        Assert.Contains("VMP_STATE=", capture, StringComparison.Ordinal);
        Assert.Contains("VMP_RESTART=", enable, StringComparison.Ordinal);
        Assert.Contains("VMP_RESTART=", disable, StringComparison.Ordinal);
        Assert.Contains("[BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$failure.Exception.HResult), 0)", capture, StringComparison.Ordinal);
        Assert.Contains("VMP_ERROR_HRESULT=0x{0:X8}' -f $hr", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("-LimitAccess", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("-Remove", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("dism.exe", Source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-ExecutionPolicy", Source, StringComparison.OrdinalIgnoreCase);

        var runner = Method("RunVmpPowerShell");
        Assert.Contains("WindowsPowerShell", runner, StringComparison.Ordinal);
        Assert.Contains("Environment.Is64BitOperatingSystem", runner, StringComparison.Ordinal);
        Assert.Contains("Environment.Is64BitProcess", runner, StringComparison.Ordinal);
        Assert.Contains("Sysnative", runner, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add(\"-NoLogo\")", runner, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add(\"-NoProfile\")", runner, StringComparison.Ordinal);
        Assert.Contains("ArgumentList.Add(\"-NonInteractive\")", runner, StringComparison.Ordinal);
        Assert.Contains("WaitForExitAsync(timeout.Token)", runner, StringComparison.Ordinal);
        Assert.Contains("CancelAfter(VirtualMachinePlatformCommandTimeout)", runner, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource(cancellationToken)", runner, StringComparison.Ordinal);
        Assert.Contains("if (cancellationToken.IsCancellationRequested)", runner, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", runner, StringComparison.Ordinal);
        Assert.Contains("ReadBoundedOutputAsync(process.StandardOutput)", runner, StringComparison.Ordinal);
        Assert.Contains("ReadBoundedOutputAsync(process.StandardError)", runner, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Enabled", FrontendWindowsTweakState.Enabled, true, true)]
    [InlineData("Disabled", FrontendWindowsTweakState.Disabled, false, false)]
    [InlineData("EnablePending", FrontendWindowsTweakState.EnablePendingRestart, true, false)]
    [InlineData("DisablePending", FrontendWindowsTweakState.DisablePendingRestart, false, true)]
    [InlineData("DisabledWithPayloadRemoved", FrontendWindowsTweakState.Unsupported, null, null)]
    [InlineData("Unexpected", FrontendWindowsTweakState.Unknown, null, null)]
    public void Virtual_machine_platform_state_mapping_does_not_treat_pending_or_unknown_as_off(
        string state,
        FrontendWindowsTweakState expected,
        bool? configured,
        bool? effective)
    {
        var observation = WindowsTweaksOperatingSystem.ResolveVirtualMachinePlatformState(state);
        var snapshot = WindowsTweaksStateMapper.Map(FrontendWindowsTweakId.VirtualMachinePlatform, observation);

        Assert.Equal(expected, snapshot.State);
        Assert.Equal(configured, snapshot.ConfiguredEnabled);
        Assert.Equal(effective, snapshot.EffectiveEnabled);
        Assert.Equal(expected != FrontendWindowsTweakState.Unsupported && expected != FrontendWindowsTweakState.Unknown,
            snapshot.CanChange);
    }

    [Fact]
    public void Virtual_machine_platform_process_result_seam_requires_success_and_typed_markers()
    {
        var captureRunner = new WindowsTweaksOperatingSystem((operation, script, _) =>
        {
            Assert.Equal(VmpOperation.Read, operation);
            Assert.Contains("Get-WindowsOptionalFeature", script, StringComparison.Ordinal);
            return new(0, "VMP_STATE=EnablePending\r\n", string.Empty);
        });
        var pending = captureRunner.CaptureVirtualMachinePlatform(CancellationToken.None);
        Assert.True(pending.ConfiguredEnabled);
        Assert.False(pending.EffectiveEnabled);

        var mutationRunner = new WindowsTweaksOperatingSystem((operation, script, _) =>
        {
            Assert.Equal(VmpOperation.Enable, operation);
            Assert.Contains("-All -NoRestart", script, StringComparison.Ordinal);
            return new(0, "VMP_RESTART=True\r\n", string.Empty);
        });
        Assert.True(mutationRunner.SetVirtualMachinePlatform(true, CancellationToken.None).RestartRequired);

        var failedRunner = new WindowsTweaksOperatingSystem((_, _, _) =>
            new(1, "VMP_STATE=Enabled\r\n",
                "VMP_ERROR_HRESULT=0x800F081F\r\nVMP_ERROR_ID=SourceUnavailable\r\nVMP_ERROR_CATEGORY=InvalidOperation\r\nVMP_ERROR_DETAIL=Feature source unavailable.\r\n"));
        var failure = Assert.Throws<VmpCommandException>(() => failedRunner.CaptureVirtualMachinePlatform(CancellationToken.None));
        Assert.Equal("Exited", failure.Completion);
        Assert.Equal(1, failure.ProcessExitCode);
        Assert.Equal((uint?)0x800F081F, failure.WindowsErrorCode);
        Assert.Equal("SourceUnavailable", failure.WindowsErrorId);
        Assert.Throws<VmpCommandException>(() => failedRunner.SetVirtualMachinePlatform(true, CancellationToken.None));

        var markerlessRunner = new WindowsTweaksOperatingSystem((_, _, _) => new(0, "", string.Empty));
        Assert.Throws<VmpCommandException>(() => markerlessRunner.CaptureVirtualMachinePlatform(CancellationToken.None));
    }

    [Fact]
    public void Vmp_catch_script_preserves_negative_hresult_bits_in_windows_powershell()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var script = WindowsTweaksOperatingSystem.BuildVmpScript(
            "throw [System.Runtime.InteropServices.COMException]::new('Synthetic VMP failure', -2146233088)",
            "[Console]::Out.WriteLine('UNEXPECTED_SUCCESS')");
        var result = WindowsTweaksOperatingSystem.RunVmpPowerShell(
            VmpOperation.Read, script, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("VMP_ERROR_HRESULT=0x80131500", result.StandardError, StringComparison.Ordinal);

        var operatingSystem = new WindowsTweaksOperatingSystem((_, _, _) => result);
        var failure = Assert.Throws<VmpCommandException>(
            () => operatingSystem.CaptureVirtualMachinePlatform(CancellationToken.None));
        Assert.Equal((uint?)0x80131500, failure.WindowsErrorCode);
    }

    [Fact]
    public void Faster_menus_tweak_reads_and_writes_animation_and_submenu_delay()
    {
        var capture = Method("CaptureInstantContextMenus");
        var mutation = Method("ApplyInstantContextMenuSettings");
        var setter = Method("SetInstantContextMenus");
        Assert.Contains("SpiGetMenuAnimation", capture, StringComparison.Ordinal);
        Assert.Contains("SpiGetMenuShowDelay", capture, StringComparison.Ordinal);
        Assert.Contains("SystemParametersInfoW(SpiGetMenuShowDelay, 0, ref delayMs, 0)", capture, StringComparison.Ordinal);
        Assert.Contains("ApplyInstantContextMenuSettings(enabled", setter, StringComparison.Ordinal);
        Assert.Contains("SpiSetMenuAnimation", mutation, StringComparison.Ordinal);
        Assert.Contains("SpiSetMenuShowDelay", mutation, StringComparison.Ordinal);
        Assert.Contains("SystemParametersInfoW(uint action, uint parameter, IntPtr value, uint flags)", Source, StringComparison.Ordinal);
        Assert.Contains("SystemParametersInfoW(uint action, uint parameter, ref int value, uint flags)", Source, StringComparison.Ordinal);
        Assert.Contains("0x006A", Source, StringComparison.Ordinal);
        Assert.Contains("0x006B", Source, StringComparison.Ordinal);
        Assert.Contains("enabled ? 10u : 400u", mutation, StringComparison.Ordinal);
        Assert.Contains("SpifUpdateIniFile | SpifSendChange", mutation, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsTweakRegistry.Write", mutation, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(capture, "throw new Win32Exception\\(Marshal.GetLastWin32Error\\(\\)\\)").Count);
        Assert.True(mutation.IndexOf("SpiSetMenuAnimation", StringComparison.Ordinal)
            < mutation.IndexOf("SpiSetMenuShowDelay", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, 0x1003u, 0u, 0u)]
    [InlineData(false, 0x1003u, 0u, 1u)]
    public void Faster_menus_set_animation_with_bool_value_and_delay_with_uiparam(
        bool enabled,
        uint expectedAnimationAction,
        uint expectedAnimationParameter,
        uint expectedAnimationValue)
    {
        var calls = new List<(uint Action, uint Parameter, IntPtr Value, uint Flags)>();

        WindowsTweaksOperatingSystem.ApplyInstantContextMenuSettings(enabled,
            (action, parameter, value, flags) =>
            {
                calls.Add((action, parameter, value, flags));
                return true;
            });

        Assert.Equal(2, calls.Count);
        Assert.Equal((expectedAnimationAction, expectedAnimationParameter, new IntPtr(expectedAnimationValue), 3u), calls[0]);
        Assert.Equal((0x006Bu, enabled ? 10u : 400u, IntPtr.Zero, 3u), calls[1]);
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

    [Fact]
    public void Widgets_policy_support_fails_closed_by_windows_edition()
    {
        Assert.True(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("Professional"));
        Assert.True(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("Enterprise"));
        Assert.True(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("Education"));
        Assert.True(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("IoTEnterpriseS"));
        Assert.False(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("CoreSingleLanguage"));
        Assert.False(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("IoTCore"));
        Assert.Null(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport("FutureEdition"));
        Assert.Null(WindowsTweaksOperatingSystem.ResolveWidgetsPolicyEditionSupport(null));

        var capture = Method("CaptureDisableWindowsWidgets");
        var mutation = Method("SetDisableWindowsWidgets");
        var editionRead = Method("ReadWidgetsPolicyEditionSupport");
        Assert.Contains("WindowsTweakRegistry.Read(Registry.LocalMachine, WindowsCurrentVersionPath, \"EditionID\")", editionRead, StringComparison.Ordinal);
        Assert.Contains("ReadWidgetsPolicyEditionSupport()", capture, StringComparison.Ordinal);
        Assert.Contains("if (!editionSupport.Value)", capture, StringComparison.Ordinal);
        Assert.Contains("ReadWidgetsPolicyEditionSupport()", mutation, StringComparison.Ordinal);
        Assert.Contains("if (!editionSupport.Value)", mutation, StringComparison.Ordinal);
        Assert.True(mutation.IndexOf("if (!editionSupport.Value)", StringComparison.Ordinal)
            < mutation.IndexOf("WindowsTweakRegistry.Write", StringComparison.Ordinal));
        Assert.Contains("WindowsTweakRegistry.Read(Registry.LocalMachine, WidgetsPolicyPath, \"AllowNewsAndInterests\")", capture, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.LocalMachine, WidgetsPolicyPath, \"AllowNewsAndInterests\", 0, RegistryValueKind.DWord)", mutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, WidgetsPolicyPath, \"AllowNewsAndInterests\")", mutation, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_tweak_mapping_requires_all_exact_zero_dwords_or_all_values_absent()
    {
        var widgetsAbsent = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new WindowsTweakRegistryValueRead(false, false, null, null));
        var widgetsZero = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new WindowsTweakRegistryValueRead(true, true, 0, RegistryValueKind.DWord));
        var widgetsOne = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new WindowsTweakRegistryValueRead(true, true, 1, RegistryValueKind.DWord));
        var widgetsWrongKind = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new WindowsTweakRegistryValueRead(true, true, "0", RegistryValueKind.String));
        var absent = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new(false, false, null, null),
            new(true, false, null, null));
        var zero = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new(true, true, 0, RegistryValueKind.DWord),
            new(true, true, 0, RegistryValueKind.DWord));
        var nonzero = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new(true, true, 0, RegistryValueKind.DWord),
            new(true, true, 1, RegistryValueKind.DWord));
        var wrongKind = WindowsTweaksOperatingSystem.ResolveZeroDwordRegistryValues(
            new(true, true, 0, RegistryValueKind.DWord),
            new(true, true, "0", RegistryValueKind.String));

        Assert.False(widgetsAbsent.ConfiguredEnabled);
        Assert.False(widgetsAbsent.IsMixed);
        Assert.True(widgetsZero.ConfiguredEnabled);
        Assert.False(widgetsZero.IsMixed);
        Assert.True(widgetsOne.IsMixed);
        Assert.True(widgetsWrongKind.IsMixed);
        Assert.False(absent.ConfiguredEnabled);
        Assert.False(absent.IsMixed);
        Assert.True(zero.ConfiguredEnabled);
        Assert.False(zero.IsMixed);
        Assert.True(nonzero.IsMixed);
        Assert.True(wrongKind.IsMixed);

        foreach (var observation in new[] { nonzero, wrongKind })
        {
            var snapshot = WindowsTweaksStateMapper.Map(FrontendWindowsTweakId.DisableEdgeBackground, observation);
            Assert.Equal(FrontendWindowsTweakState.Mixed, snapshot.State);
            Assert.False(snapshot.ConfiguredEnabled);
            Assert.Null(snapshot.EffectiveEnabled);
            Assert.True(snapshot.CanChange);
        }
    }

    [Fact]
    public void Edge_and_startup_delay_touch_only_the_approved_exact_registry_values()
    {
        var edgeCapture = Method("CaptureDisableEdgeBackground");
        var edgeMutation = Method("SetDisableEdgeBackground");
        Assert.Contains("Registry.LocalMachine, EdgePolicyPath, \"StartupBoostEnabled\"", edgeCapture, StringComparison.Ordinal);
        Assert.Contains("Registry.LocalMachine, EdgePolicyPath, \"BackgroundModeEnabled\"", edgeCapture, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.LocalMachine, EdgePolicyPath, \"StartupBoostEnabled\", 0, RegistryValueKind.DWord)", edgeMutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.LocalMachine, EdgePolicyPath, \"BackgroundModeEnabled\", 0, RegistryValueKind.DWord)", edgeMutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, EdgePolicyPath, \"StartupBoostEnabled\")", edgeMutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, EdgePolicyPath, \"BackgroundModeEnabled\")", edgeMutation, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommended", edgeMutation, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", edgeMutation, StringComparison.Ordinal);

        var startupCapture = Method("CaptureDisableStartupAppDelay");
        var startupMutation = Method("SetDisableStartupAppDelay");
        Assert.Contains("Registry.CurrentUser, ExplorerSerializePath, \"StartupDelayInMSec\"", startupCapture, StringComparison.Ordinal);
        Assert.Contains("Registry.CurrentUser, ExplorerSerializePath, \"WaitForIdleState\"", startupCapture, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.CurrentUser, ExplorerSerializePath, \"StartupDelayInMSec\", 0, RegistryValueKind.DWord)", startupMutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.Write(Registry.CurrentUser, ExplorerSerializePath, \"WaitForIdleState\", 0, RegistryValueKind.DWord)", startupMutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.DeleteValue(Registry.CurrentUser, ExplorerSerializePath, \"StartupDelayInMSec\")", startupMutation, StringComparison.Ordinal);
        Assert.Contains("WindowsTweakRegistry.DeleteValue(Registry.CurrentUser, ExplorerSerializePath, \"WaitForIdleState\")", startupMutation, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoStartDelay", Source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSubKey", startupMutation, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", startupMutation, StringComparison.Ordinal);
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
        Assert.Contains("IsForeignClassicContextMenuRegistration(registration)", mutation, StringComparison.Ordinal);
        var capture = Method("CaptureClassicContextMenu");
        Assert.Contains("WindowsTweakRegistry.Read(Registry.CurrentUser, ClassicContextMenuPath, string.Empty)", capture, StringComparison.Ordinal);
        Assert.Contains("IsForeignClassicContextMenuRegistration(registration)", capture, StringComparison.Ordinal);
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
        var match = Regex.Match(Source, $@"(?:private|internal) static [^\r\n]*\b{Regex.Escape(name)}\(");
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
