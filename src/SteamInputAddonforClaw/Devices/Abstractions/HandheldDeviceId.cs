namespace SteamInputAddonforClaw.Devices.Abstractions;

public readonly record struct HandheldDeviceId
{
    public HandheldDeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A handheld device ID is required.", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
