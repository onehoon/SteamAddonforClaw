namespace SteamInputAddonforClaw.Contracts.Frontend;

/// <summary>Shared Quick Settings product contract (Shared Frontend V2, SF-V2-03): one closed,
/// stateless, typed definition of what Device (and, later, Profile) Quick Settings rows exist, their
/// order/labels/control kind/value/range/options, and which mutation intent each row represents.
/// Steam QAM and the Addon Overlay will later render the SAME <see cref="QuickSettingsPageSnapshot"/>
/// with different UI technologies -- this contract carries the shared product semantics, never a
/// renderer or a lifecycle/admission authority (work order sections 1/31).</summary>
public enum QuickSettingsPageId
{
    Device,

    /// <summary>Reserved for the next approved parity page (SF-V2-08). Projection/mutation for
    /// Profile is not implemented until then -- see <see cref="QuickSettingsPageSnapshot.Unavailable"/>.</summary>
    Profile,
}

public enum QuickSettingsSectionId
{
    DeviceTdp,
    DeviceCpuBoost,
    DevicePowerMode,

    // Reserved Profile parity identities -- vocabulary only, not implemented in SF-V2-03.
    ProfileGeneral,
    ProfileTdp,
    ProfileCpuBoost,
    ProfilePowerMode,
}

public enum QuickSettingsRowId
{
    DeviceTdpEnabled,
    DeviceTdpAcPl1,
    DeviceTdpAcPl2,
    DeviceTdpDcPl1,
    DeviceTdpDcPl2,

    DeviceCpuBoostEnabled,
    DeviceCpuBoostAc,
    DeviceCpuBoostDc,

    DevicePowerModeEnabled,
    DevicePowerModeAc,
    DevicePowerModeDc,

    // Reserved Profile parity identities -- vocabulary only, not implemented in SF-V2-03.
    ProfileEnabled,

    ProfileTdpEnabled,
    ProfileTdpAcPl1,
    ProfileTdpAcPl2,
    ProfileTdpDcPl1,
    ProfileTdpDcPl2,

    ProfileCpuBoostEnabled,
    ProfileCpuBoostAc,
    ProfileCpuBoostDc,

    ProfilePowerModeEnabled,
    ProfilePowerModeAc,
    ProfilePowerModeDc,
}

public enum QuickSettingsControlKind { Toggle, Slider }
public enum QuickSettingsSliderKind { Numeric, Discrete }
public enum QuickSettingsCommitMode { Immediate, TrailingDebounce }

/// <summary>Identifies a set of sliders that share one pending draft / one trailing commit. A row
/// with no group commits independently (work order section 12).</summary>
public enum QuickSettingsCommitGroupId
{
    DeviceTdpConfiguration,

    // Reserved for the Profile parity milestone -- vocabulary only.
    ProfileTdpConfiguration,
}

public enum QuickSettingsValueKind { Boolean, Integer }

/// <summary>Closed, strictly typed Quick Settings value (work order section 8). Exactly one of
/// <see cref="BooleanValue"/>/<see cref="IntegerValue"/> is populated, matching <see cref="Kind"/> --
/// see <see cref="IsStructurallyValid"/>.</summary>
public sealed record QuickSettingsValue(QuickSettingsValueKind Kind, bool? BooleanValue = null, int? IntegerValue = null)
{
    public static QuickSettingsValue Boolean(bool value) => new(QuickSettingsValueKind.Boolean, BooleanValue: value);
    public static QuickSettingsValue Integer(int value) => new(QuickSettingsValueKind.Integer, IntegerValue: value);

    public bool IsStructurallyValid => Kind switch
    {
        QuickSettingsValueKind.Boolean => BooleanValue is not null && IntegerValue is null,
        QuickSettingsValueKind.Integer => IntegerValue is not null && BooleanValue is null,
        _ => false,
    };
}

public sealed record QuickSettingsCommitPolicy(QuickSettingsCommitMode Mode, int DelayMilliseconds)
{
    public static readonly QuickSettingsCommitPolicy Immediate = new(QuickSettingsCommitMode.Immediate, 0);

