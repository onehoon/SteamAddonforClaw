using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;

namespace SteamInputAddonforClaw.Status;

/// <summary>
/// Full1902 0903 cleanup (section 4.4): the one pure boundary that decides whether the Runtime can
/// positively prove a healthy Center M Disabled / Addon-authority controller path, so ordinary status
/// refresh stops reporting the legacy false <c>Indeterminate</c> during normal Full1902 operation.
///
/// <para>
/// Deliberately conservative: it only recognises the healthy state and returns <see langword="null"/>
/// for everything else (startup still pending, admission blocked, physical ownership not established,
/// input source stopped, presentation not attached, device lost/recovering). Non-null is never a
/// failure taxonomy -- a <see langword="null"/> result keeps the existing legacy
/// <see cref="AddonStatusEvaluator"/> output unchanged. It reads only already-proven in-memory facts
/// and does no PnP / HidHide / VIIPER probe.
/// </para>
/// </summary>
internal static class Full1902AddonStatusEvaluator
{
    internal static AddonStatusSnapshot? Evaluate(
        FrontendCenterMStartupState? centerMStartupState,
        bool disabledControllerStartupPending,
        bool physicalInputSourceRunning,
        AddonPresentationKind? activePresentation)
    {
        if (centerMStartupState != FrontendCenterMStartupState.Disabled) return null;
        if (disabledControllerStartupPending) return null;
        if (!physicalInputSourceRunning) return null;
        if (activePresentation is not { } presentation) return null;

        return new AddonStatusSnapshot(AddonOperationalStatus.Ready,
            $"Full1902 controller authority is active ({presentation}).");
    }
}
