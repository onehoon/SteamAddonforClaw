using System.Buffers.Binary;
using System.Text;

namespace SteamInputAddonforClaw.ClawHud;

internal static class ClawHudControlCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] EncodeRequest(ClawHudControlRequest request)
    {
        if (request.RequestId == 0) throw new ArgumentOutOfRangeException(nameof(request), "Request id must be non-zero.");
        var payload = request.Operation switch
        {
            ClawHudControlOperation.GetRuntimeInfo or
            ClawHudControlOperation.GetSettingsSnapshot or
            ClawHudControlOperation.RequestShutdown => Empty(request),
            ClawHudControlOperation.SetStartWithWindows or
            ClawHudControlOperation.SetHudEnabled or
            ClawHudControlOperation.SetIntelVrrRangeFixEnabled => Bool(request),
            ClawHudControlOperation.SetHudVisibilityMode => EnumValue(request, IsVisibilityMode),
            ClawHudControlOperation.SetHudFont => EnumValue(request, IsFont),
            ClawHudControlOperation.SetHudAlignment => EnumValue(request, IsAlignment),
            ClawHudControlOperation.SetHudBackgroundMode => EnumValue(request, IsBackgroundMode),
            ClawHudControlOperation.SetHudSizeOffset => SizeOffset(request),
            ClawHudControlOperation.PreviewHudOpacity or ClawHudControlOperation.CommitHudOpacity => Opacity(request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), $"Unknown ClawHUD operation {request.Operation}.")
        };

        var frame = new byte[ClawHudControlProtocol.HeaderSize + payload.Length];
        ClawHudControlProtocol.Magic.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), ClawHudControlProtocol.ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), ClawHudControlProtocol.HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8), (ushort)ClawHudControlMessageKind.Request);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(10), (ushort)request.Operation);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12), request.RequestId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(20), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(ClawHudControlProtocol.HeaderSize));
        return frame;
    }

    internal static ClawHudControlDecodeResult DecodeResponse(
        ReadOnlySpan<byte> frame,
        uint expectedRequestId,
        ClawHudControlOperation expectedOperation)
    {
        if (frame.Length < ClawHudControlProtocol.HeaderSize || !frame[..4].SequenceEqual(ClawHudControlProtocol.Magic))
            return ClawHudControlDecodeResult.Malformed;

        var protocol = BinaryPrimitives.ReadUInt16LittleEndian(frame[4..]);
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(frame[6..]);
        var kind = BinaryPrimitives.ReadUInt16LittleEndian(frame[8..]);
        var operation = BinaryPrimitives.ReadUInt16LittleEndian(frame[10..]);
        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]);
        var status = BinaryPrimitives.ReadUInt32LittleEndian(frame[16..]);
        var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(frame[20..]);

        if (protocol != ClawHudControlProtocol.ProtocolVersion || headerSize != ClawHudControlProtocol.HeaderSize
            || kind != (ushort)ClawHudControlMessageKind.Response || requestId == 0 || requestId != expectedRequestId
            || operation != (ushort)expectedOperation || payloadSize > ClawHudControlProtocol.MaxPayloadBytes
            || frame.Length != ClawHudControlProtocol.HeaderSize + (int)payloadSize)
            return ClawHudControlDecodeResult.Malformed;
        if (status > (uint)ClawHudControlStatus.ShuttingDown)
            return ClawHudControlDecodeResult.Malformed;

        var controlStatus = (ClawHudControlStatus)status;
        var payload = frame[ClawHudControlProtocol.HeaderSize..];
        if (controlStatus != ClawHudControlStatus.Ok)
            return payload.Length == 0 ? ClawHudControlDecodeResult.ProtocolError(controlStatus) : ClawHudControlDecodeResult.Malformed;

        var reader = new Reader(payload);
        if (expectedOperation == ClawHudControlOperation.GetRuntimeInfo)
        {
            if (!reader.TryRuntimeInfo(out var info) || !reader.AtEnd) return ClawHudControlDecodeResult.Malformed;
            return new(ClawHudControlDecodeOutcome.Success, controlStatus, RuntimeInfo: info);
        }
        if (CarriesSnapshot(expectedOperation))
        {
            if (!reader.TrySnapshot(out var snapshot) || !reader.AtEnd) return ClawHudControlDecodeResult.Malformed;
            return new(ClawHudControlDecodeOutcome.Success, controlStatus, Snapshot: snapshot);
        }
        if (expectedOperation == ClawHudControlOperation.RequestShutdown && payload.Length == 0)
            return new(ClawHudControlDecodeOutcome.Success, controlStatus, EmptySuccess: true);
        return ClawHudControlDecodeResult.Malformed;
    }

    private static bool CarriesSnapshot(ClawHudControlOperation operation) => operation is
        ClawHudControlOperation.GetSettingsSnapshot or ClawHudControlOperation.SetStartWithWindows or
        ClawHudControlOperation.SetHudEnabled or ClawHudControlOperation.SetHudVisibilityMode or
        ClawHudControlOperation.SetHudSizeOffset or ClawHudControlOperation.SetHudFont or
        ClawHudControlOperation.SetHudAlignment or ClawHudControlOperation.SetHudBackgroundMode or
        ClawHudControlOperation.PreviewHudOpacity or ClawHudControlOperation.CommitHudOpacity or
        ClawHudControlOperation.SetIntelVrrRangeFixEnabled;

    private static byte[] Empty(ClawHudControlRequest request)
    {
        if (request.Flag is not null || request.WireEnum is not null || request.SizeOffset is not null || request.OpacityPercent is not null)
            throw new ArgumentException("The operation must have an empty payload.", nameof(request));
        return [];
    }

    private static byte[] Bool(ClawHudControlRequest request)
    {
        if (request.Flag is null || request.WireEnum is not null || request.SizeOffset is not null || request.OpacityPercent is not null)
            throw new ArgumentException("The operation requires only a boolean flag.", nameof(request));
        return [(byte)(request.Flag.Value ? 1 : 0)];
    }

    private static byte[] EnumValue(ClawHudControlRequest request, Func<byte, bool> validator)
    {
        if (request.WireEnum is null || !validator(request.WireEnum.Value)
            || request.Flag is not null || request.SizeOffset is not null || request.OpacityPercent is not null)
            throw new ArgumentException("The operation requires one valid wire enum.", nameof(request));
        return [request.WireEnum.Value];
    }

    private static byte[] SizeOffset(ClawHudControlRequest request)
    {
        if (request.SizeOffset is null || !IsHudSizeOffset(request.SizeOffset.Value)
            || request.Flag is not null || request.WireEnum is not null || request.OpacityPercent is not null)
            throw new ArgumentException("The operation requires one valid size offset.", nameof(request));
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, request.SizeOffset.Value);
        return bytes;
    }

    private static byte[] Opacity(ClawHudControlRequest request)
    {
        if (request.OpacityPercent is null || !IsOpacity(request.OpacityPercent.Value)
            || request.Flag is not null || request.WireEnum is not null || request.SizeOffset is not null)
            throw new ArgumentException("The operation requires one valid opacity value.", nameof(request));
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, request.OpacityPercent.Value);
        return bytes;
    }

    private static bool IsHudSizeOffset(int value) => value is >= ClawHudControlProtocol.MinHudSizeOffset and <= ClawHudControlProtocol.MaxHudSizeOffset;
    private static bool IsOpacity(ushort value) => value is >= ClawHudControlProtocol.MinOpacityPercent and <= ClawHudControlProtocol.MaxOpacityPercent && value % ClawHudControlProtocol.OpacityStepPercent == 0;
    private static bool IsVisibilityMode(byte value) => value is >= 1 and <= 2;
    private static bool IsFont(byte value) => value is >= 1 and <= 2;
    private static bool IsAlignment(byte value) => value is >= 1 and <= 3;
    private static bool IsBackgroundMode(byte value) => value is >= 1 and <= 2;

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;
        internal readonly bool AtEnd => _position == _bytes.Length;

        internal bool TryByte(out byte value)
        {
            if (_position + 1 > _bytes.Length) { value = 0; return false; }
            value = _bytes[_position++];
            return true;
        }

        internal bool TryBool(out bool value)
        {
            value = false;
            if (!TryByte(out var raw) || raw > 1) return false;
            value = raw == 1;
            return true;
        }

        internal bool TryUInt16(out ushort value)
        {
            if (_position + 2 > _bytes.Length) { value = 0; return false; }
            value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes[_position..]);
            _position += 2;
            return true;
        }

        internal bool TryInt32(out int value)
        {
            if (_position + 4 > _bytes.Length) { value = 0; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_bytes[_position..]);
            _position += 4;
            return true;
        }

        internal bool TryString(out string value)
        {
            value = string.Empty;
            if (!TryUInt16(out var length) || length > ClawHudControlProtocol.MaxStringBytes || _position + length > _bytes.Length)
                return false;
            try { value = StrictUtf8.GetString(_bytes.Slice(_position, length)); }
            catch (DecoderFallbackException) { return false; }
            if (value.Contains('\0')) return false;
            _position += length;
            return true;
        }

        internal bool TryRuntimeInfo(out ClawHudRuntimeInfo info)
        {
            info = null!;
            if (!TryString(out var version) || !TryUInt16(out var min) || !TryUInt16(out var max)
                || !TryByte(out var launch) || launch is < 1 or > 2
                || !TryByte(out var state) || state is < 1 or > 3)
                return false;
            info = new(version, min, max, (ClawHudWireLaunchMode)launch, (ClawHudWireRuntimeState)state);
            return true;
        }

        internal bool TrySnapshot(out ClawHudSettingsSnapshot snapshot)
        {
            snapshot = null!;
            if (!TryBool(out var startWithWindows) || !TryBool(out var hudEnabled)
                || !TryInt32(out var sizeOffset) || !IsHudSizeOffset(sizeOffset)
                || !TryByte(out var font) || !IsFont(font)
                || !TryByte(out var visibility) || !IsVisibilityMode(visibility)
                || !TryByte(out var alignment) || !IsAlignment(alignment)
                || !TryByte(out var background) || !IsBackgroundMode(background)
                || !TryUInt16(out var opacity) || !IsOpacity(opacity)
                || !TryBool(out var intelVrr) || !TryByte(out var hasVrr) || hasVrr > 1)
                return false;

            ClawHudIntelVrrResult? vrr = null;
            if (hasVrr == 1)
            {
                if (!TryByte(out var status) || status is < 1 or > 9
                    || !TryString(out var panel) || !TryString(out var before)
                    || !TryString(out var after) || !TryString(out var message) || !TryString(out var timestamp))
                    return false;
                vrr = new(status, panel, before, after, message, timestamp);
            }

            snapshot = new(startWithWindows, hudEnabled, sizeOffset, (ClawHudWireFont)font,
                (ClawHudWireVisibilityMode)visibility, (ClawHudWireAlignment)alignment,
                (ClawHudWireBackgroundMode)background, opacity, intelVrr, vrr);
            return true;
        }
    }
}
