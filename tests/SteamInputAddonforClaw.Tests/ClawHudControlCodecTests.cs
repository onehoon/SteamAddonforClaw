using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using SteamInputAddonforClaw.ClawHud;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawHudControlCodecTests
{
    [Fact]
    public void EncodeRequest_UsesExactLittleEndianHeader_AndSupportsRequestShutdown()
    {
        var frame = ClawHudControlCodec.EncodeRequest(new(ClawHudControlOperation.RequestShutdown, 7));

        Assert.Equal(24, frame.Length);
        Assert.Equal("CHUD"u8.ToArray(), frame[..4]);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)));
        Assert.Equal((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)));
        Assert.Equal((ushort)ClawHudControlMessageKind.Request, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(8)));
        Assert.Equal((ushort)ClawHudControlOperation.RequestShutdown, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(10)));
        Assert.Equal((uint)7, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(20)));
    }

    [Fact]
    public void DecodeResponse_RejectsWrongCorrelationAndTrailingPayload()
    {
        var frame = RuntimeInfoResponse(11, "1.0.1", ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready);

        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(frame, 12, ClawHudControlOperation.GetRuntimeInfo).Outcome);

        var trailing = frame.Concat(new byte[] { 0 }).ToArray();
        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(trailing, 11, ClawHudControlOperation.GetRuntimeInfo).Outcome);
    }

    [Fact]
    public void DecodeResponse_DecodesRuntimeInfoAndEmptyShutdownResponse()
    {
        var runtime = ClawHudControlCodec.DecodeResponse(
            RuntimeInfoResponse(3, "1.0.1", ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready),
            3, ClawHudControlOperation.GetRuntimeInfo);
        Assert.Equal(ClawHudControlDecodeOutcome.Success, runtime.Outcome);
        Assert.Equal("1.0.1", runtime.RuntimeInfo!.ApplicationVersion);
        Assert.Equal(ClawHudWireLaunchMode.Managed, runtime.RuntimeInfo.LaunchMode);

        var shutdown = ClawHudControlCodec.DecodeResponse(
            ResponseFrame(3, ClawHudControlOperation.RequestShutdown, []),
            3, ClawHudControlOperation.RequestShutdown);
        Assert.Equal(ClawHudControlDecodeOutcome.Success, shutdown.Outcome);
        Assert.True(shutdown.EmptySuccess);
    }

    [Fact]
    public void DecodeResponse_DecodesFullSettingsSnapshotWithOptionalIntelVrrResult()
    {
        var snapshot = ClawHudControlCodec.DecodeResponse(
            ResponseFrame(4, ClawHudControlOperation.GetSettingsSnapshot, SnapshotPayload()),
            4, ClawHudControlOperation.GetSettingsSnapshot);

        Assert.Equal(ClawHudControlDecodeOutcome.Success, snapshot.Outcome);
        Assert.True(snapshot.Snapshot!.StartWithWindows);
        Assert.False(snapshot.Snapshot.HudEnabled);
        Assert.Equal(-1, snapshot.Snapshot.HudSizeOffset);
        Assert.Equal(ClawHudWireFont.SegoeUiVariable, snapshot.Snapshot.HudFont);
        Assert.Equal(ClawHudWireVisibilityMode.InGameOnly, snapshot.Snapshot.VisibilityMode);
        Assert.Equal(ClawHudWireAlignment.Right, snapshot.Snapshot.Alignment);
        Assert.Equal(ClawHudWireBackgroundMode.ContentWidth, snapshot.Snapshot.BackgroundMode);
        Assert.Equal((ushort)85, snapshot.Snapshot.BackgroundOpacityPercent);
        Assert.True(snapshot.Snapshot.IntelVrrRangeFixEnabled);
        Assert.Equal("MSI Panel", snapshot.Snapshot.IntelVrrLastResult!.PanelName);
        Assert.Equal("16-235", snapshot.Snapshot.IntelVrrLastResult.RangeBefore);
        Assert.Equal("0-255", snapshot.Snapshot.IntelVrrLastResult.RangeAfter);
    }

    [Fact]
    public void EncodeRequest_RejectsWrongPayloadShapeAndZeroRequestId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClawHudControlCodec.EncodeRequest(new(ClawHudControlOperation.GetRuntimeInfo, 0)));
        Assert.Throws<ArgumentException>(() => ClawHudControlCodec.EncodeRequest(new(ClawHudControlOperation.RequestShutdown, 1, Flag: true)));
        Assert.Throws<ArgumentException>(() => ClawHudControlCodec.EncodeRequest(new(ClawHudControlOperation.SetHudEnabled, 1)));
    }

    [Fact]
    public void DecodeResponse_RejectsBadMetadataBoundsEnumsAndUtf8()
    {
        var valid = RuntimeInfoResponse(5, "1.0.1", ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready);

        var wrongMagic = valid.ToArray();
        wrongMagic[0] = (byte)'X';
        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(wrongMagic, 5, ClawHudControlOperation.GetRuntimeInfo).Outcome);

        var wrongProtocol = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongProtocol.AsSpan(4), 2);
        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(wrongProtocol, 5, ClawHudControlOperation.GetRuntimeInfo).Outcome);

        var wrongOperation = valid.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongOperation.AsSpan(10), (ushort)ClawHudControlOperation.GetSettingsSnapshot);
        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(wrongOperation, 5, ClawHudControlOperation.GetRuntimeInfo).Outcome);

        var oversized = ResponseFrame(5, ClawHudControlOperation.GetRuntimeInfo, new byte[ClawHudControlProtocol.MaxPayloadBytes + 1]);
        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(oversized, 5, ClawHudControlOperation.GetRuntimeInfo).Outcome);

        var unknownEnum = SnapshotPayload();
        unknownEnum[6] = 99; // HudFont
        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(
                ResponseFrame(5, ClawHudControlOperation.GetSettingsSnapshot, unknownEnum),
                5, ClawHudControlOperation.GetSettingsSnapshot).Outcome);

        Assert.Equal(ClawHudControlDecodeOutcome.Malformed,
            ClawHudControlCodec.DecodeResponse(
                ResponseFrame(5, ClawHudControlOperation.GetRuntimeInfo, InvalidUtf8RuntimeInfoPayload()),
                5, ClawHudControlOperation.GetRuntimeInfo).Outcome);
    }

    [Fact]
    public async Task ControlClient_CompletesOneRequestOneResponse_AndSupportsRequestShutdown()
    {
        var pipeName = $"ClawHud.Tests.{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var request = new byte[ClawHudControlProtocol.MaxFrameBytes];
            var read = await server.ReadAsync(request);
            var requestId = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(12));
            var operation = (ClawHudControlOperation)BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(10));
            var response = ResponseFrame(requestId, operation, []);
            await server.WriteAsync(response);
            await server.FlushAsync();
        });

        var client = new ClawHudControlClient(TimeSpan.FromSeconds(2), pipeName);
        var result = await client.RequestShutdownAsync();

        Assert.True(result.Succeeded);
        await serverTask;
    }

    [Fact]
    public async Task ControlClient_SupportsRuntimeInfoSnapshotAndHudEnableOperations()
    {
        var runtime = await ExecutePipeRequestAsync(
            client => client.GetRuntimeInfoAsync(),
            (requestId, operation) => RuntimeInfoResponse(requestId, "1.0.1", ClawHudWireLaunchMode.Managed, ClawHudWireRuntimeState.Ready));
        Assert.True(runtime.Succeeded);
        Assert.Equal("1.0.1", runtime.Value!.ApplicationVersion);

        var snapshot = await ExecutePipeRequestAsync(
            client => client.GetSettingsSnapshotAsync(),
            (requestId, operation) => ResponseFrame(requestId, operation, SnapshotPayload()));
        Assert.True(snapshot.Succeeded);
        Assert.Equal(ClawHudWireBackgroundMode.ContentWidth, snapshot.Value!.BackgroundMode);

        var enabled = await ExecutePipeRequestAsync(
            client => client.SetHudEnabledAsync(true),
            (requestId, operation) => ResponseFrame(requestId, operation, SnapshotPayload(hudEnabled: true)));
        Assert.True(enabled.Succeeded);
        Assert.True(enabled.Value!.HudEnabled);
    }

    [Fact]
    public async Task ControlClient_DistinguishesProtocolMalformedTransportTimeoutAndCallerCancellation()
    {
        var protocol = await ExecutePipeRequestAsync(
            client => client.GetRuntimeInfoAsync(),
            (requestId, operation) => ResponseFrame(requestId, operation, [], ClawHudControlStatus.InvalidValue));
        Assert.Equal(ClawHudControlResultKind.ProtocolError, protocol.Kind);
        Assert.Equal(ClawHudControlStatus.InvalidValue, protocol.Status);

        var malformed = await ExecutePipeRequestAsync(
            client => client.GetRuntimeInfoAsync(),
            (_, _) => [0]);
        Assert.Equal(ClawHudControlResultKind.MalformedResponse, malformed.Kind);

        var transport = await new ClawHudControlClient(TimeSpan.FromMilliseconds(50), $"ClawHud.Missing.{Guid.NewGuid():N}").GetRuntimeInfoAsync();
        Assert.Contains(transport.Kind, new[] { ClawHudControlResultKind.TransportUnavailable, ClawHudControlResultKind.TimedOut });

        var timeoutPipeName = $"ClawHud.Timeout.{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(timeoutPipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync();
                var request = new byte[ClawHudControlProtocol.MaxFrameBytes];
                await server.ReadExactlyAsync(request.AsMemory(0, ClawHudControlProtocol.HeaderSize));
                await Task.Delay(250);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        });
        var timedOut = await new ClawHudControlClient(TimeSpan.FromMilliseconds(50), timeoutPipeName).GetRuntimeInfoAsync();
        Assert.Equal(ClawHudControlResultKind.TimedOut, timedOut.Kind);
        await serverTask;

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ClawHudControlClient(TimeSpan.FromSeconds(1), $"ClawHud.Cancel.{Guid.NewGuid():N}").GetRuntimeInfoAsync(cancellation.Token));
    }

    private static byte[] RuntimeInfoResponse(uint requestId, string version, ClawHudWireLaunchMode mode, ClawHudWireRuntimeState state)
    {
        var versionBytes = System.Text.Encoding.UTF8.GetBytes(version);
        var payload = new byte[2 + versionBytes.Length + 2 + 2 + 1 + 1];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)versionBytes.Length);
        versionBytes.CopyTo(payload, 2);
        var offset = 2 + versionBytes.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 2), 1);
        payload[offset + 4] = (byte)mode;
        payload[offset + 5] = (byte)state;
        return ResponseFrame(requestId, ClawHudControlOperation.GetRuntimeInfo, payload);
    }

    private static byte[] InvalidUtf8RuntimeInfoPayload() =>
    [
        1, 0, 0xff, // one-byte invalid UTF-8 applicationVersion
        1, 0,       // minimumProtocolVersion
        1, 0,       // maximumProtocolVersion
        (byte)ClawHudWireLaunchMode.Managed,
        (byte)ClawHudWireRuntimeState.Ready,
    ];

    private static byte[] SnapshotPayload(bool hudEnabled = false)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(1); // StartWithWindows
        stream.WriteByte(hudEnabled ? (byte)1 : (byte)0); // HudEnabled
        var int32 = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(int32, -1);
        stream.Write(int32);
        stream.WriteByte((byte)ClawHudWireFont.SegoeUiVariable);
        stream.WriteByte((byte)ClawHudWireVisibilityMode.InGameOnly);
        stream.WriteByte((byte)ClawHudWireAlignment.Right);
        stream.WriteByte((byte)ClawHudWireBackgroundMode.ContentWidth);
        var uint16 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(uint16, 85);
        stream.Write(uint16);
        stream.WriteByte(1); // IntelVrrRangeFixEnabled
        stream.WriteByte(1); // has IntelVrrLastResult
        stream.WriteByte(2); // success status
        WriteString(stream, "MSI Panel");
        WriteString(stream, "16-235");
        WriteString(stream, "0-255");
        WriteString(stream, "Range corrected");
        WriteString(stream, "2026-09-22T00:00:00Z");
        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static async Task<T> ExecutePipeRequestAsync<T>(
        Func<ClawHudControlClient, Task<T>> operation,
        Func<uint, ClawHudControlOperation, byte[]> responseFactory)
    {
        var pipeName = $"ClawHud.Tests.{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var request = new byte[ClawHudControlProtocol.MaxFrameBytes];
            var read = await server.ReadAsync(request);
            if (read == 0) throw new InvalidOperationException("The test pipe received no request.");
            var requestId = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(12));
            var requestOperation = (ClawHudControlOperation)BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(10));
            var response = responseFactory(requestId, requestOperation);
            await server.WriteAsync(response);
            await server.FlushAsync();
        });

        var result = await operation(new ClawHudControlClient(TimeSpan.FromSeconds(2), pipeName));
        await serverTask;
        return result;
    }

    private static byte[] ResponseFrame(uint requestId, ClawHudControlOperation operation, byte[] payload, ClawHudControlStatus status = ClawHudControlStatus.Ok)
    {
        var frame = new byte[ClawHudControlProtocol.HeaderSize + payload.Length];
        ClawHudControlProtocol.Magic.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), 24);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8), (ushort)ClawHudControlMessageKind.Response);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(10), (ushort)operation);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12), requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16), (uint)status);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(20), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(24));
        return frame;
    }
}
