using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal enum SafeMainUiTerminationResult
{
    Terminated,
    AlreadyExited,
    IdentityMismatch,
    VisibleAgain,
    AdditionalMainUiDetected,
    IdentityUncertain,
    AccessDenied,
    WaitTimedOut,
    Failed
}

/// <summary>Every fact that must be freshly re-verified immediately before terminating a tracked
/// real MainUI process. Deliberately a plain data bag so <see cref="SafeMainUiTerminator"/>'s
/// decision logic is pure and independently testable from however each fact is captured.</summary>
internal readonly record struct SafeMainUiTerminationEvidence(
    bool HandleStillValid,
    int? HandleProcessId,
    bool ProcessAlive,
    string? CurrentProcessName,
    string? CurrentExecutablePath,
    bool SeenVisible,
    MainUiWindowSnapshot? FreshWindowSnapshot,
    bool AdditionalForeignMainUiExists);

/// <summary>Native TerminateProcess/WaitForSingleObject seam, isolated so termination decision
/// logic can be unit tested without ever calling into a real process.</summary>
internal interface ITerminateProcessInvoker
{
    bool TryTerminate(SafeProcessHandle handle, out int win32Error);
    bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout);
}

internal sealed class Win32TerminateProcessInvoker : ITerminateProcessInvoker
{
    private const uint WAIT_OBJECT_0 = 0;

    public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
    {
        if (TerminateProcess(handle, 1)) { win32Error = 0; return true; }
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout) =>
        WaitForSingleObject(handle, (uint)timeout.TotalMilliseconds) == WAIT_OBJECT_0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}

/// <summary>
/// Terminates ONLY a previously tracked, previously-visible, freshly-revalidated real Center M
/// MainUI process -- never by process name, never a process tree, never any other MSI process.
/// Every precondition failing routes to a specific fail-open classified result; TerminateProcess is
/// invoked only when every single check passes (research handoff sections 23-24, 36).
/// </summary>
internal sealed class SafeMainUiTerminator(ITerminateProcessInvoker? invoker = null)
{
    private readonly ITerminateProcessInvoker _invoker = invoker ?? new Win32TerminateProcessInvoker();

    internal static bool PathMatchesExpectedPackage(string? executablePath) =>
        executablePath is not null
        && executablePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        && executablePath.EndsWith(@"MSI Center M\MSI Center M.exe", StringComparison.OrdinalIgnoreCase);

    internal SafeMainUiTerminationResult TryTerminate(TrackedCenterMMainUi tracked, SafeMainUiTerminationEvidence evidence, TimeSpan waitTimeout)
    {
        var result = Evaluate(tracked, evidence);
        AppLog.Info("CenterM.MainUi", "Safe MainUI termination evaluated.", ("Result", result), ("ProcessId", tracked.ProcessId));

        if (result != SafeMainUiTerminationResult.Terminated) return result;

        if (!_invoker.TryTerminate(tracked.Handle, out var win32Error))
        {
            AppLog.Warn("CenterM.MainUi", "TerminateProcess failed.", null, ("Win32Error", win32Error));
            return SafeMainUiTerminationResult.Failed;
        }

        return _invoker.WaitForExit(tracked.Handle, waitTimeout)
            ? SafeMainUiTerminationResult.Terminated
            : SafeMainUiTerminationResult.WaitTimedOut;
    }

    /// <summary>Pure precondition evaluation. Returns <see cref="SafeMainUiTerminationResult.Terminated"/>
    /// only as a signal that every check passed and TerminateProcess should now be attempted --
    /// callers must not treat that return value alone as proof of actual termination.</summary>
    internal static SafeMainUiTerminationResult Evaluate(TrackedCenterMMainUi tracked, SafeMainUiTerminationEvidence evidence)
    {
        if (!evidence.ProcessAlive) return SafeMainUiTerminationResult.AlreadyExited;

        if (!evidence.HandleStillValid || evidence.HandleProcessId != tracked.ProcessId)
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (!string.Equals(evidence.CurrentProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal))
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (!PathMatchesExpectedPackage(evidence.CurrentExecutablePath))
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (!evidence.SeenVisible)
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (evidence.FreshWindowSnapshot is null)
            return SafeMainUiTerminationResult.IdentityUncertain;

        if (evidence.FreshWindowSnapshot.Value.VisibleMainWindowCount > 0)
            return SafeMainUiTerminationResult.VisibleAgain;

        if (evidence.AdditionalForeignMainUiExists)
            return SafeMainUiTerminationResult.AdditionalMainUiDetected;

        if (!tracked.HasTerminateRights)
            return SafeMainUiTerminationResult.AccessDenied;

        return SafeMainUiTerminationResult.Terminated;
    }
}
