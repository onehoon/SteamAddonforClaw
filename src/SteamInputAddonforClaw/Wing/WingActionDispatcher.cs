using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Wing;

internal sealed class WingActionDispatcher
{
    private readonly Func<WingMapping> _mapping;
    private readonly Func<bool> _trySteam;
    private readonly Action<Oem1HotkeyBinding> _hotkey;
    private readonly Action<Oem1LaunchApplicationBinding> _launch;
    internal WingActionDispatcher(Func<WingMapping> mapping, Func<bool> trySteam, Action<Oem1HotkeyBinding>? hotkey = null, Action<Oem1LaunchApplicationBinding>? launch = null)
    { _mapping = mapping; _trySteam = trySteam; _hotkey = hotkey ?? Oem1KeyboardHotkeyExecutor.Send; _launch = launch ?? Oem1ApplicationLauncher.Launch; }

    internal void Dispatch(WingGesture gesture)
    {
        try
        {
            // Event88 reaches this dispatcher only while the existing routing authority is owned.
            // WING is fixed to the Steam Button in that domain; persisted actions cannot override it.
            if (!_trySteam()) AppLog.Debug("Wing.Action", "SteamButtonUnavailable");
            else AppLog.Debug("Wing.Action", "SteamButtonRequested", ("Gesture", gesture));
        }
        catch (Exception ex) { AppLog.Warn("Wing.Action", "SteamButtonRequestFailed", ex, ("Gesture", gesture)); }
    }
}
