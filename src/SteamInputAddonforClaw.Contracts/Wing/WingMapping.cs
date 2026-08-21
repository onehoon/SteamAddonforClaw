namespace SteamInputAddonforClaw.Contracts.Wing;

public enum WingAction { None, SteamButton, KeyboardHotkey, LaunchApplication }
public enum WingHotkeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Windows = 8 }
public enum WingHotkeyKey { None = 0, Enter = 0x0D, Escape = 0x1B, Space = 0x20, A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }

public sealed record WingHotkeyBinding(WingHotkeyModifiers Modifiers = WingHotkeyModifiers.None, WingHotkeyKey Key = WingHotkeyKey.None)
{
    public bool IsConfigured => Key != WingHotkeyKey.None && Enum.IsDefined(Key);
    public static WingHotkeyBinding Empty { get; } = new();
}

public sealed record WingLaunchApplicationBinding(string ExecutablePath = "", string Arguments = "")
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExecutablePath);
    public static WingLaunchApplicationBinding Empty { get; } = new();
}

public sealed record WingSlotBinding
{
    public WingAction Action { get; init; } = WingAction.None;
    public WingHotkeyBinding Hotkey { get; init; } = WingHotkeyBinding.Empty;
    public WingLaunchApplicationBinding Launch { get; init; } = WingLaunchApplicationBinding.Empty;
    public static WingSlotBinding Of(WingAction action) => new() { Action = action };
}

public sealed record WingMappingSettings
{
    public WingSlotBinding Single { get; init; } = WingSlotBinding.Of(WingAction.SteamButton);
    public WingSlotBinding Double { get; init; } = WingSlotBinding.Of(WingAction.None);
    public static WingMappingSettings Default { get; } = new();
}

public static class WingActionCapabilities
{
    public static IReadOnlyList<WingAction> Actions { get; } = [WingAction.None, WingAction.SteamButton, WingAction.KeyboardHotkey, WingAction.LaunchApplication];
    public static bool Supports(WingAction action) => action is WingAction.None or WingAction.SteamButton or WingAction.KeyboardHotkey or WingAction.LaunchApplication;
}
