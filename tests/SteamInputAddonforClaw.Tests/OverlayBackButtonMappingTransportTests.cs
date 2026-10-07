using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayBackButtonMappingTransportTests
{
    private static string Pipe() => $"SteamInputAddonforClaw.Overlay.BackButton.Tests.{Guid.NewGuid():N}";

    private static OverlayBackButtonMappingState AvailableState(BackButtonMappingSettings? mapping = null) =>
        new(true, mapping ?? BackButtonMappingSettings.Default);

    [Fact]
    public void Overlay_protocol_is_v16()
    {
        Assert.Equal(16, OverlayTransportProtocol.CurrentVersion);
    }

    [Fact]
    public async Task State_and_mutation_result_round_trip_with_typed_mapping()
    {
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper);
        var expectedState = AvailableState(mapping);
        var expectedResponse = new OverlayBackButtonMappingMutationResponse(7, true, null, expectedState);
        var message = new OverlayWireMessage(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.BackButtonMappingMutationResult,
            BackButtonMappingMutationResponse: expectedResponse);
        await using var stream = new MemoryStream();
        using var gate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.WriteAsync(stream, message, gate, CancellationToken.None);
        stream.Position = 0;
        var restored = await OverlayWireCodec.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.BackButtonMappingMutationResult, restored.Kind);
        Assert.Equal(expectedResponse, restored.BackButtonMappingMutationResponse);
        Assert.True(OverlayBackButtonMappingWireValidation.IsStructurallyValid(restored.BackButtonMappingMutationResponse));
        Assert.True(OverlayBackButtonMappingWireValidation.IsStructurallyValid(expectedState));
    }

    [Fact]
    public void Mapping_and_correlated_request_validation_fail_closed()
    {
        Assert.False(OverlayBackButtonMappingWireValidation.IsStructurallyValid(
            new OverlayBackButtonMappingState(true, new((Xbox360BackButtonTarget)999, Xbox360BackButtonTarget.Disabled))));
        Assert.False(OverlayBackButtonMappingWireValidation.IsStructurallyValid(
            new OverlayBackButtonMappingState(true, null!)));
        Assert.False(OverlayBackButtonMappingWireValidation.IsStructurallyValid(
            new OverlayBackButtonMappingMutationRequest(0, BackButtonMappingSettings.Default)));
        Assert.False(OverlayBackButtonMappingWireValidation.IsStructurallyValid(
            new OverlayBackButtonMappingMutationRequest(1, new(Xbox360BackButtonTarget.Disabled, (Xbox360BackButtonTarget)999))));
        Assert.True(OverlayBackButtonMappingWireValidation.IsStructurallyValid(
            new OverlayBackButtonMappingMutationRequest(1, new(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.A))));
    }

    [Fact]
    public void Mapping_message_kinds_reject_mixed_mapping_payloads()
    {
        var state = AvailableState();
        var request = new OverlayBackButtonMappingMutationRequest(1, BackButtonMappingSettings.Default);
        var response = new OverlayBackButtonMappingMutationResponse(1, true, null, state);

        Assert.False(OverlayBackButtonMappingWireValidation.IsValidStateMessage(
            new(13, OverlayWireMessageKind.BackButtonMappingState, BackButtonMappingState: state,
                BackButtonMappingMutationRequest: request)));
        Assert.False(OverlayBackButtonMappingWireValidation.IsValidMutationRequestMessage(
            new(13, OverlayWireMessageKind.BackButtonMappingMutationRequest, BackButtonMappingMutationRequest: request,
                BackButtonMappingState: state)));
        Assert.False(OverlayBackButtonMappingWireValidation.IsValidMutationResultMessage(
            new(13, OverlayWireMessageKind.BackButtonMappingMutationResult, BackButtonMappingMutationResponse: response,
                BackButtonMappingState: state)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hidden_or_not_ready_server_does_not_invoke_mapping_mutation(bool markReady)
    {
        var pipeName = Pipe();
        var mutationCount = 0;
        var expected = AvailableState(new(Xbox360BackButtonTarget.B, Xbox360BackButtonTarget.Menu));
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureBackButtonMapping: _ => Task.FromResult(expected),
            mutateBackButtonMapping: (_, _) =>
            {
                Interlocked.Increment(ref mutationCount);
                return Task.FromResult(new OverlayBackButtonMappingMutationOutcome(true, null, expected));
            });
        await server.StartAsync();
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.HandshakeAccepted,
            (await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);
        Assert.Equal(OverlayWireMessageKind.TabOrderState,
            (await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        if (markReady)
        {
            await OverlayWireCodec.WriteAsync(pipe,
                new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
            Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        }

        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.BackButtonMappingMutationRequest,
                BackButtonMappingMutationRequest: new(1, new(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.RightBumper))),
            writeGate, CancellationToken.None);
        var result = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.BackButtonMappingMutationResult, result.Kind);
        Assert.False(result.BackButtonMappingMutationResponse!.Succeeded);
        Assert.Equal(expected, result.BackButtonMappingMutationResponse.State);
        Assert.Equal(0, mutationCount);
    }

    [Fact]
    public async Task Visible_mapping_mutation_invokes_one_delegate_and_returns_authoritative_state()
    {
        var pipeName = Pipe();
        var requested = new BackButtonMappingSettings(Xbox360BackButtonTarget.LeftTrigger, Xbox360BackButtonTarget.XboxGuide);
        var mutationCount = 0;
        await using var server = new NamedPipeOverlayServer(pipeName,
            mutateBackButtonMapping: (mapping, _) =>
            {
                Interlocked.Increment(ref mutationCount);
                return Task.FromResult(new OverlayBackButtonMappingMutationOutcome(true, null, AvailableState(mapping)));
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null,
            _ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var response = await client.SendBackButtonMappingMutationAsync(requested).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(response.Succeeded);
        Assert.Equal(requested, response.State.Mapping);
        Assert.Null(response.FailureMessage);
        Assert.Equal(1, mutationCount);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dismiss_remains_readable_while_mapping_persistence_is_pending()
    {
        var pipeName = Pipe();
        var mutationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowMutationToFinish = new TaskCompletionSource<OverlayBackButtonMappingMutationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dismissalReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new BackButtonMappingSettings(Xbox360BackButtonTarget.Menu, Xbox360BackButtonTarget.View);
        await using var server = new NamedPipeOverlayServer(pipeName,
            mutateBackButtonMapping: async (mapping, _) =>
            {
                mutationStarted.TrySetResult();
                return await allowMutationToFinish.Task;
            });
        server.DismissRequested += _ => dismissalReceived.TrySetResult();
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null,
            _ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var pendingMutation = client.SendBackButtonMappingMutationAsync(requested);
        await mutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.SendDismissRequestedAsync();
        await dismissalReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        allowMutationToFinish.TrySetResult(new(true, null, AvailableState(requested)));
        Assert.True((await pendingMutation.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Wrong_request_id_does_not_complete_the_pending_mapping_mutation()
    {
        var pipeName = Pipe();
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null,
            _ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState,
                TabOrderState: AddonQuickSettingsTabOrderProduct.Create(AddonQuickSettingsTabOrderContract.DefaultOrder)),
            writeGate, CancellationToken.None);
        Assert.Equal(OverlayState.Ready, (await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None)).State);

        var requested = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.B);
        var pending = client.SendBackButtonMappingMutationAsync(requested);
        var request = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        var requestId = request.BackButtonMappingMutationRequest!.RequestId;
        var state = AvailableState(requested);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.BackButtonMappingMutationResult,
                BackButtonMappingMutationResponse: new(requestId + 1, true, null, state)),
            writeGate, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);

        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.BackButtonMappingMutationResult,
                BackButtonMappingMutationResponse: new(requestId, true, null, state)),
            writeGate, CancellationToken.None);
        Assert.Equal(requestId, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).RequestId);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Command, Command: OverlayCommand.Shutdown),
            writeGate, CancellationToken.None);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disconnect_cancels_and_clears_pending_mapping_mutation()
    {
        var pipeName = Pipe();
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null,
            _ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState,
                TabOrderState: AddonQuickSettingsTabOrderProduct.Create(AddonQuickSettingsTabOrderContract.DefaultOrder)),
            writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        var pending = client.SendBackButtonMappingMutationAsync(
            new(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y));
        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);

        await client.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(typeof(NamedPipeOverlayClient).GetField("_pendingBackButtonMappingMutation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        Assert.Equal(0L, typeof(NamedPipeOverlayClient).GetField("_pendingBackButtonMappingRequestId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Server_rejects_a_back_button_payload_mixed_into_an_unrelated_message()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderMoveRequest,
                TabOrderMove: new(AddonQuickSettingsTabId.Device, 1),
                BackButtonMappingState: AvailableState()),
            writeGate, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => OverlayWireCodec.ReadAsync(pipe, timeout.Token));
    }

    [Fact]
    public async Task Overlay_process_controller_publishes_mapping_only_while_visible_and_capture_failure_is_local()
    {
        var root = CreateOverlayRoot();
        var pipeName = FrontendPipeEndpoint.CreateOverlayForCurrentUser();
        var expected = AvailableState(new(Xbox360BackButtonTarget.Menu, Xbox360BackButtonTarget.View));
        var captureCount = 0;
        var received = new TaskCompletionSource<OverlayBackButtonMappingState>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"), StartTestProcess);
            controller.BindBackButtonMappingAuthority(_ =>
            {
                Interlocked.Increment(ref captureCount);
                return Task.FromResult(expected);
            }, (_, _) => Task.FromResult(new OverlayBackButtonMappingMutationOutcome(false, "Rejected.", expected)));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null,
                state =>
            {
                received.TrySetResult(state);
                return Task.CompletedTask;
            });

            Assert.True(await controller.StartAsync());
            await controller.RefreshBackButtonMappingAsync();
            Assert.Equal(0, captureCount);

            Assert.True(await controller.ShowAsync());
            await controller.RefreshBackButtonMappingAsync();
            Assert.Equal(expected, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, captureCount);

            Assert.True(await controller.EnsureHiddenAsync());
            await controller.RefreshBackButtonMappingAsync();
            Assert.Equal(1, captureCount);
            Assert.True(controller.HasTrackedProcess);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally { DeleteOverlayRoot(root); }
    }

    [Fact]
    public async Task Overlay_process_controller_publishes_unavailable_state_when_capture_fails()
    {
        var root = CreateOverlayRoot();
        var pipeName = FrontendPipeEndpoint.CreateOverlayForCurrentUser();
        var received = new TaskCompletionSource<OverlayBackButtonMappingState>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"), StartTestProcess);
            controller.BindBackButtonMappingAuthority(
                _ => throw new InvalidOperationException("test-only failure"),
                (_, _) => Task.FromResult(new OverlayBackButtonMappingMutationOutcome(false, "Rejected.", OverlayBackButtonMappingState.Unavailable())));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null, null, null, null,
                state =>
            {
                received.TrySetResult(state);
                return Task.CompletedTask;
            });

            Assert.True(await controller.ShowAsync());
            await controller.RefreshBackButtonMappingAsync();
            var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(state.Available);
            Assert.Equal(BackButtonMappingSettings.Default, state.Mapping);
            Assert.Equal("M1 / M2 mapping is unavailable.", state.FailureMessage);
            Assert.True(controller.IsVisible);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally { DeleteOverlayRoot(root); }
    }

    private static string CreateOverlayRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        return root;
    }

    private static Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = "/c timeout /t 30 /nobreak >nul",
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    private static void DeleteOverlayRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
