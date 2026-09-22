namespace SteamInputAddonforClaw.ClawHud;

internal static class ClawHudControlProtocol
{
    internal static readonly byte[] Magic = "CHUD"u8.ToArray();
    internal const ushort ProtocolVersion = 1;
    internal const ushort HeaderSize = 24;
    internal const int MaxPayloadBytes = 16 * 1024;
    internal const int MaxFrameBytes = HeaderSize + MaxPayloadBytes;
    internal const int MaxStringBytes = 4096;
    internal const int MinHudSizeOffset = -2;
    internal const int MaxHudSizeOffset = 2;
    internal const int MinOpacityPercent = 50;
    internal const int MaxOpacityPercent = 100;
    internal const int OpacityStepPercent = 5;
}

internal enum ClawHudControlMessageKind : ushort
{
    Request = 1,
    Response = 2,
}

internal enum ClawHudControlOperation : ushort
{
    GetRuntimeInfo = 1,
    GetSettingsSnapshot = 2,
    SetStartWithWindows = 10,
    SetHudEnabled = 11,
    SetHudVisibilityMode = 12,
    SetHudSizeOffset = 13,
    SetHudFont = 14,
    SetHudAlignment = 15,
    SetHudBackgroundMode = 16,
    PreviewHudOpacity = 17,
    CommitHudOpacity = 18,
    SetIntelVrrRangeFixEnabled = 19,
    RequestShutdown = 20,
}

internal enum ClawHudControlStatus : uint
{
    Ok = 0,
    InvalidFrame = 1,
    UnsupportedVersion = 2,
    UnknownOperation = 3,
    InvalidPayload = 4,
    InvalidValue = 5,
    RuntimeUnavailable = 6,
    OperationFailed = 7,
    ShuttingDown = 8,
}

internal enum ClawHudWireLaunchMode : byte
{
    Standalone = 1,
    Managed = 2,
}

internal enum ClawHudWireRuntimeState : byte
{
    Starting = 1,
    Ready = 2,
    ShuttingDown = 3,
}

// Stable Managed launch exit values from ClawHUD's integration/steamaddon branch.
internal enum ClawHudManagedStartupExitCode
{
    AlreadyRunning = 20,
    UnsupportedHardware = 21,
    HardwareIndeterminate = 22,
    PresentMonRebootRequired = 30,
    PresentMonElevationCancelled = 31,
    PresentMonMsiMissing = 32,
    PresentMonInstallTimedOut = 33,
    PresentMonInstallFailed = 34,
    PresentMonValidationFailed = 35,
    RuntimeInitializationFailed = 40,
    ControlIpcUnavailable = 41,
}

internal enum ClawHudWireVisibilityMode : byte
{
    Always = 1,
    InGameOnly = 2,
}

internal enum ClawHudWireAlignment : byte
{
    Left = 1,
    Center = 2,
    Right = 3,
}

internal enum ClawHudWireFont : byte
{
    Unispace = 1,
    SegoeUiVariable = 2,
}

internal enum ClawHudWireBackgroundMode : byte
{
    FullWidth = 1,
    ContentWidth = 2,
}

internal enum ClawHudWireIntelVrrStatus : byte
{
    Disabled = 1,
    Unavailable = 2,
    UnsupportedPanel = 3,
    AmbiguousDisplay = 4,
    AlreadyCorrect = 5,
    SkippedUserProfile = 6,
    Applied = 7,
    ApplyFailed = 8,
    VerificationFailed = 9,
}

internal sealed record ClawHudControlRequest(
    ClawHudControlOperation Operation,
    uint RequestId,
    bool? Flag = null,
    byte? WireEnum = null,
    int? SizeOffset = null,
    ushort? OpacityPercent = null);

internal sealed record ClawHudRuntimeInfo(
    string ApplicationVersion,
    ushort MinimumProtocolVersion,
    ushort MaximumProtocolVersion,
    ClawHudWireLaunchMode LaunchMode,
    ClawHudWireRuntimeState RuntimeState);

internal sealed record ClawHudSettingsSnapshot(
    bool StartWithWindows,
    bool HudEnabled,
    int HudSizeOffset,
    ClawHudWireFont HudFont,
    ClawHudWireVisibilityMode VisibilityMode,
    ClawHudWireAlignment Alignment,
    ClawHudWireBackgroundMode BackgroundMode,
    ushort BackgroundOpacityPercent,
    bool IntelVrrRangeFixEnabled,
    ClawHudIntelVrrResult? IntelVrrLastResult);

internal sealed record ClawHudIntelVrrResult(
    ClawHudWireIntelVrrStatus Status,
    string PanelName,
    string RangeBefore,
    string RangeAfter,
    string Message,
    string TimestampUtc);

internal enum ClawHudControlDecodeOutcome
{
    Success,
    ProtocolError,
    Malformed,
}

internal sealed record ClawHudControlDecodeResult(
    ClawHudControlDecodeOutcome Outcome,
    ClawHudControlStatus Status,
    ClawHudRuntimeInfo? RuntimeInfo = null,
    ClawHudSettingsSnapshot? Snapshot = null,
    bool EmptySuccess = false)
{
    internal static ClawHudControlDecodeResult Malformed { get; } =
        new(ClawHudControlDecodeOutcome.Malformed, ClawHudControlStatus.InvalidFrame);

    internal static ClawHudControlDecodeResult ProtocolError(ClawHudControlStatus status) =>
        new(ClawHudControlDecodeOutcome.ProtocolError, status);
}

internal enum ClawHudControlResultKind
{
    Success,
    ProtocolError,
    TransportUnavailable,
    MalformedResponse,
    TimedOut,
}

internal sealed record ClawHudControlResult<T>(
    ClawHudControlResultKind Kind,
    T? Value = default,
    ClawHudControlStatus? Status = null)
{
    internal bool Succeeded => Kind == ClawHudControlResultKind.Success;

    internal static ClawHudControlResult<T> Success(T value) => new(ClawHudControlResultKind.Success, value);
    internal static ClawHudControlResult<T> Protocol(ClawHudControlStatus status) => new(ClawHudControlResultKind.ProtocolError, Status: status);
    internal static ClawHudControlResult<T> Transport => new(ClawHudControlResultKind.TransportUnavailable);
    internal static ClawHudControlResult<T> Malformed => new(ClawHudControlResultKind.MalformedResponse);
    internal static ClawHudControlResult<T> Timeout => new(ClawHudControlResultKind.TimedOut);
}

internal sealed record ClawHudUnit;
