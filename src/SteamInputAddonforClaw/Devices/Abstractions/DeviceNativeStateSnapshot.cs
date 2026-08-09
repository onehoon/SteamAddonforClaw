using System.Text.Json;

namespace SteamInputAddonforClaw.Devices.Abstractions;

public sealed record DeviceNativeStateSnapshot
{
    public DeviceNativeStateSnapshot(HandheldDeviceId deviceId, int formatVersion, DateTimeOffset capturedAt, JsonElement payload)
    {
        if (!deviceId.IsValid) throw new ArgumentException("A valid device ID is required.", nameof(deviceId));
        if (formatVersion <= 0) throw new ArgumentOutOfRangeException(nameof(formatVersion));
        if (payload.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("A payload is required.", nameof(payload));
        DeviceId = deviceId; FormatVersion = formatVersion; CapturedAt = capturedAt; Payload = payload;
    }
    public HandheldDeviceId DeviceId { get; }
    public int FormatVersion { get; }
    public DateTimeOffset CapturedAt { get; }
    public JsonElement Payload { get; }
}
