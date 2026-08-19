using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontendNamedPipeTransportTests
{
    private static readonly FrontendSettingsSnapshot Settings = new(true, FrontendLogLevel.Debug, true, false);
    private static readonly FrontendStatusSnapshot Status = new(
        new("MSI", "Claw", "Board", ["GPU"]), new(FrontendHardwareStatus.Supported, "Family", "Model", "Ready"), [],
        FrontendControllerEnvironmentStatus.Supported, "Ready", new(FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, ""),
        new(false, 0, FrontendSteamSource.Actual), new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, true, false, false),
        FrontendAddonOperationalStatus.Ready, "Ready", true, false, FrontendSetupStatus.Complete, "Complete", false);

    [Fact]
    public void Current_user_endpoint_is_stable_and_does_not_expose_a_sid()
    {
        var first = FrontendPipeEndpoint.CreateForCurrentUser();
        var second = FrontendPipeEndpoint.CreateForCurrentUser();

        Assert.Equal(first, second);
        Assert.StartsWith("SteamInputAddonforClaw.Frontend.", first);
        Assert.DoesNotContain("S-1-", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_frontend_operation_round_trips_typed_values_and_arguments()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);

        Assert.Equal(fake.Bootstrap, await client.GetBootstrapAsync());
        Assert.Equivalent(Status, await client.CaptureStatusAsync(), strict: true);
        Assert.Equal(fake.LaunchResult, await client.SetLaunchAtWindowsStartupAsync(false));
        Assert.False(fake.LastLaunchAtStartupEnabled);
        Assert.Equal(Settings, await client.SetRouteInSteamBigPictureAsync(false));
        Assert.False(fake.LastRouteInBigPictureEnabled);
        Assert.Equal(Settings, await client.SetLogLevelAsync(FrontendLogLevel.Info));
        Assert.Equal(FrontendLogLevel.Info, fake.LastLogLevel);
        Assert.Equal(Settings, await client.SuppressDeveloperMenuWarningAsync());
        Assert.Equal(1, fake.SuppressDeveloperWarningCount);
        Assert.Equal(new FrontendDeveloperSnapshot(true), await client.SetDeveloperTestModeAsync(true));
        Assert.True(fake.LastDeveloperTestModeEnabled);
        var setupResult = await client.RunPrerequisiteSetupAsync();
        Assert.Equal(fake.SetupResult.Result, setupResult.Result);
        Assert.Equivalent(fake.SetupResult.Status, setupResult.Status, strict: true);
        Assert.Equal(fake.EnvironmentResult, await client.GenerateEnvironmentReportAsync());
    }

    [Fact]
    public async Task Mismatched_protocol_rejects_connection_without_frontend_call()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = new NamedPipeAddonFrontendClient(pipeName, FrontendTransportProtocol.CurrentVersion + 1);

        await Assert.ThrowsAsync<FrontendProtocolException>(() => client.ConnectAsync());
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Post_handshake_protocol_mismatch_closes_connection_without_frontend_call()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion + 1, FrontendWireMessageKind.Request, 1, FrontendRpcMethod.GetBootstrap), writeGate, CancellationToken.None);

        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.ProtocolMismatch, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Malformed_response_payload_disconnects_client()
    {
        var pipeName = $"SteamInputAddonforClaw.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var writeGate = new SemaphoreSlim(1, 1);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
            var request = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            using var document = JsonDocument.Parse("{}");
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, request.RequestId, request.Method, Payload: document.RootElement.Clone()), writeGate, CancellationToken.None);
        });

        await using var client = new NamedPipeAddonFrontendClient(pipeName);
        await client.ConnectAsync();
        await Assert.ThrowsAsync<FrontendProtocolException>(() => client.GetBootstrapAsync());
        await Assert.ThrowsAsync<FrontendTransportException>(() => client.GetBootstrapAsync());
        await serverTask;
    }

    [Fact]
    public async Task Dispose_during_connect_prevents_ownership_publish()
    {
        var pipeName = $"SteamInputAddonforClaw.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var handshakeRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var writeGate = new SemaphoreSlim(1, 1);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            handshakeRead.TrySetResult();
            await releaseHandshake.Task;
            try { await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None); } catch (IOException) { }
        });

        await using var client = new NamedPipeAddonFrontendClient(pipeName);
        var connect = client.ConnectAsync();
        await handshakeRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.DisposeAsync();
        releaseHandshake.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
        await serverTask;
    }

    [Fact]
    public async Task Unknown_response_correlation_id_fails_closed()
    {
        var pipeName = $"SteamInputAddonforClaw.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var writeGate = new SemaphoreSlim(1, 1);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, 999, FrontendRpcMethod.GetBootstrap), writeGate, CancellationToken.None);
        });

        await using var client = new NamedPipeAddonFrontendClient(pipeName);
        await client.ConnectAsync();
        await Assert.ThrowsAsync<FrontendTransportException>(() => client.GetBootstrapAsync());
        await serverTask;
    }

    [Fact]
    public async Task Duplicate_request_id_is_rejected_while_original_request_is_in_flight()
    {
        var fake = new RecordingFrontendControl { BlockPrerequisiteSetup = true };
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        var request = new FrontendWireEnvelope(
            FrontendTransportProtocol.CurrentVersion,
            FrontendWireMessageKind.Request,
            1,
            FrontendRpcMethod.RunPrerequisiteSetup);
        await FrontendWireCodec.WriteAsync(pipe, request, writeGate, CancellationToken.None);
        await fake.PrerequisiteSetupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await FrontendWireCodec.WriteAsync(pipe, request, writeGate, CancellationToken.None);
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(1, fake.TotalCalls);
    }

    [Fact]
    public async Task State_invalidation_crosses_pipe_and_connection_remains_usable()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateInvalidated += (_, _) => invalidated.TrySetResult();

        fake.RaiseStateInvalidated();
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equivalent(Status, await client.CaptureStatusAsync(), strict: true);
    }

    [Fact]
    public async Task State_invalidation_burst_is_coalesced_without_breaking_rpc_flow()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        var firstNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationCount = 0;
        client.StateInvalidated += (_, _) =>
        {
            Interlocked.Increment(ref notificationCount);
            firstNotification.TrySetResult();
        };

        for (var index = 0; index < 10; index++)
            fake.RaiseStateInvalidated();

        await firstNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equivalent(Status, await client.CaptureStatusAsync(), strict: true);
        Assert.InRange(notificationCount, 1, 10);
    }

    [Fact]
    public async Task Invalidation_raised_while_notification_is_completing_is_not_lost()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        var notifications = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        client.StateInvalidated += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 1)
                fake.RaiseStateInvalidated();
            if (Volatile.Read(ref count) == 2)
                notifications.TrySetResult();
        };

        fake.RaiseStateInvalidated();
        await notifications.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Server_dispose_during_notification_delivery_completes_cleanly()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var client = await ConnectAsync(pipeName);

        fake.RaiseStateInvalidated();
        await server.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Unexpected_server_disconnect_is_reported_once_after_connection_establishment()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var client = await ConnectAsync(pipeName);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        client.Disconnected += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 1)
                disconnected.TrySetResult();
        };

        await server.DisposeAsync();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Intentional_client_dispose_does_not_report_disconnect()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        var count = 0;
        client.Disconnected += (_, _) => Interlocked.Increment(ref count);

        await client.DisposeAsync();

        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Local_dispose_wins_over_a_late_read_failure_without_disconnect_notification()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        var count = 0;
        client.Disconnected += (_, _) => Interlocked.Increment(ref count);

        await client.DisposeAsync();
        var markDisconnected = typeof(NamedPipeAddonFrontendClient).GetMethod(
            "MarkDisconnected",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        markDisconnected.Invoke(client, [new IOException("late local read failure")]);

        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Cancellation_reaches_underlying_frontend_operation()
    {
        var fake = new RecordingFrontendControl { BlockPrerequisiteSetup = true };
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        using var cts = new CancellationTokenSource();
        var request = client.RunPrerequisiteSetupAsync(cts.Token);

        await fake.PrerequisiteSetupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await fake.PrerequisiteSetupCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equivalent(Status, await client.CaptureStatusAsync(), strict: true);
    }

    [Fact]
    public async Task Cancellation_before_request_write_sends_no_request_and_keeps_connection_usable()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);
        var writeGate = (SemaphoreSlim)typeof(NamedPipeAddonFrontendClient).GetField("_writeGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client)!;
        await writeGate.WaitAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            var request = client.GetBootstrapAsync(cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        }
        finally { writeGate.Release(); }

        Assert.Equivalent(Status, await client.CaptureStatusAsync(), strict: true);
        Assert.Equal(1, fake.TotalCalls);
    }

    [Fact]
    public async Task Non_cancellation_operation_canceled_exception_maps_to_operation_failed()
    {
        var fake = new RecordingFrontendControl { ThrowOperationCanceledWithoutToken = true };
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);

        var exception = await Assert.ThrowsAsync<FrontendRemoteException>(() => client.GetBootstrapAsync());
        Assert.Equal(FrontendRemoteErrorCode.OperationFailed, exception.Code);
    }

    [Fact]
    public async Task Named_pipe_client_cancellation_after_frame_start_sends_cancel_and_connection_survives()
    {
        var pipeName = $"SteamInputAddonforClaw.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var prefixRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePayload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var writeGate = new SemaphoreSlim(1, 1);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
            var prefix = new byte[4];
            await FrontendWireCodec.ReadExactlyAsync(server, prefix, CancellationToken.None);
            var length = BitConverter.ToInt32(prefix);
            prefixRead.TrySetResult();
            await releasePayload.Task;
            var data = new byte[length];
            await FrontendWireCodec.ReadExactlyAsync(server, data, CancellationToken.None);
            var request = JsonSerializer.Deserialize<FrontendWireEnvelope>(data, FrontendWireCodec.Json)!;
            var cancel = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            Assert.Equal(FrontendWireMessageKind.CancelRequest, cancel.Kind);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, request.RequestId, request.Method, Error: new(FrontendRemoteErrorCode.Cancelled, "Operation cancelled.")), writeGate, CancellationToken.None);
            var next = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, next.RequestId, next.Method, Payload: FrontendWireCodec.Payload(new FrontendBootstrapSnapshot(new(true, FrontendLogLevel.Debug, true, false), "Registered", new(false), @"C:\Logs"))), writeGate, CancellationToken.None);
        });

        await using var client = new NamedPipeAddonFrontendClient(pipeName);
        await client.ConnectAsync();
        using var payloadDocument = JsonDocument.Parse("\"" + new string('x', 900_000) + "\"");
        using var cancellation = new CancellationTokenSource();
        var send = typeof(NamedPipeAddonFrontendClient).GetMethod("SendAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(typeof(FrontendBootstrapSnapshot));
        var request = (Task<FrontendBootstrapSnapshot>)send.Invoke(client, new object?[] { FrontendRpcMethod.GetBootstrap, payloadDocument.RootElement.Clone(), cancellation.Token })!;
        await prefixRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        releasePayload.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(Settings, (await client.GetBootstrapAsync()).Settings);
        await serverTask;
    }

    [Fact]
    public void Response_and_cancellation_have_one_atomic_terminal_state_winner()
    {
        var responseFirst = new FrontendRequestTerminalState();
        Assert.True(responseFirst.TryStart());
        var marker = true;
        Assert.True(responseFirst.TryCompleteResponse());
        Assert.False(responseFirst.TryCancelStarted());
        marker = false;
        Assert.False(marker);

        var cancellationFirst = new FrontendRequestTerminalState();
        Assert.True(cancellationFirst.TryStart());
        marker = true;
        Assert.True(cancellationFirst.TryCancelStarted());
        Assert.False(cancellationFirst.TryCompleteResponse());
        marker = false;
        Assert.False(marker);
    }

    [Fact]
    public async Task New_client_reconnects_after_previous_client_disconnects()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using (var first = await ConnectAsync(pipeName)) Assert.Equal(fake.Bootstrap, await first.GetBootstrapAsync());
        await using var second = await ConnectAsync(pipeName);
        Assert.Equal(fake.Bootstrap, await second.GetBootstrapAsync());
    }

    [Fact]
    public async Task Reconnect_after_abrupt_client_disconnect_cancels_old_operation()
    {
        var fake = new RecordingFrontendControl { BlockPrerequisiteSetup = true };
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;

        var first = await ConnectAsync(pipeName);
        var request = first.RunPrerequisiteSetupAsync();
        await fake.PrerequisiteSetupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.DisposeAsync();
        await Assert.ThrowsAsync<FrontendTransportException>(() => request);
        await fake.PrerequisiteSetupCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var second = await ConnectAsync(pipeName);
        Assert.Equal(fake.Bootstrap, await second.GetBootstrapAsync());
    }

    [Fact]
    public async Task Server_dispose_is_idempotent()
    {
        var fake = new RecordingFrontendControl();
        var (server, _) = await StartServerAsync(fake);

        await server.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_requests_are_correlated_to_their_own_results()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var client = await ConnectAsync(pipeName);

        var bootstrap = client.GetBootstrapAsync();
        var status = client.CaptureStatusAsync();
        await Task.WhenAll(bootstrap, status);
        var bootstrapResult = await bootstrap;
        var statusResult = await status;
        Assert.Equal(fake.Bootstrap, bootstrapResult);
        Assert.Equivalent(Status, statusResult, strict: true);
    }

    [Fact]
    public async Task Server_disconnect_fails_in_flight_request_and_cancels_frontend_operation()
    {
        var fake = new RecordingFrontendControl { BlockPrerequisiteSetup = true };
        var (server, pipeName) = await StartServerAsync(fake);
        await using var client = await ConnectAsync(pipeName);
        var request = client.RunPrerequisiteSetupAsync();

        await fake.PrerequisiteSetupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.DisposeAsync();
        await Assert.ThrowsAsync<FrontendTransportException>(() => request);
        await fake.PrerequisiteSetupCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1048577)]
    public async Task Invalid_frame_lengths_are_rejected_without_frontend_operation(int length)
    {
        await using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(length));
        stream.Position = 0;
        await Assert.ThrowsAsync<FrontendProtocolException>(() => FrontendWireCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_json_frame_cannot_deserialize_or_invoke_operation()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("not-json");
        await using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length));
        await stream.WriteAsync(bytes);
        stream.Position = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => FrontendWireCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_json_over_pipe_executes_zero_frontend_operations()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var handshake = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, handshake.Kind);

        var invalid = System.Text.Encoding.UTF8.GetBytes("not-json");
        await pipe.WriteAsync(BitConverter.GetBytes(invalid.Length));
        await pipe.WriteAsync(invalid);
        await pipe.FlushAsync();
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Oversized_frame_over_pipe_returns_invalid_message_without_frontend_operation()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await pipe.WriteAsync(BitConverter.GetBytes(FrontendWireCodec.MaxFrameBytes + 1));
        await pipe.FlushAsync();
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_request_id_is_rejected_without_frontend_operation(long requestId)
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Request, requestId, FrontendRpcMethod.GetBootstrap), writeGate, CancellationToken.None);
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Cancel_request_without_id_is_rejected_without_frontend_operation()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.CancelRequest), writeGate, CancellationToken.None);

        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Cancel_request_with_zero_id_is_rejected_without_frontend_operation()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.CancelRequest, 0), writeGate, CancellationToken.None);

        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Cancel_request_for_unknown_id_does_not_break_connection()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.CancelRequest, 999), writeGate, CancellationToken.None);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Request, 1, FrontendRpcMethod.GetBootstrap), writeGate, CancellationToken.None);

        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.Response, response.Kind);
        Assert.Equal(FrontendRpcMethod.GetBootstrap, response.Method);
        Assert.Null(response.Error);
        Assert.Equal(1, fake.TotalCalls);
    }

    [Theory]
    [InlineData(FrontendRemoteErrorCode.InvalidMessage)]
    [InlineData(FrontendRemoteErrorCode.OperationFailed)]
    public async Task Remote_error_is_mapped_to_frontend_remote_exception(FrontendRemoteErrorCode errorCode)
    {
        var pipeName = $"SteamInputAddonforClaw.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var writeGate = new SemaphoreSlim(1, 1);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
            var request = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, request.RequestId, request.Method, Error: new(errorCode, "Remote failure.")), writeGate, CancellationToken.None);
        });

        await using var client = new NamedPipeAddonFrontendClient(pipeName);
        await client.ConnectAsync();
        var exception = await Assert.ThrowsAsync<FrontendRemoteException>(() => client.GetBootstrapAsync());
        Assert.Equal(errorCode, exception.Code);
        await serverTask;
    }

    [Fact]
    public async Task Remote_cancelled_error_is_mapped_to_operation_canceled_exception()
    {
        var pipeName = $"SteamInputAddonforClaw.Test.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var writeGate = new SemaphoreSlim(1, 1);
            _ = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
            var request = await FrontendWireCodec.ReadAsync(server, CancellationToken.None);
            await FrontendWireCodec.WriteAsync(server, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Response, request.RequestId, request.Method, Error: new(FrontendRemoteErrorCode.Cancelled, "Remote cancellation.")), writeGate, CancellationToken.None);
        });

        await using var client = new NamedPipeAddonFrontendClient(pipeName);
        await client.ConnectAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetBootstrapAsync());
        await serverTask;
    }

    [Fact]
    public async Task Frame_reader_handles_partial_prefix_and_payload_reads()
    {
        var envelope = new FrontendWireEnvelope(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake);
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, FrontendWireCodec.Json);
        await using var stream = new PartialReadStream();
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length));
        await stream.WriteAsync(payload);
        stream.Position = 0;

        var restored = await FrontendWireCodec.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(envelope, restored);
    }

    [Fact]
    public async Task Integer_wire_enums_are_rejected()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"ProtocolVersion\":1,\"Kind\":927531}");
        await using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length));
        await stream.WriteAsync(payload);
        stream.Position = 0;

        await Assert.ThrowsAnyAsync<Exception>(() => FrontendWireCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_string_method_returns_unsupported_method_without_invoking_frontend()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await WriteRawFrameAsync(pipe, "{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":\"FutureMethod\"}");
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.Response, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.UnsupportedMethod, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("01")]
    [InlineData("999")]
    [InlineData(" GetBootstrap ")]
    public async Task Numeric_or_noncanonical_method_strings_return_unsupported_without_invoking_frontend(string method)
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await WriteRawFrameAsync(pipe, $"{{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":\"{method}\"}}");
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.Response, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.UnsupportedMethod, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Theory]
    [InlineData("{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1}")]
    [InlineData("{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":null}")]
    [InlineData("{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":123}")]
    public async Task Invalid_method_shapes_return_invalid_message_without_invoking_frontend(string json)
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await WriteRawFrameAsync(pipe, json);
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.ProtocolError, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Theory]
    [InlineData("{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1}")]
    [InlineData("{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":null}")]
    public async Task Missing_or_null_method_deserializes_as_invalid_request(string json)
    {
        await using var stream = new MemoryStream();
        await WriteRawFrameAsync(stream, json);
        stream.Position = 0;

        var envelope = await FrontendWireCodec.ReadAsync(stream, CancellationToken.None);
        Assert.Null(envelope.Method);
    }

    [Theory]
    [InlineData("SetLaunchAtWindowsStartup", "{}")]
    [InlineData("SetRouteInSteamBigPicture", "{}")]
    [InlineData("SetDeveloperTestMode", "{}")]
    [InlineData("SetLogLevel", "{}")]
    [InlineData("SetLaunchAtWindowsStartup", "null")]
    [InlineData("SetRouteInSteamBigPicture", "null")]
    [InlineData("SetDeveloperTestMode", "null")]
    [InlineData("SetLogLevel", "null")]
    [InlineData("SetLaunchAtWindowsStartup", "{\"Enabled\":\"false\"}")]
    [InlineData("SetRouteInSteamBigPicture", "{\"Enabled\":\"false\"}")]
    [InlineData("SetDeveloperTestMode", "{\"Enabled\":\"false\"}")]
    [InlineData("SetLogLevel", "{\"Level\":123}")]
    public async Task Malformed_mutation_payload_is_rejected_without_invoking_frontend(string method, string payload)
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await WriteRawFrameAsync(pipe, $"{{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":\"{method}\",\"Payload\":{payload}}}");
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendWireMessageKind.Response, response.Kind);
        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task No_argument_method_with_payload_is_rejected_without_invoking_frontend()
    {
        var fake = new RecordingFrontendControl();
        var (server, pipeName) = await StartServerAsync(fake);
        await using var serverLifetime = server;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await FrontendWireCodec.WriteAsync(pipe, new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(FrontendWireMessageKind.HandshakeAccepted, (await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);

        await WriteRawFrameAsync(pipe, "{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":\"GetBootstrap\",\"Payload\":{}}");
        var response = await FrontendWireCodec.ReadAsync(pipe, CancellationToken.None);

        Assert.Equal(FrontendRemoteErrorCode.InvalidMessage, response.Error?.Code);
        Assert.Equal(0, fake.TotalCalls);
    }

    [Fact]
    public async Task Numeric_method_is_rejected_as_invalid_message()
    {
        await using var stream = new MemoryStream();
        await WriteRawFrameAsync(stream, "{\"ProtocolVersion\":1,\"Kind\":\"Request\",\"RequestId\":1,\"Method\":123}");
        stream.Position = 0;

        await Assert.ThrowsAnyAsync<Exception>(() => FrontendWireCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Server_start_after_dispose_is_rejected()
    {
        var server = new NamedPipeAddonFrontendServer($"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}", new RecordingFrontendControl());
        await server.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await server.StartAsync());
    }

    [Fact]
    public async Task Client_connect_after_dispose_is_rejected()
    {
        await using var client = new NamedPipeAddonFrontendClient($"SteamInputAddonForClaw.Tests.{Guid.NewGuid():N}");
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task Concurrent_client_connect_is_rejected_deterministically()
    {
        await using var client = new NamedPipeAddonFrontendClient($"SteamInputAddonForClaw.Tests.{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        var first = client.ConnectAsync(cancellation.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task Caller_cancellation_after_frame_start_does_not_cancel_payload_write()
    {
        await using var stream = new BlockingWriteStream();
        using var gateCancellation = new CancellationTokenSource();
        using var writeCancellation = new CancellationTokenSource();
        using var gate = new SemaphoreSlim(1, 1);
        var write = FrontendWireCodec.WriteAsync(
            stream,
            new(FrontendTransportProtocol.CurrentVersion, FrontendWireMessageKind.Request, 1, FrontendRpcMethod.GetBootstrap),
            gate,
            gateCancellation.Token,
            writeCancellation.Token);

        await stream.PrefixWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gateCancellation.Cancel();
        Assert.False(write.IsCompleted);
        stream.ReleasePayload();
        await write;
        Assert.Equal(2, stream.WriteCount);
    }

    private static async Task WriteRawFrameAsync(Stream stream, string json)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length));
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private static async Task<(NamedPipeAddonFrontendServer Server, string PipeName)> StartServerAsync(RecordingFrontendControl fake)
    {
        var pipeName = $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}";
        var server = new NamedPipeAddonFrontendServer(pipeName, fake);
        await server.StartAsync();
        return (server, pipeName);
    }

    private static async Task<NamedPipeAddonFrontendClient> ConnectAsync(string pipeName)
    {
        var client = new NamedPipeAddonFrontendClient(pipeName);
        await client.ConnectAsync();
        return client;
    }

    private sealed class RecordingFrontendControl : IAddonFrontendControl
    {
        public event EventHandler? StateInvalidated;
        public FrontendBootstrapSnapshot Bootstrap { get; } = new(Settings, "Registered", new(false), @"C:\Logs");
        public FrontendLaunchAtStartupResult LaunchResult { get; } = new(Settings, "Updated");
        public FrontendPrerequisiteSetupResult SetupResult { get; } = new(FrontendPrerequisiteSetupResultKind.Installed, Status);
        public FrontendEnvironmentReportResult EnvironmentResult { get; } = new(true, null);
        public int TotalCalls { get; private set; }
        public bool LastLaunchAtStartupEnabled { get; private set; }
        public bool LastRouteInBigPictureEnabled { get; private set; }
        public FrontendLogLevel LastLogLevel { get; private set; }
        public bool LastDeveloperTestModeEnabled { get; private set; }
        public int SuppressDeveloperWarningCount { get; private set; }
        public bool BlockPrerequisiteSetup { get; init; }
        public bool ThrowOperationCanceledWithoutToken { get; init; }
        public TaskCompletionSource PrerequisiteSetupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PrerequisiteSetupCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RaiseStateInvalidated() => StateInvalidated?.Invoke(this, EventArgs.Empty);
        public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) { TotalCalls++; if (ThrowOperationCanceledWithoutToken) throw new OperationCanceledException(); return Task.FromResult(Bootstrap); }
        public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) { TotalCalls++; return Task.FromResult(Status); }
        public Task<FrontendLaunchAtStartupResult> SetLaunchAtWindowsStartupAsync(bool enabled, CancellationToken t = default) { TotalCalls++; LastLaunchAtStartupEnabled = enabled; return Task.FromResult(LaunchResult); }
        public Task<FrontendSettingsSnapshot> SetRouteInSteamBigPictureAsync(bool enabled, CancellationToken t = default) { TotalCalls++; LastRouteInBigPictureEnabled = enabled; return Task.FromResult(Settings); }
        public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) { TotalCalls++; LastLogLevel = level; return Task.FromResult(Settings); }
        public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) { TotalCalls++; SuppressDeveloperWarningCount++; return Task.FromResult(Settings); }
        public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) { TotalCalls++; LastDeveloperTestModeEnabled = enabled; return Task.FromResult(new FrontendDeveloperSnapshot(enabled)); }
        public async Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) { TotalCalls++; if (!BlockPrerequisiteSetup) return SetupResult; PrerequisiteSetupStarted.TrySetResult(); try { await Task.Delay(Timeout.InfiniteTimeSpan, t); throw new UnreachableException(); } catch (OperationCanceledException) { PrerequisiteSetupCancelled.TrySetResult(); throw; } }
        public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) { TotalCalls++; return Task.FromResult(EnvironmentResult); }
    }

    private sealed class PartialReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private sealed class BlockingWriteStream : MemoryStream
    {
        private readonly TaskCompletionSource _releasePayload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PrefixWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WriteCount { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            if (WriteCount == 1)
            {
                await base.WriteAsync(buffer, cancellationToken);
                PrefixWritten.TrySetResult();
                return;
            }

            await _releasePayload.Task.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
        public void ReleasePayload() => _releasePayload.TrySetResult();
    }
}
