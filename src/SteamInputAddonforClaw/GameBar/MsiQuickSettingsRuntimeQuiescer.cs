using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal readonly record struct MsiQuickSettingsRuntimeQuiesceResult(
    int CandidateCount,
    int IdentityUnavailableCount,
    int PackageCount,
    int TerminatedPackageCount,
    int FailedPackageCount);

internal static class MsiQuickSettingsRuntimeQuiescer
{
    private const string CandidateProcessName = "Gamebar_Widget";
    private const string MsiQuickSettingsPackageName = "9426MICRO-STARINTERNATION.MSIQuickSettings";
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;
    private const uint MaximumPackageFullNameLength = 32768;

    private static readonly Guid PackageDebugSettingsClassId = new("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D");

    internal static MsiQuickSettingsRuntimeQuiesceResult Quiesce()
    {
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(CandidateProcessName);
        }
        catch (Exception exception)
        {
            AppLog.Warn("MsiQuickSettings", "Could not enumerate MSI Quick Settings widget candidates; startup continues.", exception,
                ("Event", "MsiQuickSettingsTerminationFailed"), ("Reason", "CandidateEnumerationFailed"));
            return new(0, 0, 0, 0, 1);
        }

        if (candidates.Length == 0)
        {
            AppLog.Info("MsiQuickSettings", "MSI Quick Settings packaged runtime is not running.",
                ("Event", "MsiQuickSettingsQuiesceNotRunning"));
            return new(0, 0, 0, 0, 0);
        }

        var packageNames = new List<string?>();
        var processIdsByPackage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var identityUnavailableCount = 0;

        foreach (var candidate in candidates)
        {
            using (candidate)
            {
                int processId;
                try
                {
                    processId = candidate.Id;
                }
                catch (Exception exception)
                {
                    identityUnavailableCount++;
                    AppLog.Debug("MsiQuickSettings", "Candidate exited before package identity could be checked.",
                        ("Event", "MsiQuickSettingsIdentityUnavailable"),
                        ("ExceptionType", exception.GetType().Name));
                    continue;
                }

                if (!TryGetPackageFullName(processId, out var packageFullName, out var errorCode))
                {
                    identityUnavailableCount++;
                    AppLog.Debug("MsiQuickSettings", "Candidate package identity could not be proven; candidate was skipped.",
                        ("Event", "MsiQuickSettingsIdentityUnavailable"), ("ProcessId", processId),
                        ("Win32Error", errorCode),
                        ("Reason", errorCode == AppModelErrorNoPackage ? "NoPackageIdentity" : "PackageIdentityReadFailed"));
                    continue;
                }

                if (!IsExactMsiQuickSettingsPackageFullName(packageFullName))
                {
                    AppLog.Debug("MsiQuickSettings", "Widget candidate belongs to another package and was skipped.",
                        ("ProcessId", processId), ("PackageFullName", packageFullName));
                    continue;
                }

                packageNames.Add(packageFullName);
                processIdsByPackage.TryAdd(packageFullName!, processId);
            }
        }

        var targetPackages = GetDistinctTargetPackageFullNames(packageNames);
        if (targetPackages.Count == 0)
            return new(candidates.Length, identityUnavailableCount, 0, 0, 0);

        foreach (var packageFullName in targetPackages)
        {
            AppLog.Info("MsiQuickSettings", "Exact MSI Quick Settings package identity was confirmed.",
                ("Event", "MsiQuickSettingsPackageIdentified"),
                ("ProcessId", processIdsByPackage[packageFullName]), ("PackageFullName", packageFullName));
        }

