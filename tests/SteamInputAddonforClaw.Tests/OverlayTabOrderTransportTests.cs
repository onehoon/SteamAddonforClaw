using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// OQ5-UI-09: Runtime <-> Overlay tab-order preference transport over .Overlay v5.
public sealed class OverlayTabOrderTransportTests
{
    private static readonly OverlayTabId[] Custom =
    [
        OverlayTabId.Controller,
        OverlayTabId.Device,
        OverlayTabId.Profile,
        OverlayTabId.Shortcut,
        OverlayTabId.Setting,
    ];

    private static string Pipe() => $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

    private static async Task<OverlayWireMessage> HandshakeAsClientAsync(Stream client, SemaphoreSlim writeGate)
    {
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted
        var state = await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // TabOrderState
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        return state;
    }

    [Fact]
    public async Task A_v4_peer_is_rejected_by_the_v5_server()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.WriteAsync(client, new(4, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
    }

    [Fact]
    public async Task B_initial_authoritative_order_is_applied_before_ready()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName, () => Custom, _ => false);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var received = new TaskCompletionSource<IReadOnlyList<OverlayTabId>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, async order =>
        {
            received.TrySetResult(order);
            await release.Task;
        });

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Custom, got);
        Assert.False(await server.WaitForReadyAsync(TimeSpan.FromMilliseconds(300))); // handler still running
        release.TrySetResult();
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task C_initial_tab_order_crosses_the_wire_as_enum_names()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName, () => Custom, _ => false);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted

        var json = await ReadRawFrameJsonAsync(client);
        Assert.Contains("\"TabOrderState\"", json);
        foreach (var tab in Custom)
            Assert.Contains($"\"{tab}\"", json);
        Assert.DoesNotContain("\"TabOrder\":[0", json.Replace(" ", "").Replace("\n", ""));
    }

    [Fact]
    public async Task D_set_tab_order_reaches_the_runtime_mutator_and_the_result_is_republished()
    {
        var pipeName = Pipe();
        IReadOnlyList<OverlayTabId> current = OverlayTabOrderContract.DefaultOrder;
        IReadOnlyList<OverlayTabId>? seen = null;
        await using var server = new NamedPipeOverlayServer(pipeName,
            () => current,
            requested => { seen = requested; current = requested; return true; });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var orders = new List<IReadOnlyList<OverlayTabId>>();
        var republished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, order =>
        {
            lock (orders) { orders.Add(order); if (orders.Count == 2) republished.TrySetResult(); }
            return Task.CompletedTask;
        });
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        Assert.True(await client.SendSetTabOrderAsync(Custom));
        await republished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Custom, seen);
        lock (orders) Assert.Equal(Custom, orders[^1]);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task E_rejected_request_republishes_the_previous_authority_and_the_connection_stays_usable()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName, () => OverlayTabOrderContract.DefaultOrder, _ => false);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var orders = new List<IReadOnlyList<OverlayTabId>>();
        var republished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, order =>
        {
            lock (orders) { orders.Add(order); if (orders.Count == 2) republished.TrySetResult(); }
            return Task.CompletedTask;
        });
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        Assert.True(await client.SendSetTabOrderAsync(Custom));
        await republished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (orders) Assert.Equal(OverlayTabOrderContract.DefaultOrder, orders[^1]); // unchanged

        // Command / navigation still work after preference traffic.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task F_tab_order_replies_and_navigation_share_one_write_gate_and_never_interleave()
    {
        var pipeName = Pipe();
        IReadOnlyList<OverlayTabId> current = OverlayTabOrderContract.DefaultOrder;
        await using var server = new NamedPipeOverlayServer(pipeName,
            () => current,
            requested => { current = requested; return true; });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var navActions = new List<OverlayNavigationAction>();
        var orders = new List<IReadOnlyList<OverlayTabId>>();
        var run = client.RunAsync(
            _ => Task.CompletedTask,
            action => { lock (navActions) navActions.Add(action); return Task.CompletedTask; },
            order => { lock (orders) orders.Add(order); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show)); // navigation only delivers while Visible

        // Overlap authoritative TabOrderState republishes (client -> server -> republish) with
        // server navigation writes. A corrupt/interleaved frame would throw out of the client loop.
        for (var i = 0; i < 40; i++)
        {
            var order = i % 2 == 0 ? Custom : (IReadOnlyList<OverlayTabId>)OverlayTabOrderContract.DefaultOrder;
            var setTask = client.SendSetTabOrderAsync(order);
            var navTask = server.SendNavigationAsync(OverlayNavigationAction.NavigateDown);
            Assert.True(await setTask);
            await navTask;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (navActions) lock (orders)
                if (navActions.Count >= 40 && orders.Count >= 41) break;
            Assert.False(run.IsFaulted, run.Exception?.ToString());
            await Task.Delay(20);
        }

        Assert.False(run.IsFaulted, run.Exception?.ToString());
        lock (navActions) Assert.All(navActions, a => Assert.Equal(OverlayNavigationAction.NavigateDown, a));
        lock (orders) Assert.All(orders, o => Assert.Equal(5, o.Count));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task G_client_rejects_a_malformed_initial_tab_order_state()
    {
        var pipeName = Pipe();
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None); // Handshake
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        // Malformed: a TabOrderState carrying no order at all.
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState), writeGate, CancellationToken.None);

        await Assert.ThrowsAsync<FrontendProtocolException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task G_client_rejects_an_incomplete_initial_order()
    {
        var pipeName = Pipe();
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState,
            TabOrder: new[] { OverlayTabId.Device, OverlayTabId.Profile, OverlayTabId.Controller, OverlayTabId.Shortcut }), writeGate, CancellationToken.None);

        await Assert.ThrowsAsync<FrontendProtocolException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task G_server_rejects_a_mixed_purpose_set_tab_order_frame()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await HandshakeAsClientAsync(client, writeGate);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.SetTabOrder,
            Command: OverlayCommand.Show, TabOrder: OverlayTabOrderContract.DefaultOrder), writeGate, CancellationToken.None);

        // The server aborts the connection; the next read fails rather than returning a frame.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => OverlayWireCodec.ReadAsync(client, timeout.Token));
    }

    private static async Task<string> ReadRawFrameJsonAsync(Stream stream)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload);
        return Encoding.UTF8.GetString(payload);
    }
}
