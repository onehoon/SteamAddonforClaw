using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayTransportTests
{
    [Fact]
    public void Overlay_endpoint_is_distinct_from_frontend_and_qam_endpoints()
    {
        var frontend = FrontendPipeEndpoint.CreateForCurrentUser();
        var qam = FrontendPipeEndpoint.CreateQamForCurrentUser();
        var overlay = FrontendPipeEndpoint.CreateOverlayForCurrentUser();

        Assert.NotEqual(frontend, qam);
        Assert.NotEqual(frontend, overlay);
        Assert.NotEqual(qam, overlay);
        Assert.EndsWith(".Overlay", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_overlay_frame_is_rejected()
    {
        await using var stream = new MemoryStream();
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, OverlayTransportProtocol.MaxFrameBytes + 1);
        await stream.WriteAsync(prefix);
        stream.Position = 0;

        await Assert.ThrowsAsync<FrontendProtocolException>(() => OverlayWireCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Version_mismatch_is_rejected_by_the_overlay_server()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion + 1, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
        Assert.Contains("version", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warm_overlay_protocol_round_trips_show_hide_shutdown()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Overlay_client_sends_ready_without_an_unsolicited_hidden_state()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);

        var hello = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.Handshake, hello.Kind);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);

        var ready = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(OverlayState.Ready, ready.State);
        using var noExtraState = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OverlayWireCodec.ReadAsync(pipe, noExtraState.Token));

        await client.DisposeAsync();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
    }

    [Fact]
    public async Task Immediate_show_after_ready_is_acknowledged_by_the_real_visible_state()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        var show = server.SendCommandAsync(OverlayCommand.Show);
        var command = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        Assert.Equal(OverlayCommand.Show, command.Command);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Visible), writeGate, CancellationToken.None);

        Assert.True(await show.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(OverlayState.Visible, server.State);
    }

    [Fact]
    public async Task Dismiss_requested_round_trips_without_mutating_overlay_state()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var dismissal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DismissRequested += _ => dismissal.TrySetResult();
        var run = client.RunAsync(_ => Task.CompletedTask);

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        await client.SendDismissRequestedAsync();
        await dismissal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dismiss_requested_does_not_complete_an_in_flight_show_acknowledgement()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var showReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseShow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dismissal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DismissRequested += _ => dismissal.TrySetResult();
        var run = client.RunAsync(async command =>
        {
            if (command == OverlayCommand.Show)
            {
                showReceived.TrySetResult();
                await releaseShow.Task;
            }
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        var show = server.SendCommandAsync(OverlayCommand.Show);
        await showReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.SendDismissRequestedAsync();
        await dismissal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await Task.WhenAny(show, Task.Delay(100)) == show);

        releaseShow.TrySetResult();
        Assert.True(await show.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Runtime_routes_dismissal_through_one_hide_transition()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var commands = new List<OverlayCommand>();
        var hideHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(command =>
            {
                lock (commands) commands.Add(command);
                if (command == OverlayCommand.Hide) hideHandled.TrySetResult();
                return Task.CompletedTask;
            });

            await controller.ToggleForPocAsync();
            await client.SendDismissRequestedAsync();
            await hideHandled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            lock (commands) Assert.Equal([OverlayCommand.Show, OverlayCommand.Hide], commands);

            await controller.DisposeAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Disconnect_allows_a_later_overlay_client_to_reconnect()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();

        await using (var first = new NamedPipeOverlayClient(pipeName))
        {
            var run = first.RunAsync(_ => Task.CompletedTask);
            Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
            await first.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }

        await using var second = new NamedPipeOverlayClient(pipeName);
        var secondRun = second.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        await server.SendCommandAsync(OverlayCommand.Shutdown);
        await secondRun.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Missing_overlay_publish_payload_is_feature_local_and_shutdown_rejects_later_start()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"));

        Assert.False(await controller.StartAsync());
        controller.BeginShutdown();
        Assert.False(await controller.StartAsync());
    }

    [Fact]
    public async Task Failed_overlay_command_retires_the_session_for_the_next_toggle()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var firstPipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var secondPipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var currentPipeName = firstPipeName;
        var starts = 0;
        using var firstClientCancellation = new CancellationTokenSource();

        Process? StartTestProcess(ProcessStartInfo _) {
            Interlocked.Increment(ref starts);
            return Process.Start(new ProcessStartInfo {
                FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
                UseShellExecute = false, CreateNoWindow = true
            });
        }

        async Task RunClientAsync(bool acknowledgeShow, CancellationToken cancellationToken = default)
        {
            await using var client = new NamedPipeOverlayClient(currentPipeName);
            try
            {
                await client.RunAsync(async command => {
                    if (command == OverlayCommand.Show && !acknowledgeShow)
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }, cancellationToken);
            }
            catch (Exception) { }
        }

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(currentPipeName));
            var firstClient = RunClientAsync(acknowledgeShow: false, cancellationToken: firstClientCancellation.Token);
            await controller.ToggleForPocAsync();
            Assert.Equal(1, starts);
            Assert.False(controller.HasTrackedProcess);
            // The intentionally hung handler models a dispatcher that never acknowledges Show.
            // Cancel the client after the controller retires that failed session.
            firstClientCancellation.Cancel();
            await firstClient.WaitAsync(TimeSpan.FromSeconds(5));

            currentPipeName = secondPipeName;
            var secondClient = RunClientAsync(acknowledgeShow: true);
            await Task.Delay(250);
            await controller.ToggleForPocAsync();
            Assert.Equal(2, starts);
            // A second process-start attempt proves the failed first session was retired;
            // cleanup below also covers the intentionally synthetic process.
            await controller.DisposeAsync();
            await secondClient.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
