using System.Text.Json.Serialization;

namespace SteamInputAddonforClaw.Contracts.FrontButtons;

/// <summary>
/// The two MSI Claw front buttons the Addon owns. User-facing names are "Gamebar Button"
/// (internal WING / Event88) and "Center M Button" (internal OEM1 / Event41); the hardware/internal
/// names stay where they are legitimate implementation terminology.
/// </summary>
public enum FrontButtonKind
{
    Gamebar,
    CenterM
}

/// <summary>
/// The runtime presentation domain a front-button press resolves against. Decided per press from the
/// actual Full1902 presentation (SteamDeck presentation active =&gt; <see cref="Steam"/>, anything
/// else =&gt; <see cref="Normal"/>) -- never a persisted flag or a raw Steam/Big Picture probe.
/// User-facing labels are "Normal" and "Steam Game / Big Picture".
/// </summary>
public enum FrontButtonDomain
{
    Normal,
    Steam
}

/// <summary>
/// The single shared action vocabulary for both physical buttons. There is deliberately no
/// <c>None</c>: every button/domain binding is always assigned. Which actions are actually offered
/// depends on the domain -- see <see cref="FrontButtonActionCapabilities"/>.
/// </summary>
public enum FrontButtonAction
{
    QuickSettingsOverlay,
    SteamBigPicture,
    SteamButton,
    SteamQuickAccess,
    KeyboardHotkey,
    LaunchApplication
}

/// <summary>Optional modifiers for the single keyboard hotkey a front-button binding may send.</summary>
[Flags]
public enum FrontButtonHotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

/// <summary>
/// The one keyboard key a front-button hotkey binding may press. Values ARE the Win32 virtual-key
/// codes so the executor needs no translation table that could drift from this list.
/// </summary>
public enum FrontButtonHotkeyKey
{
    None = 0,
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    PrintScreen = 0x2C,
    Insert = 0x2D,
    Delete = 0x2E,
    D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
    D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,
    A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47,
    H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E,
    O = 0x4F, P = 0x50, Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55,
    V = 0x56, W = 0x57, X = 0x58, Y = 0x59, Z = 0x5A,
    F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74, F6 = 0x75,
    F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79, F11 = 0x7A, F12 = 0x7B
}

/// <summary>One hotkey: optional modifiers plus exactly one key, pressed and released once.</summary>
public sealed record FrontButtonHotkeyBinding(
    FrontButtonHotkeyModifiers Modifiers = FrontButtonHotkeyModifiers.None,
    FrontButtonHotkeyKey Key = FrontButtonHotkeyKey.None)
{
    /// <summary><see cref="FrontButtonHotkeyKey.None"/> means the user has not finished configuring
    /// the hotkey yet; the dispatcher treats that as nothing to do rather than as a failure.</summary>
    public bool IsConfigured => Key != FrontButtonHotkeyKey.None && Enum.IsDefined(Key);

    public static FrontButtonHotkeyBinding Empty { get; } = new();
}

/// <summary>Executable path plus optional arguments. No process monitoring / toggle / lifecycle
/// ownership is implied -- pressing the button launches it, and that is all.</summary>
public sealed record FrontButtonLaunchApplicationBinding(string ExecutablePath = "", string Arguments = "")
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExecutablePath);

    public static FrontButtonLaunchApplicationBinding Empty { get; } = new();
}

/// <summary>
/// What one physical button is bound to in one domain: the action plus the action-specific
/// configuration that action needs. <see cref="Hotkey"/> and <see cref="Launch"/> are always carried
/// (never null) so switching a binding's action back and forth in the UI does not discard what the
/// user already typed.
/// </summary>
/// <remarks>
/// Every member is <see cref="JsonRequiredAttribute"/>: a persisted mapping that omits any member is
/// rejected on load (System.Text.Json throws, the loader falls back to the frozen defaults) rather
/// than silently completed from these property initializers. The initializers exist only for
/// convenient in-code construction (<see cref="Of"/>, <c>with</c> expressions).
/// </remarks>
public sealed record FrontButtonBinding
{
    [JsonRequired]
    public FrontButtonAction Action { get; init; } = FrontButtonAction.QuickSettingsOverlay;

