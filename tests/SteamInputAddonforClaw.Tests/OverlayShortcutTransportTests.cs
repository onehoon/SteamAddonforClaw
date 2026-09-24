using System.Diagnostics;
using System.IO.Pipes;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayShortcutTransportTests
{
    [Fact]
    public void Overlay_protocol_is_v12_and_rejects_a_v11_peer()
    {
        Assert.Equal(12, OverlayTransportProtocol.CurrentVersion);
    }

    [Fact]
    public async Task V11_handshake_is_rejected()
    {
        var pipeName = NewPipeName();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.WriteAsync(client, new(11, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);

        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
    }

    [Fact]
    public async Task Shortcut_state_round_trips_all_tiles_in_runtime_order()
    {
        var pipeName = NewPipeName();
        var expected = Snapshot("First", "Second", "Third", "Fourth", "Fifth", "Sixth");
        var received = new TaskCompletionSource<FrontendShortcutDashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null, null, shortcutStateHandler: state =>
        {
            received.TrySetResult(state);
            return Task.CompletedTask;
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendShortcutStateAsync(expected));

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected.Tiles, actual.Tiles);
        Assert.Equal(expected.Tiles.Select(tile => tile.Title), actual.Tiles.Select(tile => tile.Title));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Shortcut_messages_reject_malformed_shapes_and_unrelated_payloads()
    {
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(
            new FrontendShortcutDashboardSnapshot(true, [null!])));
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(
            new FrontendShortcutDashboardSnapshot(true, [new(Guid.Empty, "Tile", null, FrontendShortcutTileState.Neutral, true)])));
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(
            new FrontendShortcutDashboardSnapshot(true, [new(Guid.NewGuid(), null!, null, FrontendShortcutTileState.Neutral, true)])));
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(
            new FrontendShortcutDashboardSnapshot(true, [new(Guid.NewGuid(), "Tile", null, (FrontendShortcutTileState)999, true)])));
        var duplicateId = Guid.NewGuid();
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(new FrontendShortcutDashboardSnapshot(true,
        [
            new(duplicateId, "First", null, FrontendShortcutTileState.Neutral, true),
            new(duplicateId, "Second", null, FrontendShortcutTileState.Neutral, true),
        ])));

        var tileId = Guid.NewGuid();
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(new OverlayShortcutExecuteRequest(0, tileId)));
        Assert.False(OverlayShortcutWireValidation.IsStructurallyValid(new OverlayShortcutExecuteRequest(1, Guid.Empty)));
        Assert.True(OverlayShortcutWireValidation.IsStructurallyValid(new OverlayShortcutExecuteRequest(1, tileId)));

        var request = new OverlayWireMessage(12, OverlayWireMessageKind.ShortcutExecuteRequest,
            ShortcutExecuteRequest: new OverlayShortcutExecuteRequest(1, tileId),
            Error: "unexpected");
        Assert.False(OverlayShortcutWireValidation.IsValidExecuteRequestMessage(request));
    }

    [Fact]
    public void Shortcut_state_and_result_are_bounded_using_the_actual_wire_envelopes()
    {
        Assert.Equal(512 * 1024, OverlayTransportProtocol.MaxFrameBytes);

        var id = Guid.NewGuid();
        var low = 0;
        var high = OverlayTransportProtocol.MaxFrameBytes;
        FrontendShortcutDashboardSnapshot state = Snapshot("x");
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidate = new FrontendShortcutDashboardSnapshot(true,
                [new FrontendShortcutTile(id, new string('T', middle), null, FrontendShortcutTileState.Neutral, true)]);
            var envelope = new OverlayWireMessage(12, OverlayWireMessageKind.ShortcutState, ShortcutState: candidate);
            if (OverlayWireCodec.GetSerializedLength(envelope) <= OverlayTransportProtocol.MaxFrameBytes)
            {
                low = middle;
                state = candidate;
            }
            else high = middle - 1;
        }

        var stateEnvelope = OverlayShortcutWireValidation.CreateBoundedStateMessage(state);
        Assert.True(OverlayWireCodec.GetSerializedLength(stateEnvelope) <= OverlayTransportProtocol.MaxFrameBytes);
        Assert.Equal(state, stateEnvelope.ShortcutState);

        var smallResult = new OverlayShortcutExecuteResponse(1, id, false, "Shortcut could not be launched.", Snapshot("Small"));
        var smallResultEnvelope = new OverlayWireMessage(12, OverlayWireMessageKind.ShortcutExecuteResult,
            ShortcutExecuteResult: smallResult);
        Assert.True(OverlayWireCodec.GetSerializedLength(smallResultEnvelope) <= OverlayTransportProtocol.MaxFrameBytes);
        Assert.Equal(smallResult, OverlayShortcutWireValidation.CreateBoundedResultMessage(smallResult).ShortcutExecuteResult);

        var oversizedByResultWrapper = new OverlayShortcutExecuteResponse(2, id, false,
            "Shortcut could not be launched.", state);
        var fullResultEnvelope = new OverlayWireMessage(12, OverlayWireMessageKind.ShortcutExecuteResult,
            ShortcutExecuteResult: oversizedByResultWrapper);
        Assert.True(OverlayWireCodec.GetSerializedLength(stateEnvelope) <= OverlayTransportProtocol.MaxFrameBytes);
        Assert.True(OverlayWireCodec.GetSerializedLength(fullResultEnvelope) > OverlayTransportProtocol.MaxFrameBytes);

        var boundedResultEnvelope = OverlayShortcutWireValidation.CreateBoundedResultMessage(oversizedByResultWrapper);
        Assert.True(OverlayWireCodec.GetSerializedLength(boundedResultEnvelope) <= OverlayTransportProtocol.MaxFrameBytes);
        Assert.False(boundedResultEnvelope.ShortcutExecuteResult!.Snapshot.Available);
        Assert.Equal("Shortcut configuration is too large to display.", boundedResultEnvelope.ShortcutExecuteResult.Snapshot.FailureMessage);
        Assert.Equal("Shortcut could not be launched.", boundedResultEnvelope.ShortcutExecuteResult.FailureMessage);

        var oversized = new FrontendShortcutDashboardSnapshot(true,
            [new FrontendShortcutTile(Guid.NewGuid(), new string('X', OverlayTransportProtocol.MaxFrameBytes * 2), null,
                FrontendShortcutTileState.Neutral, true)]);
        var oversizedState = OverlayShortcutWireValidation.CreateBoundedStateMessage(oversized);
        Assert.True(OverlayWireCodec.GetSerializedLength(oversizedState) <= OverlayTransportProtocol.MaxFrameBytes);
        Assert.False(oversizedState.ShortcutState!.Available);

        var oversizedResult = OverlayShortcutWireValidation.CreateBoundedResultMessage(
            new OverlayShortcutExecuteResponse(3, id, true, null, oversized));
        Assert.True(OverlayWireCodec.GetSerializedLength(oversizedResult) <= OverlayTransportProtocol.MaxFrameBytes);
        Assert.False(oversizedResult.ShortcutExecuteResult!.Snapshot.Available);
        Assert.True(OverlayShortcutWireValidation.IsValidExecuteResultMessage(oversizedResult));
    }

    [Fact]
    public async Task Hidden_requests_are_not_admitted_and_visible_requests_execute_only_the_requested_tile_id()
    {
        var pipeName = NewPipeName();
        var tileId = Guid.NewGuid();
        var failedTileId = Guid.NewGuid();
        var executionCount = 0;
        var executedTileId = Guid.Empty;
        var snapshot = Snapshot("Before execution");
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureShortcut: _ => Task.FromResult(snapshot),
            executeShortcut: (requested, _) =>
            {
                executionCount++;
                executedTileId = requested;
                if (requested == failedTileId)
                {
                    snapshot = Snapshot("After failed execution");
                    return Task.FromResult(new OverlayShortcutExecutionOutcome(false, "Shortcut could not be executed."));
                }
                snapshot = Snapshot("After successful execution");
                return Task.FromResult(new OverlayShortcutExecutionOutcome(true));
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        var hiddenResult = await client.SendShortcutExecuteAsync(tileId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(hiddenResult.Succeeded);
        Assert.Equal(0, executionCount);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        var visibleResult = await client.SendShortcutExecuteAsync(tileId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(visibleResult.Succeeded);
        Assert.Equal(1, executionCount);
        Assert.Equal(tileId, executedTileId);
        Assert.Equal("After successful execution", visibleResult.Snapshot.Tiles.Single().Title);

        var failedResult = await client.SendShortcutExecuteAsync(failedTileId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(failedResult.Succeeded);
        Assert.Equal("Shortcut could not be executed.", failedResult.FailureMessage);
        Assert.Equal("After failed execution", failedResult.Snapshot.Tiles.Single().Title);
        Assert.Equal(2, executionCount);

        // A typed action failure is feature-local: the same pipe still accepts a later execution.
        Assert.True((await client.SendShortcutExecuteAsync(tileId).WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.Equal(3, executionCount);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Only_one_shortcut_execution_can_be_pending_per_client()
    {
        var pipeName = NewPipeName();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tileId = Guid.NewGuid();
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureShortcut: _ => Task.FromResult(Snapshot("Tile")),
            executeShortcut: async (_, token) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return new OverlayShortcutExecutionOutcome(true);
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var first = client.SendShortcutExecuteAsync(tileId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendShortcutExecuteAsync(tileId));

        release.TrySetResult();
        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disconnect_cancels_the_pending_shortcut_request_and_releases_execution_gate()
    {
        var pipeName = NewPipeName();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tileId = Guid.NewGuid();
        var server = new NamedPipeOverlayServer(pipeName,
            captureShortcut: _ => Task.FromResult(Snapshot("Tile")),
            executeShortcut: async (_, token) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return new OverlayShortcutExecutionOutcome(true);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var execution = client.SendShortcutExecuteAsync(tileId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await server.DisposeAsync();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(5)));
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The canceled request released the one-request gate instead of leaving it permanently held.
        await client.DisposeAsync();
    }

    [Fact]
    public async Task In_flight_screenshot_style_execution_does_not_block_hide_or_hidden_acknowledgement()
    {
        var pipeName = NewPipeName();
        var tileId = Guid.NewGuid();
        var executeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureShortcut: _ => Task.FromResult(Snapshot("Screenshot")),
            executeShortcut: async (_, token) =>
            {
                executeStarted.TrySetResult();
                await releaseExecution.Task.WaitAsync(token);
                return new OverlayShortcutExecutionOutcome(true);
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var execution = client.SendShortcutExecuteAsync(tileId);
        await executeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(OverlayState.Hidden, server.State);

        releaseExecution.TrySetResult();
        var response = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(response.Succeeded);
        Assert.Equal(tileId, response.TileId);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Overlay_controller_binds_and_publishes_runtime_shortcut_authority()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = FrontendPipeEndpoint.CreateOverlayForCurrentUser();
        var expected = Snapshot("A", "B", "C");
        var stateReceived = new TaskCompletionSource<FrontendShortcutDashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedTile = Guid.NewGuid();
        var executed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"), StartTestProcess);
            controller.BindShortcutAuthority(
                _ => Task.FromResult(expected),
                (tileId, _) =>
                {
                    executed.TrySetResult(tileId);
                    return Task.FromResult(new OverlayShortcutExecutionOutcome(true));
                });
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null, null, shortcutStateHandler: state =>
            {
                stateReceived.TrySetResult(state);
                return Task.CompletedTask;
            });

            Assert.True(await controller.ShowAsync());
            await controller.RefreshShortcutAsync();
            Assert.Equal(expected.Tiles, (await stateReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))).Tiles);

            var response = await client.SendShortcutExecuteAsync(requestedTile).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(requestedTile, (await executed.Task.WaitAsync(TimeSpan.FromSeconds(5))));
            Assert.True(response.Succeeded);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Overlay_shortcut_capture_exception_is_published_as_unavailable_without_affecting_the_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = FrontendPipeEndpoint.CreateOverlayForCurrentUser();
        var received = new TaskCompletionSource<FrontendShortcutDashboardSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"), StartTestProcess);
            controller.BindShortcutAuthority(
                _ => throw new InvalidOperationException("test-only failure"),
                (_, _) => Task.FromResult(new OverlayShortcutExecutionOutcome(false)));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null, null, shortcutStateHandler: state =>
            {
                received.TrySetResult(state);
                return Task.CompletedTask;
            });

            Assert.True(await controller.ShowAsync());
            await controller.RefreshShortcutAsync();
            var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(state.Available);
            Assert.Empty(state.Tiles);
            Assert.Equal("Shortcut settings are unavailable.", state.FailureMessage);
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
    public async Task Controller_refresh_is_hidden_session_noop_and_publish_loss_does_not_stop_the_overlay()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = FrontendPipeEndpoint.CreateOverlayForCurrentUser();
        var captureCount = 0;
        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"), StartTestProcess);
            controller.BindShortcutAuthority(
                _ =>
                {
                    Interlocked.Increment(ref captureCount);
                    return Task.FromResult(Snapshot("Tile"));
                },
                (_, _) => Task.FromResult(new OverlayShortcutExecutionOutcome(true)));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask);

            Assert.True(await controller.StartAsync());
            await controller.RefreshShortcutAsync();
            Assert.Equal(0, captureCount);

            Assert.True(await controller.ShowAsync());
            await controller.RefreshShortcutAsync();
            Assert.Equal(1, captureCount);

            // Simulate the transport peer disappearing while the process is still tracked/visible.
            await client.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            await controller.RefreshShortcutAsync();

            Assert.True(controller.IsVisible);
            Assert.True(controller.HasTrackedProcess);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static FrontendShortcutDashboardSnapshot Snapshot(params string[] titles) => new(true,
        titles.Select(title => new FrontendShortcutTile(Guid.NewGuid(), title, null, FrontendShortcutTileState.Neutral, true)).ToArray());

    private static string NewPipeName() => $"SteamInputAddonforClaw.Overlay.Shortcut.Tests.{Guid.NewGuid():N}";
}
