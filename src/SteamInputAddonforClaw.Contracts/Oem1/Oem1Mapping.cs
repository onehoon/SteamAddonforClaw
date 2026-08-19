namespace SteamInputAddonforClaw.Contracts.Oem1;

/// <summary>
/// Semantic action an OEM1 (Center M) gesture can be bound to. Names are deliberately
/// non-contextual (never "SteamMenu"): the same action must read the same way in every mapping slot
/// it is offered in.
/// </summary>
public enum Oem1Action
{
    None,
    SteamBigPicture,
    SteamQuickAccess,
    KeyboardHotkey,
    LaunchApplication
}

/// <summary>
/// The four -- and only four -- logical OEM1 mapping slots. Flags purely so one action can declare
/// the set of slots it is valid in (<see cref="Oem1ActionCapabilities"/>); an individual binding is
/// always addressed by exactly one slot value.
/// </summary>
/// <remarks>
/// The Normal/Routing split is a MAPPING domain, not a lifecycle or ownership concept: which domain
/// applies is decided per gesture from the canonical Steam Deck output's actual
/// <c>SteamOutputActive</c> state, and switching domains never arms, disarms, or reinitializes OEM1
/// suppression.
/// </remarks>
[Flags]
public enum Oem1BindingSlot
{
    NormalSingle = 1,
    NormalDouble = 2,
    RoutingSingle = 4,
    RoutingDouble = 8
}

/// <summary>
/// THE capability definition. This single table is the reason the settings UI and the runtime
/// dispatcher can never disagree about which actions a slot accepts: the UI builds each slot's
/// ComboBox from <see cref="ActionsFor"/>, and the dispatcher validates every persisted binding with
/// <see cref="Supports"/> before executing it. Lives in the Contracts assembly precisely so the
/// out-of-process UI shares the definition rather than restating it.
/// </summary>
/// <remarks>
/// Deliberately a static table, not a resolver/provider abstraction: the matrix is fixed product
/// behaviour (Big Picture is meaningless while the Steam Deck output owns the controller; Quick
/// Access is meaningless when it does not exist), not a policy anyone configures.
/// </remarks>
public static class Oem1ActionCapabilities
{
    /// <summary>Every slot -- the capability value for actions that are valid everywhere.</summary>
    public const Oem1BindingSlot AllSlots =
        Oem1BindingSlot.NormalSingle | Oem1BindingSlot.NormalDouble |
        Oem1BindingSlot.RoutingSingle | Oem1BindingSlot.RoutingDouble;

    private const Oem1BindingSlot NormalSlots = Oem1BindingSlot.NormalSingle | Oem1BindingSlot.NormalDouble;
    private const Oem1BindingSlot RoutingSlots = Oem1BindingSlot.RoutingSingle | Oem1BindingSlot.RoutingDouble;

    /// <summary>Declaration order is also the order the settings UI presents actions in.</summary>
    private static readonly (Oem1Action Action, Oem1BindingSlot SupportedSlots)[] Catalog =
    [
        (Oem1Action.None, AllSlots),
        (Oem1Action.SteamBigPicture, NormalSlots),
        (Oem1Action.SteamQuickAccess, RoutingSlots),
        (Oem1Action.KeyboardHotkey, AllSlots),
        (Oem1Action.LaunchApplication, AllSlots)
    ];

    /// <summary>The slots <paramref name="action"/> may be bound to. An unrecognized value reports
    /// no slots at all, so an action this build does not know about can never be executed.</summary>
    public static Oem1BindingSlot SupportedSlots(Oem1Action action)
    {
        foreach (var entry in Catalog)
            if (entry.Action == action) return entry.SupportedSlots;
        return default;
    }

    public static bool Supports(Oem1Action action, Oem1BindingSlot slot) =>
        slot != default && (SupportedSlots(action) & slot) == slot;

    /// <summary>The actions the settings UI may offer for <paramref name="slot"/>, in catalog
    /// order.</summary>
    public static IReadOnlyList<Oem1Action> ActionsFor(Oem1BindingSlot slot)
    {
        var actions = new List<Oem1Action>(Catalog.Length);
        foreach (var entry in Catalog)
            if ((entry.SupportedSlots & slot) == slot && slot != default) actions.Add(entry.Action);
        return actions;
    }
}

/// <summary>Optional modifiers for the single keyboard hotkey an OEM1 gesture may send.</summary>
[Flags]
public enum Oem1HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

