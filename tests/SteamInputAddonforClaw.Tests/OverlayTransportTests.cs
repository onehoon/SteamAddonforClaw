using System.Buffers.Binary;
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
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
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
}
