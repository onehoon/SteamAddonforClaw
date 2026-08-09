namespace SteamInputAddonforClaw.Devices.Abstractions;

public readonly record struct AuxiliaryControlId
{
    public AuxiliaryControlId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("An auxiliary control ID is required.", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}