/// <summary>
/// The one keyboard key an OEM1 hotkey binding may press. Values ARE the Win32 virtual-key codes, so
/// the executor needs no translation table that could drift from this list.
/// </summary>
public enum Oem1HotkeyKey
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

/// <summary>
/// One hotkey: optional modifiers plus exactly one key, pressed and released once. Deliberately not
/// a macro/sequence/hold model -- there is nothing else to persist and nothing else to execute.
/// </summary>
public sealed record Oem1HotkeyBinding(Oem1HotkeyModifiers Modifiers = Oem1HotkeyModifiers.None, Oem1HotkeyKey Key = Oem1HotkeyKey.None)
{
    /// <summary><see cref="Oem1HotkeyKey.None"/> means the user has not finished configuring the
    /// hotkey yet; the dispatcher treats that as nothing to do rather than as a failure.</summary>
    public bool IsConfigured => Key != Oem1HotkeyKey.None && Enum.IsDefined(Key);

    public static Oem1HotkeyBinding Empty { get; } = new();
}

/// <summary>Executable path plus optional arguments. No process monitoring, toggle/kill, or
/// lifecycle ownership is implied -- pressing the button launches it, and that is all.</summary>
public sealed record Oem1LaunchApplicationBinding(string ExecutablePath = "", string Arguments = "")
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExecutablePath);

    public static Oem1LaunchApplicationBinding Empty { get; } = new();
}

/// <summary>
/// What one slot is bound to, plus the action-specific configuration that binding needs. Both
/// configuration records are always carried (never null) so that switching a slot's action back and
/// forth in the UI does not silently discard what the user already typed.
/// </summary>
public sealed record Oem1SlotBinding
{
    public Oem1Action Action { get; init; } = Oem1Action.None;
    public Oem1HotkeyBinding Hotkey { get; init; } = Oem1HotkeyBinding.Empty;
    public Oem1LaunchApplicationBinding Launch { get; init; } = Oem1LaunchApplicationBinding.Empty;

    public static Oem1SlotBinding None { get; } = new();
    public static Oem1SlotBinding Of(Oem1Action action) => new() { Action = action };
}

/// <summary>
/// The one persisted OEM1 mapping source of truth: the global remapping switch plus the four slot
/// bindings. Everything downstream (dispatcher, capability validation, settings UI) reads this same
/// record -- there is no second mapping store, provider, or rule set.
/// </summary>
/// <remarks>
/// <see cref="RemappingEnabled"/> is independent of the mapping values by design: turning remapping
/// off restores native MSI Center M behaviour but must never erase the four bindings, so turning it
/// back on restores exactly what the user had. It is also entirely independent of the Steam Input
/// Routing master switch, which is a different setting for a different feature.
/// </remarks>
public sealed record Oem1MappingSettings
{
    public bool RemappingEnabled { get; init; } = true;
    public Oem1SlotBinding NormalSingle { get; init; } = Oem1SlotBinding.Of(Oem1Action.SteamBigPicture);
    public Oem1SlotBinding NormalDouble { get; init; } = Oem1SlotBinding.None;
    public Oem1SlotBinding RoutingSingle { get; init; } = Oem1SlotBinding.Of(Oem1Action.SteamQuickAccess);
    public Oem1SlotBinding RoutingDouble { get; init; } = Oem1SlotBinding.None;

    /// <summary>First-install / no-persisted-value defaults (locked by the work order): remapping on,
    /// Normal single = Big Picture, Routing single = Quick Access, both doubles unbound.</summary>
    public static Oem1MappingSettings Default { get; } = new();

    public Oem1SlotBinding Resolve(Oem1BindingSlot slot) => slot switch
    {
        Oem1BindingSlot.NormalSingle => NormalSingle,
        Oem1BindingSlot.NormalDouble => NormalDouble,
        Oem1BindingSlot.RoutingSingle => RoutingSingle,
        Oem1BindingSlot.RoutingDouble => RoutingDouble,
        _ => Oem1SlotBinding.None
    };

    public Oem1MappingSettings With(Oem1BindingSlot slot, Oem1SlotBinding binding) => slot switch
    {
        Oem1BindingSlot.NormalSingle => this with { NormalSingle = binding },
        Oem1BindingSlot.NormalDouble => this with { NormalDouble = binding },
        Oem1BindingSlot.RoutingSingle => this with { RoutingSingle = binding },
        Oem1BindingSlot.RoutingDouble => this with { RoutingDouble = binding },
        _ => this
    };
}
