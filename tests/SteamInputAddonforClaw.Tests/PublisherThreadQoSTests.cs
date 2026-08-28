using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PublisherThreadQoSTests
{
    [Fact]
    public void HighQoS_request_disables_execution_speed_throttling()
    {
        PublisherThreadQoS.PublisherThreadQoSRequest? request = null;
        PublisherThreadQoS.NativeCallOverrideForTests = value =>
        {
            request = value;
            return (true, 0);
        };

        try
        {
            Assert.True(PublisherThreadQoS.ApplyHighQoS("Test"));
        }
        finally
        {
            PublisherThreadQoS.NativeCallOverrideForTests = null;
        }

        Assert.Equal(PublisherThreadQoS.ThreadInformationClass.ThreadPowerThrottling, request?.ThreadInformationClass);
        Assert.Equal(0x1u, request?.ControlMask);
        Assert.Equal(0u, request?.StateMask);
    }

    [Fact]
    public void HighQoS_failure_is_reported_without_throwing()
    {
        PublisherThreadQoS.NativeCallOverrideForTests = _ => (false, 5);
        try
        {
            var exception = Record.Exception(() => PublisherThreadQoS.ApplyHighQoS("Test"));
            Assert.Null(exception);
        }
        finally
        {
            PublisherThreadQoS.NativeCallOverrideForTests = null;
        }
    }
}
