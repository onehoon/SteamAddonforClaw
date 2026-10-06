using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class EffectiveNonSteamBackButtonMappingTests
{
    private const string Key = "store:9PK8PHLCQDF6";

    [Fact]
    public void Only_an_enabled_XBOX_profile_with_a_valid_mapping_produces_an_override()
    {
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        var enabled = Capture(Key, exists: true, enabled: true, mapping);

        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(ActiveProfileTarget.None, enabled));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(ActiveProfileTarget.ForSteam(42), enabled));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(ActiveProfileTarget.ForXbox(Key), Capture(Key, false, false, mapping)));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(ActiveProfileTarget.ForXbox(Key), Capture(Key, true, false, mapping)));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(ActiveProfileTarget.ForXbox(Key), Capture(Key, true, true, null)));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(
            ActiveProfileTarget.ForXbox(Key), Capture(Key, true, true,
                new((Xbox360BackButtonTarget)999, Xbox360BackButtonTarget.Disabled))));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(
            ActiveProfileTarget.ForXbox(Key), Capture(Key, true, true, mapping, writable: false)));
        Assert.Null(AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(
            ActiveProfileTarget.ForXbox("store:other"), enabled));
        Assert.Equal(mapping, AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(ActiveProfileTarget.ForXbox(Key), enabled));
    }

    [Fact]
    public void Effective_mapping_transitions_between_global_and_the_active_XBOX_override()
    {
        var global = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.B);
        var overrideMapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        var activeProfile = Capture(Key, true, true, overrideMapping);

        Assert.Equal(global, Effective(ActiveProfileTarget.None, null, global));
        Assert.Equal(overrideMapping, Effective(ActiveProfileTarget.ForXbox(Key), activeProfile, global));
        Assert.Equal(global, Effective(ActiveProfileTarget.None, activeProfile, global));
        Assert.Equal(global, Effective(ActiveProfileTarget.ForSteam(42), activeProfile, global));
        Assert.Equal(overrideMapping, Effective(ActiveProfileTarget.ForXbox(Key), activeProfile, global));
    }

    private static BackButtonMappingSettings Effective(
        ActiveProfileTarget target,
        XboxGameProfileMutations.Capture? profile,
        BackButtonMappingSettings global) =>
        AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(target, profile) ?? global;

    private static XboxGameProfileMutations.Capture Capture(
        string key,
        bool exists,
        bool enabled,
        BackButtonMappingSettings? mapping,
        bool writable = true) =>
        new(key, new XboxGameProfile
        {
            Enabled = enabled,
            Controller = new NonSteamGameControllerOverrides { BackButtonMapping = mapping }
        }, exists, writable);
}
