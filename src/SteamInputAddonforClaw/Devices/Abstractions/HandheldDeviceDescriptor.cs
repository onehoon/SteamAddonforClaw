namespace SteamInputAddonforClaw.Devices.Abstractions;

public sealed record HandheldDeviceDescriptor
{
    public HandheldDeviceDescriptor(HandheldDeviceId id, string manufacturer, string familyName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(manufacturer)) throw new ArgumentException("A manufacturer is required.", nameof(manufacturer));
        if (string.IsNullOrWhiteSpace(familyName)) throw new ArgumentException("A family name is required.", nameof(familyName));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A display name is required.", nameof(displayName));

        Id = id;
        Manufacturer = manufacturer;
        FamilyName = familyName;
        DisplayName = displayName;
    }

    public HandheldDeviceId Id { get; }
    public string Manufacturer { get; }
    public string FamilyName { get; }
    public string DisplayName { get; }
}
