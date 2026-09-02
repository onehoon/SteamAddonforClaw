namespace SteamInputAddonforClaw.Contracts.Overlay;

/// <summary>
/// The five fixed top-level Addon Quick Settings Overlay tabs. The user may reorder them but may
/// never add, remove, hide, or rename one. Shared here so Runtime settings persistence, the future
/// <c>.Overlay</c> transport (OQ5-UI-09), and the Overlay shell all use one identity authority.
/// </summary>
public enum OverlayTabId
{
    Device,
    Profile,
    Controller,
    Shortcut,
    Setting,
}

/// <summary>
/// The single narrow invariant for an Overlay tab order: exactly the five known tabs, each once.
/// Used by persisted settings load, the Runtime mutation seam, and the Overlay shell's local
/// normalization so the same rule is not re-implemented in three places. This is deliberately
/// specific to the five Overlay tabs -- not a generic enum-list validator or migration framework.
/// </summary>
public static class OverlayTabOrderContract
{
    private static readonly OverlayTabId[] Default =
    [
        OverlayTabId.Device,
        OverlayTabId.Profile,
        OverlayTabId.Controller,
        OverlayTabId.Shortcut,
        OverlayTabId.Setting,
    ];

    /// <summary>The frozen default order. A fresh copy each call -- callers can never mutate the canonical array.</summary>
    public static IReadOnlyList<OverlayTabId> DefaultOrder => (OverlayTabId[])Default.Clone();

    /// <summary>
    /// True when <paramref name="requested"/> contains all five known tabs exactly once. On success
    /// <paramref name="normalized"/> is an independent read-only copy in the requested order; on
    /// failure it is the default order.
    /// </summary>
    public static bool TryNormalize(IReadOnlyList<OverlayTabId>? requested, out IReadOnlyList<OverlayTabId> normalized)
    {
        if (requested is null || requested.Count != Default.Length)
        {
            normalized = DefaultOrder;
            return false;
        }

        var seen = new HashSet<OverlayTabId>();
        foreach (var id in requested)
        {
            if (!Enum.IsDefined(id) || !seen.Add(id))
            {
                normalized = DefaultOrder;
                return false;
            }
        }

        normalized = requested.ToArray();
        return true;
    }

    /// <summary>Normalize a candidate order, falling back to the default when it is missing or malformed.</summary>
    public static IReadOnlyList<OverlayTabId> NormalizeOrDefault(IReadOnlyList<OverlayTabId>? requested)
    {
        TryNormalize(requested, out var normalized);
        return normalized;
    }
}
