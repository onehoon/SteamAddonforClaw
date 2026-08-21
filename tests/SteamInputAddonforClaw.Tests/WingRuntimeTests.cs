using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.GameBar;
using SteamInputAddonforClaw.Wing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WingRuntimeTests
{
    [Fact]
    public async Task WinG_authority_is_active_only_between_successful_arm_and_rollback()
    {
        var stage = new WinGProtectionRoutingStage(() => true, () => { });
        Assert.False(stage.CaptureAuthority().Active);
        await stage.ExecuteMutationAsync(CancellationToken.None);
        var active = stage.CaptureAuthority();
        Assert.True(active.Active);
        await stage.RollbackMutationAsync(CancellationToken.None);
        var inactive = stage.CaptureAuthority();
        Assert.False(inactive.Active);
        Assert.NotEqual(active.Epoch, inactive.Epoch);
    }

    [Fact]
    public void Default_wing_mapping_dispatches_immediate_steam_pulse()
    {
        var requests = 0;
        var dispatcher = new WingActionDispatcher(() => WingMapping.Default, () => { requests++; return true; });
        dispatcher.Dispatch(WingGesture.Single);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Wing_hotkey_rejects_win_g_before_execution()
    {
        var executed = 0;
        var mapping = new WingMapping(new(WingAction.KeyboardHotkey,
            new Oem1HotkeyBinding(Oem1HotkeyModifiers.Windows, Oem1HotkeyKey.G)), new());
        var dispatcher = new WingActionDispatcher(() => mapping, () => false, _ => executed++);
        dispatcher.Dispatch(WingGesture.Single);
        Assert.Equal(0, executed);
    }
}
