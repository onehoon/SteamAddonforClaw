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

    [Fact]
    public void XBOX_exit_before_performance_startup_readiness_clears_the_mapping_before_publisher_start()
    {
        var global = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.B);
        var startupOverride = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        var cachedOverride = AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(
            ActiveProfileTarget.ForXbox(Key), Capture(Key, true, true, startupOverride));
        var performanceReconcileCount = 0;

        AddonProcessHost.ReconcileActiveXboxGameTransition(
            processShutdownStarted: false,
            profileRuntimeStartupReady: false,
            reconcileBackButtonMapping: () => cachedOverride = AddonProcessHost.ResolveNonSteamBackButtonMappingOverride(
                ActiveProfileTarget.None, null),
            reconcileGameProfile: () => performanceReconcileCount++);

        // The publisher reads this cache when it starts after the XBOX process-exit transition.
        Assert.Equal(global, cachedOverride ?? global);
        Assert.Equal(0, performanceReconcileCount);
    }

    [Fact]
    public void XBOX_active_game_transition_keeps_shutdown_and_performance_startup_gates_scoped()
    {
        var mappingReconciles = 0;
        var performanceReconciles = 0;

        AddonProcessHost.ReconcileActiveXboxGameTransition(
            processShutdownStarted: false,
            profileRuntimeStartupReady: true,
            reconcileBackButtonMapping: () => mappingReconciles++,
            reconcileGameProfile: () => performanceReconciles++);
        AddonProcessHost.ReconcileActiveXboxGameTransition(
            processShutdownStarted: true,
            profileRuntimeStartupReady: true,
            reconcileBackButtonMapping: () => mappingReconciles++,
            reconcileGameProfile: () => performanceReconciles++);

        Assert.Equal(1, mappingReconciles);
        Assert.Equal(1, performanceReconciles);
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
