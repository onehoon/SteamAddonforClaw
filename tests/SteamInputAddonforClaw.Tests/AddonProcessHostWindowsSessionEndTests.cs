using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostWindowsSessionEndTests
{
    [Fact]
    public void Session_end_is_wired_to_the_existing_tray_hwnd_and_preserves_the_icon_subclass()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var tray = Method(host, "internal bool TryInitializeTray(Action restart)");

        Assert.Contains("private const bool EnableA2vmAutomaticRumbleLifecycle = false;", host, StringComparison.Ordinal);
        Assert.Contains("new NativeTrayHostWindow(", tray, StringComparison.Ordinal);
        Assert.Contains("EnableA2vmAutomaticRumbleLifecycle ? HandleWindowsSessionEnd : null", tray, StringComparison.Ordinal);
        Assert.Contains("new SystemTrayIcon(_trayHostWindow.Handle", tray, StringComparison.Ordinal);
        Assert.Contains("operation.Wait(WindowsSessionEndPreparationBudget)", host, StringComparison.Ordinal);
        Assert.Contains("deadline.Cancel()", host, StringComparison.Ordinal);
        Assert.Contains("ObserveLateSessionEndTask(operation)", host, StringComparison.Ordinal);
        Assert.Contains("PnP1901Verified", host, StringComparison.Ordinal);
    }

    [Fact]
    public void One_disabled_host_policy_controls_both_automatic_entry_points()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var ownerFactory = Method(host, "private Devices.MSI.Claw.MsiClawAddonPhysicalOwnership? CreatePhysicalOwnership(");
        var tray = Method(host, "internal bool TryInitializeTray(Action restart)");

        Assert.Contains("private const bool EnableA2vmAutomaticRumbleLifecycle = false;", host, StringComparison.Ordinal);
        Assert.Contains("enableA2vmAutomaticRumbleLifecycle: EnableA2vmAutomaticRumbleLifecycle", ownerFactory, StringComparison.Ordinal);
        Assert.Contains("new NativeTrayHostWindow(", tray, StringComparison.Ordinal);
        Assert.Contains("EnableA2vmAutomaticRumbleLifecycle ? HandleWindowsSessionEnd : null", tray, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_end_closes_existing_shutdown_admission_then_retires_presentation_before_physical_input()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var begin = Method(host, "private async Task<SteamInputAddonforClaw.Devices.MSI.Claw.A2vmWindowsSessionEndResult> PrepareForWindowsSessionEndAsync(");
        var admission = begin.IndexOf("GetWindowsSessionEndAdmissionFailure(admissionReconcile)", StringComparison.Ordinal);
        var shutdownGate = begin.IndexOf("TryBeginProcessShutdownCore()", admission, StringComparison.Ordinal);
        var presentation = begin.IndexOf("presentation.PrepareForWindowsSessionEndAsync", shutdownGate, StringComparison.Ordinal);
        var presentationFailureGate = begin.IndexOf("if (!presentationRetired)", presentation, StringComparison.Ordinal);
        var presentationFailureReturn = begin.IndexOf("A2vmWindowsSessionEndResult.Skipped(\"PresentationRetirementUnproven\")", presentationFailureGate, StringComparison.Ordinal);
        var physical = begin.IndexOf("physical.PrepareForWindowsSessionEndAsync", presentation, StringComparison.Ordinal);
        Assert.True(presentation >= 0 && physical > presentation);
        var presentationToPhysical = begin[presentation..physical];

        Assert.True(admission >= 0 && shutdownGate > admission && presentation > shutdownGate
            && presentationFailureGate > presentation && presentationFailureReturn > presentationFailureGate
            && physical > presentationFailureReturn);
        // A failed return exits before physical preparation, and a thrown retirement is not caught here.
        Assert.DoesNotContain("catch", presentationToPhysical, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseForCenterMEnableAsync", begin, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchModeAsync", begin, StringComparison.Ordinal);
        Assert.Contains("IsA2vmBootRumblePrimeModel", host, StringComparison.Ordinal);
        Assert.Contains("CenterMAuthorityNotDisabled", host, StringComparison.Ordinal);
        Assert.Contains("OwnedStrongPid1902SessionUnavailable", host, StringComparison.Ordinal);
        Assert.Contains("ControllerLifecycleOperationInProgress", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_end_drains_only_the_captured_reconcile_and_revalidates_owners_before_retirement()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var begin = Method(host, "private async Task<SteamInputAddonforClaw.Devices.MSI.Claw.A2vmWindowsSessionEndResult> PrepareForWindowsSessionEndAsync(");
        var capture = begin.IndexOf("var admissionReconcile = _presentationReconcile", StringComparison.Ordinal);
        var shutdownGate = begin.IndexOf("TryBeginProcessShutdownCore()", capture, StringComparison.Ordinal);
        var shutdownReconcile = begin.IndexOf("var presentationReconcile = _presentationReconcile", shutdownGate, StringComparison.Ordinal);
        var boundedWait = begin.IndexOf("DrainWindowsSessionEndPresentationReconcileAsync(presentationReconcile, cancellationToken)", shutdownReconcile, StringComparison.Ordinal);
        var revalidation = begin.IndexOf("GetWindowsSessionEndPostReconcileFailure", boundedWait, StringComparison.Ordinal);
        var retirement = begin.IndexOf("presentation.PrepareForWindowsSessionEndAsync", revalidation, StringComparison.Ordinal);
        var physical = begin.IndexOf("physical.PrepareForWindowsSessionEndAsync", retirement, StringComparison.Ordinal);

        Assert.True(capture >= 0 && shutdownGate > capture && shutdownReconcile > shutdownGate && boundedWait > shutdownReconcile
            && revalidation > boundedWait && retirement > revalidation && physical > retirement);
        Assert.Contains("PresentationReconcileCancelled", begin, StringComparison.Ordinal);
        Assert.Contains("PresentationReconcileFailed", begin, StringComparison.Ordinal);

        var request = Method(host, "private void RequestControllerPresentationReconcile(");
        Assert.Contains("lock (_controllerOwnershipOperationSync)", request, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ReconcileControllerPresentationAsync", request, StringComparison.Ordinal);
        Assert.Contains("if (Volatile.Read(ref _processShutdownStarted) != 0) return", request, StringComparison.Ordinal);

        var reconcile = Method(host, "private async Task<bool> ReconcileControllerPresentationAsync(");
        Assert.Contains("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)", reconcile, StringComparison.Ordinal);
        Assert.Contains("throw;", reconcile, StringComparison.Ordinal);

        var revalidateMethod = Method(host, "private string? GetWindowsSessionEndPostReconcileFailure(");
        Assert.Contains("expectedIdentity.StronglyMatches(currentIdentity)", revalidateMethod, StringComparison.Ordinal);
        Assert.Contains("presentation.IsActivePresentationLive", revalidateMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Pre_ownership_presentation_events_do_not_publish_a_reconcile_but_active_recovery_stays_tracked()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var request = Method(host, "private void RequestControllerPresentationReconcile(");

        Assert.Contains("lock (_controllerOwnershipOperationSync)", request, StringComparison.Ordinal);
        Assert.Contains(
            "else if (_presentationOwnership is { } presentation\n                && _physicalOwnership is { } physical\n                && (physical.LiveInputSource is not null\n                    || presentation.ActivePresentation is not null))",
            request,
            StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ReconcileControllerPresentationAsync", request, StringComparison.Ordinal);
        Assert.Contains("_presentationReconcile = reconcile", request, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_end_keeps_one_global_budget_and_reports_actual_stage_and_command_outcomes()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var physical = ReadSource("src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs");
        var modeController = ReadSource("src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawModeController.cs");
        var handler = Method(host, "private void HandleWindowsSessionEnd()");

        Assert.Contains("TimeSpan.FromMilliseconds(2000)", host, StringComparison.Ordinal);
        Assert.Contains("deadline.CancelAfter(WindowsSessionEndPreparationBudget)", handler, StringComparison.Ordinal);
        Assert.Contains("operation.Wait(WindowsSessionEndPreparationBudget)", handler, StringComparison.Ordinal);
        Assert.Equal(1, handler.Split("A2vmWindowsSessionEndPid1901Prepare", StringSplitOptions.None).Length - 1);
        Assert.Contains("PnP1901Verified", handler, StringComparison.Ordinal);
        Assert.Contains("(\"Outcome\"", handler, StringComparison.Ordinal);
        Assert.Contains("A2vmWindowsSessionEndAdmissionCompleted", host, StringComparison.Ordinal);
        Assert.Contains("A2vmWindowsSessionEndPresentationRetirement", host, StringComparison.Ordinal);
        Assert.Contains("A2vmWindowsSessionEndPhysicalPreflight", physical, StringComparison.Ordinal);
        Assert.Contains("A2vmWindowsSessionEndDirectInputStopped", physical, StringComparison.Ordinal);
        Assert.Contains("RemainingBudgetMs", physical, StringComparison.Ordinal);
        Assert.Contains("A2vmWindowsSessionEndCommandSourceResolved", modeController, StringComparison.Ordinal);
        Assert.Contains("ResultUnconfirmed", modeController, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Session_end_reconcile_drain_waits_for_and_preserves_the_reconcile_result(bool succeeded)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var drain = AddonProcessHost.DrainWindowsSessionEndPresentationReconcileAsync(completion.Task, CancellationToken.None);

        Assert.False(drain.IsCompleted);
        completion.SetResult(succeeded);

        Assert.Equal(succeeded, await drain);
    }

    [Fact]
    public async Task Session_end_reconcile_drain_observes_task_and_deadline_cancellation()
    {
        var canceledReconcile = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledDrain = AddonProcessHost.DrainWindowsSessionEndPresentationReconcileAsync(canceledReconcile.Task, CancellationToken.None);
        canceledReconcile.SetCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledDrain);

        var pendingReconcile = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource();
        var deadlineDrain = AddonProcessHost.DrainWindowsSessionEndPresentationReconcileAsync(pendingReconcile.Task, deadline.Token);
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deadlineDrain);
        pendingReconcile.SetResult(true);
    }

    [Fact]
    public void Normal_runtime_exit_and_restart_paths_do_not_call_the_session_end_preparation()
    {
        var runtimeApplication = ReadSource("src/SteamInputAddonforClaw/Hosting/RuntimeProcessApplication.cs");
        var trayWindow = ReadSource("src/SteamInputAddonforClaw/Lifecycle/NativeTrayHostWindow.cs");

        Assert.DoesNotContain("PrepareForWindowsSessionEndAsync", runtimeApplication, StringComparison.Ordinal);
        Assert.Contains("WindowsSessionEndMessage.TryStartOnce", trayWindow, StringComparison.Ordinal);
        Assert.Contains("message != WmEndSession || wParam == IntPtr.Zero", trayWindow, StringComparison.Ordinal);
        Assert.Contains("EndSessionLogoff | EndSessionCloseApp", trayWindow, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Method(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find source marker: {marker}");
        var end = source.IndexOf("\n    private ", start + marker.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source[start..end];
    }
}
