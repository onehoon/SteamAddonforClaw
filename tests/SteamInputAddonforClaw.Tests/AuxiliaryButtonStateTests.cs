using SteamInputAddonforClaw.Input;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AuxiliaryButtonStateTests
{
    [Fact]
    public void ZeroControlState_Works()
    {
        var state = new AuxiliaryButtonState([]);

        Assert.Equal(0, state.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = state[0]);
    }

    [Fact]
    public void TwoAndSixControls_AreIndependent()
    {
        var two = new AuxiliaryButtonState([true, false]);
        var six = new AuxiliaryButtonState([false, true, false, false, false, true]);

        Assert.True(two[0]);
        Assert.False(two[1]);
        Assert.True(six[1]);
        Assert.True(six[5]);
        Assert.False(six[0]);
    }

    [Fact]
    public void States_CompareByButtonValuesAndDefensivelyCopyInput()
    {
        var source = new[] { true, false };
        var state = new AuxiliaryButtonState(source);
        source[0] = false;

        Assert.Equal(new AuxiliaryButtonState([true, false]), state);
        Assert.NotEqual(state, new AuxiliaryButtonState([false, false]));
    }

    [Fact]
    public void InvalidIndex_DoesNotSilentlyMapToAnotherControl()
    {
        var state = new AuxiliaryButtonState([true]);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = state[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = state[1]);
    }
}
