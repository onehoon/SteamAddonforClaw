using SteamInputAddonforClaw.Contracts.Oem1;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// The capability matrix is the single definition both the settings UI's per-slot ComboBoxes and the
/// runtime dispatcher's pre-execution validation read, so it is pinned here directly rather than
/// only indirectly through either consumer.
/// </summary>
public sealed class Oem1ActionCapabilitiesTests
{
    private static readonly Oem1BindingSlot[] AllSlots =
    [
        Oem1BindingSlot.NormalSingle,
        Oem1BindingSlot.NormalDouble,
        Oem1BindingSlot.RoutingSingle,
        Oem1BindingSlot.RoutingDouble
    ];

    [Theory]
    [InlineData(Oem1Action.None, true, true, true, true)]
    [InlineData(Oem1Action.SteamBigPicture, true, true, false, false)]
    [InlineData(Oem1Action.SteamQuickAccess, false, false, true, true)]
    [InlineData(Oem1Action.KeyboardHotkey, true, true, true, true)]
    [InlineData(Oem1Action.LaunchApplication, true, true, true, true)]
    public void Capability_matrix_matches_the_locked_specification(
        Oem1Action action, bool normalSingle, bool normalDouble, bool routingSingle, bool routingDouble)
    {
        Assert.Equal(normalSingle, Oem1ActionCapabilities.Supports(action, Oem1BindingSlot.NormalSingle));
        Assert.Equal(normalDouble, Oem1ActionCapabilities.Supports(action, Oem1BindingSlot.NormalDouble));
        Assert.Equal(routingSingle, Oem1ActionCapabilities.Supports(action, Oem1BindingSlot.RoutingSingle));
        Assert.Equal(routingDouble, Oem1ActionCapabilities.Supports(action, Oem1BindingSlot.RoutingDouble));
    }

    [Fact]
    public void Normal_slots_offer_everything_except_quick_access()
    {
        foreach (var slot in new[] { Oem1BindingSlot.NormalSingle, Oem1BindingSlot.NormalDouble })
        {
            var offered = Oem1ActionCapabilities.ActionsFor(slot);
            Assert.Equal(
                [Oem1Action.None, Oem1Action.SteamBigPicture, Oem1Action.KeyboardHotkey, Oem1Action.LaunchApplication],
                offered);
        }
    }

    [Fact]
    public void Routing_slots_offer_everything_except_big_picture()
    {
        foreach (var slot in new[] { Oem1BindingSlot.RoutingSingle, Oem1BindingSlot.RoutingDouble })
        {
            var offered = Oem1ActionCapabilities.ActionsFor(slot);
            Assert.Equal(
                [Oem1Action.None, Oem1Action.SteamQuickAccess, Oem1Action.KeyboardHotkey, Oem1Action.LaunchApplication],
                offered);
        }
    }

    /// <summary>The UI's offered set and the runtime's validation must be the same set, by
    /// construction -- not merely equal today.</summary>
    [Fact]
    public void Offered_actions_and_runtime_validation_agree_for_every_slot()
    {
        foreach (var slot in AllSlots)
        {
            var offered = Oem1ActionCapabilities.ActionsFor(slot);
            foreach (var action in Enum.GetValues<Oem1Action>())
                Assert.Equal(offered.Contains(action), Oem1ActionCapabilities.Supports(action, slot));
        }
    }

    [Fact]
    public void An_unknown_action_value_supports_no_slot_at_all()
    {
        var unknown = (Oem1Action)9999;

        Assert.Equal(default, Oem1ActionCapabilities.SupportedSlots(unknown));
        foreach (var slot in AllSlots)
            Assert.False(Oem1ActionCapabilities.Supports(unknown, slot));
    }

    [Fact]
    public void Slot_resolution_and_update_address_exactly_one_slot_each()
    {
        var mapping = Oem1MappingSettings.Default;

        foreach (var slot in AllSlots)
        {
            var updated = mapping.With(slot, Oem1SlotBinding.Of(Oem1Action.KeyboardHotkey));
            Assert.Equal(Oem1Action.KeyboardHotkey, updated.Resolve(slot).Action);
            foreach (var other in AllSlots)
                if (other != slot)
                    Assert.Equal(mapping.Resolve(other), updated.Resolve(other));
        }
    }
}
