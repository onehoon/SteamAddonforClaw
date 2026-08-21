using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class VibrationTestRpcContractTests
{
    [Fact]
    public void Vibration_test_commands_and_session_lifecycle_have_named_wire_methods()
    {
        Assert.Equal("RunVibrationTest", FrontendRpcMethod.RunVibrationTest.ToString());
        Assert.Equal("OpenVibrationTestSession", FrontendRpcMethod.OpenVibrationTestSession.ToString());
        Assert.Equal("CloseVibrationTestSession", FrontendRpcMethod.CloseVibrationTestSession.ToString());
        Assert.Equal(3, Enum.GetValues<FrontendVibrationTestCommand>().Length);
        Assert.Contains(FrontendVibrationTestCommand.Rumble, Enum.GetValues<FrontendVibrationTestCommand>());
        Assert.Contains(FrontendVibrationTestCommand.Haptic, Enum.GetValues<FrontendVibrationTestCommand>());
        Assert.Contains(FrontendVibrationTestCommand.Stop, Enum.GetValues<FrontendVibrationTestCommand>());
        Assert.Equal(0, (int)FrontendVibrationTestCommand.Rumble);
        Assert.Equal(1, (int)FrontendVibrationTestCommand.Haptic);
        Assert.Equal(3, (int)FrontendVibrationTestCommand.Stop);
    }
}
