using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests.ClawSensorProbe;

/// <summary>
/// Testable-static-helper coverage for <see cref="ClawSensorProbePage"/> (mirrors
/// ProfilePagePolicyTests): the compact Bias completion summary line must always include gyro
/// mean/stddev/span and accel span (work order section 19) -- a follow-up PR B review finding was that
/// the gyro span silently disappeared from that line.
/// </summary>
public sealed class ClawSensorProbePagePolicyTests
{
    [Fact]
    public void FormatBiasSummaryLine_includes_gyro_mean_stddev_and_span()
    {
        var bias = new FrontendClawSensorProbeBiasSummary(
            GyroSampleCount: 500, GyroEffectiveHz: 100,
            GyroMeanX: 0.11, GyroMeanY: -0.22, GyroMeanZ: 0.05,
            GyroStandardDeviationX: 0.01, GyroStandardDeviationY: 0.02, GyroStandardDeviationZ: 0.015,
            GyroSpanX: 0.31, GyroSpanY: 0.42, GyroSpanZ: 0.23,
            AccelSampleCount: 500, AccelEffectiveHz: 100,
            AccelSpanX: 0.02, AccelSpanY: 0.03, AccelSpanZ: 0.01,
            AccelMagnitudeGMean: 1.0, AccelMagnitudeGSpan: 0.02);

        var line = ClawSensorProbePage.FormatBiasSummaryLine(bias);

        Assert.Contains("mean: X=0.11", line);
        Assert.Contains("stddev: X=0.01", line);
        Assert.Contains("span: X=0.31, Y=0.42, Z=0.23", line);
        Assert.Contains("Bias accel span: X=0.02, Y=0.03, Z=0.01", line);
        Assert.Contains("|g| mean=1, span=0.02", line);
    }

    [Fact]
    public void FormatBiasSummaryLine_omits_magnitude_when_unit_basis_is_not_proven_G()
    {
        var bias = new FrontendClawSensorProbeBiasSummary(
            GyroSampleCount: 10, GyroEffectiveHz: 50,
            GyroMeanX: 0, GyroMeanY: 0, GyroMeanZ: 0,
            GyroStandardDeviationX: 0, GyroStandardDeviationY: 0, GyroStandardDeviationZ: 0,
            GyroSpanX: 0, GyroSpanY: 0, GyroSpanZ: 0,
            AccelSampleCount: 10, AccelEffectiveHz: 50,
            AccelSpanX: 0, AccelSpanY: 0, AccelSpanZ: 0,
            AccelMagnitudeGMean: null, AccelMagnitudeGSpan: null);

        var line = ClawSensorProbePage.FormatBiasSummaryLine(bias);

        Assert.DoesNotContain("|g|", line);
    }
}
