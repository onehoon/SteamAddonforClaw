using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawRumbleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 0)]
    [InlineData(256, 1)]
    [InlineData(32767, 127)]
    [InlineData(32768, 128)]
    [InlineData(65280, 255)]
    [InlineData(65535, 255)]
    public void Conversion_uses_the_exact_high_byte_boundary(ushort value, byte expected)
        => Assert.Equal(expected, MsiClawRumblePacketBuilder.ToPhysicalByte(value));

    [Fact]
    public void PacketBuilder_preserves_MSI_small_then_large_order_and_stop()
    {
        Assert.Equal([0x05, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(TwoMotorRumble.Stopped));
        Assert.Equal([0x05, 0x01, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(new(ushort.MaxValue, ushort.MaxValue)));
        Assert.Equal([0x05, 0x01, 0, 0, 0, 0xFF, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(new(ushort.MaxValue, 0)));
        Assert.Equal([0x05, 0x01, 0, 0, 0xFF, 0, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(new(0, ushort.MaxValue)));
        Assert.All(new[] { MsiClawRumblePacketBuilder.Build(TwoMotorRumble.Stopped) }, packet => Assert.Equal(11, packet.Length));
    }

    [Fact]
    public void Sink_returns_unavailable_without_authoritative_identity_and_repeats_stop()
    {
        var identity = new FakeIdentity(null);
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(identity, transport);
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(new(1, 2)).Status);
        identity.Current = new(Guid.NewGuid(), "path-a", "PNP", "USB\\VID_0DB0&PID_1902");
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(2, transport.Packets.Count);
        Assert.Equal(transport.Packets[0], transport.Packets[1]);
    }

    [Fact]
    public void Transport_reuses_handle_changes_path_and_invalidates_after_write_failure()
    {
        var native = new FakeNativeHid();
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var packet = new byte[11];
        Assert.True(transport.Write("path-a", packet).Succeeded);
        Assert.True(transport.Write("path-a", packet).Succeeded);
        Assert.Equal(1, native.OpenCount);
        Assert.True(transport.Write("path-b", packet).Succeeded);
        Assert.Equal(2, native.OpenCount);
        native.WriteResult = false;
        Assert.False(transport.Write("path-b", packet).Succeeded);
        native.WriteResult = true;
        Assert.True(transport.Write("path-b", packet).Succeeded);
        Assert.Equal(3, native.OpenCount);
    }

    [Fact]
    public void Transport_reopens_same_path_after_physical_session_retirement()
    {
        var native = new FakeNativeHid();
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var packet = new byte[11];
        Assert.True(transport.Write("path-a", packet).Succeeded);
        transport.InvalidatePhysicalSession();
        Assert.True(transport.Write("path-a", packet).Succeeded);
        Assert.Equal(2, native.OpenCount);
    }

    [Fact]
    public void Transport_rejects_non_11_byte_packets_before_native_io_and_reopens_after_partial_write()
    {
        var native = new FakeNativeHid();
        using var transport = new WindowsMsiClawRumbleTransport(native);
        Assert.False(transport.Write("path-a", new byte[10]).Succeeded);
        Assert.Equal(0, native.OpenCount);
        Assert.True(transport.Write("path-a", new byte[11]).Succeeded);
        native.PartialWrite = true;
        Assert.Equal("PartialWrite", transport.Write("path-a", new byte[11]).Reason);
        native.PartialWrite = false;
        Assert.True(transport.Write("path-a", new byte[11]).Succeeded);
        Assert.Equal(2, native.OpenCount);
    }

    [Fact]
    public void Sink_returns_disposed_without_transport_io()
    {
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "USB\\VID_0DB0&PID_1902")), transport);
        sink.Dispose();
        Assert.Equal(PhysicalRumbleWriteStatus.Disposed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Empty(transport.Packets);
    }

    [Fact]
    public async Task Transport_serializes_native_writes_and_preserves_request_order()
    {
        var native = new FakeNativeHid { BlockFirstWrite = true };
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var first = Task.Run(() => transport.Write("path-a", [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        await native.FirstWriteEntered.Task;
        var secondRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.WriteRequested = () => secondRequested.TrySetResult();
        // The request signal is installed before a second request so the test positively observes
        // that B reached the transport boundary while A is still inside native Write.
        var second = Task.Run(() => transport.Write("path-a", [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        await secondRequested.Task;
        Assert.Equal(1, native.WriteCalls);
        native.ReleaseFirstWrite.Set();
        Assert.True((await first).Succeeded);
        Assert.True((await second).Succeeded);
        Assert.Equal([[1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]], native.Writes);
    }

    [Fact]
    public void Sink_maps_transport_failures_and_exceptions_to_generic_failed_result()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "USB\\VID_0DB0&PID_1902"));
        var transport = new FakeTransport { Result = new(false, "OpenFailed", 5) };
        using var sink = new MsiClawRumbleSink(identity, transport);
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        transport.Result = new(false, "PartialWrite");
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        transport.Exception = new IOException("test");
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
    }

    [Fact]
    public void Sink_rejects_empty_identity_path_without_transport_call()
    {
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(new FakeIdentity(new(Guid.NewGuid(), "", "PNP", "USB\\VID_0DB0&PID_1902")), transport);
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Empty(transport.Packets);
    }

    private sealed class FakeIdentity(MsiClawPhysicalInputIdentity? identity) : IMsiClawPhysicalInputIdentityProvider
    {
        public MsiClawPhysicalInputIdentity? Current { get; set; } = identity;
        public MsiClawPhysicalInputIdentity? CurrentIdentity => Current;
    }

    private sealed class FakeTransport : IMsiClawRumbleTransport
    {
        public List<byte[]> Packets { get; } = [];
        public MsiClawRumbleTransportResult Result { get; set; } = new(true, "OK");
        public Exception? Exception { get; set; }
        public MsiClawRumbleTransportResult Write(string _, ReadOnlySpan<byte> packet) { if (Exception is not null) throw Exception; Packets.Add(packet.ToArray()); return Result; }
        public void Dispose() { }
        public void InvalidatePhysicalSession() { }
    }

    private sealed class FakeNativeHid : IMsiClawNativeHidApi
    {
        public int LastError { get; private set; }
        public int OpenCount { get; private set; }
        public bool WriteResult { get; set; } = true;
        public bool PartialWrite { get; set; }
        public bool BlockFirstWrite { get; init; }
        public int WriteCalls { get; private set; }
        public TaskCompletionSource FirstWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFirstWrite { get; } = new(false);
        public List<byte[]> Writes { get; } = [];
        public SafeFileHandle Open(string path, uint desiredAccess, uint shareMode, uint creationDisposition)
        { OpenCount++; return new SafeFileHandle(new IntPtr(OpenCount), ownsHandle: false); }
        public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
        {
            WriteCalls++;
            Writes.Add(buffer.ToArray());
            if (BlockFirstWrite && WriteCalls == 1)
            {
                FirstWriteEntered.TrySetResult();
                ReleaseFirstWrite.Wait();
            }
            bytesWritten = WriteResult ? (uint)(PartialWrite ? buffer.Length - 1 : buffer.Length) : 0;
            LastError = WriteResult ? 0 : 5;
            return WriteResult;
        }
    }
}
