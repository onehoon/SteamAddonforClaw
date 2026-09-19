using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonQuickSettingsTabOrderTransportTests
{
    private static readonly AddonQuickSettingsTabId[] Custom =
    [
        AddonQuickSettingsTabId.Controller,
        AddonQuickSettingsTabId.Device,
        AddonQuickSettingsTabId.Profile,
        AddonQuickSettingsTabId.Shortcut,
        AddonQuickSettingsTabId.Setting,
    ];

    private static string Pipe() => $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
    private static AddonQuickSettingsTabOrderSnapshot Snapshot(IReadOnlyList<AddonQuickSettingsTabId> order) =>
        AddonQuickSettingsTabOrderProduct.Create(order);

    [Fact]
    public async Task V7_peer_is_rejected_by_the_v8_server()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.WriteAsync(client, new(7, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.ProtocolError, (await OverlayWireCodec.ReadAsync(client, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task Initial_typed_state_is_applied_before_ready()
    {
        var pipeName = Pipe();
        var expected = Snapshot(Custom);
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureTabOrder: _ => Task.FromResult(expected));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var received = new TaskCompletionSource<AddonQuickSettingsTabOrderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, async state =>
        {
            received.TrySetResult(state);
            await release.Task;
        });

        AssertStateEqual(expected, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await server.WaitForReadyAsync(TimeSpan.FromMilliseconds(300)));
        release.TrySetResult();
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Typed_state_crosses_the_wire_with_labels_and_boundaries()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureTabOrder: _ => Task.FromResult(Snapshot(Custom)));
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        var json = await ReadRawFrameJsonAsync(client);
        Assert.Contains("\"TabOrderState\"", json);
        Assert.Contains("\"Rows\"", json);
        Assert.Contains("\"CanMoveEarlier\":false", json);
        Assert.Contains("\"CanMoveLater\":false", json);
        Assert.DoesNotContain("\"TabOrder\":[", json);
    }

    [Fact]
    public async Task Typed_move_reaches_the_shared_mutator_and_returns_authoritative_state()
    {
        var pipeName = Pipe();
        var expected = Snapshot(Custom);
        AddonQuickSettingsTabOrderMoveIntent? seen = null;
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureTabOrder: _ => Task.FromResult(Snapshot(AddonQuickSettingsTabOrderContract.DefaultOrder)),
            moveTabOrder: (intent, _) =>
            {
                seen = intent;
                return Task.FromResult(new AddonQuickSettingsTabOrderMutationResult(true, null, expected));
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, _ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendTabOrderMoveAsync(new(AddonQuickSettingsTabId.Profile, -1));
        Assert.Equal(new(AddonQuickSettingsTabId.Profile, -1), seen);
        Assert.True(result.Succeeded);
        AssertStateEqual(expected, result.State);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Failed_typed_move_still_returns_current_authoritative_state()
    {
        var pipeName = Pipe();
        var expected = Snapshot(AddonQuickSettingsTabOrderContract.DefaultOrder);
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureTabOrder: _ => Task.FromResult(expected),
            moveTabOrder: (_, _) => Task.FromResult(new AddonQuickSettingsTabOrderMutationResult(false, "Rejected.", expected)));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, null, _ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendTabOrderMoveAsync(new(AddonQuickSettingsTabId.Device, 1));
        Assert.False(result.Succeeded);
        Assert.Equal("Rejected.", result.FailureMessage);
        AssertStateEqual(expected, result.State);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Client_rejects_a_malformed_initial_typed_state()
    {
        var pipeName = Pipe();
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState), writeGate, CancellationToken.None);

        await Assert.ThrowsAsync<FrontendProtocolException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Server_rejects_a_mixed_purpose_typed_move_frame()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);

        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderMoveRequest,
            Command: OverlayCommand.Show,
            TabOrderMove: new(AddonQuickSettingsTabId.Device, 1)), writeGate, CancellationToken.None);
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

    private static void AssertStateEqual(AddonQuickSettingsTabOrderSnapshot expected, AddonQuickSettingsTabOrderSnapshot actual)
    {
        Assert.Equal(expected.Available, actual.Available);
        Assert.Equal(expected.Rows.Count, actual.Rows.Count);
        for (var i = 0; i < expected.Rows.Count; i++)
            Assert.Equal(expected.Rows[i], actual.Rows[i]);
    }
}
