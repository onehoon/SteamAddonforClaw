using System.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.QamHost;
using System.Text.Json;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamHostProcessControllerTests
{
    [Fact]
    public void Runtime_enable_is_serialized_as_a_CDP_method_not_an_expression()
    {
        using var document = JsonDocument.Parse(SteamGamepadUiCdpClient.SerializeCommandPayload(1, "Runtime.enable", null));
        var root = document.RootElement;
        Assert.Equal("Runtime.enable", root.GetProperty("method").GetString());
        Assert.False(root.GetProperty("params").TryGetProperty("expression", out _));
    }

    [Fact]
    public async Task Managed_stop_cancels_readiness_before_endpoint_becomes_available()
    {
        var input = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifetime = QamHostManagedLifetime.Start(() => input.Task);
        input.SetResult("stop");

        await lifetime.StopTask;
        Assert.True(lifetime.Token.IsCancellationRequested);
    }

    [Fact]
    public void Launch_uses_managed_mode_hidden_stdin_and_canonical_log_directory()
    {
        var runtime = Path.Combine(Path.GetTempPath(), "qam-test-" + Guid.NewGuid().ToString("N"));
        var qamDirectory = Path.Combine(runtime, "qam");
        Directory.CreateDirectory(qamDirectory);
        var executable = Path.Combine(qamDirectory, "SteamInputAddonforClaw.QamHost.exe");
        File.WriteAllText(executable, string.Empty);
        var starts = new List<ProcessStartInfo>();

        try
        {
            var controller = new QamHostProcessController(runtime, @"C:\logs", info =>
            {
                starts.Add(info);
                return null;
            });

            controller.OnBigPictureStateChanged(true);
            SpinWait.SpinUntil(() => starts.Count == 1, TimeSpan.FromSeconds(1));

            var start = Assert.Single(starts);
            Assert.False(start.UseShellExecute);
            Assert.True(start.CreateNoWindow);
            Assert.True(start.RedirectStandardInput);
            Assert.Equal(["--managed", "--log-directory", @"C:\logs"], start.ArgumentList);
        }
        finally
        {
            Directory.Delete(runtime, recursive: true);
        }
    }

    [Fact]
    public async Task Already_exited_child_is_cleared_on_stop()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ => StartCommand("/c", "exit 0"));
        controller.OnBigPictureStateChanged(true);
        await Task.Delay(100);
        await controller.StopAsync();
        Assert.False(controller.HasTrackedProcess);
    }

    [Fact]
    public async Task Steam_game_exit_stops_qam_host_without_big_picture()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ => StartCommand("/c", "ping 127.0.0.1 -n 30 > nul"));

        controller.OnActualRunningAppIdChanged(123);
        await WaitForTrackedProcessAsync(controller);
        controller.OnActualRunningAppIdChanged(0);

        await WaitForNoTrackedProcessAsync(controller);
        Assert.False(controller.HasTrackedProcess);
    }

    [Fact]
    public async Task Game_exit_keeps_qam_host_running_while_big_picture_is_active()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ => StartCommand("/c", "ping 127.0.0.1 -n 30 > nul"));

        controller.OnBigPictureStateChanged(true);
        controller.OnActualRunningAppIdChanged(123);
        await WaitForTrackedProcessAsync(controller);
        controller.OnActualRunningAppIdChanged(0);
        await Task.Delay(50);

        Assert.True(controller.HasTrackedProcess);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task Big_picture_exit_keeps_qam_host_running_while_steam_game_is_active()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ => StartCommand("/c", "ping 127.0.0.1 -n 30 > nul"));

        controller.OnBigPictureStateChanged(true);
        controller.OnActualRunningAppIdChanged(123);
        await WaitForTrackedProcessAsync(controller);
        controller.OnBigPictureStateChanged(false);
        await Task.Delay(50);

        Assert.True(controller.HasTrackedProcess);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task Both_sources_must_be_inactive_before_qam_host_stops()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ => StartCommand("/c", "ping 127.0.0.1 -n 30 > nul"));

        controller.OnBigPictureStateChanged(true);
        controller.OnActualRunningAppIdChanged(123);
        await WaitForTrackedProcessAsync(controller);
        controller.OnActualRunningAppIdChanged(0);
        await Task.Delay(50);
        Assert.True(controller.HasTrackedProcess);
        controller.OnBigPictureStateChanged(false);
        await WaitForNoTrackedProcessAsync(controller);
        Assert.False(controller.HasTrackedProcess);
    }

    [Fact]
    public async Task Game_is_last_active_source_before_automatic_qam_host_stop()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ => StartCommand("/c", "ping 127.0.0.1 -n 30 > nul"));

        controller.OnBigPictureStateChanged(true);
        controller.OnActualRunningAppIdChanged(123);
        await WaitForTrackedProcessAsync(controller);
        controller.OnBigPictureStateChanged(false);
        await Task.Delay(50);
        Assert.True(controller.HasTrackedProcess);

        controller.OnActualRunningAppIdChanged(0);
        await WaitForNoTrackedProcessAsync(controller);
        Assert.False(controller.HasTrackedProcess);
    }

    [Fact]
    public async Task Duplicate_source_notifications_do_not_start_a_second_process()
    {
        using var scope = new QamHostTestScope();
        var starts = 0;
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ =>
        {
            Interlocked.Increment(ref starts);
            return StartCommand("/c", "ping 127.0.0.1 -n 30 > nul");
        });

        controller.OnActualRunningAppIdChanged(123);
        controller.OnActualRunningAppIdChanged(456);
        await WaitForTrackedProcessAsync(controller);
        await Task.Delay(50);

        Assert.Equal(1, starts);
        await controller.DisposeAsync();
    }

    [Fact]
    public async Task Shutdown_prevents_later_source_notifications_from_restarting_qam_host()
    {
        using var scope = new QamHostTestScope();
        var starts = 0;
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ =>
        {
            Interlocked.Increment(ref starts);
            return StartCommand("/c", "ping 127.0.0.1 -n 30 > nul");
        });

        controller.OnBigPictureStateChanged(true);
        await WaitForTrackedProcessAsync(controller);
        controller.BeginShutdown();
        controller.OnActualRunningAppIdChanged(123);
        await controller.StopAsync();
        await Task.Delay(50);

        Assert.Equal(1, starts);
        Assert.False(controller.HasTrackedProcess);
    }

    [Fact]
    public async Task Failed_stdin_stop_falls_back_to_termination_and_clears_child()
    {
        using var scope = new QamHostTestScope();
        var controller = new QamHostProcessController(scope.Runtime, @"C:\logs", _ =>
        {
            var process = StartCommand("/c", "ping 127.0.0.1 -n 30 > nul");
            process.StandardInput.Close();
            return process;
        });
        controller.OnBigPictureStateChanged(true);
        await Task.Delay(100);
        await controller.StopAsync();
        Assert.False(controller.HasTrackedProcess);
    }

    private static Process StartCommand(params string[] arguments)
    {
        var info = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static async Task WaitForTrackedProcessAsync(QamHostProcessController controller)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!controller.HasTrackedProcess && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(controller.HasTrackedProcess);
    }

    private static async Task WaitForNoTrackedProcessAsync(QamHostProcessController controller)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (controller.HasTrackedProcess && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.False(controller.HasTrackedProcess);
    }

    private sealed class QamHostTestScope : IDisposable
    {
        internal string Runtime { get; } = Path.Combine(Path.GetTempPath(), "qam-test-" + Guid.NewGuid().ToString("N"));
        public QamHostTestScope()
        {
            Directory.CreateDirectory(Path.Combine(Runtime, "qam"));
            File.WriteAllText(Path.Combine(Runtime, "qam", "SteamInputAddonforClaw.QamHost.exe"), string.Empty);
        }
        public void Dispose() => Directory.Delete(Runtime, true);
    }
}
