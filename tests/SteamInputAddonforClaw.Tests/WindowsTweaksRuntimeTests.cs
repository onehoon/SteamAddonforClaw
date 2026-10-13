using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.WindowsTweaks;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsTweaksRuntimeTests
{
    [Fact]
    public void Catalog_contains_only_the_five_approved_entries_in_order()
    {
        Assert.Equal(
        [
            FrontendWindowsTweakId.MemoryIntegrity,
            FrontendWindowsTweakId.VirtualMachinePlatform,
            FrontendWindowsTweakId.InstantContextMenus,
            FrontendWindowsTweakId.ClassicContextMenu,
            FrontendWindowsTweakId.DisablePasswordOnWake,
        ], FrontendWindowsTweakCatalog.All.Select(item => item.Id));
        Assert.All(FrontendWindowsTweakCatalog.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.Equal(FrontendWindowsTweakApplicability.AllSupportedWindows, item.Applicability);
        });
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

        Assert.Equal(5, snapshot.Tweaks.Count);
        var failedRow = Assert.Single(snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.VirtualMachinePlatform);
        Assert.Equal(FrontendWindowsTweakState.Unknown, failedRow.State);
        Assert.False(failedRow.CanChange);
        Assert.Equal(FrontendWindowsTweakState.Disabled,
            Assert.Single(snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.InstantContextMenus).State);
    }

    [Fact]
    public async Task Mutation_revalidates_before_writing_and_returns_readback()
    {
        var reads = 0;
        var platform = new FakePlatform
        {
            Read = _ => Interlocked.Increment(ref reads) == 1
                ? new(true, false, false, false)
                : new(true, false, true, false),
        };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.MemoryIntegrity, enabled: true);

        Assert.True(result.Succeeded);
        Assert.Equal([(FrontendWindowsTweakId.MemoryIntegrity, true)], platform.Writes);
        var row = Assert.Single(result.Snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.MemoryIntegrity);
        Assert.Equal(FrontendWindowsTweakState.EnablePendingRestart, row.State);
        Assert.True(row.ConfiguredEnabled);
        Assert.False(row.EffectiveEnabled);
    }

    [Fact]
    public async Task Mutation_does_not_write_when_current_state_is_not_changeable()
    {
        var platform = new FakePlatform { Read = _ => new(true, true, false, false) };
        var runtime = new WindowsTweaksRuntime(platform, isKnownA2vmModel: false);

        var result = await runtime.SetAsync(FrontendWindowsTweakId.MemoryIntegrity, enabled: true);

        Assert.False(result.Succeeded);
        Assert.Empty(platform.Writes);
        Assert.Equal(FrontendWindowsTweakState.Managed,
            Assert.Single(result.Snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.MemoryIntegrity).State);
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
        Assert.Equal(FrontendWindowsTweakState.Disabled,
            Assert.Single(result.Snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.MemoryIntegrity).State);
        Assert.Contains("UnauthorizedAccessException", Assert.Single(result.Snapshot.Tweaks, item => item.Id == FrontendWindowsTweakId.MemoryIntegrity).Detail);
        Assert.NotNull(result.FailureMessage);
        Assert.Equal(2, targetReads);
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
        public List<(FrontendWindowsTweakId Id, bool Enabled)> Writes { get; } = [];

        public WindowsTweakObservation Capture(FrontendWindowsTweakId id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Read(id);
        }

        public void Set(FrontendWindowsTweakId id, bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add((id, enabled));
            Write?.Invoke(id, enabled);
        }
    }
}
