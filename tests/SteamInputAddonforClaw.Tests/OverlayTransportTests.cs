using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using SteamInputAddonforClaw.Contracts.Overlay;
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
    public async Task Pre_navigation_v2_peer_is_rejected_by_the_overlay_server()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(2, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
    }

    [Fact]
    public async Task Server_delivers_a_semantic_navigation_frame_to_a_visible_overlay()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var actions = new List<OverlayNavigationAction>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, action =>
        {
            lock (actions) actions.Add(action);
            received.TrySetResult();
            return Task.CompletedTask;
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendNavigationAsync(OverlayNavigationAction.NavigateDown));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (actions) Assert.Equal([OverlayNavigationAction.NavigateDown], actions);
        Assert.Equal(OverlayState.Visible, server.State);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Hidden_or_unready_overlay_does_not_accept_navigation_delivery()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();

        // No client connected yet -> unready.
        Assert.False(await server.SendNavigationAsync(OverlayNavigationAction.Accept));

        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        // Ready but still Hidden (no Show yet).
        Assert.False(await server.SendNavigationAsync(OverlayNavigationAction.Accept));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        // Back to Hidden -> rejected again.
        Assert.False(await server.SendNavigationAsync(OverlayNavigationAction.Accept));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Command_and_navigation_frames_stay_intact_through_the_shared_write_gate()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var actions = new List<OverlayNavigationAction>();
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, action =>
        {
            lock (actions)
            {
                actions.Add(action);
                if (actions.Count == Enum.GetValues<OverlayNavigationAction>().Length) all.TrySetResult();
            }
            return Task.CompletedTask;
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var everyAction = Enum.GetValues<OverlayNavigationAction>();
        await Task.WhenAll(everyAction.Select(a => server.SendNavigationAsync(a)));
        await all.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (actions) Assert.Equal(everyAction.OrderBy(x => x), actions.OrderBy(x => x));

        // Connection still usable for a normal command after the navigation burst.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
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
        // OQ5-UI-09: the client applies the initial authoritative order before it reports Ready.
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState, TabOrder: OverlayTabOrderContract.DefaultOrder), writeGate, CancellationToken.None);

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
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted
        var initialOrder = await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // OQ5-UI-09 TabOrderState
        Assert.Equal(OverlayWireMessageKind.TabOrderState, initialOrder.Kind);
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
    public async Task Dismiss_request_raises_the_runtime_signal_and_the_controller_does_not_send_hide_itself()
    {
        // OQ4 section 10: the controller no longer finishes a visible Hide on outside-click -- it
        // validates the dismissal and raises OverlayDismissRequested; AddonProcessHost runs the
        // unified capture-retirement path (which then calls EnsureHiddenAsync).
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var commands = new List<OverlayCommand>();
        var dismissSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // `pause` with a redirected-but-never-written stdin blocks reliably; `timeout` exits early
        // when stdin is not a console, which would race the process-exit path into this test.
        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c pause",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.OverlayDismissRequested += () => dismissSignal.TrySetResult();
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(command =>
            {
                lock (commands) commands.Add(command);
                return Task.CompletedTask;
            });

            Assert.True(await controller.ShowAsync());
            Assert.True(controller.IsVisible);
            await client.SendDismissRequestedAsync();
            await dismissSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);

            // The controller raised the signal but issued no Hide of its own; the overlay is still
            // visible until the Runtime runs the unified retirement path.
            lock (commands) Assert.Equal([OverlayCommand.Show], commands);
            Assert.True(controller.IsVisible);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Ensure_hidden_that_cannot_run_reports_failure_and_keeps_the_surface_visible()
    {
        // OQ4 PR3 review [2]: this is the exact signal AddonProcessHost.RetireOverlayCaptureUnder-
        // TransitionAsync gates on -- EnsureHiddenAsync() == false while IsVisible stays true means
        // "retirement not proven", which blocks the following Main UI launch.
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c pause",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask);

            Assert.True(await controller.ShowAsync());
            Assert.True(controller.IsVisible);

            controller.BeginShutdown(); // a transient state where EnsureHiddenAsync cannot run

            Assert.False(await controller.EnsureHiddenAsync());
            Assert.True(controller.IsVisible); // surface not proven gone -> host blocks Main UI launch

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Explicit_show_and_hide_track_visibility_and_are_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var commands = new List<OverlayCommand>();

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
                return Task.CompletedTask;
            });

            Assert.False(controller.IsVisible);
            Assert.True(await controller.EnsureHiddenAsync()); // idempotent while already hidden
            Assert.True(await controller.ShowAsync());
            Assert.True(controller.IsVisible);
            Assert.True(await controller.ShowAsync()); // idempotent while already visible
            Assert.True(await controller.EnsureHiddenAsync());
            Assert.False(controller.IsVisible);
            await Task.Delay(100);
            lock (commands) Assert.Equal([OverlayCommand.Show, OverlayCommand.Hide], commands);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_explicit_hide_retires_the_session_and_reports_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        using var hangCancellation = new CancellationTokenSource();

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(async command =>
            {
                if (command == OverlayCommand.Hide)
                    await Task.Delay(Timeout.InfiniteTimeSpan, hangCancellation.Token);
            });

            Assert.True(await controller.ShowAsync());
            Assert.False(await controller.EnsureHiddenAsync());
            Assert.False(controller.HasTrackedProcess);
            Assert.False(controller.IsVisible);

            hangCancellation.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            await client.DisposeAsync();
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
