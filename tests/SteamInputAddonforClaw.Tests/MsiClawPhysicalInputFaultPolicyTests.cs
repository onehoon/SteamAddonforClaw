using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawPhysicalInputFaultPolicyTests
{
    [Fact]
    public void Owned_read_state_failure_is_fatal()
    {
        Assert.True(MsiClawPhysicalInputFaultPolicy.IsFatal(MsiClawInputStopReason.ReadStateFailed, ownedSessionIdentityPresent: true));
    }

    [Fact]
    public void Owned_invalid_button_layout_is_fatal()
    {
        Assert.True(MsiClawPhysicalInputFaultPolicy.IsFatal(MsiClawInputStopReason.InvalidButtonLayout, ownedSessionIdentityPresent: true));
    }

    [Fact]
    public void Owned_normal_stop_is_not_fatal()
    {
        Assert.False(MsiClawPhysicalInputFaultPolicy.IsFatal(MsiClawInputStopReason.Stopped, ownedSessionIdentityPresent: true));
    }

    [Fact]
    public void Unowned_read_state_failure_is_not_fatal()
    {
        Assert.False(MsiClawPhysicalInputFaultPolicy.IsFatal(MsiClawInputStopReason.ReadStateFailed, ownedSessionIdentityPresent: false));
    }

    [Fact]
    public void Unowned_normal_stop_is_not_fatal()
    {
        Assert.False(MsiClawPhysicalInputFaultPolicy.IsFatal(MsiClawInputStopReason.Stopped, ownedSessionIdentityPresent: false));
    }

    [Fact]
    public void Unowned_initial_state_not_ready_is_not_fatal()
    {
        // InitialStateNotReady happens before routing ownership is committed; ordinary
        // pipeline-entry failure handling is sufficient for it.
        Assert.False(MsiClawPhysicalInputFaultPolicy.IsFatal(MsiClawInputStopReason.InitialStateNotReady, ownedSessionIdentityPresent: false));
    }
}
