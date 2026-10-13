using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.WindowsTweaks;

internal sealed class WindowsTweaksOperatingSystem : IWindowsTweaksOperatingSystem
{
    private const string HvciRegistryPath = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string DeviceGuardRegistryPath = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string DeviceGuardPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";
    private const string DeviceGuardMdmPath = @"SOFTWARE\Microsoft\PolicyManager\current\device\DeviceGuard";
    private const string ClassicContextMenuPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string ClassicContextMenuParentPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string DismFeatureName = "VirtualMachinePlatform";
    private const uint SpiGetMenuAnimation = 0x1002;
    private const uint SpiSetMenuAnimation = 0x1003;
    private const uint SpiGetMenuShowDelay = 0x006A;
    private const uint SpiSetMenuShowDelay = 0x006B;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private static readonly Guid NoSubgroup = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid ConsoleLockSetting = new("0e796bdb-100d-47d6-a2d5-f7d2daa51f51");

    public WindowsTweakObservation Capture(FrontendWindowsTweakId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return new(false, false, null, null, "Windows 11 is required.");

        return id switch
        {
            FrontendWindowsTweakId.MemoryIntegrity => CaptureMemoryIntegrity(cancellationToken),
            FrontendWindowsTweakId.VirtualMachinePlatform => CaptureVirtualMachinePlatform(cancellationToken),
            FrontendWindowsTweakId.InstantContextMenus => CaptureInstantContextMenus(),
            FrontendWindowsTweakId.ClassicContextMenu => CaptureClassicContextMenu(),
            FrontendWindowsTweakId.DisablePasswordOnWake => CapturePasswordOnWake(),
            _ => new(false, false, null, null, "This setting is not supported.")
        };
    }

