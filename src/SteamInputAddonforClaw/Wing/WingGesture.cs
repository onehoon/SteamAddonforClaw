namespace SteamInputAddonforClaw.Wing;

/// <summary>
/// Low-level WING (Gamebar Button / Event88) gesture the recognizer can still classify. The product
/// mapping model is one action per physical press per domain, so production wires the recognizer with
/// double-click disabled and only <see cref="Single"/> is ever delivered; <see cref="Double"/> is
/// retained dormant so deleting it would not broaden this change into gesture-infrastructure cleanup.
/// </summary>
internal enum WingGesture { Single, Double }

internal readonly record struct WingGestureDelivery(WingGesture Gesture, long AuthorityEpoch);
