namespace SteamInputAddonforClaw.Devices.Abstractions;

public enum ControlSurface { Front, Rear, Top, Side, Unknown }
public enum ControlSide { Left, Right, Center, Unknown }

public sealed record AuxiliaryControlDescriptor
{
    public AuxiliaryControlDescriptor(AuxiliaryControlId id, string displayName, ControlSurface surface, ControlSide side, int? ordinal = null)
    {
        if (!id.IsValid) throw new ArgumentException("A valid auxiliary control ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("An auxiliary control display name is required.", nameof(displayName));
        if (ordinal is <= 0) throw new ArgumentOutOfRangeException(nameof(ordinal), "An ordinal must be positive when specified.");

        Id = id;
        DisplayName = displayName;
        Surface = surface;
        Side = side;
        Ordinal = ordinal;
    }

    public AuxiliaryControlId Id { get; }
    public string DisplayName { get; }
    public ControlSurface Surface { get; }
    public ControlSide Side { get; }
    public int? Ordinal { get; }
}
