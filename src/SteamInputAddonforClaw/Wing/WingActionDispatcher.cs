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
        WingAction action = WingAction.None;
        try
        {
            var mapping = _mapping();
            var binding = gesture == WingGesture.Single ? mapping.Single : mapping.Double;
            action = binding.Action;
            if (!Enum.IsDefined(action)) { AppLog.Warn("Wing.Action", "Unknown persisted action rejected."); return; }
            AppLog.Debug("Wing.Action", "MappingResolved", ("Gesture", gesture), ("Action", binding.Action));
            switch (binding.Action)
            {
                case WingAction.None: return;
                case WingAction.SteamButton:
                    if (!_trySteam()) AppLog.Debug("Wing.Action", "SteamButtonUnavailable");
                    else AppLog.Debug("Wing.Action", "SteamButtonRequested");
                    return;
                case WingAction.KeyboardHotkey:
                    if (binding.Hotkey is null || !binding.Hotkey.IsConfigured) return;
                    if (binding.Hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Windows) && binding.Hotkey.Key == Oem1HotkeyKey.G)
                    { AppLog.Warn("Wing.Action", "InvalidWinGMappingRejected", null); return; }
                    _hotkey(binding.Hotkey); return;
                case WingAction.LaunchApplication:
                    if (binding.Launch is not null) _launch(binding.Launch);
                    return;
            }
        }
        catch (Exception ex) { AppLog.Warn("Wing.Action", "ActionFailed", ex, ("Gesture", gesture), ("Action", action)); }
    }
}
