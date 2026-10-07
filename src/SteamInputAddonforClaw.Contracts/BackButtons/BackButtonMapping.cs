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

public static class BackButtonMappingLabels
{
    public static string GetDisplayName(Xbox360BackButtonTarget target) => target switch
    {
        Xbox360BackButtonTarget.Disabled => "Disabled",
        Xbox360BackButtonTarget.A => "A",
        Xbox360BackButtonTarget.B => "B",
        Xbox360BackButtonTarget.X => "X",
        Xbox360BackButtonTarget.Y => "Y",
        Xbox360BackButtonTarget.DPadUp => "D-Pad Up",
        Xbox360BackButtonTarget.DPadRight => "D-Pad Right",
        Xbox360BackButtonTarget.DPadDown => "D-Pad Down",
        Xbox360BackButtonTarget.DPadLeft => "D-Pad Left",
        Xbox360BackButtonTarget.LeftBumper => "Left Bumper (LB)",
        Xbox360BackButtonTarget.RightBumper => "Right Bumper (RB)",
        Xbox360BackButtonTarget.LeftTrigger => "Left Trigger (LT)",
        Xbox360BackButtonTarget.RightTrigger => "Right Trigger (RT)",
        Xbox360BackButtonTarget.LeftStickClick => "Left Stick Click (L3)",
        Xbox360BackButtonTarget.RightStickClick => "Right Stick Click (R3)",
        Xbox360BackButtonTarget.View => "View",
        Xbox360BackButtonTarget.Menu => "Menu",
        Xbox360BackButtonTarget.XboxGuide => "Xbox Guide",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };
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
