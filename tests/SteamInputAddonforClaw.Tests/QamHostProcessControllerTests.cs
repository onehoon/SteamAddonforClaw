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
