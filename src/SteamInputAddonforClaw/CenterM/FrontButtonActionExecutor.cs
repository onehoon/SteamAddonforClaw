using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// The one place a resolved <see cref="FrontButtonBinding"/> is validated against its domain and
/// executed. Shared by both physical-button dispatchers (Gamebar / Center M) so the switch is not
/// hand-copied -- it is a stateless executor invoked through delegates the dispatchers receive, not
/// an authority or a state machine.
/// </summary>
/// <remarks>
/// This is the runtime half of <see cref="FrontButtonActionCapabilities"/>: a persisted binding whose
/// action is not valid for the domain it resolved in (an older build, a hand-edited settings file, a
/// future action this build does not know) is refused here rather than executed, so capability
/// validation can never be bypassed by persistence.
/// </remarks>
internal sealed class FrontButtonActionExecutor
{
    private readonly Action _requestOverlayToggle;
    private readonly Action _launchBigPicture;
    private readonly Func<bool> _tryRequestSteamPulse;
    private readonly Func<bool> _tryRequestQuickAccessPulse;
    private readonly Action<FrontButtonHotkeyBinding> _sendHotkey;
    private readonly Action<FrontButtonLaunchApplicationBinding> _launchApplication;

    internal FrontButtonActionExecutor(
        Action requestOverlayToggle,
        Action launchBigPicture,
        Func<bool> tryRequestSteamPulse,
        Func<bool> tryRequestQuickAccessPulse,
        Action<FrontButtonHotkeyBinding>? sendHotkey = null,
        Action<FrontButtonLaunchApplicationBinding>? launchApplication = null)
    {
        _requestOverlayToggle = requestOverlayToggle ?? throw new ArgumentNullException(nameof(requestOverlayToggle));
        _launchBigPicture = launchBigPicture ?? throw new ArgumentNullException(nameof(launchBigPicture));
        _tryRequestSteamPulse = tryRequestSteamPulse ?? throw new ArgumentNullException(nameof(tryRequestSteamPulse));
        _tryRequestQuickAccessPulse = tryRequestQuickAccessPulse ?? throw new ArgumentNullException(nameof(tryRequestQuickAccessPulse));
        _sendHotkey = sendHotkey ?? Oem1KeyboardHotkeyExecutor.Send;
        _launchApplication = launchApplication ?? Oem1ApplicationLauncher.Launch;
    }

    /// <summary>
    /// Executes <paramref name="binding"/> for <paramref name="domain"/>. Returns <see langword="false"/>
    /// only when a domain-supported action was actually invoked and its execution threw -- a
    /// capability-refused binding did nothing, so it is not a backend failure. The caller decides what
    /// a false return means (the Center M path treats it as a replacement-backend failure and fails
    /// open to native Center M; the Gamebar path logs and continues).
    /// </summary>
    internal bool Execute(string logCategory, FrontButtonKind kind, FrontButtonDomain domain, FrontButtonBinding binding)
    {
        var action = binding.Action;
        if (!FrontButtonActionCapabilities.Supports(action, domain))
        {
            AppLog.Warn(logCategory, "Persisted front-button binding is not valid for its domain; refusing to execute it.", null,
                ("Button", kind), ("Domain", domain), ("Action", action));
            return true;
        }

        AppLog.Debug(logCategory, "Front-button action dispatch", ("Button", kind), ("Domain", domain), ("Action", action));

        try
        {
            switch (action)
            {
                case FrontButtonAction.QuickSettingsOverlay:
                    // §13.1: route through the existing Runtime-owned coordinated Overlay toggle seam,
                    // never OverlayProcessController / the Overlay transport directly. It is
                    // fire-and-forget and its own feature-local failure policy applies, so it cannot
                    // synchronously throw here and is never a reason to fail open.
                    _requestOverlayToggle();
                    return true;

                case FrontButtonAction.SteamBigPicture:
                    _launchBigPicture();
                    return true;

                case FrontButtonAction.SteamButton:
                    _tryRequestSteamPulse();
                    return true;

                case FrontButtonAction.SteamQuickAccess:
                    _tryRequestQuickAccessPulse();
                    return true;

                case FrontButtonAction.KeyboardHotkey:
                    // §7 / Full1902 Policy B: the Gamebar Button must never become an indirect escape
                    // hatch back to native Win+G / Xbox Game Bar while Addon controller authority is
                    // active. A Keyboard / Hotkey = Win+G binding on the Gamebar Button is refused.
                    if (kind == FrontButtonKind.Gamebar
                        && binding.Hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Windows)
                        && binding.Hotkey.Key == FrontButtonHotkeyKey.G)
                    {
                        AppLog.Warn(logCategory, "Refused a Gamebar Button Win+G hotkey binding.", null, ("Button", kind));
                        return true;
                    }
                    _sendHotkey(binding.Hotkey);
                    return true;

                case FrontButtonAction.LaunchApplication:
                    _launchApplication(binding.Launch);
                    return true;

                default:
                    return true;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn(logCategory, "Front-button replacement action execution failed.", exception, ("Button", kind), ("Domain", domain), ("Action", action));
            return false;
        }
    }
}
