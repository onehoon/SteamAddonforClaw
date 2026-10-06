using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class IntelGpuIgclProbeTests
{
    [Fact]
    public void Igcl_v298_native_layouts_match_x64_header_sizes_and_offsets() =>
        Assert.True(IntelGpuIgclProbe.AbiLayoutIsExpectedForTests());

    [Theory]
    [InlineData(300, 2100, true, 2100)]
    [InlineData(2100, 300, false, 0)]
    [InlineData(300, 0, false, 0)]
    public void Max_max_uses_hardware_max_only_for_a_valid_hardware_range(
        double hardwareMin,
        double hardwareMax,
        bool expectedValid,
        double expectedMax)
    {
        var valid = IntelGpuIgclProbePolicy.TryCreateMaxMax(hardwareMin, hardwareMax, out var range);

        Assert.Equal(expectedValid, valid);
        if (expectedValid) Assert.Equal(new IntelGpuFrequencyRange(expectedMax, expectedMax), range);
    }

    [Fact]
    public void Max_max_rejects_non_finite_hardware_range_values()
    {
        Assert.False(IntelGpuIgclProbePolicy.TryCreateMaxMax(300, double.NaN, out _));
        Assert.False(IntelGpuIgclProbePolicy.TryCreateMaxMax(300, double.PositiveInfinity, out _));
    }

    [Fact]
    public void Explicit_external_limits_are_restored_exactly_and_verified_with_small_tolerance()
    {
        var original = new IntelGpuFrequencyRange(600, 1800);
        var restore = IntelGpuIgclProbePolicy.CreateRestoreRequest(original);

        Assert.Equal(original, restore);
        Assert.True(IntelGpuIgclProbePolicy.MatchesRestoredRange(original, new(600.05, 1799.95)));
        Assert.False(IntelGpuIgclProbePolicy.MatchesRestoredRange(original, new(700, 1800)));
    }

    [Fact]
    public void Negative_external_range_sides_restore_to_factory_and_verify_any_negative_readback()
    {
        var original = new IntelGpuFrequencyRange(-1, 1700);
        var restore = IntelGpuIgclProbePolicy.CreateRestoreRequest(original);

        Assert.Equal(new IntelGpuFrequencyRange(-1, 1700), restore);
        Assert.True(IntelGpuIgclProbePolicy.MatchesRestoredRange(original, new(-2, 1700)));
        Assert.False(IntelGpuIgclProbePolicy.MatchesRestoredRange(original, new(0, 1700)));
    }

    [Fact]
    public void Negative_external_max_restores_to_factory_without_changing_explicit_min()
    {
        var original = new IntelGpuFrequencyRange(500, -2);
        var restore = IntelGpuIgclProbePolicy.CreateRestoreRequest(original);

        Assert.Equal(new IntelGpuFrequencyRange(500, -1), restore);
        Assert.True(IntelGpuIgclProbePolicy.MatchesRestoredRange(original, new(500, -1)));
        Assert.False(IntelGpuIgclProbePolicy.MatchesRestoredRange(original, new(500, 0)));
    }

    [Fact]
    public void Max_max_readback_must_match_both_requested_sides()
    {
        var requested = new IntelGpuFrequencyRange(2100, 2100);

        Assert.True(IntelGpuIgclProbePolicy.MatchesRequestedRange(requested, new(2100.05, 2099.95)));
        Assert.False(IntelGpuIgclProbePolicy.MatchesRequestedRange(requested, new(2000, 2100)));
    }

    [Fact]
    public void Pl1_range_validation_and_mutation_preserve_tau_pl2_and_pl4()
    {
        var original = new IntelGpuPowerLimits(true, 15000, 28000, true, 22000, 30000, 25000);

        Assert.True(IntelGpuIgclProbePolicy.IsPl1TargetValid(14000, 5000, 30000));
        Assert.False(IntelGpuIgclProbePolicy.IsPl1TargetValid(4000, 5000, 30000));
        Assert.False(IntelGpuIgclProbePolicy.IsPl1TargetValid(30001, 5000, 30000));

        var test = IntelGpuIgclProbePolicy.WithPl1(original, 14000);

        Assert.Equal(new IntelGpuPowerLimits(true, 14000, 28000, true, 22000, 30000, 25000), test);
        Assert.True(IntelGpuIgclProbePolicy.MatchesPl1(test, 14000));
        Assert.True(IntelGpuIgclProbePolicy.MatchesPowerRestore(original, original));
        Assert.False(IntelGpuIgclProbePolicy.MatchesPowerRestore(original, test));
    }
}
