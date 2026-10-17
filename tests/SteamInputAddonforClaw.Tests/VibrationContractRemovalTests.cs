using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Retired developer vibration actions stay absent while production vibration controls remain.</summary>
public sealed class VibrationContractRemovalTests
{
    [Fact]
    public void Frontend_protocol_is_current()
        => Assert.Equal(86, FrontendTransportProtocol.CurrentVersion);

    [Fact]
    public void Retired_vibration_diagnostic_rpc_methods_remain_absent()
    {
        foreach (var removed in new[]
        {
            "RunVibrationTest", "OpenVibrationTestSession", "CloseVibrationTestSession",
            "RunControllerVibrationProfileWriteProbe"
        })
            Assert.DoesNotContain(removed, Enum.GetNames<FrontendRpcMethod>());
    }

    [Fact]
    public void No_vibration_test_contract_types_remain_on_the_frontend_contract_assembly()
    {
        var contracts = typeof(SteamInputAddonforClaw.Contracts.Frontend.IAddonFrontendControl).Assembly;
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendVibrationTestCommand"));
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendVibrationTestResult"));
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendControllerVibrationProfileWriteProbeMode"));
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendControllerVibrationProfileWriteProbeOutcome"));
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendControllerVibrationProfileWriteProbeResult"));
        var runtime = typeof(SteamInputAddonforClaw.Frontend.InProcessAddonFrontendControl).Assembly;
        Assert.Null(runtime.GetType("SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawVibrationProfileWriteProbeMode"));
        Assert.Null(runtime.GetType("SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawVibrationProfileWriteProbeOutcome"));
        Assert.Null(runtime.GetType("SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawVibrationProfileWriteProbeResult"));
        var strengthClient = runtime.GetType("SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawVibrationStrengthClient");
        Assert.NotNull(strengthClient);
        Assert.DoesNotContain(strengthClient!.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            method => method.Name == "RunDiagnosticMotorPairWriteAsync");
        Assert.DoesNotContain(
            typeof(SteamInputAddonforClaw.Contracts.Frontend.IAddonFrontendControl).GetMethods(),
            m => m.Name.Contains("VibrationTest", StringComparison.Ordinal));
        var productionMethods = typeof(SteamInputAddonforClaw.Contracts.Frontend.IAddonFrontendControl).GetMethods()
            .Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("CaptureControllerVibrationStrengthAsync", productionMethods);
        Assert.Contains("SetControllerVibrationStrengthAsync", productionMethods);
        Assert.Contains("TestControllerVibrationMotorAsync", productionMethods);
        Assert.Contains("RunDeveloperRumbleRearmAsync", productionMethods);
    }

    [Fact] // Full1902 production rumble (work order section 19.7): the production feedback path must
           // not indirectly reconnect the Cleanup-J Developer Vibration Test or the deleted
           // routing-era feedback authority/bridge.
    public void Production_rumble_feedback_does_not_reconnect_the_developer_vibration_or_legacy_feedback_authority()
    {
        var runtime = typeof(SteamInputAddonforClaw.Feedback.TwoMotorRumble).Assembly;
        foreach (var forbidden in new[]
        {
            "SteamInputAddonforClaw.Feedback.FeedbackAuthority",
            "SteamInputAddonforClaw.Feedback.FeedbackAuthorityToken",
            "SteamInputAddonforClaw.Feedback.FeedbackAuthorityLease",
            "SteamInputAddonforClaw.Feedback.SteamDeckRumbleFeedbackBridge",
            "SteamInputAddonforClaw.Feedback.RumbleManager",
            "SteamInputAddonforClaw.Feedback.FeedbackManager",
        })
            Assert.Null(runtime.GetType(forbidden));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var bridge = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Feedback/PresentationRumbleFeedback.cs"));
        var presentation = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs"));
        foreach (var forbidden in new[] { "FeedbackAuthority", "RunVibrationTest", "OpenVibrationTestSession", "VibrationTestSession" })
        {
            Assert.DoesNotContain(forbidden, bridge, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, presentation, StringComparison.Ordinal);
        }
    }
}
