using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.ClawHud;

internal static class ClawHudFrontendMapping
{
    internal static FrontendClawHudSnapshot MapState(ClawHudState state, FrontendClawHudSettingsSnapshot? settings = null) =>
        new(
            state.DesiredEnabled,
            state.ActualState switch
            {
                ClawHudFeatureState.Disabled => FrontendClawHudRuntimeState.Disabled,
                ClawHudFeatureState.Starting => FrontendClawHudRuntimeState.Starting,
                ClawHudFeatureState.Ready => FrontendClawHudRuntimeState.Ready,
                ClawHudFeatureState.StandaloneConflict => FrontendClawHudRuntimeState.StandaloneConflict,
                _ => FrontendClawHudRuntimeState.Unavailable,
            },
            StatusMessage(state),
            state.RuntimeVersion,
            state.ApplicationVersion,
            settings);

    internal static bool TryMapSettings(ClawHudSettingsSnapshot source, out FrontendClawHudSettingsSnapshot mapped)
    {
        if (!Enum.IsDefined(source.HudFont) || !Enum.IsDefined(source.VisibilityMode) ||
            !Enum.IsDefined(source.Alignment) || !Enum.IsDefined(source.BackgroundMode) ||
            source.HudSizeOffset is < -2 or > 2 || source.BackgroundOpacityPercent is < 50 or > 100 ||
            source.BackgroundOpacityPercent % 5 != 0)
        {
            mapped = null!;
            return false;
        }

        mapped = new(
            source.VisibilityMode switch
            {
                ClawHudWireVisibilityMode.Always => FrontendClawHudDisplayMode.Always,
                ClawHudWireVisibilityMode.InGameOnly => FrontendClawHudDisplayMode.InGameOnly,
                _ => throw new ArgumentOutOfRangeException(),
            },
            source.HudSizeOffset,
            source.HudFont switch
            {
                ClawHudWireFont.Unispace => FrontendClawHudFont.Unispace,
                ClawHudWireFont.SegoeUiVariable => FrontendClawHudFont.SegoeUiVariable,
                _ => throw new ArgumentOutOfRangeException(),
            },
            source.Alignment switch
            {
                ClawHudWireAlignment.Left => FrontendClawHudAlignment.Left,
                ClawHudWireAlignment.Center => FrontendClawHudAlignment.Center,
                ClawHudWireAlignment.Right => FrontendClawHudAlignment.Right,
                _ => throw new ArgumentOutOfRangeException(),
            },
            source.BackgroundMode switch
            {
                ClawHudWireBackgroundMode.FullWidth => FrontendClawHudBackgroundMode.FullWidth,
                ClawHudWireBackgroundMode.ContentWidth => FrontendClawHudBackgroundMode.ContentWidth,
                _ => throw new ArgumentOutOfRangeException(),
            },
            source.BackgroundOpacityPercent,
            source.IntelVrrRangeFixEnabled,
            source.IntelVrrLastResult is { } vrr ? new FrontendClawHudIntelVrrResult(
                vrr.Status switch
                {
                    ClawHudWireIntelVrrStatus.Disabled => FrontendClawHudIntelVrrStatus.Disabled,
                    ClawHudWireIntelVrrStatus.Unavailable => FrontendClawHudIntelVrrStatus.Unavailable,
                    ClawHudWireIntelVrrStatus.UnsupportedPanel => FrontendClawHudIntelVrrStatus.UnsupportedPanel,
                    ClawHudWireIntelVrrStatus.AmbiguousDisplay => FrontendClawHudIntelVrrStatus.AmbiguousDisplay,
                    ClawHudWireIntelVrrStatus.AlreadyCorrect => FrontendClawHudIntelVrrStatus.AlreadyCorrect,
                    ClawHudWireIntelVrrStatus.SkippedUserProfile => FrontendClawHudIntelVrrStatus.SkippedUserProfile,
                    ClawHudWireIntelVrrStatus.Applied => FrontendClawHudIntelVrrStatus.Applied,
                    ClawHudWireIntelVrrStatus.ApplyFailed => FrontendClawHudIntelVrrStatus.ApplyFailed,
                    ClawHudWireIntelVrrStatus.VerificationFailed => FrontendClawHudIntelVrrStatus.VerificationFailed,
                    _ => throw new ArgumentOutOfRangeException(),
                },
                vrr.PanelName, vrr.RangeBefore, vrr.RangeAfter, vrr.Message, vrr.TimestampUtc) : null);
        return true;
    }

