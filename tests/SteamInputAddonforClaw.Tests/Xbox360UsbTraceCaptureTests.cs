using System.Collections.Concurrent;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class Xbox360UsbTraceCaptureTests
{
    [Fact]
    public async Task Start_configures_all_required_providers_and_stop_resolves_the_generated_etl_inside_addon_logs()
    {
        using var directory = new TemporaryDirectory();
        using var appLogDirectory = new AppLogDirectoryOverride(directory.Path);
        var runner = new FakeElevatedProcessRunner(directory.Path);
        var capture = CreateCapture(runner);

        await capture.StartAsync("run-123", CancellationToken.None);
        Assert.True(capture.IsStarted);
        await capture.StopAsync("run-123", "ManualStop");

        Assert.Equal(6, runner.Calls.Count);
        var calls = runner.Calls.ToArray();
        Assert.StartsWith("stop -n", calls[0].Arguments, StringComparison.Ordinal);
        Assert.StartsWith("delete -n", calls[1].Arguments, StringComparison.Ordinal);
        Assert.StartsWith("create trace -n", calls[2].Arguments, StringComparison.Ordinal);
        Assert.StartsWith("start -n", calls[3].Arguments, StringComparison.Ordinal);
        Assert.StartsWith("stop -n", calls[4].Arguments, StringComparison.Ordinal);
        Assert.StartsWith("delete -n", calls[5].Arguments, StringComparison.Ordinal);
        Assert.Equal(
        [
            "Microsoft-Windows-USB-USBXHCI (Default,PartialDataBusTrace)",
            "Microsoft-Windows-USB-UCX (Default,HeadersBusTrace,FullDataBusTrace,IRP,HWVerifyHost,HWVerifyHub,HWVerifyDevice)",
            "Microsoft-Windows-USB-USBHUB3 (Default,PartialDataBusTrace)",
            "Microsoft-Windows-USB-USBPORT",
            "Microsoft-Windows-USB-USBHUB",
            "Microsoft-Windows-Kernel-IoTrace 0 2",
            "{ed18c9c5-8322-48ae-bf78-d01d898a1562} 0xffffffff 0xff"
        ], runner.Providers);
        Assert.StartsWith(Path.GetFullPath(directory.Path), Path.GetFullPath(capture.OutputBasePath!), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(directory.Path), Path.GetFullPath(capture.FinalOutputPath!), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(capture.FinalOutputPath));
        Assert.EndsWith(".etl", runner.OutputPath!, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("logman.exe", calls[0].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\System32\\logman.exe", calls[0].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracefmt", string.Join(' ', calls.Select(call => call.Arguments)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wdk", string.Join(' ', calls.Select(call => call.Arguments)), StringComparison.OrdinalIgnoreCase);
        Assert.False(capture.IsStarted);
        Assert.False(File.Exists(runner.ProviderFilePath));
    }

    [Fact]
    public async Task New_start_attempts_stale_stop_and_delete_before_creating_the_session()
    {
        using var directory = new TemporaryDirectory();
        using var appLogDirectory = new AppLogDirectoryOverride(directory.Path);
        var runner = new FakeElevatedProcessRunner(directory.Path);
        var capture = CreateCapture(runner);

        await capture.StartAsync("run-stale", CancellationToken.None);

        Assert.Collection(runner.Calls,
            call => Assert.StartsWith("stop -n", call.Arguments, StringComparison.Ordinal),
            call => Assert.StartsWith("delete -n", call.Arguments, StringComparison.Ordinal),
            call => Assert.StartsWith("create trace -n", call.Arguments, StringComparison.Ordinal),
            call => Assert.StartsWith("start -n", call.Arguments, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uac_cancel_makes_trace_unavailable_but_does_not_block_the_rumble_loop()
    {
        using var directory = new TemporaryDirectory();
        using var appLogDirectory = new AppLogDirectoryOverride(directory.Path);
        var runner = new FakeElevatedProcessRunner(directory.Path)
        {
            ResultProvider = (_, index) => index == 2
                ? new(ElevatedProcessResultKind.CancelledBeforeStart)
                : new(ElevatedProcessResultKind.Completed, 0)
        };
        var capture = CreateCapture(runner);
        using var cancellation = new CancellationTokenSource();
        var firstOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var xinput = new FakeXbox360RumbleLoopXInput
        {
            OnSetState = (_, _, _) =>
            {
                firstOutput.TrySetResult();
                cancellation.Cancel();
                return Xbox360RumbleLoopDiagnostic.ErrorSuccess;
            }
        };
        var diagnostic = new Xbox360RumbleLoopDiagnostic(
            0, xinput, () => new(PhysicalRumbleWriteStatus.Succeeded, "OK"),
            TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(50), capture);

        await diagnostic.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await diagnostic.CompleteManualStopAsync();

        Assert.NotEmpty(xinput.SetStates);
        Assert.Equal(FrontendXbox360RumbleLoopState.Stopped, diagnostic.Snapshot.State);
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task Logman_commands_are_bounded_when_the_elevated_runner_does_not_return()
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeElevatedProcessRunner(directory.Path)
        {
            Hang = true
        };
        var capture = new Xbox360UsbTraceCapture(
            runner,
            () => directory.Path,
            () => directory.Path,
            TimeSpan.FromMilliseconds(25));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();

        await capture.StartAsync("run-timeout", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(5, runner.Calls.Count);
    }

    private static Xbox360UsbTraceCapture CreateCapture(FakeElevatedProcessRunner runner) =>
        new(runner, systemDirectoryProvider: () => Environment.GetFolderPath(Environment.SpecialFolder.System), commandTimeout: TimeSpan.FromSeconds(1));

    private sealed class FakeElevatedProcessRunner(string outputDirectory) : IElevatedProcessRunner
    {
        internal ConcurrentQueue<(string FileName, string Arguments)> Calls { get; } = new();
        internal Func<string, int, ElevatedProcessResult>? ResultProvider { get; init; }
        internal bool Hang { get; init; }
        internal string[] Providers { get; private set; } = [];
        internal string? OutputPath { get; private set; }
        internal string? ProviderFilePath { get; private set; }

        public Task<ElevatedProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            var index = Calls.Count;
            Calls.Enqueue((fileName, arguments));
            if (arguments.StartsWith("create trace", StringComparison.Ordinal))
            {
                ProviderFilePath = ReadArgumentAfter(arguments, "-pf");
                OutputPath = ReadArgumentAfter(arguments, "-o");
                Providers = File.ReadAllLines(ProviderFilePath);
            }
            else if (arguments.StartsWith("stop -n", StringComparison.Ordinal) && OutputPath is not null)
            {
                var finalPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(OutputPath) + "_000001.etl");
                File.WriteAllBytes(finalPath, []);
            }

            if (Hang) return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith<ElevatedProcessResult>(
                static _ => new(ElevatedProcessResultKind.FailedToStart, Reason: "Cancelled"),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return Task.FromResult(ResultProvider?.Invoke(arguments, index) ?? new(ElevatedProcessResultKind.Completed, 0));
        }

        private static string ReadArgumentAfter(string arguments, string key)
        {
            var marker = key + " \"";
            var start = arguments.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = arguments.IndexOf('"', start);
            return arguments[start..end];
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Xbox360UsbTraceCaptureTests-" + Guid.NewGuid().ToString("N"));

        internal TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class AppLogDirectoryOverride : IDisposable
    {
        private readonly string? _previous = AppLog.DirectoryOverride;

        internal AppLogDirectoryOverride(string path) => AppLog.DirectoryOverride = path;

        public void Dispose()
        {
            AppLog.DrainForTests();
            AppLog.DirectoryOverride = _previous;
        }
    }
}
