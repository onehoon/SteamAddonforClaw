using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostWindowsSessionEndTests
{
    [Fact]
    public void Session_end_is_wired_to_the_existing_tray_hwnd_and_preserves_the_icon_subclass()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var tray = Method(host, "internal bool TryInitializeTray(Action restart)");

        Assert.Contains("new NativeTrayHostWindow(HandleWindowsSessionEnd)", tray, StringComparison.Ordinal);
        Assert.Contains("new SystemTrayIcon(_trayHostWindow.Handle", tray, StringComparison.Ordinal);
        Assert.Contains("operation.Wait(WindowsSessionEndPreparationBudget)", host, StringComparison.Ordinal);
        Assert.Contains("deadline.Cancel()", host, StringComparison.Ordinal);
        Assert.Contains("ObserveLateSessionEndTask(operation)", host, StringComparison.Ordinal);
        Assert.Contains("PnP1901Verified", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_end_closes_existing_shutdown_admission_then_retires_presentation_before_physical_input()
    {
        var host = ReadSource("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs");
        var begin = Method(host, "private async Task<SteamInputAddonforClaw.Devices.MSI.Claw.A2vmWindowsSessionEndResult> PrepareForWindowsSessionEndAsync(");
        var admission = begin.IndexOf("GetWindowsSessionEndAdmissionFailure()", StringComparison.Ordinal);
        var shutdownGate = begin.IndexOf("TryBeginProcessShutdownCore()", admission, StringComparison.Ordinal);
        var presentation = begin.IndexOf("presentation.PrepareForWindowsSessionEndAsync", shutdownGate, StringComparison.Ordinal);
        var physical = begin.IndexOf("physical.PrepareForWindowsSessionEndAsync", presentation, StringComparison.Ordinal);

        Assert.True(admission >= 0 && shutdownGate > admission && presentation > shutdownGate && physical > presentation);
        Assert.DoesNotContain("ReleaseForCenterMEnableAsync", begin, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchModeAsync", begin, StringComparison.Ordinal);
        Assert.Contains("IsA2vmBootRumblePrimeModel", host, StringComparison.Ordinal);
        Assert.Contains("CenterMAuthorityNotDisabled", host, StringComparison.Ordinal);
        Assert.Contains("OwnedStrongPid1902SessionUnavailable", host, StringComparison.Ordinal);
        Assert.Contains("ControllerLifecycleOperationInProgress", host, StringComparison.Ordinal);
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