    internal static bool TryCreateRequest(FrontendClawHudMutationIntent intent, out ClawHudControlRequest request, out string failure)
    {
        request = null!;
        if (!intent.TryValidate(out var validationFailure))
        {
            failure = validationFailure ?? "The ClawHUD setting mutation is invalid.";
            return false;
        }

        request = intent.Kind switch
        {
            FrontendClawHudMutationKind.DisplayMode => new(ClawHudControlOperation.SetHudVisibilityMode, 1, WireEnum: intent.DisplayMode == FrontendClawHudDisplayMode.Always ? (byte)ClawHudWireVisibilityMode.Always : (byte)ClawHudWireVisibilityMode.InGameOnly),
            FrontendClawHudMutationKind.HudSizeOffset => new(ClawHudControlOperation.SetHudSizeOffset, 1, SizeOffset: intent.HudSizeOffset),
            FrontendClawHudMutationKind.Font => new(ClawHudControlOperation.SetHudFont, 1, WireEnum: intent.Font == FrontendClawHudFont.Unispace ? (byte)ClawHudWireFont.Unispace : (byte)ClawHudWireFont.SegoeUiVariable),
            FrontendClawHudMutationKind.Alignment => new(ClawHudControlOperation.SetHudAlignment, 1, WireEnum: (byte)(intent.Alignment!.Value switch
            {
                FrontendClawHudAlignment.Left => ClawHudWireAlignment.Left,
                FrontendClawHudAlignment.Center => ClawHudWireAlignment.Center,
                _ => ClawHudWireAlignment.Right,
            })),
            FrontendClawHudMutationKind.BackgroundMode => new(ClawHudControlOperation.SetHudBackgroundMode, 1, WireEnum: intent.BackgroundMode == FrontendClawHudBackgroundMode.FullWidth ? (byte)ClawHudWireBackgroundMode.FullWidth : (byte)ClawHudWireBackgroundMode.ContentWidth),
            FrontendClawHudMutationKind.PreviewOpacity => new(ClawHudControlOperation.PreviewHudOpacity, 1, OpacityPercent: (ushort)intent.OpacityPercent!.Value),
            FrontendClawHudMutationKind.CommitOpacity => new(ClawHudControlOperation.CommitHudOpacity, 1, OpacityPercent: (ushort)intent.OpacityPercent!.Value),
            FrontendClawHudMutationKind.IntelVrrRangeFixEnabled => new(ClawHudControlOperation.SetIntelVrrRangeFixEnabled, 1, Flag: intent.IntelVrrRangeFixEnabled),
            _ => null!,
        };

        failure = request is null ? "The ClawHUD setting mutation is invalid." : string.Empty;
        return request is not null;
    }

    internal static string StatusMessage(ClawHudState state)
    {
        if (state.ActualState == ClawHudFeatureState.Disabled) return "Off";
        if (state.ActualState == ClawHudFeatureState.Starting) return "Starting…";
        if (state.ActualState == ClawHudFeatureState.Ready) return "Ready";
        if (state.ActualState == ClawHudFeatureState.StandaloneConflict)
            return "ClawHUD Standalone is already running. Close it and retry.";

        return state.Failure switch
        {
            "StartupExit:UnsupportedHardware" => "ClawHUD is not supported on this device.",
            "StartupExit:HardwareIndeterminate" => "ClawHUD hardware support could not be verified.",
            "StartupExit:PresentMonRebootRequired" => "Restart Windows to finish PresentMon setup.",
            "StartupExit:PresentMonElevationCancelled" => "PresentMon setup permission was cancelled.",
            "StartupExit:PresentMonInstallFailed" => "PresentMon installation failed.",
            "StartupExit:PresentMonValidationFailed" => "PresentMon could not be validated.",
            "LockInvalid" or "Network" or "HashMismatch" or "ArchiveInvalid" or "ManifestInvalid" or "PayloadInvalid" or "Storage" => "HUD Runtime could not be prepared.",
            _ => string.IsNullOrWhiteSpace(state.Failure) ? "ClawHUD is unavailable." : state.Failure!,
        };
    }
}
