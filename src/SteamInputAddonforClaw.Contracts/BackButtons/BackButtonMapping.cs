namespace SteamInputAddonforClaw.Contracts.BackButtons;

/// <summary>Xbox 360 controls that a physical Full1902 rear button may target.</summary>
public enum Xbox360BackButtonTarget
{
    Disabled = 0,
    A,
    B,
    X,
    Y,
    DPadUp,
    DPadRight,
    DPadDown,
    DPadLeft,
    LeftBumper,
    RightBumper,
    LeftTrigger,
    RightTrigger,
    LeftStickClick,
    RightStickClick,
    View,
    Menu,
    XboxGuide,
}

/// <summary>One global Xbox360-only mapping for the two physical rear buttons.</summary>
public sealed record BackButtonMappingSettings(
    Xbox360BackButtonTarget M1,
    Xbox360BackButtonTarget M2)
{
    public static BackButtonMappingSettings Default { get; } =
        new(Xbox360BackButtonTarget.Disabled, Xbox360BackButtonTarget.Disabled);
}

public static class BackButtonMappingValidation
{
    public static bool IsValid(BackButtonMappingSettings? mapping) => Validate(mapping) is null;

    public static string? Validate(BackButtonMappingSettings? mapping)
    {
        if (mapping is null) return "MappingMissing";
        if (!Enum.IsDefined(mapping.M1)) return "InvalidM1Target";
        if (!Enum.IsDefined(mapping.M2)) return "InvalidM2Target";
        return null;
    }
}