    /// <summary>The one shared slider commit policy (work order section 11): every current Device
    /// Quick Settings slider uses a 2000 ms trailing debounce. Defined once here rather than repeating
    /// the literal per row.</summary>
    public static readonly QuickSettingsCommitPolicy TrailingDebounce2000 = new(QuickSettingsCommitMode.TrailingDebounce, 2000);
}

public sealed record QuickSettingsDiscreteOption(int Value, string Label);

/// <summary>Either a numeric range (<see cref="QuickSettingsSliderKind.Numeric"/>) or a discrete
/// ordered option set (<see cref="QuickSettingsSliderKind.Discrete"/>) -- never both (work order
/// section 10).</summary>
public sealed record QuickSettingsSliderSpec(
    QuickSettingsSliderKind Kind,
    int Minimum = 0,
    int Maximum = 0,
    int Step = 1,
    string? Suffix = null,
    IReadOnlyList<QuickSettingsDiscreteOption>? Options = null);

/// <summary>A narrow linked-slider gap policy (work order section 13): editing <see cref="LowerRowId"/>
/// or <see cref="UpperRowId"/> should keep at least <see cref="MinimumGap"/> between them. Product
/// policy data only -- the existing typed Runtime mutation remains the final validity authority.</summary>
public sealed record QuickSettingsLinkedSliderConstraint(QuickSettingsRowId LowerRowId, QuickSettingsRowId UpperRowId, int MinimumGap);

/// <summary>One Quick Settings row. Carries only shared product facts -- never a surface-specific
/// admission/busy/focus fact (work order section 9.3/31).</summary>
public sealed record QuickSettingsRow(
    QuickSettingsRowId RowId,
    string Label,
    QuickSettingsControlKind ControlKind,
    bool Available,
    bool Writable,
    QuickSettingsValue? Value,
    QuickSettingsSliderSpec? SliderSpec,
    QuickSettingsCommitPolicy CommitPolicy,
    QuickSettingsCommitGroupId? CommitGroupId = null);

public sealed record QuickSettingsSection(QuickSettingsSectionId SectionId, string? Label, IReadOnlyList<QuickSettingsRow> Rows, string? Message = null);

/// <summary>One projected Quick Settings page. <see cref="AppId"/> is the optional context identity
/// used by the later Profile parity page; Device has none.</summary>
public sealed record QuickSettingsPageSnapshot(
    QuickSettingsPageId PageId,
    uint? AppId,
    bool Available,
    string? Message,
    IReadOnlyList<QuickSettingsSection> Sections,
    IReadOnlyList<QuickSettingsLinkedSliderConstraint> LinkedSliderConstraints)
{
    public static QuickSettingsPageSnapshot Unavailable(QuickSettingsPageId pageId, uint? appId = null, string? message = null) =>
        new(pageId, appId, false, message ?? "Quick Settings are unavailable.", [], []);
}

/// <summary>A closed mutation intent (work order section 20). For an independent Toggle/Slider,
/// <see cref="Values"/> contains exactly the edited row. For the grouped Device TDP slider commit,
/// <see cref="Values"/> contains the entire current TDP draft required to reconstruct one
/// <c>FrontendTdpConfiguration</c>. Never a string method name; transport correlation is owned
/// separately by each transport.</summary>
public sealed record QuickSettingsMutationIntent(
    QuickSettingsPageId PageId,
    uint? AppId,
    QuickSettingsRowId EditedRowId,
    IReadOnlyList<QuickSettingsRowValue> Values);

public sealed record QuickSettingsRowValue(QuickSettingsRowId RowId, QuickSettingsValue Value);

/// <summary><see cref="Page"/> is always a freshly captured authoritative projection (work order
/// section 21/28) -- never the submitted draft patched onto the previous page.</summary>
public sealed record QuickSettingsMutationResult(bool Succeeded, string? FailureMessage, QuickSettingsPageSnapshot Page);
