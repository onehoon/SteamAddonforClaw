using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests.ClawSensorProbe;

/// <summary>
/// Coverage for sensor summary values and reset-phase behavior.
/// </summary>
public sealed class ClawSensorProbePagePolicyTests
{
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void Probe_reset_returns_guidance_to_the_rest_phase()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs"));

        Assert.Contains("InstructionText.Text = PhaseInstruction(FrontendClawSensorProbePhase.Rest);", page, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBiasSummaryLine_includes_measured_gyro_and_accelerometer_values()
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
        foreach (var value in new[] { bias.GyroMeanX, bias.GyroMeanY, bias.GyroMeanZ,
                     bias.GyroStandardDeviationX, bias.GyroStandardDeviationY, bias.GyroStandardDeviationZ,
                     bias.GyroSpanX, bias.GyroSpanY, bias.GyroSpanZ, bias.AccelSpanX, bias.AccelSpanY, bias.AccelSpanZ })
            Assert.Contains(value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
    }
    [Fact]
    public void FormatBiasSummaryLine_omits_acceleration_magnitude_without_proven_G_units()
    {
        var bias = new FrontendClawSensorProbeBiasSummary(
            GyroSampleCount: 13, GyroEffectiveHz: 47,
            GyroMeanX: 0.1123, GyroMeanY: -0.2245, GyroMeanZ: 0.0531,
            GyroStandardDeviationX: 0.0171, GyroStandardDeviationY: 0.0212, GyroStandardDeviationZ: 0.0153,
            GyroSpanX: 0.3117, GyroSpanY: 0.4238, GyroSpanZ: 0.2379,
            AccelSampleCount: 17, AccelEffectiveHz: 43,
            AccelSpanX: 0.0281, AccelSpanY: 0.0392, AccelSpanZ: 0.0113,
            AccelMagnitudeGMean: 987.6543, AccelMagnitudeGSpan: 123.4567);
        var line = ClawSensorProbePage.FormatBiasSummaryLine(bias);
        Assert.DoesNotContain("987.6543", line, StringComparison.Ordinal);
        Assert.DoesNotContain("123.4567", line, StringComparison.Ordinal);
    }
}
