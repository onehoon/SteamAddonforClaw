namespace SteamInputAddonforClaw.Contracts.Frontend;

public enum FrontendClawHudRuntimeState
{
    Disabled,
    Starting,
    Ready,
    Unavailable,
    StandaloneConflict,
}

public enum FrontendClawHudDisplayMode
{
    Always,
    InGameOnly,
}

public enum FrontendClawHudFont
{
    Unispace,
    SegoeUiVariable,
}

public enum FrontendClawHudAlignment
{
    Left,
    Center,
    Right,
}

public enum FrontendClawHudBackgroundMode
{
    FullWidth,
    ContentWidth,
}

public enum FrontendClawHudIntelVrrStatus
{
    Disabled,
    Unavailable,
    UnsupportedPanel,
    AmbiguousDisplay,
    AlreadyCorrect,
    SkippedUserProfile,
    Applied,
    ApplyFailed,
    VerificationFailed,
}

public sealed record FrontendClawHudIntelVrrResult(
    FrontendClawHudIntelVrrStatus Status,
    string PanelName,
    string RangeBefore,
    string RangeAfter,
    string Message,
    string TimestampUtc);

public sealed record FrontendClawHudSettingsSnapshot(
    FrontendClawHudDisplayMode DisplayMode,
    int HudSizeOffset,
    FrontendClawHudFont Font,
    FrontendClawHudAlignment Alignment,
    FrontendClawHudBackgroundMode BackgroundMode,
    int BackgroundOpacityPercent,
    bool IntelVrrRangeFixEnabled,
    FrontendClawHudIntelVrrResult? IntelVrrLastResult);

public sealed record FrontendClawHudSnapshot(
    bool DesiredEnabled,
    FrontendClawHudRuntimeState RuntimeState,
    string StatusMessage,
    string? RuntimeVersion,
    string? ApplicationVersion,
    FrontendClawHudSettingsSnapshot? Settings)
{
    public static FrontendClawHudSnapshot Unavailable(bool desiredEnabled, string message) =>
        new(desiredEnabled, FrontendClawHudRuntimeState.Unavailable, message, null, null, null);
}

public enum FrontendClawHudMutationKind
{
    DisplayMode,
    HudSizeOffset,
    Font,
    Alignment,
    BackgroundMode,
    PreviewOpacity,
    CommitOpacity,
    IntelVrrRangeFixEnabled,
}

public sealed record FrontendClawHudMutationIntent(
    FrontendClawHudMutationKind Kind,
    FrontendClawHudDisplayMode? DisplayMode = null,
    int? HudSizeOffset = null,
    FrontendClawHudFont? Font = null,
    FrontendClawHudAlignment? Alignment = null,
    FrontendClawHudBackgroundMode? BackgroundMode = null,
    int? OpacityPercent = null,
    bool? IntelVrrRangeFixEnabled = null)
{
    public bool TryValidate(out string? failureMessage)
    {
        failureMessage = null;
        var hasDisplayMode = DisplayMode is not null;
        var hasSize = HudSizeOffset is not null;
        var hasFont = Font is not null;
        var hasAlignment = Alignment is not null;
        var hasBackground = BackgroundMode is not null;
        var hasOpacity = OpacityPercent is not null;
        var hasVrr = IntelVrrRangeFixEnabled is not null;

        var expected = Kind switch
        {
            FrontendClawHudMutationKind.DisplayMode => hasDisplayMode,
            FrontendClawHudMutationKind.HudSizeOffset => hasSize,
            FrontendClawHudMutationKind.Font => hasFont,
            FrontendClawHudMutationKind.Alignment => hasAlignment,
            FrontendClawHudMutationKind.BackgroundMode => hasBackground,
            FrontendClawHudMutationKind.PreviewOpacity or FrontendClawHudMutationKind.CommitOpacity => hasOpacity,
            FrontendClawHudMutationKind.IntelVrrRangeFixEnabled => hasVrr,
            _ => false,
        };

        if (!expected || new[] { hasDisplayMode, hasSize, hasFont, hasAlignment, hasBackground, hasOpacity, hasVrr }.Count(value => value) != 1)
        {
            failureMessage = "The ClawHUD setting mutation shape is invalid.";
            return false;
        }

        if (HudSizeOffset is { } size && size is < -2 or > 2)
        {
            failureMessage = "HUD size must be between -2 and +2.";
            return false;
        }

        if (OpacityPercent is { } opacity && (opacity is < 50 or > 100 || opacity % 5 != 0))
        {
            failureMessage = "HUD opacity must be a 5% step between 50% and 100%.";
            return false;
        }

        if (DisplayMode is { } displayMode && !Enum.IsDefined(displayMode) ||
            Font is { } font && !Enum.IsDefined(font) ||
            Alignment is { } alignment && !Enum.IsDefined(alignment) ||
            BackgroundMode is { } backgroundMode && !Enum.IsDefined(backgroundMode))
        {
            failureMessage = "The ClawHUD setting value is invalid.";
            return false;
        }

        return true;
    }
}

public sealed record FrontendClawHudMutationResult(
    bool Succeeded,
    string? FailureMessage,
    FrontendClawHudSnapshot Snapshot)
{
    public static FrontendClawHudMutationResult Failed(FrontendClawHudSnapshot snapshot, string message) =>
        new(false, message, snapshot);
}
