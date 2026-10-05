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
    int TerminatedProcessCount,
    int FailureCount);

internal static class MsiQuickSettingsRuntimeQuiescer
{
    private const string CandidateProcessName = "Gamebar_Widget";
    private const string MsiQuickSettingsPackageName = "9426MICRO-STARINTERNATION.MSIQuickSettings";
    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;
    private const uint MaximumPackageFullNameLength = 32768;

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

        return TerminateExactPackageProcesses(candidates.Length, identityUnavailableCount, targetPackages);
    }

    internal static bool IsExactMsiQuickSettingsPackageFullName(string? packageFullName) =>
        packageFullName?.StartsWith(MsiQuickSettingsPackageName + "_", StringComparison.OrdinalIgnoreCase) == true;

    internal static IReadOnlyList<string> GetDistinctTargetPackageFullNames(IEnumerable<string?> packageFullNames) =>
        packageFullNames
            .Where(IsExactMsiQuickSettingsPackageFullName)
            .Select(packageFullName => packageFullName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static bool IsExactPackageFullNameMatch(string? candidate, string target) =>
        string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPackageFullName(int processId, out string? packageFullName, out int errorCode)
    {
        packageFullName = null;
        using var processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (processHandle.IsInvalid)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        return TryReadPackageFullName(processHandle, out packageFullName, out errorCode);
    }

    private static bool TryReadPackageFullName(
        SafeProcessHandle processHandle,
        out string? packageFullName,
        out int errorCode)
    {
        packageFullName = null;
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

    private static MsiQuickSettingsRuntimeQuiesceResult TerminateExactPackageProcesses(
        int candidateCount,
        int identityUnavailableCount,
        IReadOnlyList<string> targetPackageFullNames)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception)
        {
            AppLog.Warn("MsiQuickSettings", "Could not enumerate processes for exact-package termination; startup continues.", exception,
                ("Event", "MsiQuickSettingsTerminationFailed"), ("Reason", "ProcessEnumerationFailed"),
                ("PackageCount", targetPackageFullNames.Count));
            return new(candidateCount, identityUnavailableCount, targetPackageFullNames.Count, 0, 1);
        }

        var terminatedProcessCount = 0;
        var failedProcessCount = 0;
        foreach (var process in processes)
        {
            using (process)
            {
                int processId;
                try
                {
                    processId = process.Id;
                }
                catch (Exception)
                {
                    continue;
                }

                if (!TryGetPackageFullName(processId, out var packageFullName, out _))
                    continue;

                var targetPackageFullName = targetPackageFullNames.FirstOrDefault(
                    target => IsExactPackageFullNameMatch(packageFullName, target));
                if (targetPackageFullName is null)
                    continue;

                using var terminationHandle = OpenProcess(
                    PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION,
                    false,
                    (uint)processId);
                if (terminationHandle.IsInvalid)
                {
                    failedProcessCount++;
                    LogTerminationFailed(targetPackageFullName, processId, Marshal.GetLastWin32Error(), "OpenProcessForTerminationFailed");
                    continue;
                }

                if (!TryReadPackageFullName(terminationHandle, out var terminationHandlePackageFullName, out _)
                    || !IsExactPackageFullNameMatch(terminationHandlePackageFullName, targetPackageFullName))
                {
                    identityUnavailableCount++;
                    continue;
                }

                if (!TerminateProcess(terminationHandle, 0))
                {
                    failedProcessCount++;
                    LogTerminationFailed(targetPackageFullName, processId, Marshal.GetLastWin32Error(), "TerminateProcessFailed");
                    continue;
                }

                terminatedProcessCount++;
                AppLog.Info("MsiQuickSettings", "A process with the exact MSI Quick Settings package identity was terminated.",
                    ("Event", "MsiQuickSettingsProcessTerminated"), ("ProcessId", processId),
                    ("PackageFullName", targetPackageFullName));
            }
        }

        return new(candidateCount, identityUnavailableCount, targetPackageFullNames.Count, terminatedProcessCount, failedProcessCount);
    }

    private static void LogTerminationFailed(string packageFullName, int processId, int errorCode, string reason) =>
        AppLog.Warn("MsiQuickSettings", "Exact MSI Quick Settings package process termination failed; controller startup continues.", null,
            ("Event", "MsiQuickSettingsTerminationFailed"), ("ProcessId", processId),
            ("PackageFullName", packageFullName), ("Win32Error", errorCode), ("Reason", reason));

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint packageFullNameLength, StringBuilder? packageFullName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
