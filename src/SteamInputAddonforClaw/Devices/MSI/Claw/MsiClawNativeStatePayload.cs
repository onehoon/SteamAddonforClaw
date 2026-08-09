namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawNativeStatePayload(
    MsiClawNativeMode Mode,
    string? InstanceId,
    string? ParentInstanceId,
    Guid? ContainerId,
    ushort? ProductId);
