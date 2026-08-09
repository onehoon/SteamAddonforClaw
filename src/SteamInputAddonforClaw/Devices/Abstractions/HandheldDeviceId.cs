using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Devices.Abstractions;

public readonly record struct HandheldDeviceId
{
    [JsonConstructor]
    public HandheldDeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A handheld device ID is required.", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}
