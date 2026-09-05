using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Full1902 Cleanup J: the disconnected legacy Vibration Test RPC/session wire contract is
/// gone and the frontend protocol is bumped so a v22 peer fails the handshake up front instead of
/// invoking methods the Runtime no longer implements.</summary>
public sealed class VibrationContractRemovalTests
{
    [Fact]
    public void Frontend_protocol_is_current()
        => Assert.Equal(26, FrontendTransportProtocol.CurrentVersion);

    [Fact]
    public void No_vibration_test_rpc_methods_remain()
    {
        foreach (var removed in new[] { "RunVibrationTest", "OpenVibrationTestSession", "CloseVibrationTestSession" })
            Assert.DoesNotContain(removed, Enum.GetNames<FrontendRpcMethod>());
    }

    [Fact]
    public void No_vibration_test_contract_types_remain_on_the_frontend_contract_assembly()
    {
        var contracts = typeof(SteamInputAddonforClaw.Contracts.Frontend.IAddonFrontendControl).Assembly;
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendVibrationTestCommand"));
        Assert.Null(contracts.GetType("SteamInputAddonforClaw.Contracts.Frontend.FrontendVibrationTestResult"));
        Assert.DoesNotContain(
            typeof(SteamInputAddonforClaw.Contracts.Frontend.IAddonFrontendControl).GetMethods(),
            m => m.Name.Contains("VibrationTest", StringComparison.Ordinal));
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
