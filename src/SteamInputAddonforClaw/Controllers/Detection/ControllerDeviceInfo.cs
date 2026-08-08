namespace SteamInputAddonforClaw.Controllers.Detection;

public sealed record ControllerDeviceInfo(
    string InstanceId,
    Guid? ContainerId,
    string? ParentInstanceId,
    string? EnumeratorName,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    string? ClassName,
    string? ClassGuid,
    string? Service,
    ushort? VendorId,
    ushort? ProductId,
    bool Present);
