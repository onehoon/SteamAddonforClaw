using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.WindowsTweaks;

internal enum VmpOperation
{
    Read,
    Enable,
    Disable,
}

internal sealed record VmpPowerShellResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed class VmpCommandException : Exception
{
    internal VmpCommandException(
        VmpOperation operation,
        string completion,
        int? processExitCode,
        uint? windowsErrorCode,
        string? windowsErrorId,
        string? windowsErrorCategory,
        string? windowsErrorDetail)
        : base("Virtual Machine Platform PowerShell operation failed.")
    {
        Operation = operation;
        Completion = completion;
        ProcessExitCode = processExitCode;
        WindowsErrorCode = windowsErrorCode;
        WindowsErrorId = windowsErrorId;
        WindowsErrorCategory = windowsErrorCategory;
        WindowsErrorDetail = windowsErrorDetail;
    }

    internal VmpOperation Operation { get; }
    internal string Completion { get; }
    internal int? ProcessExitCode { get; }
    internal uint? WindowsErrorCode { get; }
    internal string? WindowsErrorId { get; }
    internal string? WindowsErrorCategory { get; }
    internal string? WindowsErrorDetail { get; }
}

internal sealed class WindowsTweaksOperatingSystem : IWindowsTweaksOperatingSystem
{
    private const string HvciRegistryPath = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string DeviceGuardRegistryPath = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string DeviceGuardPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";
    private const string DeviceGuardMdmPath = @"SOFTWARE\Microsoft\PolicyManager\current\device\DeviceGuard";
    private const string ClassicContextMenuPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string ClassicContextMenuParentPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string WindowsCurrentVersionPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string WidgetsPolicyPath = @"SOFTWARE\Policies\Microsoft\Dsh";
    private const string EdgePolicyPath = @"SOFTWARE\Policies\Microsoft\Edge";
    private const string ExplorerSerializePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
    private const string VirtualMachinePlatformFeatureName = "VirtualMachinePlatform";
    private static readonly TimeSpan VirtualMachinePlatformCommandTimeout = TimeSpan.FromMinutes(30);
    private const int MaximumVirtualMachinePlatformOutputCharacters = 16 * 1024;
    private const uint SpiGetMenuAnimation = 0x1002;
    private const uint SpiSetMenuAnimation = 0x1003;
    private const uint SpiGetMenuShowDelay = 0x006A;
    private const uint SpiSetMenuShowDelay = 0x006B;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private static readonly Guid NoSubgroup = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid ConsoleLockSetting = new("0e796bdb-100d-47d6-a2d5-f7d2daa51f51");
    private static readonly Regex WindowsPathInErrorMessage = new(@"(?i)(?:[A-Z]:\\|\\\\)[^\r\n]*", RegexOptions.Compiled);

    private readonly Func<VmpOperation, string, CancellationToken, VmpPowerShellResult> _runVmpPowerShell;

