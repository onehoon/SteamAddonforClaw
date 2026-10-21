using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using System.Reflection;
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
    public void Axis_characterization_phases_have_explicit_korean_guidance_and_correct_yaw_hands()
    {
        var expected = new Dictionary<FrontendClawSensorProbePhase, (string Label, string Instruction)>
        {
            [FrontendClawSensorProbePhase.Rest] = ("정지", "화면이 위로 향하도록 기기를 평평한 책상에 놓고 손대지 마세요."),
            [FrontendClawSensorProbePhase.RollLeft] = ("롤 왼쪽", "기기를 세워 잡고 자동차 핸들처럼 반시계 방향으로 돌리세요. 왼손은 아래로, 오른손은 위로 움직입니다. 천천히 원위치로 돌아오세요."),
            [FrontendClawSensorProbePhase.RollRight] = ("롤 오른쪽", "기기를 세워 잡고 자동차 핸들처럼 시계 방향으로 돌리세요. 오른손은 아래로, 왼손은 위로 움직입니다. 천천히 원위치로 돌아오세요."),
            [FrontendClawSensorProbePhase.PitchUp] = ("피치 위로", "화면을 정면으로 향하게 세워 잡고, 기기 위쪽을 몸에서 멀어지게 기울이세요. 천천히 원위치로 돌아오세요."),
            [FrontendClawSensorProbePhase.PitchDown] = ("피치 아래로", "화면을 정면으로 향하게 세워 잡고, 기기 위쪽을 몸쪽으로 기울이세요. 천천히 원위치로 돌아오세요."),
            [FrontendClawSensorProbePhase.YawLeft] = ("요 왼쪽", "기기를 세워 잡고 왼손을 앞으로 밀고, 오른손을 몸쪽으로 당기세요. 화면이 왼쪽을 향합니다. 핸들처럼 기울이지 말고 원위치로 돌아오세요."),
            [FrontendClawSensorProbePhase.YawRight] = ("요 오른쪽", "기기를 세워 잡고 오른손을 앞으로 밀고, 왼손을 몸쪽으로 당기세요. 화면이 오른쪽을 향합니다. 핸들처럼 기울이지 말고 원위치로 돌아오세요.")
        };

        foreach (var (phase, copy) in expected)
        {
            var label = InvokePhaseText("PhaseLabel", phase);
            var instruction = InvokePhaseText("PhaseInstruction", phase);

            Assert.Equal(copy.Label, label);
            Assert.Equal(copy.Instruction, instruction);
            Assert.DoesNotMatch("[A-Za-z]", label);
            Assert.DoesNotMatch("[A-Za-z]", instruction);
        }
    }

    [Fact]
    public void Probe_reset_uses_the_korean_rest_phase_instruction()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ClawSensorProbePage.xaml.cs"));

        Assert.Contains("InstructionText.Text = PhaseInstruction(FrontendClawSensorProbePhase.Rest);", page, StringComparison.Ordinal);
    }

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

    private static string InvokePhaseText(string methodName, FrontendClawSensorProbePhase phase)
    {
        var method = typeof(ClawSensorProbePage).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [phase]));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SteamInputAddonforClaw.slnx")))
            current = current.Parent;

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
