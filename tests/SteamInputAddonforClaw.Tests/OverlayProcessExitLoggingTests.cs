using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class OverlayProcessExitLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_process_exit_is_classified_by_visibility(bool visible)
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.OverlayExit.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        var logDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(overlayDirectory);
        Directory.CreateDirectory(logDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        AppLog.DirectoryOverride = logDirectory;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;

        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        Process? process = null;
        var visibleSessionLostCount = 0;
        OverlayProcessController? controller = null;
        NamedPipeOverlayClient? client = null;
        Task? clientRun = null;
        try
        {
            Process? StartTestProcess(ProcessStartInfo _) => process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c pause",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            });

            controller = new OverlayProcessController(root, logDirectory, StartTestProcess,
                _ => new NamedPipeOverlayServer(pipeName));
            controller.VisibleSessionLost += () => Interlocked.Increment(ref visibleSessionLostCount);
            client = new NamedPipeOverlayClient(pipeName);
            clientRun = client.RunAsync(_ => Task.CompletedTask);

            Assert.True(await (visible ? controller.ShowAsync() : controller.StartAsync()).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(visible, controller.IsVisible);
            Assert.NotNull(process);

            process!.Kill(entireProcessTree: true);
            try { await clientRun.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception) { /* The Runtime-owned pipe is expected to close with its process. */ }

            AppLog.DrainForTests();
            var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
            if (visible)
            {
                Assert.Matches(@"\[WARN\].*\[Overlay\] Visible Overlay process exited unexpectedly; active capture will be retired\.", log);
                Assert.Equal(1, Volatile.Read(ref visibleSessionLostCount));
            }
            else
            {
                Assert.Matches(@"\[INFO\].*\[Overlay\] Hidden Overlay process exited; the next explicit Overlay request may start a new process\.", log);
                Assert.DoesNotMatch(@"\[WARN\].*\[Overlay\] Hidden Overlay process exited", log);
                Assert.Equal(0, Volatile.Read(ref visibleSessionLostCount));
            }
        }
        finally
        {
            try
            {
                if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
            }
            catch { }
            if (controller is not null)
            {
                try { await controller.DisposeAsync(); } catch { }
            }
            if (client is not null)
            {
                try { await client.DisposeAsync(); } catch { }
            }
            if (clientRun is not null)
            {
                try { await clientRun.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            process?.Dispose();
            AppLog.DrainForTests();
            AppLog.MinimumLevelOverride = AppLogLevel.Off;
            AppLog.DirectoryOverride = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
