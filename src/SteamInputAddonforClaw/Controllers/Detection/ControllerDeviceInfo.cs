namespace SteamInputAddonforClaw.Controllers.Detection;

public sealed record ControllerDeviceInfo(
    string InstanceId,
    Guid? ContainerId,
    string? ParentInstanceId,
    IReadOnlyList<string> AncestorInstanceIds,
    string? EnumeratorName,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    string? ClassName,
    string? ClassGuid,
    string? Service,
    ushort? VendorId,
    ushort? ProductId,
    bool Present,
    string? FriendlyName = null,
    ushort? UsagePage = null,
    ushort? Usage = null);
