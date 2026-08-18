namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Semantic classification of an MSI_Event low byte. OEM1 = Center M / MainUI (event code 41,
/// semantic LaunchMcxMainUI). OEM2 = WING / Game Bar (event code 88, semantic LaunchMcxOSD) and is
/// never the target of any suppression/remapping in this feature -- it stays fully native.
/// Physical left/right button position differs by MSI Claw model, so this type deliberately never
/// encodes "left"/"right".
/// </summary>
internal enum CenterMOemCode
{
    Oem1,
    Oem2,
    Other
}

internal readonly record struct MsiOemEvent(uint RawCode, CenterMOemCode Code);

internal static class CenterMOemEventMapper
{
    internal const uint Oem1LowByte = 41;
    internal const uint Oem2LowByte = 88;

    /// <summary>Classifies an already-parsed raw MSI_Event code by its low byte (rawCode &amp; 0xFF).</summary>
    internal static CenterMOemCode Classify(uint rawCode) => (rawCode & 0xFF) switch
    {
        Oem1LowByte => CenterMOemCode.Oem1,
        Oem2LowByte => CenterMOemCode.Oem2,
        _ => CenterMOemCode.Other
    };
}
