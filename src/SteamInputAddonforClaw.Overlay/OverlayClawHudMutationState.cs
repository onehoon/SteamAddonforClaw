using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

// Keeps only the Overlay's latest ClawHUD snapshot and one accepted local preview. An ordinary
// state publication updates the snapshot without completing the correlated mutation.
internal sealed class OverlayClawHudMutationState
{
    internal FrontendClawHudSnapshot? Snapshot { get; private set; }
    internal bool IsInFlight { get; private set; }
    internal bool? PendingEnabled { get; private set; }
    internal FrontendClawHudMutationIntent? PendingIntent { get; private set; }

    internal bool DisplayEnabled => PendingEnabled ?? (Snapshot?.DesiredEnabled == true);

    internal FrontendClawHudSettingsSnapshot? DisplaySettings
    {
        get
        {
            var settings = Snapshot?.Settings;
            if (settings is null || PendingIntent is not { } intent)
                return settings;

            return intent.Kind switch
            {
                FrontendClawHudMutationKind.DisplayMode when intent.DisplayMode is { } value => settings with { DisplayMode = value },
                FrontendClawHudMutationKind.HudSizeOffset when intent.HudSizeOffset is { } value => settings with { HudSizeOffset = value },
                FrontendClawHudMutationKind.Font when intent.Font is { } value => settings with { Font = value },
                FrontendClawHudMutationKind.Alignment when intent.Alignment is { } value => settings with { Alignment = value },
                FrontendClawHudMutationKind.BackgroundMode when intent.BackgroundMode is { } value => settings with { BackgroundMode = value },
                FrontendClawHudMutationKind.PreviewOpacity or FrontendClawHudMutationKind.CommitOpacity
                    when intent.OpacityPercent is { } value => settings with { BackgroundOpacityPercent = value },
                FrontendClawHudMutationKind.IntelVrrRangeFixEnabled when intent.IntelVrrRangeFixEnabled is { } value => settings with { IntelVrrRangeFixEnabled = value },
                _ => settings,
            };
        }
    }

    internal void ApplyState(FrontendClawHudSnapshot snapshot) => Snapshot = snapshot;

    internal bool TryBeginEnabled(bool desired)
    {
        if (IsInFlight)
            return false;

        IsInFlight = true;
        PendingEnabled = desired;
        return true;
    }

    internal bool TryBeginSetting(FrontendClawHudMutationIntent intent)
    {
        if (IsInFlight)
            return false;

        IsInFlight = true;
        PendingIntent = intent;
        return true;
    }

    internal void Complete(FrontendClawHudMutationResult result)
    {
        Snapshot = result.Snapshot;
        ClearPending();
    }

    internal void Fail() => ClearPending();

    private void ClearPending()
    {
        IsInFlight = false;
        PendingEnabled = null;
        PendingIntent = null;
    }
}
