using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.Wing;

internal enum WingAction { None, SteamButton, KeyboardHotkey, LaunchApplication }
internal enum WingGesture { Single, Double }
internal readonly record struct WingGestureDelivery(WingGesture Gesture, long AuthorityEpoch);
internal sealed record WingActionBinding(
    WingAction Action = WingAction.None,
    Oem1HotkeyBinding? Hotkey = null,
    Oem1LaunchApplicationBinding? Launch = null);

internal sealed record WingMapping(
    WingActionBinding Single,
    WingActionBinding Double)
{
    internal static WingMapping Default { get; } = new(new(WingAction.SteamButton), new());
}
