using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Diagnostics.SteamController1304;

internal sealed class WindowsSteamController1304StateProbe : ISteamController1304StateProbe
{
    private const ushort SteamVendorId = 0x28DE;
    private const ushort SteamControllerProductId = 0x1304;
    private const uint RidDeviceInfo = 0x2000000b;
    private const uint RidDeviceName = 0x20000007;

    private readonly Func<IReadOnlyList<ControllerDeviceInfo>> _enumerateDevices;
    private readonly ISteamController1304ConnectionProbe _connectionProbe;

    public WindowsSteamController1304StateProbe(Func<IReadOnlyList<ControllerDeviceInfo>>? enumerateDevices = null, ISteamController1304ConnectionProbe? connectionProbe = null)
    {
        _enumerateDevices = enumerateDevices ?? new WindowsControllerDeviceEnumerator().EnumeratePresentDevices;
        _connectionProbe = connectionProbe ?? new WindowsSteamController1304ConnectionProbe(new WindowsSteamController1304ReadOnlyTransport());
    }

    public SteamController1304DiagnosticSnapshot Capture()
    {
        try
        {
            var devices = _enumerateDevices()
                .Where(IsSteamController1304)
                .OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(device => new SteamController1304DeviceSnapshot(
                    device.InstanceId, device.ParentInstanceId, device.ContainerId, device.ClassName,
                    device.Service, device.EnumeratorName, device.HardwareIds, device.CompatibleIds,
                    device.UsagePage, device.Usage, device.Present))
                .ToArray();

            var receiver = _enumerateDevices()
                .Where(device => device.Present && device.VendorId == SteamVendorId && device.ProductId == SteamControllerProductId &&
                    string.Equals(device.EnumeratorName, "USB", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(device.Service, "usbccgp", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var connection = receiver.Length == 1
                ? _connectionProbe.Probe(receiver[0])
                : new(SteamController1304ConnectionStatus.Indeterminate, receiver.Length == 0 ? "SteamController1304ReceiverMissing" : "SteamController1304ReceiverAmbiguous", receiver.Length == 0 ? "<none>" : "<multiple>", $"ReceiverCount={receiver.Length}");
            return new SteamController1304DiagnosticSnapshot(devices, CaptureRawInputDevices(), Connection: connection);
        }
        catch (Exception exception)
        {
            return new SteamController1304DiagnosticSnapshot([], [], exception.GetType().Name);
        }
    }

    private static bool IsSteamController1304(ControllerDeviceInfo device) =>
        (device.VendorId == SteamVendorId && device.ProductId == SteamControllerProductId) ||
        device.InstanceId.Contains("VID_28DE&PID_1304", StringComparison.OrdinalIgnoreCase) ||
        device.HardwareIds.Any(id => id.Contains("VID_28DE&PID_1304", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SteamController1304RawInputSnapshot> CaptureRawInputDevices()
    {
        var count = 0u;
        var listed = GetRawInputDeviceList(null, ref count, (uint)Marshal.SizeOf<RawInputDeviceListEntry>());
        if (listed == uint.MaxValue && count == 0) return [];
        if (count == uint.MaxValue || count == 0) return [];
        var entries = new RawInputDeviceListEntry[count];
        var size = (uint)Marshal.SizeOf<RawInputDeviceListEntry>();
        if (GetRawInputDeviceList(entries, ref count, size) == uint.MaxValue) return [];
        var result = new List<SteamController1304RawInputSnapshot>();
        foreach (var entry in entries.Take((int)count))
        {
            var nameLength = 0u;
            var nameResult = GetRawInputDeviceInfo(entry.Device, RidDeviceName, null, ref nameLength);
            if (nameResult == uint.MaxValue) continue;
            if (nameLength == uint.MaxValue || nameLength == 0) continue;
            var name = new string('\0', (int)nameLength + 1);
            var nameChars = name.ToCharArray();
            var chars = nameChars.Length;
            var nameBufferLength = (uint)chars;
            if (GetRawInputDeviceInfo(entry.Device, RidDeviceName, nameChars, ref nameBufferLength) == uint.MaxValue) continue;
            var deviceName = new string(nameChars).TrimEnd('\0');
            if (!deviceName.Contains("VID_28DE&PID_1304", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new SteamController1304RawInputSnapshot(deviceName, entry.Type, Parse(deviceName, "VID_"), Parse(deviceName, "PID_"), null, null));
        }
        return result.OrderBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ushort? Parse(string value, string marker)
    {
        var match = Regex.Match(value, marker + "([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
        return match.Success && ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var parsed) ? parsed : null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RawInputDeviceListEntry { public IntPtr Device; public uint Type; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList([Out] RawInputDeviceListEntry[]? devices, ref uint count, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, [Out] char[]? data, ref uint size);
}