    internal WindowsTweaksOperatingSystem(
        Func<VmpOperation, string, CancellationToken, VmpPowerShellResult>? runVmpPowerShell = null)
    {
        _runVmpPowerShell = runVmpPowerShell ?? RunVmpPowerShell;
    }

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
            FrontendWindowsTweakId.DisableWindowsWidgets => CaptureDisableWindowsWidgets(),
            FrontendWindowsTweakId.DisableEdgeBackground => CaptureDisableEdgeBackground(),
            FrontendWindowsTweakId.DisableStartupAppDelay => CaptureDisableStartupAppDelay(),
            _ => new(false, false, null, null, "This setting is not supported.")
        };
    }

    public WindowsTweakApplyResult Set(FrontendWindowsTweakId id, bool enabled, CancellationToken cancellationToken)
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
                return SetVirtualMachinePlatform(enabled, cancellationToken);
            case FrontendWindowsTweakId.InstantContextMenus:
                SetInstantContextMenus(enabled);
                break;
            case FrontendWindowsTweakId.ClassicContextMenu:
                SetClassicContextMenu(enabled);
                break;
            case FrontendWindowsTweakId.DisablePasswordOnWake:
                SetPasswordOnWake(enabled);
                break;
            case FrontendWindowsTweakId.DisableWindowsWidgets:
                SetDisableWindowsWidgets(enabled);
                break;
            case FrontendWindowsTweakId.DisableEdgeBackground:
                SetDisableEdgeBackground(enabled);
                break;
            case FrontendWindowsTweakId.DisableStartupAppDelay:
                SetDisableStartupAppDelay(enabled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }

        return default;
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
        cancellationToken.ThrowIfCancellationRequested();
        if (HasMemoryIntegrityPolicy())
            throw new UnauthorizedAccessException("Windows policy or firmware owns Memory Integrity.");

        if (IsMemoryIntegrityLocked())
            throw new UnauthorizedAccessException("Memory Integrity is UEFI locked.");

        WindowsTweakRegistry.Write(Registry.LocalMachine, HvciRegistryPath, "Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    internal WindowsTweakObservation CaptureVirtualMachinePlatform(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _runVmpPowerShell(VmpOperation.Read, BuildVmpCaptureScript(), cancellationToken);
        if (result.ExitCode != 0)
            throw CreateVmpCommandException(VmpOperation.Read, result, "Exited");

        var state = ReadRequiredMarker(result.StandardOutput, "VMP_STATE=", VmpOperation.Read, result);
        return ResolveVirtualMachinePlatformState(state);
    }

    internal WindowsTweakApplyResult SetVirtualMachinePlatform(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = enabled ? VmpOperation.Enable : VmpOperation.Disable;
        var result = _runVmpPowerShell(operation, BuildVmpMutationScript(enabled), cancellationToken);
        if (result.ExitCode != 0)
            throw CreateVmpCommandException(operation, result, "Exited");

        var restartValue = ReadRequiredMarker(result.StandardOutput, "VMP_RESTART=", operation, result);
        if (!bool.TryParse(restartValue, out var restartRequired))
            throw new VmpCommandException(operation, "Exited", result.ExitCode, null,
                "InvalidRestartMarker", null, "PowerShell returned an invalid restart marker.");

        return new(RestartRequired: restartRequired);
    }

    internal static string BuildVmpCaptureScript() => BuildVmpScript(
        $"$feature = Get-WindowsOptionalFeature -Online -FeatureName '{VirtualMachinePlatformFeatureName}' -ErrorAction Stop",
        "[Console]::Out.WriteLine(('VMP_STATE=' + [string]$feature.State))");

    internal static string BuildVmpMutationScript(bool enabled) => BuildVmpScript(
        enabled
            ? $"$result = Enable-WindowsOptionalFeature -Online -FeatureName '{VirtualMachinePlatformFeatureName}' -All -NoRestart -ErrorAction Stop"
            : $"$result = Disable-WindowsOptionalFeature -Online -FeatureName '{VirtualMachinePlatformFeatureName}' -NoRestart -ErrorAction Stop",
        "[Console]::Out.WriteLine(('VMP_RESTART=' + [string]($result.RestartNeeded -eq $true)))");

    internal static WindowsTweakObservation ResolveVirtualMachinePlatformState(string? state)
    {
        if (string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase))
            return new(true, false, true, true);
        if (string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase))
            return new(true, false, false, false);
        if (string.Equals(state, "EnablePending", StringComparison.OrdinalIgnoreCase))
            return new(true, false, true, false);
        if (string.Equals(state, "DisablePending", StringComparison.OrdinalIgnoreCase))
            return new(true, false, false, true);
        if (string.Equals(state, "DisabledWithPayloadRemoved", StringComparison.OrdinalIgnoreCase))
            return new(false, false, null, null,
                "Virtual Machine Platform payload is unavailable on this Windows installation.");

        return new(true, false, null, null,
            "Windows returned an unrecognized Virtual Machine Platform feature state.");
    }

    internal static string BuildVmpScript(string command, string resultMarker)
    {
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("try {");
        script.Append("    ").AppendLine(command);
        script.Append("    ").AppendLine(resultMarker);
        script.AppendLine("    exit 0");
        script.AppendLine("} catch {");
        script.AppendLine("    $failure = $_");
        script.AppendLine("    $message = [string]$failure.Exception.Message");
        script.AppendLine("    $message = $message -replace '[\\r\\n\\t]+', ' '");
        script.AppendLine("    $message = $message -replace '(?i)(?:[A-Z]:\\\\|\\\\\\\\).*', '<path>'");
        script.AppendLine("    if ($message.Length -gt 256) { $message = $message.Substring(0, 256) }");
        script.AppendLine("    $errorId = [string]$failure.FullyQualifiedErrorId");
        script.AppendLine("    if ($errorId.Length -gt 128) { $errorId = $errorId.Substring(0, 128) }");
        script.AppendLine("    $category = ([string]$failure.CategoryInfo.Category) -replace '[^A-Za-z0-9_.-]', '_'");
        script.AppendLine("    $hr = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$failure.Exception.HResult), 0)");
        script.AppendLine("    [Console]::Error.WriteLine(('VMP_ERROR_HRESULT=0x{0:X8}' -f $hr))");
        script.AppendLine("    [Console]::Error.WriteLine('VMP_ERROR_ID=' + $errorId)");
        script.AppendLine("    [Console]::Error.WriteLine('VMP_ERROR_CATEGORY=' + $category)");
        script.AppendLine("    [Console]::Error.WriteLine('VMP_ERROR_DETAIL=' + $message)");
        script.AppendLine("    exit 1");
        script.AppendLine("}");
        return script.ToString();
    }

    private static string ReadRequiredMarker(
        string output,
        string marker,
        VmpOperation operation,
        VmpPowerShellResult result)
    {
        var values = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(marker, StringComparison.Ordinal))
            .Select(line => line[marker.Length..].Trim())
            .ToArray();
        if (values.Length != 1 || values[0].Length == 0)
            throw new VmpCommandException(operation, "Exited", result.ExitCode, null,
                values.Length == 0 ? "MissingResultMarker" : "InvalidResultMarker",
                null, "PowerShell returned no unambiguous feature result.");
        return values[0];
    }

    private static VmpCommandException CreateVmpCommandException(
        VmpOperation operation,
        VmpPowerShellResult result,
        string completion)
    {
        var errorCode = ReadMarker(result.StandardError, "VMP_ERROR_HRESULT=");
        var errorId = ReadMarker(result.StandardError, "VMP_ERROR_ID=");
        var category = ReadMarker(result.StandardError, "VMP_ERROR_CATEGORY=");
        var detail = ReadMarker(result.StandardError, "VMP_ERROR_DETAIL=");
        return new(operation, completion, result.ExitCode, ParseWindowsErrorCode(errorCode),
            CleanDiagnosticField(errorId), CleanDiagnosticField(category), CleanDiagnosticField(detail));
    }

    private static string? ReadMarker(string output, string marker)
    {
        var values = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(marker, StringComparison.Ordinal))
            .Select(line => line[marker.Length..].Trim())
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static uint? ParseWindowsErrorCode(string? value)
    {
        if (value is null) return null;
        var digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)
            ? code
            : null;
    }

    private static string? CleanDiagnosticField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = WindowsPathInErrorMessage.Replace(value.Replace('\r', ' ').Replace('\n', ' ').Trim(), "<path>");
        cleaned = new string(cleaned.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        return cleaned.Length <= 256 ? cleaned : cleaned[..256];
    }

    internal static VmpPowerShellResult RunVmpPowerShell(
        VmpOperation operation,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Environment.Is64BitOperatingSystem)
            throw new VmpCommandException(operation, "StartupFailed", null, null,
                "UnsupportedWindowsArchitecture", null, "64-bit Windows is required for Windows optional-feature servicing.");

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDirectory = Environment.Is64BitProcess
            ? Environment.GetFolderPath(Environment.SpecialFolder.System)
            : string.IsNullOrWhiteSpace(windowsDirectory) ? string.Empty : Path.Combine(windowsDirectory, "Sysnative");
        if (string.IsNullOrWhiteSpace(systemDirectory))
            throw new VmpCommandException(operation, "StartupFailed", null, null,
                "SystemDirectoryUnavailable", null, "Windows system directory is unavailable.");

        var powershellPath = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershellPath))
            throw new VmpCommandException(operation, "StartupFailed", null, null,
                "PowerShellExecutableUnavailable", null, "Windows PowerShell is unavailable.");

        var start = new ProcessStartInfo(powershellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);

        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new VmpCommandException(operation, "StartupFailed", null, null,
                    "PowerShellStartFailed", null, "Windows PowerShell could not be started.");
            }
        }
        catch (VmpCommandException)
        {
            throw;
        }
        catch (Exception exception)
        {
            process.Dispose();
            throw new VmpCommandException(operation, "StartupFailed", null,
                unchecked((uint)exception.HResult), "PowerShellStartFailed", null, exception.GetType().Name);
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(VirtualMachinePlatformCommandTimeout);
            var stdout = ReadBoundedOutputAsync(process.StandardOutput);
            var stderr = ReadBoundedOutputAsync(process.StandardError);
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
                try { process.WaitForExitAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch { }
                Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                throw new VmpCommandException(operation, "TimedOut", null, null,
                    "PowerShellCommandTimedOut", null, "Windows optional-feature servicing exceeded the 30-minute limit.");
            }

            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }

    private static async Task<string> ReadBoundedOutputAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (count == 0) break;
            if (text.Length < MaximumVirtualMachinePlatformOutputCharacters)
            {
                var retained = Math.Min(count, MaximumVirtualMachinePlatformOutputCharacters - text.Length);
                text.Append(buffer, 0, retained);
            }
        }
        return text.ToString();
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
        ApplyInstantContextMenuSettings(enabled,
            (action, parameter, value, flags) => SystemParametersInfoW(action, parameter, value, flags));
    }

    internal static void ApplyInstantContextMenuSettings(
        bool enabled,
        Func<uint, uint, IntPtr, uint, bool> setSystemParameter)
    {
        ArgumentNullException.ThrowIfNull(setSystemParameter);
        var menuAnimation = enabled ? IntPtr.Zero : new IntPtr(1);
        if (!setSystemParameter(SpiSetMenuAnimation, 0, menuAnimation, SpifUpdateIniFile | SpifSendChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        uint targetDelay = enabled ? 10u : 400u;
        if (!setSystemParameter(SpiSetMenuShowDelay, targetDelay, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static WindowsTweakObservation CaptureDisableWindowsWidgets()
    {
        var editionSupport = ReadWidgetsPolicyEditionSupport();
        if (editionSupport is null)
            return new(true, false, null, null, "Windows Widgets policy support could not be determined for this edition.");
        if (!editionSupport.Value)
            return new(false, false, null, null, "The Windows Widgets policy is not supported on this Windows edition.");

        return ResolveZeroDwordRegistryValues(
            WindowsTweakRegistry.Read(Registry.LocalMachine, WidgetsPolicyPath, "AllowNewsAndInterests"));
    }

    private static void SetDisableWindowsWidgets(bool enabled)
    {
        var editionSupport = ReadWidgetsPolicyEditionSupport();
        if (editionSupport is null)
            throw new InvalidOperationException("Windows Widgets policy support could not be determined for this edition.");
        if (!editionSupport.Value)
            throw new PlatformNotSupportedException("The Windows Widgets policy is not supported on this Windows edition.");

        if (enabled)
            WindowsTweakRegistry.Write(Registry.LocalMachine, WidgetsPolicyPath, "AllowNewsAndInterests", 0, RegistryValueKind.DWord);
        else
            WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, WidgetsPolicyPath, "AllowNewsAndInterests");
    }

    private static WindowsTweakObservation CaptureDisableEdgeBackground() =>
        ResolveZeroDwordRegistryValues(
            WindowsTweakRegistry.Read(Registry.LocalMachine, EdgePolicyPath, "StartupBoostEnabled"),
            WindowsTweakRegistry.Read(Registry.LocalMachine, EdgePolicyPath, "BackgroundModeEnabled"));

    private static void SetDisableEdgeBackground(bool enabled)
    {
        if (enabled)
        {
            WindowsTweakRegistry.Write(Registry.LocalMachine, EdgePolicyPath, "StartupBoostEnabled", 0, RegistryValueKind.DWord);
            WindowsTweakRegistry.Write(Registry.LocalMachine, EdgePolicyPath, "BackgroundModeEnabled", 0, RegistryValueKind.DWord);
            return;
        }

        WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, EdgePolicyPath, "StartupBoostEnabled");
        WindowsTweakRegistry.DeleteValue(Registry.LocalMachine, EdgePolicyPath, "BackgroundModeEnabled");
    }

    private static WindowsTweakObservation CaptureDisableStartupAppDelay() =>
        ResolveZeroDwordRegistryValues(
            WindowsTweakRegistry.Read(Registry.CurrentUser, ExplorerSerializePath, "StartupDelayInMSec"),
            WindowsTweakRegistry.Read(Registry.CurrentUser, ExplorerSerializePath, "WaitForIdleState"));

    private static void SetDisableStartupAppDelay(bool enabled)
    {
        if (enabled)
        {
            WindowsTweakRegistry.Write(Registry.CurrentUser, ExplorerSerializePath, "StartupDelayInMSec", 0, RegistryValueKind.DWord);
            WindowsTweakRegistry.Write(Registry.CurrentUser, ExplorerSerializePath, "WaitForIdleState", 0, RegistryValueKind.DWord);
            return;
        }

        WindowsTweakRegistry.DeleteValue(Registry.CurrentUser, ExplorerSerializePath, "StartupDelayInMSec");
        WindowsTweakRegistry.DeleteValue(Registry.CurrentUser, ExplorerSerializePath, "WaitForIdleState");
    }

    internal static bool? ResolveWidgetsPolicyEditionSupport(string? editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId))
            return null;

        var edition = editionId.Trim();
        if (edition.StartsWith("Core", StringComparison.OrdinalIgnoreCase)
            || string.Equals(edition, "IoTCore", StringComparison.OrdinalIgnoreCase))
            return false;

        return edition.ToUpperInvariant() switch
        {
            "PROFESSIONAL" or "PROFESSIONALN" or "PROFESSIONALWORKSTATION" or "PROFESSIONALWORKSTATIONN"
                or "ENTERPRISE" or "ENTERPRISEN" or "ENTERPRISES" or "ENTERPRISESN"
                or "EDUCATION" or "EDUCATIONN" or "IOTENTERPRISE" or "IOTENTERPRISES" => true,
            _ => null,
        };
    }

    private static bool? ReadWidgetsPolicyEditionSupport()
    {
        var edition = WindowsTweakRegistry.Read(Registry.LocalMachine, WindowsCurrentVersionPath, "EditionID");
        if (!edition.ValueExists || edition.Kind != RegistryValueKind.String || edition.Value is not string editionId)
            return null;
        return ResolveWidgetsPolicyEditionSupport(editionId);
    }

    internal static WindowsTweakObservation ResolveZeroDwordRegistryValues(params WindowsTweakRegistryValueRead[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("At least one registry value is required.", nameof(values));

        if (values.All(value => value.ValueExists && value.Kind == RegistryValueKind.DWord && value.Value is int dword && dword == 0))
            return new(true, false, true, true);
        if (values.All(value => !value.ValueExists))
            return new(true, false, false, false);

        return new(true, false, false, null, "Registry values are mixed or custom.", IsMixed: true);
    }

    private static WindowsTweakObservation CaptureClassicContextMenu()
    {
        var registration = WindowsTweakRegistry.Read(Registry.CurrentUser, ClassicContextMenuPath, string.Empty);
        if (IsForeignClassicContextMenuRegistration(registration))
            return new(true, true, null, null, "The exact shell registration exists but is not owned by this tweak.");

        var configured = registration.KeyExists;

        var parentWriteTime = ReadRegistryLastWriteTime(Registry.CurrentUser, ClassicContextMenuParentPath);
        var shellStartTime = ReadShellProcessStartTimeUtc();
        var restartPending = parentWriteTime is { } writeTime && writeTime > shellStartTime;
        return new(true, false, configured, restartPending ? null : configured, ExplorerRestartPending: restartPending);
    }

    private static void SetClassicContextMenu(bool enabled)
    {
        var registration = WindowsTweakRegistry.Read(Registry.CurrentUser, ClassicContextMenuPath, string.Empty);
        if (IsForeignClassicContextMenuRegistration(registration))
            throw new UnauthorizedAccessException("The shell registration belongs to another setting and was left unchanged.");

        if (enabled)
        {
            WindowsTweakRegistry.Write(Registry.CurrentUser, ClassicContextMenuPath, string.Empty, string.Empty, RegistryValueKind.String);
            return;
        }

        using var parent = Registry.CurrentUser.OpenSubKey(ClassicContextMenuParentPath, writable: true);
        parent?.DeleteSubKey("InprocServer32", throwOnMissingSubKey: false);
    }

    private static bool IsForeignClassicContextMenuRegistration(WindowsTweakRegistryValueRead registration) =>
        registration.KeyExists
        && (!registration.ValueExists
            || registration.Kind != RegistryValueKind.String
            || registration.Value is not string value
            || value.Length != 0);

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
        return HasMemoryIntegrityPolicy() || IsMemoryIntegrityLocked();
    }

    private static bool HasMemoryIntegrityPolicy() =>
        HasPolicyValues(Registry.LocalMachine, DeviceGuardPolicyPath,
            ["EnableVirtualizationBasedSecurity", "RequirePlatformSecurityFeatures", "HypervisorEnforcedCodeIntegrity"])
        || HasPolicyValues(Registry.LocalMachine, DeviceGuardMdmPath,
            ["EnableVirtualizationBasedSecurity", "RequirePlatformSecurityFeatures", "HypervisorEnforcedCodeIntegrity"]);

    private static bool IsMemoryIntegrityLocked() =>
        ReadDword(Registry.LocalMachine, DeviceGuardRegistryPath, "Locked") == 1
        || ReadDword(Registry.LocalMachine, HvciRegistryPath, "Locked") == 1;

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
}
