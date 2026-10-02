using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.FrontendTransport;

internal static class OverlayWireCodec
{
    // SF-V2-06 section 10.1: required-constructor enforcement added alongside the existing strict
    // string enum converter. Without it, an omitted PageId/EditedRowId/RowId/Value.Kind would
    // silently default to enum member 0 (Device/DeviceTdpEnabled/Boolean) instead of failing closed.
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    internal static async Task WriteAsync(Stream stream, OverlayWireMessage message, SemaphoreSlim gate, CancellationToken token)
    {
        var payload = Serialize(message);
        if (payload.Length is 0 or > OverlayTransportProtocol.MaxFrameBytes)
            throw new FrontendProtocolException("Invalid Overlay frame length.");

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(prefix, token).ConfigureAwait(false);
            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    internal static async Task<OverlayWireMessage> ReadAsync(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > OverlayTransportProtocol.MaxFrameBytes)
            throw new FrontendProtocolException("Invalid Overlay frame length.");

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<OverlayWireMessage>(payload, Json)
                ?? throw new FrontendProtocolException("Invalid Overlay JSON frame.");
        }
        catch (JsonException exception)
        {
            throw new FrontendProtocolException($"Invalid Overlay JSON frame: {exception.Message}");
        }
    }

    internal static int GetSerializedLength(OverlayWireMessage message) => Serialize(message).Length;

    private static byte[] Serialize(OverlayWireMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    {
        var offset = 0;
        while (offset < target.Length)
        {
            var read = await stream.ReadAsync(target[offset..], token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
