using System.IO.Pipes;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// SF-V2-02: .Overlay v6 Device Quick Settings state delivery and the eight approved Device
// mutations. OQ4/lifecycle regression coverage lives in OverlayTransportTests/OverlayTabOrderTransportTests
// and is unaffected by these additions (verified green alongside this file).
public sealed class OverlayDeviceQuickSettingsTransportTests
{
    private static string Pipe() => $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

    private static readonly FrontendDeviceQuickSettingsSnapshot Sample = new(
        new FrontendCpuBoostSnapshot(new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Aggressive, CpuBoostMode.Aggressive), new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Disabled), true, true, null),
        new FrontendTdpSnapshot(true, true, new(true, new(20, 30), new(15, 25)), new(8, 30, 8, 40)),
        new FrontendPowerModeSnapshot(new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced), new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.BestPowerEfficiency, WindowsPowerMode.BestPowerEfficiency), true, true, null));

    private static readonly FrontendDeviceQuickSettingsSnapshot PartialSample = Sample with { Tdp = FrontendTdpSnapshot.Unavailable };

    // ---- 23.1 Protocol / handshake ----------------------------------------------------------

    [Fact]
    public void Protocol_is_v6_and_frontend_transport_is_unaffected()
    {
        Assert.Equal(6, OverlayTransportProtocol.CurrentVersion);
        // SF-V2-02 owns only .Overlay v5 -> v6. The desktop/QAM frontend protocol is whatever current
        // main already carries (27, from the since-merged SD6A PR B #497) -- this PR must not bump it.
        Assert.Equal(27, FrontendTransportProtocol.CurrentVersion);
    }

    [Fact]
    public async Task A_v5_peer_is_rejected_by_the_v6_server()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.WriteAsync(client, new(5, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
    }

    [Fact]
    public async Task Device_state_is_not_a_mandatory_pre_ready_frame()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted

        // The mandatory pre-Ready frame is still exactly TabOrderState -- no Device state frame is
        // interleaved before it or required before the client may report Ready.
        var next = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.TabOrderState, next.Kind);

        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
    }

    // ---- 23.2 Device snapshot state delivery -------------------------------------------------

    [Fact]
    public async Task Visible_overlay_receives_the_complete_snapshot_intact()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var received = new TaskCompletionSource<FrontendDeviceQuickSettingsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, snapshot => { received.TrySetResult(snapshot); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendDeviceQuickSettingsStateAsync(Sample));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Sample, got);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Partial_child_availability_survives_the_wire()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var received = new TaskCompletionSource<FrontendDeviceQuickSettingsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, snapshot => { received.TrySetResult(snapshot); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendDeviceQuickSettingsStateAsync(PartialSample));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FrontendTdpSnapshot.Unavailable, got.Tdp);
        Assert.NotEqual(FrontendCpuBoostSnapshot.Unavailable, got.CpuBoost);
        Assert.NotEqual(FrontendPowerModeSnapshot.Unavailable, got.PowerMode);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task All_children_unavailable_survives_the_wire()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var received = new TaskCompletionSource<FrontendDeviceQuickSettingsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, snapshot => { received.TrySetResult(snapshot); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendDeviceQuickSettingsStateAsync(FrontendDeviceQuickSettingsSnapshot.Unavailable));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FrontendDeviceQuickSettingsSnapshot.Unavailable, got);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Hidden_or_unready_overlay_receives_no_device_state_frame()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();

        // No client connected yet -> unready.
        Assert.False(await server.SendDeviceQuickSettingsStateAsync(Sample));

        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        // Ready but still Hidden (no Show yet).
        Assert.False(await server.SendDeviceQuickSettingsStateAsync(Sample));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.False(await server.SendDeviceQuickSettingsStateAsync(Sample));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- 23.3 Mutation allowlist / mapping -----------------------------------------------------

    // OverlayDeviceMutationKind/Request are internal to FrontendTransport, so they cannot appear in
    // a public [Theory] method signature (CS0051) -- loop internally instead of using MemberData.
    [Fact]
    public async Task Each_approved_kind_invokes_exactly_the_matching_frontend_method_and_preserves_the_result()
    {
        foreach (var kind in Enum.GetValues<OverlayDeviceMutationKind>())
        {
            var control = new FakeDeviceMutationControl();
            var request = SampleRequestFor(kind);

            var response = await OverlayDeviceMutationDispatch.DispatchAsync(control, request, CancellationToken.None);

            Assert.Equal(new List<string> { ExpectedMethodFor(kind) }, control.Calls);
            switch (kind)
            {
                case OverlayDeviceMutationKind.SetCpuBoostEnabled or OverlayDeviceMutationKind.SetCpuBoostAc or OverlayDeviceMutationKind.SetCpuBoostDc:
                    Assert.Equal(control.CpuBoostResult, response.CpuBoost);
                    Assert.Null(response.Tdp); Assert.Null(response.PowerMode); Assert.Null(response.Error);
                    break;
                case OverlayDeviceMutationKind.SetTdpEnabled or OverlayDeviceMutationKind.SetTdp:
                    Assert.Equal(control.TdpResult, response.Tdp);
                    Assert.Null(response.CpuBoost); Assert.Null(response.PowerMode); Assert.Null(response.Error);
                    break;
                default:
                    Assert.Equal(control.PowerModeResult, response.PowerMode);
                    Assert.Null(response.CpuBoost); Assert.Null(response.Tdp); Assert.Null(response.Error);
                    break;
            }
        }
    }

    private static string ExpectedMethodFor(OverlayDeviceMutationKind kind) => kind switch
    {
        OverlayDeviceMutationKind.SetCpuBoostEnabled => nameof(IAddonFrontendControl.SetDeviceCpuBoostEnabledAsync),
        OverlayDeviceMutationKind.SetCpuBoostAc => nameof(IAddonFrontendControl.SetDeviceCpuBoostAcAsync),
        OverlayDeviceMutationKind.SetCpuBoostDc => nameof(IAddonFrontendControl.SetDeviceCpuBoostDcAsync),
        OverlayDeviceMutationKind.SetTdpEnabled => nameof(IAddonFrontendControl.SetDeviceTdpEnabledAsync),
        OverlayDeviceMutationKind.SetTdp => nameof(IAddonFrontendControl.SetDeviceTdpAsync),
        OverlayDeviceMutationKind.SetPowerModeEnabled => nameof(IAddonFrontendControl.SetDevicePowerModeEnabledAsync),
        OverlayDeviceMutationKind.SetPowerModeAc => nameof(IAddonFrontendControl.SetDevicePowerModeAcAsync),
        OverlayDeviceMutationKind.SetPowerModeDc => nameof(IAddonFrontendControl.SetDevicePowerModeDcAsync),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static OverlayDeviceMutationRequest SampleRequestFor(OverlayDeviceMutationKind kind) => kind switch
    {
        OverlayDeviceMutationKind.SetCpuBoostEnabled or OverlayDeviceMutationKind.SetTdpEnabled or OverlayDeviceMutationKind.SetPowerModeEnabled => new(1, kind, Enabled: true),
        OverlayDeviceMutationKind.SetCpuBoostAc or OverlayDeviceMutationKind.SetCpuBoostDc => new(1, kind, CpuBoostMode: CpuBoostMode.Aggressive),
        OverlayDeviceMutationKind.SetTdp => new(1, kind, TdpConfiguration: new FrontendTdpConfiguration(true, new(20, 30), new(15, 25))),
        _ => new(1, kind, PowerMode: WindowsPowerMode.Balanced),
    };

    // ---- 23.4 Strict malformed-request rejection ------------------------------------------------

    [Fact]
    public void Malformed_requests_are_rejected_by_the_shape_validator()
    {
        OverlayDeviceMutationRequest[] malformed =
        [
            new(0, OverlayDeviceMutationKind.SetCpuBoostEnabled, Enabled: true), // zero request id
            new(-1, OverlayDeviceMutationKind.SetCpuBoostEnabled, Enabled: true), // negative request id
            new(1, OverlayDeviceMutationKind.SetCpuBoostEnabled, CpuBoostMode: CpuBoostMode.Aggressive), // wrong field
            new(1, OverlayDeviceMutationKind.SetCpuBoostEnabled), // missing required value
            new(1, OverlayDeviceMutationKind.SetCpuBoostAc, Enabled: true, CpuBoostMode: CpuBoostMode.Aggressive), // conflicting fields
            new(1, OverlayDeviceMutationKind.SetTdp, TdpConfiguration: new FrontendTdpConfiguration(true, new(20, 30), new(15, 25)), Enabled: true), // conflicting fields
        ];

        Assert.All(malformed, request => Assert.False(OverlayWireValidation.IsValidDeviceMutationRequest(request)));
    }

    [Fact]
    public async Task Malformed_request_over_the_wire_invokes_zero_mutations_and_tears_the_connection()
    {
        var pipeName = Pipe();
        var control = new FakeDeviceMutationControl();
        await using var server = new NamedPipeOverlayServer(pipeName, mutateDevice: (request, token) => OverlayDeviceMutationDispatch.DispatchAsync(control, request, token));
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // TabOrderState
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        // Malformed-shape validation happens unconditionally in the read loop, before any
        // Ready/Visible admission check -- no Show/acknowledgement is needed for this assertion.

        var malformed = new OverlayDeviceMutationRequest(0, OverlayDeviceMutationKind.SetCpuBoostEnabled, Enabled: true);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.DeviceMutationRequest, DeviceMutationRequest: malformed), writeGate, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => OverlayWireCodec.ReadAsync(client, timeout.Token));
        Assert.Empty(control.Calls);
    }

    // ---- 23.5 Hidden / not-captured admission ---------------------------------------------------

    [Fact]
    public async Task Hidden_overlay_cannot_mutate_device_state()
    {
        var pipeName = Pipe();
        var control = new FakeDeviceMutationControl();
        await using var server = new NamedPipeOverlayServer(pipeName, mutateDevice: (request, token) => OverlayDeviceMutationDispatch.DispatchAsync(control, request, token));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        // Ready but still Hidden -- never Shown.

        var result = await client.SendDeviceCpuBoostEnabledAsync(true);

        Assert.Empty(control.Calls);
        Assert.Equal(FrontendCpuBoostMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.False(result.Succeeded);

        // Connection remains usable afterward.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Not_bound_mutate_delegate_is_not_admitted_and_invokes_nothing()
    {
        var pipeName = Pipe();
        // No mutateDevice bound at all -- the frozen "tests / no-authority contexts" default.
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendDeviceTdpEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- 23.6 Typed failures are not transport failures -----------------------------------------

    [Fact]
    public async Task Typed_feature_failure_returns_over_the_connection_without_closing_it()
    {
        var pipeName = Pipe();
        var control = new FakeDeviceMutationControl
        {
            TdpResult = new FrontendTdpMutationResult(FrontendTdpMutationOutcome.InvalidTarget, "PL1 exceeds PL2.", FrontendTdpSnapshot.Unavailable),
        };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateDevice: (request, token) => OverlayDeviceMutationDispatch.DispatchAsync(control, request, token));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendDeviceTdpEnabledAsync(true);

        Assert.Equal(FrontendTdpMutationOutcome.InvalidTarget, result.Outcome);
        Assert.False(result.Succeeded);

        // The connection is still usable for an ordinary operation afterward.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- 23.7 Thrown mutation failure remains feature-local ---------------------------------------

    [Fact]
    public async Task Thrown_mutation_exception_returns_a_narrow_error_and_the_connection_survives()
    {
        var pipeName = Pipe();
        var control = new FakeDeviceMutationControl { ThrowOnPowerMode = true };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateDevice: async (request, token) =>
        {
            try { return await OverlayDeviceMutationDispatch.DispatchAsync(control, request, token); }
            catch (Exception) { return new OverlayDeviceMutationResponse(request.RequestId, request.Kind, Error: "Overlay Device mutation failed."); }
        });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        await Assert.ThrowsAsync<FrontendProtocolException>(() => client.SendDevicePowerModeEnabledAsync(true));

        // Overlay command/state transport remains usable after the feature-local failure.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- 23.8 Critical regression: long TDP mutation must not block Hide --------------------------

    [Fact]
    public async Task Long_tdp_mutation_does_not_block_hide_or_subsequent_state_processing()
    {
        var pipeName = Pipe();
        var mutationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new FakeDeviceMutationControl();
        control.TdpFactory = async () =>
        {
            mutationEntered.TrySetResult();
            await releaseMutation.Task;
            return control.TdpResult!;
        };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateDevice: (request, token) => OverlayDeviceMutationDispatch.DispatchAsync(control, request, token));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var mutationResultReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var mutationTask = client.SendDeviceTdpAsync(new FrontendTdpConfiguration(true, new(20, 30), new(15, 25)))
            .ContinueWith(_ => mutationResultReceived.TrySetResult(), TaskScheduler.Default);
        await mutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Hide must complete BEFORE the blocked TDP mutation is released -- proving the sole
        // ServeAsync read loop was never blocked by the in-flight mutation.
        var hideTask = server.SendCommandAsync(OverlayCommand.Hide);
        Assert.True(await hideTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.False(mutationResultReceived.Task.IsCompleted);

        releaseMutation.TrySetResult();
        await mutationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(mutationResultReceived.Task.IsCompletedSuccessfully);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- 23.9 Request correlation -----------------------------------------------------------------

    [Fact]
    public async Task A_late_result_for_a_retired_request_never_completes_a_newer_request()
    {
        var pipeName = Pipe();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new FakeDeviceMutationControl();
        var callCount = 0;
        control.CpuBoostFactory = async () =>
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1) { firstEntered.TrySetResult(); await releaseFirst.Task; }
            return control.CpuBoostResult! with { FailureMessage = $"call-{call}" };
        };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateDevice: (request, token) => OverlayDeviceMutationDispatch.DispatchAsync(control, request, token));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        // Request A is sent and its wait is abandoned (cancelled) before the Runtime settles it.
        using var cancelA = new CancellationTokenSource();
        var requestA = client.SendDeviceCpuBoostEnabledAsync(true, cancelA.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancelA.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestA);

        // Request B becomes current and completes normally.
        var requestB = await client.SendDeviceCpuBoostEnabledAsync(false);
        Assert.Equal("call-2", requestB.FailureMessage);

        // A's late result now arrives; it must not have completed B (already proven above) and must
        // not corrupt the connection.
        releaseFirst.TrySetResult();
        await Task.Delay(200);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- 23.11 Shared write-gate integrity ---------------------------------------------------------

    [Fact]
    public async Task Device_state_and_navigation_and_tab_order_share_one_write_gate_and_never_interleave()
    {
        var pipeName = Pipe();
        IReadOnlyList<OverlayTabId> current = OverlayTabOrderContract.DefaultOrder;
        await using var server = new NamedPipeOverlayServer(pipeName, () => current, requested => { current = requested; return true; });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var deviceFrames = new List<FrontendDeviceQuickSettingsSnapshot>();
        var navActions = new List<OverlayNavigationAction>();
        var orders = new List<IReadOnlyList<OverlayTabId>>();
        var run = client.RunAsync(
            _ => Task.CompletedTask,
            action => { lock (navActions) navActions.Add(action); return Task.CompletedTask; },
            order => { lock (orders) orders.Add(order); return Task.CompletedTask; },
            snapshot => { lock (deviceFrames) deviceFrames.Add(snapshot); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        for (var i = 0; i < 30; i++)
        {
            var deviceTask = server.SendDeviceQuickSettingsStateAsync(i % 2 == 0 ? Sample : PartialSample);
            var navTask = server.SendNavigationAsync(OverlayNavigationAction.NavigateDown);
            var orderTask = client.SendSetTabOrderAsync(current);
            Assert.True(await deviceTask);
            await navTask;
            Assert.True(await orderTask);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (deviceFrames) lock (navActions)
                if (deviceFrames.Count >= 30 && navActions.Count >= 30) break;
            Assert.False(run.IsFaulted, run.Exception?.ToString());
            await Task.Delay(20);
        }

        Assert.False(run.IsFaulted, run.Exception?.ToString());
        lock (deviceFrames) Assert.All(deviceFrames, s => Assert.True(s == Sample || s == PartialSample));
        lock (navActions) Assert.All(navActions, a => Assert.Equal(OverlayNavigationAction.NavigateDown, a));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeDeviceMutationControl : IAddonFrontendControl
    {
        public event EventHandler? StateInvalidated { add { } remove { } }
        public List<string> Calls { get; } = new();

        // Not exercised by these Overlay Device mutation tests -- IAddonFrontendControl declares
        // these without a default body, unlike the Device methods below.
        public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(SteamInputAddonforClaw.Contracts.FrontButtons.FrontButtonMappingSettings mapping, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => throw new NotSupportedException();

        public FrontendCpuBoostMutationResult? CpuBoostResult { get; set; } =
            new(FrontendCpuBoostMutationOutcome.Succeeded, null, FrontendCpuBoostSnapshot.Unavailable);
        public FrontendTdpMutationResult? TdpResult { get; set; } =
            new(FrontendTdpMutationOutcome.Succeeded, null, FrontendTdpSnapshot.Unavailable);
        public FrontendPowerModeMutationResult? PowerModeResult { get; set; } =
            new(FrontendPowerModeMutationOutcome.Succeeded, null, FrontendPowerModeSnapshot.Unavailable);

        public Func<Task<FrontendCpuBoostMutationResult>>? CpuBoostFactory { get; set; }
        public Func<Task<FrontendTdpMutationResult>>? TdpFactory { get; set; }
        public bool ThrowOnPowerMode { get; set; }

        public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add(nameof(SetDeviceCpuBoostEnabledAsync)); return CpuBoostFactory?.Invoke() ?? Task.FromResult(CpuBoostResult!); }
        public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostAcAsync(CpuBoostMode mode, CancellationToken t = default)
        { Calls.Add(nameof(SetDeviceCpuBoostAcAsync)); return CpuBoostFactory?.Invoke() ?? Task.FromResult(CpuBoostResult!); }
        public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostDcAsync(CpuBoostMode mode, CancellationToken t = default)
        { Calls.Add(nameof(SetDeviceCpuBoostDcAsync)); return CpuBoostFactory?.Invoke() ?? Task.FromResult(CpuBoostResult!); }
        public Task<FrontendTdpMutationResult> SetDeviceTdpEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add(nameof(SetDeviceTdpEnabledAsync)); return TdpFactory?.Invoke() ?? Task.FromResult(TdpResult!); }
        public Task<FrontendTdpMutationResult> SetDeviceTdpAsync(FrontendTdpConfiguration configuration, CancellationToken t = default)
        { Calls.Add(nameof(SetDeviceTdpAsync)); return TdpFactory?.Invoke() ?? Task.FromResult(TdpResult!); }
        public Task<FrontendPowerModeMutationResult> SetDevicePowerModeEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add(nameof(SetDevicePowerModeEnabledAsync)); if (ThrowOnPowerMode) throw new InvalidOperationException("Power Mode boom."); return Task.FromResult(PowerModeResult!); }
        public Task<FrontendPowerModeMutationResult> SetDevicePowerModeAcAsync(WindowsPowerMode mode, CancellationToken t = default)
        { Calls.Add(nameof(SetDevicePowerModeAcAsync)); if (ThrowOnPowerMode) throw new InvalidOperationException("Power Mode boom."); return Task.FromResult(PowerModeResult!); }
        public Task<FrontendPowerModeMutationResult> SetDevicePowerModeDcAsync(WindowsPowerMode mode, CancellationToken t = default)
        { Calls.Add(nameof(SetDevicePowerModeDcAsync)); if (ThrowOnPowerMode) throw new InvalidOperationException("Power Mode boom."); return Task.FromResult(PowerModeResult!); }
    }
}