    public void Set(FrontendWindowsTweakId id, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("Windows 11 is required.");

        switch (id)
        {
            case FrontendWindowsTweakId.MemoryIntegrity:
                SetMemoryIntegrity(enabled, cancellationToken);
                break;
            case FrontendWindowsTweakId.VirtualMachinePlatform:
                SetVirtualMachinePlatform(enabled, cancellationToken);
                break;
            case FrontendWindowsTweakId.InstantContextMenus:
                SetInstantContextMenus(enabled);
                break;
            case FrontendWindowsTweakId.ClassicContextMenu:
                SetClassicContextMenu(enabled);
                break;
            case FrontendWindowsTweakId.DisablePasswordOnWake:
                SetPasswordOnWake(enabled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    private static WindowsTweakObservation CaptureMemoryIntegrity(CancellationToken cancellationToken)
    {
        var deviceGuard = ReadDeviceGuard(cancellationToken);
        var enabledValue = WindowsTweakRegistry.Read(Registry.LocalMachine, HvciRegistryPath, "Enabled");
        var configured = ResolveMemoryIntegrityConfiguredState(
            enabledValue, deviceGuard.ConfiguredServices.Contains(2));
        var running = deviceGuard.RunningServices.Contains(2);
        var managed = IsMemoryIntegrityPolicyManaged();
        var invalidConfiguration = enabledValue.ValueExists && configured is null;
        var detail = managed
            ? "Managed or locked by Windows policy or firmware."
            : invalidConfiguration
                ? "Memory Integrity has an unrecognized registry configuration value."
                : configured == true && !running
                    ? "Configured, but Windows does not report it running; a restart or system requirement may be pending."
                    : null;
        return new(true, managed, configured, invalidConfiguration ? null : running, detail);
    }

    internal static bool? ResolveMemoryIntegrityConfiguredState(
        WindowsTweakRegistryValueRead registryEnabled,
        bool wmiConfigured)
    {
        if (!registryEnabled.ValueExists)
            return wmiConfigured;
        if (registryEnabled.Kind != RegistryValueKind.DWord || registryEnabled.Value is not int value)
            return null;

        return value switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
    }

    private static void SetMemoryIntegrity(bool enabled, CancellationToken cancellationToken)
    {
        var current = CaptureMemoryIntegrity(cancellationToken);
        if (current.Managed)
            throw new UnauthorizedAccessException("Windows policy or firmware owns Memory Integrity.");
        if (current.ConfiguredEnabled is null)
            throw new InvalidOperationException("Windows could not determine Memory Integrity state.");

        var locked = ReadDword(Registry.LocalMachine, HvciRegistryPath, "Locked");
        if (locked == 1)
            throw new UnauthorizedAccessException("Memory Integrity is UEFI locked.");

        WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, "Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        if (enabled && locked is null)
            WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, "Locked", 0, RegistryValueKind.DWord);
    }

    private static WindowsTweakObservation CaptureVirtualMachinePlatform(CancellationToken cancellationToken)
    {
        var result = RunDism(["/Online", "/Get-FeatureInfo", $"/FeatureName:{DismFeatureName}", "/English"], cancellationToken);
        if (result.ExitCode != 0)
        {
            var code = unchecked((uint)result.ExitCode);
            if (IsDismFeatureUnavailable(code))
                return new(false, false, null, null, "Virtual Machine Platform payload is unavailable on this Windows installation.");
            return new(true, IsDismPolicyFailure(code), null, null, FormatDismFailure(code));
        }

        var match = System.Text.RegularExpressions.Regex.Match(result.Output, @"(?im)^\s*State\s*:\s*(.+?)\s*$");
        if (!match.Success)
            return new(true, false, null, null, "DISM did not return a recognizable feature state.");

        var state = match.Groups[1].Value.Trim();
        if (state.Contains("payload removed", StringComparison.OrdinalIgnoreCase))
            return new(false, false, null, null, "Virtual Machine Platform payload is unavailable on this Windows installation.");
        if (state.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) return new(true, false, true, true);
        if (state.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return new(true, false, false, false);
        if (state.Equals("Enable Pending", StringComparison.OrdinalIgnoreCase)) return new(true, false, true, false);
        if (state.Equals("Disable Pending", StringComparison.OrdinalIgnoreCase)) return new(true, false, false, true);
        if (state.Contains("unknown feature", StringComparison.OrdinalIgnoreCase)
            || state.Contains("not applicable", StringComparison.OrdinalIgnoreCase))
            return new(false, false, null, null, "Virtual Machine Platform is unavailable on this Windows edition.");
        return new(true, false, null, null, "DISM returned an unrecognized feature state.");
    }

    private static void SetVirtualMachinePlatform(bool enabled, CancellationToken cancellationToken)
    {
        var current = CaptureVirtualMachinePlatform(cancellationToken);
        if (!current.Supported || current.Managed || current.ConfiguredEnabled is null)
            throw new InvalidOperationException(current.Detail ?? "Virtual Machine Platform cannot currently be changed.");

        var result = RunDism(BuildDismFeatureMutationArguments(enabled), cancellationToken);
        if (result.ExitCode != 0)
        {
            var code = unchecked((uint)result.ExitCode);
            if (IsDismFeatureUnavailable(code))
                throw new InvalidOperationException("Virtual Machine Platform payload is unavailable; no download was attempted.");
            if (IsDismPolicyFailure(code))
                throw new UnauthorizedAccessException("Windows servicing policy prevented this feature change.");
            throw new Win32Exception(result.ExitCode, "DISM did not accept the Virtual Machine Platform request.");
        }
    }

    internal static IReadOnlyList<string> BuildDismFeatureMutationArguments(bool enabled)
    {
        var arguments = new List<string>
        {
            "/Online",
            enabled ? "/Enable-Feature" : "/Disable-Feature",
            $"/FeatureName:{DismFeatureName}",
            "/NoRestart",
            "/English",
        };
        if (enabled)
            arguments.Add("/LimitAccess");
        return arguments;
    }

    private static WindowsTweakObservation CaptureInstantContextMenus()
    {
        var animation = 0;
        if (!SystemParametersInfoW(SpiGetMenuAnimation, 0, ref animation, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var delayMs = 0;
        if (!SystemParametersInfoW(SpiGetMenuShowDelay, 0, ref delayMs, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return ResolveInstantContextMenus(animation != 0, delayMs);
    }

    internal static WindowsTweakObservation ResolveInstantContextMenus(bool animationEnabled, int delayMs)
    {
        if (!animationEnabled && delayMs == 10)
            return new(true, false, true, true);
        if (animationEnabled && delayMs == 400)
            return new(true, false, false, false);

        return new(true, false, false, null,
            "Menu animation and submenu delay do not match either Faster Menus setting.",
            IsMixed: true);
    }

    private static void SetInstantContextMenus(bool enabled)
    {
        var menuAnimation = enabled ? 0 : 1;
        if (!SystemParametersInfoW(SpiSetMenuAnimation, 0, ref menuAnimation, SpifUpdateIniFile | SpifSendChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        uint targetDelay = enabled ? 10u : 400u;
        if (!SystemParametersInfoW(SpiSetMenuShowDelay, targetDelay, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static WindowsTweakObservation CaptureClassicContextMenu()
    {
        var registration = WindowsTweakRegistry.Read(Registry.CurrentUser, ClassicContextMenuPath, string.Empty);
        var configured = false;
        if (registration.KeyExists)
        {
            if (!registration.ValueExists)
                return new(true, true, null, null, "The exact shell registration exists but is not owned by this tweak.");
            if (registration.Kind != RegistryValueKind.String
                || registration.Value is not string value
                || value.Length != 0)
                return new(true, true, null, null, "The exact shell registration has a different value and was left unchanged.");
            configured = true;
        }

        var parentWriteTime = ReadRegistryLastWriteTime(Registry.CurrentUser, ClassicContextMenuParentPath);
        var shellStartTime = ReadShellProcessStartTimeUtc();
        var restartPending = parentWriteTime is { } writeTime && writeTime > shellStartTime;
        return new(true, false, configured, restartPending ? null : configured, ExplorerRestartPending: restartPending);
    }

    private static void SetClassicContextMenu(bool enabled)
    {
        if (enabled)
        {
            WindowsTweakRegistry.Write(Registry.CurrentUser, ClassicContextMenuPath, string.Empty, string.Empty, RegistryValueKind.String);
            return;
        }

        using var parent = Registry.CurrentUser.OpenSubKey(ClassicContextMenuParentPath, writable: true);
        parent?.DeleteSubKey("InprocServer32", throwOnMissingSubKey: false);
    }

    private static WindowsTweakObservation CapturePasswordOnWake()
    {
        if (IsPasswordOnWakePolicyManaged())
            return new(true, true, null, null, "Managed by Windows policy.");

        var (scheme, handle) = GetActivePowerScheme();
        try
        {
            var ac = ReadPowerValue(scheme, ac: true);
            var dc = ReadPowerValue(scheme, ac: false);
            if (ac is not (0 or 1) || dc is not (0 or 1))
                return new(true, false, null, null, "Windows returned an unsupported AC/DC sign-in value.");
            if (ac != dc)
                return new(true, false, null, null, "AC and battery sign-in values differ; the setting is indeterminate.");

            var disabledPassword = ac == 0;
            return new(true, false, disabledPassword, disabledPassword);
        }
        finally
        {
            LocalFree(handle);
        }
    }

    private static void SetPasswordOnWake(bool enabled)
    {
        if (IsPasswordOnWakePolicyManaged())
            throw new UnauthorizedAccessException("Windows policy owns the sign-in-on-resume setting.");

        var (scheme, handle) = GetActivePowerScheme();
        try
        {
            var value = enabled ? 0u : 1u;
            var subgroup = NoSubgroup;
            var setting = ConsoleLockSetting;
            CheckPowerResult(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value));
            subgroup = NoSubgroup;
            setting = ConsoleLockSetting;
            CheckPowerResult(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value));
            CheckPowerResult(PowerSetActiveScheme(IntPtr.Zero, ref scheme));
        }
        finally
        {
            LocalFree(handle);
        }
    }

    private static int ReadPowerValue(Guid scheme, bool ac)
    {
        var subgroup = NoSubgroup;
        var setting = ConsoleLockSetting;
        uint value;
        var result = ac
            ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
        CheckPowerResult(result);
        return checked((int)value);
    }

    private static (Guid Scheme, IntPtr Handle) GetActivePowerScheme()
    {
        CheckPowerResult(PowerGetActiveScheme(IntPtr.Zero, out var handle));
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("The active Windows power scheme is unavailable.");
        return (Marshal.PtrToStructure<Guid>(handle), handle);
    }

    private static void CheckPowerResult(uint result)
    {
        if (result != 0) throw new Win32Exception(unchecked((int)result));
    }

    private static bool IsMemoryIntegrityPolicyManaged()
    {
        if (ReadDword(Registry.LocalMachine, DeviceGuardRegistryPath, "Locked") == 1)
            return true;
        if (ReadDword(Registry.LocalMachine, HvciRegistryPath, "Locked") == 1)
            return true;

        return HasPolicyValues(Registry.LocalMachine, DeviceGuardPolicyPath,
                ["EnableVirtualizationBasedSecurity", "RequirePlatformSecurityFeatures", "HypervisorEnforcedCodeIntegrity"])
            || HasPolicyValues(Registry.LocalMachine, DeviceGuardMdmPath,
                ["EnableVirtualizationBasedSecurity", "RequirePlatformSecurityFeatures", "HypervisorEnforcedCodeIntegrity"]);
    }

    private static bool IsPasswordOnWakePolicyManaged()
    {
        var settingPath = $@"SOFTWARE\Policies\Microsoft\Power\PowerSettings\{ConsoleLockSetting:B}";
        var mdmPath = $@"SOFTWARE\Microsoft\PolicyManager\current\device\Power\PowerSettings\{ConsoleLockSetting:B}";
        const string userPolicyPath = @"Software\Policies\Microsoft\Windows\System\Power";
        return HasPolicyValues(Registry.LocalMachine, settingPath, ["ACSettingIndex", "DCSettingIndex"])
            || HasPolicyValues(Registry.LocalMachine, mdmPath, ["ACSettingIndex", "DCSettingIndex"])
            || HasPolicyValues(Registry.CurrentUser, userPolicyPath, ["PromptPasswordOnResume"]);
    }

    private static bool HasPolicyValues(RegistryKey root, string path, IReadOnlyCollection<string> names)
    {
        using var key = root.OpenSubKey(path, writable: false);
        return key?.GetValueNames().Any(name => names.Contains(name, StringComparer.OrdinalIgnoreCase)) == true;
    }

    private static int? ReadDword(RegistryKey root, string path, string name)
    {
        var read = WindowsTweakRegistry.Read(root, path, name);
        if (!read.ValueExists)
            return null;
        if (read.Kind != RegistryValueKind.DWord || read.Value is not int value)
            throw new InvalidOperationException("Windows returned an invalid DWORD registry value for a Windows tweak.");
        return value;
    }

    private static DeviceGuardSnapshot ReadDeviceGuard(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\DeviceGuard");
        var query = new ObjectQuery("SELECT SecurityServicesConfigured, SecurityServicesRunning FROM Win32_DeviceGuard");
        var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = false };
        using var searcher = new ManagementObjectSearcher(
            scope, query, options);
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var configured = ReadServiceIds(item["SecurityServicesConfigured"]);
                var running = ReadServiceIds(item["SecurityServicesRunning"]);
                return new(configured, running);
            }
        }
        throw new InvalidOperationException("Win32_DeviceGuard returned no instance.");
    }

    private static int[] ReadServiceIds(object? value)
    {
        if (value is not Array entries)
            throw new InvalidOperationException("Win32_DeviceGuard returned an invalid service list.");
        return entries.Cast<object>().Select(entry => Convert.ToInt32(entry, CultureInfo.InvariantCulture)).ToArray();
    }

    private static DismResult RunDism(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows))
            throw new InvalidOperationException("Windows system directory is unavailable.");
        var start = new ProcessStartInfo(Path.Combine(windows, "System32", "dism.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("DISM could not be started.");
        using var registration = cancellationToken.Register(static state =>
        {
            var child = (Process)state!;
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch { }
        }, process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            throw new TimeoutException("DISM exceeded the 120-second operation bound.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        Task.WaitAll(stdout, stderr);
        return new(process.ExitCode, string.Concat(stdout.GetAwaiter().GetResult(), Environment.NewLine, stderr.GetAwaiter().GetResult()));
    }

    private static bool IsDismFeatureUnavailable(uint code) => code is 0x800F080C or 0x800F081F or 0x800F0906 or 0x800F081E;
    private static bool IsDismPolicyFailure(uint code) => code is 0x800F0900 or 0x800F0922 or 0x80070005;
    private static string FormatDismFailure(uint code) => $"DISM query failed (0x{code:X8}).";

    private static DateTime? ReadRegistryLastWriteTime(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path, writable: false);
        if (key is null) return null;
        var result = RegQueryInfoKey(key.Handle,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            out _, out _, out _, out _, out _, out _, out _, out long lastWrite);
        if (result != 0) throw new Win32Exception(result);
        return DateTime.FromFileTimeUtc(lastWrite);
    }

    private static DateTime ReadShellProcessStartTimeUtc()
    {
        var shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero || GetWindowThreadProcessId(shellWindow, out var processId) == 0 || processId == 0)
            throw new InvalidOperationException("The Windows shell process could not be identified.");
        using var process = Process.GetProcessById(checked((int)processId));
        return process.StartTime.ToUniversalTime();
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref int value, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, IntPtr value, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW", SetLastError = true)]
    private static extern int RegQueryInfoKey(
        SafeRegistryHandle key,
        IntPtr keyClass,
        IntPtr keyClassLength,
        IntPtr reserved,
        out uint subKeyCount,
        out uint maxSubKeyLength,
        out uint maxClassLength,
        out uint valueCount,
        out uint maxValueNameLength,
        out uint maxValueLength,
        out uint securityDescriptorLength,
        out long lastWriteTime);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint valueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint valueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    private sealed record DeviceGuardSnapshot(int[] ConfiguredServices, int[] RunningServices);
    private sealed record DismResult(int ExitCode, string Output);
}
