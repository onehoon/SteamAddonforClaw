namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendGameInputSystemButtonProbeState
{
    Unavailable,
    Ready,
    Running,
    Stopped,
    Failed,
}

public sealed record FrontendGameInputSystemButtonEventSnapshot(
    long Sequence,
    ulong TimestampMicroseconds,
    uint CurrentButtonsRaw,
    uint PreviousButtonsRaw,
    bool GuidePressed,
    bool GuideReleased,
    bool SharePressed,
    bool ShareReleased,
    bool DeviceInfoSucceeded,
    string? DeviceInfoFailure,
    string? VendorId,
    string? ProductId,
    string? DisplayName,
    string? PnpPath,
    string? ContainerId,
    string? DeviceId,
    string? DeviceRootId,
    int? SupportedInputRaw,
    string? SupportedInput,
    int? SupportedSystemButtonsRaw,
    string? SupportedSystemButtons);

public sealed record FrontendGameInputSystemButtonProbeSnapshot(
    bool Available,
    FrontendGameInputSystemButtonProbeState State,
    string Status,
    long EventCount,
    FrontendGameInputSystemButtonEventSnapshot? LastEvent)
{
    public static FrontendGameInputSystemButtonProbeSnapshot Unavailable(string status = "GameInput system-button diagnostics are unavailable.") =>
        new(false, FrontendGameInputSystemButtonProbeState.Unavailable, status, 0, null);
}
