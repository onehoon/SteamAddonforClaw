using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Diagnostics.SteamController1304;

internal enum SteamController1304ConnectionStatus
{
    ReceiverOnly,
    ControllerConnected,
    Indeterminate
}

internal sealed record SteamController1304ConnectionAssessment(
    SteamController1304ConnectionStatus Status,
    string Reason,
    string ReceiverInstanceId,
    string Evidence);

internal interface ISteamController1304ConnectionProbe
{
    SteamController1304ConnectionAssessment Probe(ControllerDeviceInfo receiver);
}

internal interface ISteamController1304ReadOnlyTransport
{
    byte[]? RequestConnectionStatus(ControllerDeviceInfo receiver, TimeSpan timeout);
}

internal static class SteamController1304ConnectionReportParser
{
    // Linux torvalds/linux master at f5bbbfec59b4e2fb7520a91de3df8a6174325d6a,
    // drivers/hid/hid-steam.c, documents 64-byte reports:
    // bytes 0-1 = 0x01,0x00; byte 2 = message type; byte 4 = wireless state.
    internal const int ReportLength = 64;
    internal const byte ProtocolMarker = 0x01;
    internal const byte MessageTypeWireless = 0x03;
    internal const byte WirelessDisconnected = 0x01;
    internal const byte WirelessConnected = 0x02;

    public static SteamController1304ConnectionAssessment Parse(ControllerDeviceInfo receiver, byte[]? report)
    {
        if (report is null) return Indeterminate(receiver, "ConnectionStatusReadFailed", "No response");
        if (report.Length == ReportLength + 1 && report[1] == ProtocolMarker && report[2] == 0)
            report = report[1..];
        if (report.Length != ReportLength || report[0] != ProtocolMarker || report[1] != 0)
            return Indeterminate(receiver, "ConnectionStatusMalformedReport", $"Length={report.Length}");
        if (report[2] != MessageTypeWireless)
            return Indeterminate(receiver, "ConnectionStatusUnexpectedMessage", $"MessageType=0x{report[2]:X2}");
        return report[4] switch
        {
            WirelessDisconnected => new(SteamController1304ConnectionStatus.ReceiverOnly, "WirelessControllerDisconnected", receiver.InstanceId, "WirelessEvent=0x01"),
            WirelessConnected => new(SteamController1304ConnectionStatus.ControllerConnected, "WirelessControllerConnected", receiver.InstanceId, "WirelessEvent=0x02"),
            _ => Indeterminate(receiver, "ConnectionStatusUnexpectedState", $"WirelessEvent=0x{report[4]:X2}")
        };
    }

    private static SteamController1304ConnectionAssessment Indeterminate(ControllerDeviceInfo receiver, string reason, string evidence) =>
        new(SteamController1304ConnectionStatus.Indeterminate, reason, receiver.InstanceId, evidence);
}

internal sealed class WindowsSteamController1304ConnectionProbe : ISteamController1304ConnectionProbe
{
    private readonly ISteamController1304ReadOnlyTransport? _transport;
    private readonly TimeSpan _timeout;

    public WindowsSteamController1304ConnectionProbe(ISteamController1304ReadOnlyTransport? transport = null, TimeSpan? timeout = null)
    {
        _transport = transport;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(250);
    }

    public SteamController1304ConnectionAssessment Probe(ControllerDeviceInfo receiver)
    {
        if (!IsReceiver(receiver))
            return new(SteamController1304ConnectionStatus.Indeterminate, "SteamController1304ReceiverIdentityInvalid", receiver.InstanceId, "Not28DE1304UsbReceiver");
        if (_transport is null)
            return new(SteamController1304ConnectionStatus.Indeterminate, "ConnectionStatusTransportUnavailable", receiver.InstanceId, "ReadOnlyHidTransportNotImplemented");
        try { return SteamController1304ConnectionReportParser.Parse(receiver, _transport.RequestConnectionStatus(receiver, _timeout)); }
        catch (UnauthorizedAccessException) { return new(SteamController1304ConnectionStatus.Indeterminate, "ConnectionStatusAccessDenied", receiver.InstanceId, "ReadOnlyHidTransport"); }
        catch (TimeoutException) { return new(SteamController1304ConnectionStatus.Indeterminate, "ConnectionStatusTimeout", receiver.InstanceId, "ReadOnlyHidTransport"); }
        catch (Exception exception) { return new(SteamController1304ConnectionStatus.Indeterminate, "ConnectionStatusProbeFailed", receiver.InstanceId, exception.GetType().Name); }
    }

    private static bool IsReceiver(ControllerDeviceInfo device) =>
        device.Present && device.VendorId == 0x28DE && device.ProductId == 0x1304 &&
        string.Equals(device.EnumeratorName, "USB", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(device.Service, "usbccgp", StringComparison.OrdinalIgnoreCase) &&
        device.InstanceId.StartsWith("USB\\VID_28DE&PID_1304\\", StringComparison.OrdinalIgnoreCase);
}