    [JsonRequired]
    public FrontButtonHotkeyBinding Hotkey { get; init; } = FrontButtonHotkeyBinding.Empty;

    [JsonRequired]
    public FrontButtonLaunchApplicationBinding Launch { get; init; } = FrontButtonLaunchApplicationBinding.Empty;

    public static FrontButtonBinding Of(FrontButtonAction action) =>
        new() { Action = action, Hotkey = FrontButtonHotkeyBinding.Empty, Launch = FrontButtonLaunchApplicationBinding.Empty };
}

/// <summary>Both physical buttons' bindings for one domain. The two actions must differ
/// (<see cref="FrontButtonMappingValidation"/>). Both members are required in persisted JSON.</summary>
public sealed record FrontButtonDomainMapping
{
    [JsonRequired]
    public FrontButtonBinding Gamebar { get; init; } = null!;

    [JsonRequired]
    public FrontButtonBinding CenterM { get; init; } = null!;

    public FrontButtonBinding Resolve(FrontButtonKind kind) =>
        kind == FrontButtonKind.Gamebar ? Gamebar : CenterM;

    public FrontButtonDomainMapping With(FrontButtonKind kind, FrontButtonBinding binding) =>
        kind == FrontButtonKind.Gamebar ? this with { Gamebar = binding } : this with { CenterM = binding };
}

/// <summary>
/// THE one persisted front-button mapping source of truth: two domains, each with a Gamebar and a
/// Center M binding -- four required bindings, no optional assignment, no <c>None</c>. Everything
/// downstream (dispatcher, capability validation, settings UI) reads this same record; there is no
/// second mapping store.
/// </summary>
public sealed record FrontButtonMappingSettings
{
    [JsonRequired]
    public FrontButtonDomainMapping Normal { get; init; } = null!;

    [JsonRequired]
    public FrontButtonDomainMapping Steam { get; init; } = null!;

    /// <summary>First-install / no-persisted-value defaults, frozen by the work order:
    /// Normal Gamebar = Quick Settings Overlay, Normal Center M = Steam Big Picture,
    /// Steam Gamebar = Steam Button, Steam Center M = Steam Quick Access.</summary>
    public static FrontButtonMappingSettings Default { get; } = new()
    {
        Normal = new()
        {
            Gamebar = FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay),
            CenterM = FrontButtonBinding.Of(FrontButtonAction.SteamBigPicture)
        },
        Steam = new()
        {
            Gamebar = FrontButtonBinding.Of(FrontButtonAction.SteamButton),
            CenterM = FrontButtonBinding.Of(FrontButtonAction.SteamQuickAccess)
        }
    };

    public FrontButtonDomainMapping ResolveDomain(FrontButtonDomain domain) =>
        domain == FrontButtonDomain.Steam ? Steam : Normal;

    public FrontButtonBinding Resolve(FrontButtonKind kind, FrontButtonDomain domain) =>
        ResolveDomain(domain).Resolve(kind);

    public FrontButtonMappingSettings With(FrontButtonKind kind, FrontButtonDomain domain, FrontButtonBinding binding) =>
        domain == FrontButtonDomain.Steam
            ? this with { Steam = Steam.With(kind, binding) }
            : this with { Normal = Normal.With(kind, binding) };
}

/// <summary>
/// THE capability definition. This single table is why the settings UI and the runtime dispatcher can
/// never disagree about which actions a domain accepts: the UI builds each domain's ComboBox from
/// <see cref="ActionsFor"/>, and the dispatcher validates every persisted binding with
/// <see cref="Supports"/> before executing it.
/// </summary>
public static class FrontButtonActionCapabilities
{
    /// <summary>Declaration order is also the order the settings UI presents actions in.</summary>
    private static readonly (FrontButtonAction Action, bool Normal, bool Steam)[] Catalog =
    [
        (FrontButtonAction.QuickSettingsOverlay, true, true),
        (FrontButtonAction.SteamBigPicture, true, false),
        (FrontButtonAction.SteamButton, false, true),
        (FrontButtonAction.SteamQuickAccess, false, true),
        (FrontButtonAction.KeyboardHotkey, true, true),
        (FrontButtonAction.LaunchApplication, true, true)
    ];

