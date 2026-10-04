namespace SteamInputAddonforClaw.Contracts.ControllerVibration;

/// <summary>The persisted global desired Left/Right vibration strength while Addon owns PID1902.</summary>
public sealed record ControllerVibrationSettings(int LeftPercent, int RightPercent)
{
    public static ControllerVibrationSettings Default { get; } = new(50, 50);
}

public static class ControllerVibrationSettingsValidation
{
    public static string? Validate(ControllerVibrationSettings? settings)
    {
        if (settings is null) return "SettingsRequired";
        if (settings.LeftPercent is < 0 or > 100) return "LeftPercentOutOfRange";
        if (settings.RightPercent is < 0 or > 100) return "RightPercentOutOfRange";
        return null;
    }
}
