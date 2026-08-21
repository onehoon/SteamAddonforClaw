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
    internal static WingMapping From(Contracts.Wing.WingMappingSettings settings) => new(Convert(settings.Single), Convert(settings.Double));
    private static WingActionBinding Convert(Contracts.Wing.WingSlotBinding binding) => new(
        binding.Action switch
        {
            Contracts.Wing.WingAction.SteamButton => WingAction.SteamButton,
            Contracts.Wing.WingAction.KeyboardHotkey => WingAction.KeyboardHotkey,
            Contracts.Wing.WingAction.LaunchApplication => WingAction.LaunchApplication,
            _ => WingAction.None
        },
        binding.Hotkey is { } h ? new((Contracts.Oem1.Oem1HotkeyModifiers)(int)h.Modifiers, (Contracts.Oem1.Oem1HotkeyKey)h.Key) : null,
        binding.Launch is { } l ? new(l.ExecutablePath, l.Arguments) : null);
}