    /// <summary>Whether <paramref name="action"/> may be bound in <paramref name="domain"/>. An
    /// unrecognized action value is supported nowhere, so a persisted value this build does not know
    /// can never be executed.</summary>
    public static bool Supports(FrontButtonAction action, FrontButtonDomain domain)
    {
        foreach (var entry in Catalog)
            if (entry.Action == action)
                return domain == FrontButtonDomain.Steam ? entry.Steam : entry.Normal;
        return false;
    }

    /// <summary>The actions the settings UI may offer for <paramref name="domain"/>, in catalog
    /// order.</summary>
    public static IReadOnlyList<FrontButtonAction> ActionsFor(FrontButtonDomain domain)
    {
        var actions = new List<FrontButtonAction>(Catalog.Length);
        foreach (var entry in Catalog)
            if (domain == FrontButtonDomain.Steam ? entry.Steam : entry.Normal)
                actions.Add(entry.Action);
        return actions;
    }
}

/// <summary>
/// The one shared mapping validation policy. Both the settings mutation seam and the runtime use it,
/// so a hand-edited settings file or a direct frontend RPC cannot persist an invalid mapping that the
/// UI would have prevented.
/// </summary>
public static class FrontButtonMappingValidation
{
    public static bool IsValid(FrontButtonMappingSettings? mapping) => Validate(mapping) is null;

    /// <summary>Returns null when <paramref name="mapping"/> is a complete, in-domain, duplicate-free
    /// mapping; otherwise a short reason suitable for a log line.</summary>
    public static string? Validate(FrontButtonMappingSettings? mapping)
    {
        if (mapping is null) return "mapping is null";
        if (mapping.Normal is null || mapping.Steam is null) return "a domain mapping is null";

        return ValidateDomain(mapping.Normal, FrontButtonDomain.Normal)
            ?? ValidateDomain(mapping.Steam, FrontButtonDomain.Steam);
    }

    private static string? ValidateDomain(FrontButtonDomainMapping domainMapping, FrontButtonDomain domain)
    {
        var gamebar = ValidateBinding(domainMapping.Gamebar, domain, FrontButtonKind.Gamebar);
        if (gamebar is not null) return gamebar;

        var centerM = ValidateBinding(domainMapping.CenterM, domain, FrontButtonKind.CenterM);
        if (centerM is not null) return centerM;

        // §12.1: same-domain uniqueness compares the semantic action value only, never its payload.
        if (domainMapping.Gamebar.Action == domainMapping.CenterM.Action)
            return $"{domain} domain assigns both buttons the same action ({domainMapping.Gamebar.Action})";

        return null;
    }

    private static string? ValidateBinding(FrontButtonBinding? binding, FrontButtonDomain domain, FrontButtonKind kind)
    {
        if (binding is null) return $"{domain}/{kind} binding is null";
        if (binding.Hotkey is null || binding.Launch is null) return $"{domain}/{kind} binding payload is null";
        if (!Enum.IsDefined(binding.Action)) return $"{domain}/{kind} action is not a recognized value";
        if (!FrontButtonActionCapabilities.Supports(binding.Action, domain))
            return $"{domain}/{kind} action {binding.Action} is not valid in the {domain} domain";

        // §7 / Full1902 Policy B: the Gamebar Button must never be an indirect route back to native
        // Win+G / Xbox Game Bar while Addon controller authority is active. A Keyboard / Hotkey =
        // Win+G binding on the Gamebar Button is invalid at the persistence boundary, not merely
        // refused at execution time. Center M has no such restriction.
        if (kind == FrontButtonKind.Gamebar
            && binding.Action == FrontButtonAction.KeyboardHotkey
            && binding.Hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Windows)
            && binding.Hotkey.Key == FrontButtonHotkeyKey.G)
            return $"{domain}/Gamebar cannot map Keyboard / Hotkey to Win+G";

        return null;
    }
}
