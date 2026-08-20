using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawTdpHardwareTests
{
    private static readonly HandheldDeviceModelId A2vm = new("msi.claw.a2vm.7");
    private static readonly HandheldDeviceModelId Ex = new("msi.claw.cg3em");

    [Fact]
    public void UnsupportedModelAndInvalidTargetDoNotReadOrWrite()
    {
        var transport = new FakeTransport();
        var hardware = new MsiClawTdpHardware(transport);

        Assert.Equal(MsiClawTdpFailureStage.UnsupportedModel, hardware.Apply(new("unknown"), Pair(20, 21)).FailureStage);
        Assert.Equal(MsiClawTdpFailureStage.InvalidTarget, hardware.Apply(A2vm, Pair(31, 8)).FailureStage);
        Assert.Empty(transport.Operations);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7", 30, 8, 0, 0xC4)]
    [InlineData("msi.claw.cg3em", 35, 8, 6, 0xC6)]
    public void ValidIndependentTargetsUseModelSelectorAndOemSequence(string model, int pl1, int pl2, int selector, int expectedShift)
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0x80] };
        var result = new MsiClawTdpHardware(transport).Apply(new(model), Pair(pl1, pl2));

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { $"GetAp(0)", $"SetData(210,{expectedShift})", "SetData(80,8)", $"SetData(81,{pl2})", $"SetData(80,{pl1})" }, transport.Operations);
        Assert.Equal(expectedShift, MsiClawTdpHardware.EncodeShift(0x80, selector));
        Assert.Equal(expectedShift, MsiClawTdpHardware.EncodeShift(0xA0, selector));
    }

    [Fact]
    public void ShiftReadFailurePreventsPowerLimitWrites()
    {
        var transport = new FakeTransport { GetApSucceeds = false };
        var result = new MsiClawTdpHardware(transport).Apply(A2vm, Pair(20, 21));

        Assert.Equal(MsiClawTdpFailureStage.ShiftRead, result.FailureStage);
        Assert.Equal(new[] { "GetAp(0)" }, transport.Operations);
    }

    [Fact]
    public void ShortShiftResponsePreventsPowerLimitWrites()
    {
        var transport = new FakeTransport { Ap = [] };
        var result = new MsiClawTdpHardware(transport).Apply(A2vm, Pair(20, 21));

        Assert.Equal(MsiClawTdpFailureStage.ShiftRead, result.FailureStage);
        Assert.Single(transport.Operations);
    }

    [Fact]
    public void AlreadyCorrectShiftSkipsBlock210AndStillAppliesLimits()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4] };
        var result = new MsiClawTdpHardware(transport).Apply(A2vm, Pair(20, 21));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("SetData(210,196)", transport.Operations);
        Assert.Equal(new[] { "GetAp(0)", "SetData(80,8)", "SetData(81,21)", "SetData(80,20)" }, transport.Operations);
    }

    [Fact]
    public void TrustedCacheSkipsLimitsButStillVerifiesShift()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4] };
        var hardware = new MsiClawTdpHardware(transport);
        Assert.True(hardware.Apply(A2vm, Pair(20, 21)).Succeeded);
        transport.Operations.Clear();

        Assert.True(hardware.Apply(A2vm, Pair(20, 21)).Succeeded);
        Assert.Equal(new[] { "GetAp(0)" }, transport.Operations);
    }

    [Fact]
    public void Pl1OnlyChangeUsesFloorAndFinalWithoutPl2()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4] };
        var hardware = new MsiClawTdpHardware(transport);
        hardware.Apply(A2vm, Pair(20, 21));
        transport.Operations.Clear();

        hardware.Apply(A2vm, Pair(22, 21));
        Assert.Equal(new[] { "GetAp(0)", "SetData(80,8)", "SetData(80,22)" }, transport.Operations);
    }

    [Fact]
    public void Pl2OnlyChangeWritesOnlyBlock81()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4] };
        var hardware = new MsiClawTdpHardware(transport);
        hardware.Apply(A2vm, Pair(20, 21));
        transport.Operations.Clear();

        hardware.Apply(A2vm, Pair(20, 22));
        Assert.Equal(new[] { "GetAp(0)", "SetData(81,22)" }, transport.Operations);
    }

    [Fact]
    public void ShiftWriteFailurePreventsAnyPowerLimitWrite()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0x80], FailOn = "SetData(210,196)" };
        var result = new MsiClawTdpHardware(transport).Apply(A2vm, Pair(20, 21));

        Assert.Equal(MsiClawTdpFailureStage.ShiftWrite, result.FailureStage);
        Assert.DoesNotContain(transport.Operations, operation => operation.StartsWith("SetData(80", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.Operations, operation => operation.StartsWith("SetData(81", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalShiftMismatchInvalidatesCacheAndReappliesThePair()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4] };
        var hardware = new MsiClawTdpHardware(transport);
        Assert.True(hardware.Apply(A2vm, Pair(20, 21)).Succeeded);
        transport.Operations.Clear();
        transport.Ap = [0x11, 0x22, 0xC1];

        var result = hardware.Apply(A2vm, Pair(20, 21));

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "GetAp(0)", "SetData(210,196)", "SetData(80,8)", "SetData(81,21)", "SetData(80,20)" }, transport.Operations);
    }

    [Fact]
    public void UnsupportedShiftStateFailsClosed()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0x40] };

        var result = new MsiClawTdpHardware(transport).Apply(A2vm, Pair(20, 21));

        Assert.Equal(MsiClawTdpFailureStage.ShiftRead, result.FailureStage);
        Assert.Single(transport.Operations);
    }

    [Fact]
    public void InvalidatingCacheRestoresFullUnknownSequence()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4] };
        var hardware = new MsiClawTdpHardware(transport);
        hardware.Apply(A2vm, Pair(20, 21));
        transport.Operations.Clear();
        hardware.InvalidateCachedPowerLimits();

        hardware.Apply(A2vm, Pair(20, 21));
        Assert.Equal(new[] { "GetAp(0)", "SetData(80,8)", "SetData(81,21)", "SetData(80,20)" }, transport.Operations);
    }

    [Fact]
    public void Pl2FailureAttemptsOnePl1RecoveryAndInvalidatesCache()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4], FailOn = "SetData(81,21)" };
        var hardware = new MsiClawTdpHardware(transport);
        var result = hardware.Apply(A2vm, Pair(20, 21));

        Assert.False(result.Succeeded);
        Assert.Equal(MsiClawTdpFailureStage.Pl2, result.FailureStage);
        Assert.True(result.RecoveryAttempted);
        Assert.True(result.RecoverySucceeded);
        Assert.Equal(new[] { "GetAp(0)", "SetData(80,8)", "SetData(81,21)", "SetData(80,20)" }, transport.Operations);
    }

    [Fact]
    public void FinalPl1FailureAttemptsOneRecoveryAndReportsRecoveryFailure()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4], FailOn = "SetData(80,20)", FailRecoveryToo = true };
        var result = new MsiClawTdpHardware(transport).Apply(A2vm, Pair(20, 21));

        Assert.Equal(MsiClawTdpFailureStage.Pl1Final, result.FailureStage);
        Assert.True(result.RecoveryAttempted);
        Assert.False(result.RecoverySucceeded);
        Assert.Equal(2, transport.Operations.Count(x => x == "SetData(80,20)"));
    }

    [Fact]
    public void Pl2OnlyFailureDoesNotAttemptPl1Recovery()
    {
        var transport = new FakeTransport { Ap = [0x11, 0x22, 0xC4], FailOn = "SetData(81,21)" };
        var hardware = new MsiClawTdpHardware(transport);
        hardware.Apply(A2vm, Pair(20, 21));
        transport.Operations.Clear();
        hardware.InvalidateCachedPowerLimits();
        // Establish a known PL1 while making only PL2 differ.
        transport.FailOn = null;
        hardware.Apply(A2vm, Pair(20, 20));
        transport.Operations.Clear();
        transport.FailOn = "SetData(81,21)";

        var result = hardware.Apply(A2vm, Pair(20, 21));
        Assert.Equal(MsiClawTdpFailureStage.Pl2, result.FailureStage);
        Assert.False(result.RecoveryAttempted);
        Assert.DoesNotContain("SetData(80,20)", transport.Operations);
    }

    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };

    private sealed class FakeTransport : IMsiClawTdpTransport
    {
        public List<string> Operations { get; } = [];
        public byte[] Ap { get; set; } = [0xC0];
        public bool GetApSucceeds { get; set; } = true;
        public string? FailOn { get; set; }
        public bool FailRecoveryToo { get; set; }

        public bool TryGetAp(int index, out byte[] payload)
        {
            Operations.Add($"GetAp({index})");
            payload = Ap;
            return GetApSucceeds;
        }

        public bool TrySetData(int block, byte value)
        {
            var operation = $"SetData({block},{value})";
            Operations.Add(operation);
            if (operation == FailOn)
            {
                if (FailRecoveryToo || Operations.Count(x => x == operation) == 1)
                    return false;
            }
            return true;
        }
    }
}