        return TerminatePackages(candidates.Length, identityUnavailableCount, targetPackages, processIdsByPackage);
    }

    internal static bool IsExactMsiQuickSettingsPackageFullName(string? packageFullName) =>
        packageFullName?.StartsWith(MsiQuickSettingsPackageName + "_", StringComparison.OrdinalIgnoreCase) == true;

    internal static IReadOnlyList<string> GetDistinctTargetPackageFullNames(IEnumerable<string?> packageFullNames) =>
        packageFullNames
            .Where(IsExactMsiQuickSettingsPackageFullName)
            .Select(packageFullName => packageFullName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool TryGetPackageFullName(int processId, out string? packageFullName, out int errorCode)
    {
        packageFullName = null;
        using var processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (processHandle.IsInvalid)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        uint length = 0;
        var result = GetPackageFullName(processHandle, ref length, null);
        if (result != ErrorInsufficientBuffer || length == 0 || length > MaximumPackageFullNameLength)
        {
            errorCode = result;
            return false;
        }

        var buffer = new StringBuilder((int)length);
        result = GetPackageFullName(processHandle, ref length, buffer);
        if (result != 0)
        {
            errorCode = result;
            return false;
        }

        packageFullName = buffer.ToString();
        errorCode = 0;
        return !string.IsNullOrWhiteSpace(packageFullName);
    }

    private static MsiQuickSettingsRuntimeQuiesceResult TerminatePackages(
        int candidateCount,
        int identityUnavailableCount,
        IReadOnlyList<string> packageFullNames,
        IReadOnlyDictionary<string, int> processIdsByPackage)
    {
        object? packageDebugSettingsObject = null;
        IPackageDebugSettings? packageDebugSettings;
        try
        {
            var classType = Type.GetTypeFromCLSID(PackageDebugSettingsClassId, throwOnError: true)
                ?? throw new COMException("PackageDebugSettings COM class is unavailable.");
            packageDebugSettingsObject = Activator.CreateInstance(classType)
                ?? throw new COMException("PackageDebugSettings COM activation returned no object.");
            packageDebugSettings = packageDebugSettingsObject as IPackageDebugSettings
                ?? throw new COMException("PackageDebugSettings does not expose IPackageDebugSettings.");
        }
        catch (Exception exception)
        {
            foreach (var packageFullName in packageFullNames)
                LogTerminationFailed(packageFullName, processIdsByPackage[packageFullName], exception.HResult, exception.GetType().Name);
            ReleaseComObject(packageDebugSettingsObject);
            return new(candidateCount, identityUnavailableCount, packageFullNames.Count, 0, packageFullNames.Count);
        }

        var terminatedCount = 0;
        var failedCount = 0;
        try
        {
            foreach (var packageFullName in packageFullNames)
            {
                var processId = processIdsByPackage[packageFullName];
                try
                {
                    var hresult = packageDebugSettings.TerminateAllProcesses(packageFullName);
                    if (hresult >= 0)
                    {
                        terminatedCount++;
                        AppLog.Info("MsiQuickSettings", "All processes for the exact MSI Quick Settings package were terminated.",
                            ("Event", "MsiQuickSettingsPackageTerminated"), ("ProcessId", processId),
                            ("PackageFullName", packageFullName), ("HResult", $"0x{hresult:X8}"));
                    }
                    else
                    {
                        failedCount++;
                        LogTerminationFailed(packageFullName, processId, hresult, "TerminateAllProcessesFailed");
                    }
                }
                catch (Exception exception)
                {
                    failedCount++;
                    LogTerminationFailed(packageFullName, processId, exception.HResult, exception.GetType().Name);
                }
            }
        }
        finally
        {
            ReleaseComObject(packageDebugSettingsObject);
        }

        return new(candidateCount, identityUnavailableCount, packageFullNames.Count, terminatedCount, failedCount);
    }

    private static void LogTerminationFailed(string packageFullName, int processId, int hresult, string reason) =>
        AppLog.Warn("MsiQuickSettings", "Exact MSI Quick Settings package termination failed; controller startup continues.", null,
            ("Event", "MsiQuickSettingsTerminationFailed"), ("ProcessId", processId),
            ("PackageFullName", packageFullName), ("HResult", $"0x{hresult:X8}"), ("Reason", reason));

    private static void ReleaseComObject(object? packageDebugSettingsObject)
    {
        if (packageDebugSettingsObject is null || !Marshal.IsComObject(packageDebugSettingsObject))
            return;

        try { Marshal.FinalReleaseComObject(packageDebugSettingsObject); }
        catch (Exception exception)
        {
            AppLog.Debug("MsiQuickSettings", "PackageDebugSettings COM reference release failed.",
                ("ExceptionType", exception.GetType().Name), ("HResult", $"0x{exception.HResult:X8}"));
        }
    }

    [ComImport]
    [Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPackageDebugSettings
    {
        [PreserveSig]
        int EnableDebugging(
            [MarshalAs(UnmanagedType.LPWStr)] string packageFullName,
            [MarshalAs(UnmanagedType.LPWStr)] string? debuggerCommandLine,
            nint environment);

        [PreserveSig] int DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
        [PreserveSig] int Suspend([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
        [PreserveSig] int Resume([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
        [PreserveSig] int TerminateAllProcesses([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint packageFullNameLength, StringBuilder? packageFullName);
}
