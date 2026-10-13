using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.WindowsTweaks;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsTweaksRuntimeTests
{
    [Fact]
    public void Catalog_contains_only_the_eight_approved_entries_in_order()
    {
        Assert.Equal(
        [
            FrontendWindowsTweakId.MemoryIntegrity,
            FrontendWindowsTweakId.VirtualMachinePlatform,
            FrontendWindowsTweakId.InstantContextMenus,
            FrontendWindowsTweakId.ClassicContextMenu,
            FrontendWindowsTweakId.DisablePasswordOnWake,
            FrontendWindowsTweakId.DisableWindowsWidgets,
            FrontendWindowsTweakId.DisableEdgeBackground,
            FrontendWindowsTweakId.DisableStartupAppDelay,
        ], FrontendWindowsTweakCatalog.All.Select(item => item.Id));
        Assert.All(FrontendWindowsTweakCatalog.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.Equal(FrontendWindowsTweakApplicability.AllSupportedWindows, item.Applicability);
        });
        var fasterMenus = Assert.Single(FrontendWindowsTweakCatalog.All, item => item.Id == FrontendWindowsTweakId.InstantContextMenus);
        Assert.Equal("Faster Menus", fasterMenus.Title);
        Assert.Equal("Disable menu animation and reduce submenu delay to 10 ms.", fasterMenus.Description);
        Assert.Equal(5, (int)FrontendWindowsTweakId.DisableWindowsWidgets);
        Assert.Equal(6, (int)FrontendWindowsTweakId.DisableEdgeBackground);
        Assert.Equal(7, (int)FrontendWindowsTweakId.DisableStartupAppDelay);
        Assert.Equal(82, FrontendTransportProtocol.CurrentVersion);
        Assert.Equal(7, (int)FrontendWindowsTweakState.Unknown);
        Assert.Equal(8, (int)FrontendWindowsTweakState.Mixed);
    }

    [Theory]
    [InlineData(true, false, FrontendWindowsTweakState.EnablePendingRestart)]
    [InlineData(false, true, FrontendWindowsTweakState.DisablePendingRestart)]
    [InlineData(true, true, FrontendWindowsTweakState.Enabled)]
    [InlineData(false, false, FrontendWindowsTweakState.Disabled)]
    public void Configured_and_effective_state_are_not_collapsed(bool configured, bool effective, FrontendWindowsTweakState expected)
    {
        var snapshot = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.MemoryIntegrity,
            new(true, false, configured, effective));

        Assert.Equal(expected, snapshot.State);
        Assert.Equal(configured, snapshot.ConfiguredEnabled);
        Assert.Equal(effective, snapshot.EffectiveEnabled);
    }

    [Fact]
    public void Unknown_mixed_and_managed_observations_do_not_map_to_off()
    {
        var mixed = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.DisablePasswordOnWake,
            new(true, false, null, null, "AC and DC values differ."));
        var managed = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.MemoryIntegrity,
            new(true, true, true, true));
        var unsupported = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.VirtualMachinePlatform,
            new(false, false, null, null));

        Assert.Equal(FrontendWindowsTweakState.Unknown, mixed.State);
        Assert.False(mixed.CanChange);
        Assert.Equal(FrontendWindowsTweakState.Managed, managed.State);
        Assert.True(managed.ConfiguredEnabled);
        Assert.False(managed.CanChange);
        Assert.Equal(FrontendWindowsTweakState.Unsupported, unsupported.State);
        Assert.False(unsupported.CanChange);
    }

    [Theory]
    [InlineData(FrontendWindowsTweakId.DisableWindowsWidgets)]
    [InlineData(FrontendWindowsTweakId.DisableEdgeBackground)]
    [InlineData(FrontendWindowsTweakId.DisableStartupAppDelay)]
    public void New_registry_tweaks_map_mixed_values_to_an_actionable_mixed_state(FrontendWindowsTweakId id)
    {
        var snapshot = WindowsTweaksStateMapper.Map(
            id,
            new(true, false, false, null, "Registry values are mixed or custom.", IsMixed: true));

        Assert.Equal(FrontendWindowsTweakState.Mixed, snapshot.State);
        Assert.False(snapshot.ConfiguredEnabled);
        Assert.Null(snapshot.EffectiveEnabled);
        Assert.True(snapshot.CanChange);
    }

    [Fact]
    public void Mixed_observation_does_not_change_state_semantics_for_existing_non_mixed_tweaks()
    {
        var memoryIntegrity = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.MemoryIntegrity,
            new(true, false, false, false, IsMixed: true));
        var vmPlatform = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.VirtualMachinePlatform,
            new(true, false, false, false, IsMixed: true));

        Assert.Equal(FrontendWindowsTweakState.Disabled, memoryIntegrity.State);
        Assert.Equal(FrontendWindowsTweakState.Disabled, vmPlatform.State);
    }

    [Fact]
    public void Explorer_restart_pending_keeps_the_configured_switch_value()
    {
        var snapshot = WindowsTweaksStateMapper.Map(
            FrontendWindowsTweakId.ClassicContextMenu,
            new(true, false, true, null, ExplorerRestartPending: true));

        Assert.Equal(FrontendWindowsTweakState.ExplorerRestartPending, snapshot.State);
        Assert.True(snapshot.ConfiguredEnabled);
        Assert.Null(snapshot.EffectiveEnabled);
        Assert.True(snapshot.CanChange);
    }

    [Fact]
    public async Task Capture_maps_a_setting_read_failure_to_unavailable_without_affecting_other_settings()
    {
        var platform = new FakePlatform
        {
            Read = id => id == FrontendWindowsTweakId.VirtualMachinePlatform
                ? throw new InvalidOperationException("synthetic failure")
                : new(true, false, false, false)
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var snapshot = await runtime.CaptureAsync();

        Assert.Equal(8, snapshot.Tweaks.Count);
        var failedRow = Assert.Single(snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.VirtualMachinePlatform);
        Assert.Equal(FrontendWindowsTweakState.Unknown, failedRow.State);
        Assert.False(failedRow.CanChange);
        Assert.Equal("Virtual Machine Platform state could not be read. Check Windows servicing configuration.", failedRow.Detail);
        Assert.Equal(FrontendWindowsTweakState.Disabled,
            Assert.Single(snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.InstantContextMenus).State);
    }

    [Fact]
    public async Task Faster_menus_read_failure_is_unknown_and_not_changeable()
    {
        var platform = new FakePlatform
        {
            Read = id => id == FrontendWindowsTweakId.InstantContextMenus
                ? throw new InvalidOperationException("synthetic SPI read failure")
                : new(true, false, false, false),
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var snapshot = await runtime.CaptureAsync();
        var row = Assert.Single(snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.InstantContextMenus);

        Assert.Equal(FrontendWindowsTweakState.Unknown, row.State);
        Assert.False(row.CanChange);
        Assert.Null(row.ConfiguredEnabled);
    }

    [Theory]
    [InlineData(FrontendWindowsTweakId.MemoryIntegrity)]
    [InlineData(FrontendWindowsTweakId.VirtualMachinePlatform)]
    [InlineData(FrontendWindowsTweakId.InstantContextMenus)]
    [InlineData(FrontendWindowsTweakId.ClassicContextMenu)]
    [InlineData(FrontendWindowsTweakId.DisablePasswordOnWake)]
    [InlineData(FrontendWindowsTweakId.DisableWindowsWidgets)]
    [InlineData(FrontendWindowsTweakId.DisableEdgeBackground)]
    [InlineData(FrontendWindowsTweakId.DisableStartupAppDelay)]
    public async Task Mutation_sets_without_preflight_and_returns_only_targeted_readback(FrontendWindowsTweakId id)
    {
        var current = new WindowsTweakObservation(true, false, false, false);
        var platform = new FakePlatform
        {
            Read = _ => current,
            Write = (_, enabled) => current = new(true, false, enabled, enabled),
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(id, enabled: true);

        Assert.True(result.Succeeded);
        Assert.Equal([(id, true)], platform.Writes);
        Assert.Equal([id], platform.Reads);
        Assert.Equal(id, result.UpdatedTweak.Id);
        var row = result.UpdatedTweak;
        Assert.Equal(FrontendWindowsTweakState.Enabled, row.State);
        Assert.True(row.ConfiguredEnabled);
        Assert.True(row.EffectiveEnabled);
    }

    [Fact]
    public async Task Vmp_3010_success_is_exposed_as_a_restart_pending_updated_row()
    {
        var platform = new FakePlatform
        {
            Read = _ => new(true, false, true, true),
            Apply = (id, _) => id == FrontendWindowsTweakId.VirtualMachinePlatform
                ? new WindowsTweakApplyResult(RestartRequired: true)
                : default,
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.VirtualMachinePlatform, enabled: true);

        Assert.True(result.Succeeded);
        Assert.Equal([FrontendWindowsTweakId.VirtualMachinePlatform], platform.Reads);
        Assert.Equal(FrontendWindowsTweakState.EnablePendingRestart, result.UpdatedTweak.State);
        Assert.True(result.UpdatedTweak.ConfiguredEnabled);
        Assert.False(result.UpdatedTweak.EffectiveEnabled);
        Assert.Equal("Windows requires a restart to complete this change.", result.UpdatedTweak.Detail);
    }

    [Fact]
    public async Task Vmp_process_failure_stays_failed_and_exposes_only_a_short_safe_message()
    {
        var current = new WindowsTweakObservation(true, false, false, false);
        var platform = new FakePlatform
        {
            Read = _ => current,
            Write = (_, enabled) =>
            {
                current = new(true, false, enabled, enabled);
                throw new VmpCommandException(
                    VmpOperation.Enable,
                    "Exited",
                    1,
                    0x800F081F,
                    "SourceUnavailable",
                    "InvalidOperation",
                    "Feature source unavailable at C:\\Sensitive\\Windows\\source.wim.");
            },
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.VirtualMachinePlatform, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendWindowsTweakState.Enabled, result.UpdatedTweak.State);
        Assert.True(result.UpdatedTweak.ConfiguredEnabled);
        Assert.True(result.UpdatedTweak.EffectiveEnabled);
        Assert.Equal("Virtual Machine Platform could not be enabled. Check Windows servicing configuration.", result.FailureMessage);
        Assert.DoesNotContain("VmpCommandException", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceUnavailable", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mutation_does_not_write_when_current_state_is_not_changeable()
    {
        var platform = new FakePlatform
        {
            Read = _ => new(true, true, false, false),
            Write = (_, _) => throw new UnauthorizedAccessException("Synthetic managed guard."),
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.MemoryIntegrity, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Equal([(FrontendWindowsTweakId.MemoryIntegrity, true)], platform.Writes);
        Assert.Equal([FrontendWindowsTweakId.MemoryIntegrity], platform.Reads);
        Assert.Equal(FrontendWindowsTweakState.Managed,
            result.UpdatedTweak.State);
    }

    [Fact]
    public async Task Unsupported_widgets_edition_is_not_changeable_and_does_not_write()
    {
        var platform = new FakePlatform
        {
            Read = id => id == FrontendWindowsTweakId.DisableWindowsWidgets
                ? new(false, false, null, null, "The Windows Widgets policy is not supported on this Windows edition.")
                : new(true, false, false, false),
            Write = (_, _) => throw new PlatformNotSupportedException("The Windows Widgets policy is unavailable."),
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.DisableWindowsWidgets, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Equal([(FrontendWindowsTweakId.DisableWindowsWidgets, true)], platform.Writes);
        Assert.Equal([FrontendWindowsTweakId.DisableWindowsWidgets], platform.Reads);
        var row = result.UpdatedTweak;
        Assert.Equal(FrontendWindowsTweakState.Unsupported, row.State);
        Assert.False(row.CanChange);
        Assert.Contains("not supported", row.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Widgets_policy_write_failure_returns_the_actual_readback_without_claiming_success()
    {
        var platform = new FakePlatform
        {
            Read = _ => new(true, false, false, false),
            Write = (id, _) =>
            {
                if (id == FrontendWindowsTweakId.DisableWindowsWidgets)
                    throw new UnauthorizedAccessException("Synthetic Widgets policy write denial.");
            },
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.DisableWindowsWidgets, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Equal([(FrontendWindowsTweakId.DisableWindowsWidgets, true)], platform.Writes);
        var row = result.UpdatedTweak;
        Assert.Equal(FrontendWindowsTweakState.Disabled, row.State);
        Assert.False(row.ConfiguredEnabled);
        Assert.False(row.EffectiveEnabled);
        Assert.Contains("UnauthorizedAccessException", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mixed_menu_state_can_be_reconciled_by_an_off_request_even_when_switch_is_visually_off()
    {
        var animationWriteApplied = false;
        var mixed = new WindowsTweakObservation(
            true, false, false, null,
            "Menu animation and submenu delay do not match either Faster Menus setting.",
            IsMixed: true);
        var platform = new FakePlatform
        {
            Read = _ => mixed,
            Write = (_, _) =>
            {
                animationWriteApplied = true;
                throw new InvalidOperationException("Synthetic submenu-delay write failure.");
            },
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.InstantContextMenus, enabled: false);

        Assert.False(result.Succeeded);
        Assert.True(animationWriteApplied);
        Assert.Equal([(FrontendWindowsTweakId.InstantContextMenus, false)], platform.Writes);
        var row = result.UpdatedTweak;
        Assert.Equal(FrontendWindowsTweakState.Mixed, row.State);
        Assert.False(row.ConfiguredEnabled);
        Assert.True(row.CanChange);
        Assert.Contains("InvalidOperationException", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mixed_menu_state_on_request_runs_both_platform_actions_and_verifies_the_pair()
    {
        var mixed = new WindowsTweakObservation(true, false, false, null, IsMixed: true);
        var platform = new FakePlatform
        {
            Read = id => id == FrontendWindowsTweakId.InstantContextMenus ? mixed : new(true, false, false, false),
            Write = (id, enabled) =>
            {
                if (id == FrontendWindowsTweakId.InstantContextMenus && enabled)
                    mixed = new(true, false, true, true);
            },
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.InstantContextMenus, enabled: true);

        Assert.True(result.Succeeded);
        Assert.Equal([(FrontendWindowsTweakId.InstantContextMenus, true)], platform.Writes);
        Assert.Equal(FrontendWindowsTweakState.Enabled, result.UpdatedTweak.State);
    }

    [Fact]
    public async Task Write_failure_still_reads_back_actual_state()
    {
        var targetReads = 0;
        var platform = new FakePlatform
        {
            Read = id =>
            {
                if (id == FrontendWindowsTweakId.MemoryIntegrity)
                    Interlocked.Increment(ref targetReads);
                return new(true, false, false, false);
            },
            Write = (_, _) => throw new UnauthorizedAccessException()
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.MemoryIntegrity, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendWindowsTweakState.Disabled, result.UpdatedTweak.State);
        Assert.Contains("UnauthorizedAccessException", result.FailureMessage, StringComparison.Ordinal);
        Assert.NotNull(result.FailureMessage);
        Assert.Equal(1, targetReads);
    }

    [Fact]
    public async Task Interrupted_write_reads_back_only_the_requested_tweak_and_returns_actual_state()
    {
        using var cancellation = new CancellationTokenSource();
        var mixed = new WindowsTweakObservation(true, false, false, null, "Values differ.", IsMixed: true);
        var platform = new FakePlatform
        {
            Read = _ => mixed,
            Write = (_, _) =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            },
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.DisableEdgeBackground, true, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal([FrontendWindowsTweakId.DisableEdgeBackground], platform.Reads);
        Assert.Equal(FrontendWindowsTweakState.Mixed, result.UpdatedTweak.State);
        Assert.True(result.UpdatedTweak.CanChange);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public async Task Partial_edge_policy_write_failure_returns_the_actual_mixed_readback()
    {
        var edgeState = new WindowsTweakObservation(true, false, false, false);
        var platform = new FakePlatform
        {
            Read = id => id == FrontendWindowsTweakId.DisableEdgeBackground
                ? edgeState
                : new(true, false, false, false),
            Write = (id, _) =>
            {
                if (id != FrontendWindowsTweakId.DisableEdgeBackground)
                    return;
                edgeState = new(true, false, false, null, "Registry values are mixed or custom.", IsMixed: true);
                throw new UnauthorizedAccessException("Synthetic second-value policy write failure.");
            },
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.DisableEdgeBackground, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Equal([(FrontendWindowsTweakId.DisableEdgeBackground, true)], platform.Writes);
        var row = result.UpdatedTweak;
        Assert.Equal(FrontendWindowsTweakState.Mixed, row.State);
        Assert.False(row.ConfiguredEnabled);
        Assert.Null(row.EffectiveEnabled);
        Assert.True(row.CanChange);
        Assert.Contains("UnauthorizedAccessException", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A2vm_only_catalog_entries_fail_closed_for_unknown_and_unsupported_models()
    {
        var modelSpecific = new FrontendWindowsTweakDefinition(
            FrontendWindowsTweakId.ClassicContextMenu, "Synthetic", "Synthetic", FrontendWindowsTweakApplicability.A2vmOnly);

        Assert.Empty(WindowsTweaksRuntime.FilterApplicableDefinitions([modelSpecific], isKnownA2vmModel: false));
        Assert.Same(modelSpecific,
            Assert.Single(WindowsTweaksRuntime.FilterApplicableDefinitions([modelSpecific], isKnownA2vmModel: true)));
    }

    private sealed class FakePlatform : IWindowsTweaksOperatingSystem
    {
        public Func<FrontendWindowsTweakId, WindowsTweakObservation> Read { get; init; } = _ => new(true, false, false, false);
        public Action<FrontendWindowsTweakId, bool>? Write { get; init; }
        public Func<FrontendWindowsTweakId, bool, WindowsTweakApplyResult>? Apply { get; init; }
        public List<FrontendWindowsTweakId> Reads { get; } = [];
        public List<(FrontendWindowsTweakId Id, bool Enabled)> Writes { get; } = [];

        public WindowsTweakObservation Capture(FrontendWindowsTweakId id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add(id);
            return Read(id);
        }

        public WindowsTweakApplyResult Set(FrontendWindowsTweakId id, bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add((id, enabled));
            Write?.Invoke(id, enabled);
            return Apply?.Invoke(id, enabled) ?? default;
        }
    }
}
