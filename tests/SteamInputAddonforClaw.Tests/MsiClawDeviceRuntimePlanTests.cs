using SteamInputAddonforClaw.Runtime;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawDeviceRuntimePlanTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void TdpAndBatteryCompositionAreIndependent(bool supportsTdp, bool supportsBattery, bool expectedTransport)
    {
        var plan = MsiClawDeviceRuntimePlan.Create(
            verifiedMsiClaw: true,
            supportsTdp,
            hasTdpPolicy: true,
            supportsBattery,
            headless: false);

        Assert.Equal(supportsTdp, plan.CreateTdpRuntime);
        Assert.Equal(supportsBattery, plan.CreateBatteryChargeLimitRuntime);
        Assert.Equal(expectedTransport, plan.CreateSharedTransport);
        Assert.Equal(plan.CreateTdpRuntime, plan.CreateTdpWatchers);
    }

    [Fact]
    public void InvalidTdpPolicyDoesNotBlockBatteryOnlyComposition()
    {
        var plan = MsiClawDeviceRuntimePlan.Create(true, true, false, true, headless: false);

        Assert.Equal(new MsiClawDeviceRuntimePlan(true, false, true, false), plan);
    }

    [Fact]
    public void ForeignFamilyAndHeadlessCompositionCreateNoMsiDeviceOwners()
    {
        Assert.Equal(
            new MsiClawDeviceRuntimePlan(false, false, false, false),
            MsiClawDeviceRuntimePlan.Create(true, true, true, true, headless: true));
        Assert.Equal(
            new MsiClawDeviceRuntimePlan(false, false, false, false),
            MsiClawDeviceRuntimePlan.Create(false, true, true, true, headless: false));
    }
}
