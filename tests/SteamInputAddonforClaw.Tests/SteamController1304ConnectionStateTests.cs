using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics.SteamController1304;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SteamController1304ConnectionStateTests
{
    [Fact]
    public void ConnectedWirelessEvent_IsControllerConnected()
    {
        var result = SteamController1304ConnectionReportParser.Parse(Receiver(), Report(0x02));
        Assert.Equal(SteamController1304ConnectionStatus.ControllerConnected, result.Status);
    }

    [Fact]
    public void DisconnectedWirelessEvent_IsReceiverOnly()
    {
        var result = SteamController1304ConnectionReportParser.Parse(Receiver(), Report(0x01));
        Assert.Equal(SteamController1304ConnectionStatus.ReceiverOnly, result.Status);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void ShortOrLongReport_IsIndeterminate(int length)
    {
        var result = SteamController1304ConnectionReportParser.Parse(Receiver(), new byte[length]);
        Assert.Equal(SteamController1304ConnectionStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void UnexpectedMessageOrState_IsIndeterminate()
    {
        Assert.Equal(SteamController1304ConnectionStatus.Indeterminate, SteamController1304ConnectionReportParser.Parse(Receiver(), Report(0x02, messageType: 0x01)).Status);
        Assert.Equal(SteamController1304ConnectionStatus.Indeterminate, SteamController1304ConnectionReportParser.Parse(Receiver(), Report(0x09)).Status);
    }

    [Fact]
    public void LeadingReportId_IsNormalized()
    {
        var payload = Report(0x02);
        var framed = new byte[payload.Length + 1];
        payload.CopyTo(framed, 1);

        Assert.Equal(SteamController1304ConnectionStatus.ControllerConnected, SteamController1304ConnectionReportParser.Parse(Receiver(), framed).Status);
    }

    [Fact]
    public void MissingTransport_IsIndeterminateWithoutMutation()
    {
        var result = new WindowsSteamController1304ConnectionProbe().Probe(Receiver());
        Assert.Equal(SteamController1304ConnectionStatus.Indeterminate, result.Status);
        Assert.Equal("ConnectionStatusTransportUnavailable", result.Reason);
    }

    [Fact]
    public void ReceiverIdentityMustBePhysicalUsbReceiver()
    {
        var invalid = Receiver() with { InstanceId = "HID\\VID_28DE&PID_1304\\collection" };
        var result = new WindowsSteamController1304ConnectionProbe(new FakeTransport(Report(0x02))).Probe(invalid);
        Assert.Equal(SteamController1304ConnectionStatus.Indeterminate, result.Status);
        Assert.Equal("SteamController1304ReceiverIdentityInvalid", result.Reason);
    }

    private static byte[] Report(byte state, byte messageType = 0x03)
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[2] = messageType;
        report[4] = state;
        return report;
    }

    private static ControllerDeviceInfo Receiver() => new(
        "USB\\VID_28DE&PID_1304\\PUCK",
        Guid.NewGuid(), null, [], "USB", ["USB\\VID_28DE&PID_1304"], [], "USB", null, "usbccgp", 0x28DE, 0x1304, true);

    private sealed class FakeTransport(byte[] report) : ISteamController1304ReadOnlyTransport
    {
        public byte[]? RequestConnectionStatus(ControllerDeviceInfo receiver, TimeSpan timeout) => report;
    }
}
