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
        => Assert.Equal(24, FrontendTransportProtocol.CurrentVersion);

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
}
