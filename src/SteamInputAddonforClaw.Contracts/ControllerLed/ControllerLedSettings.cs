namespace SteamInputAddonforClaw.Contracts.ControllerLed;

/// <summary>The persisted, whole-device static controller LED preference.</summary>
public sealed record ControllerLedSettings(bool Enabled, int Brightness, byte Red, byte Green, byte Blue)
{
    public static ControllerLedSettings Default { get; } = new(false, 100, 255, 255, 255);
}

public static class ControllerLedSettingsValidation
{
    public static string? Validate(ControllerLedSettings? settings)
    {
        if (settings is null) return "SettingsRequired";
        return settings.Brightness is < 0 or > 100 ? "BrightnessOutOfRange" : null;
    }
}
