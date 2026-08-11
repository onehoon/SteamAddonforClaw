namespace SteamInputAddonforClaw.Diagnostics.SteamController1304;

internal sealed record SteamController1304DeviceSnapshot(
    string InstanceId,
    string? ParentInstanceId,
    Guid? ContainerId,
    string? ClassName,
    string? Service,
    string? EnumeratorName,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    ushort? UsagePage,
    ushort? Usage,
    bool Present);

internal sealed record SteamController1304RawInputSnapshot(
    string DeviceName,
    uint Type,
    ushort? VendorId,
    ushort? ProductId,
    ushort? UsagePage,
    ushort? Usage);

internal sealed record SteamController1304DiagnosticSnapshot(
    IReadOnlyList<SteamController1304DeviceSnapshot> Devices,
    IReadOnlyList<SteamController1304RawInputSnapshot> RawInputDevices,
    string? Failure = null,
    SteamController1304ConnectionAssessment? Connection = null);

internal interface ISteamController1304StateProbe
{
    SteamController1304DiagnosticSnapshot Capture();
}
