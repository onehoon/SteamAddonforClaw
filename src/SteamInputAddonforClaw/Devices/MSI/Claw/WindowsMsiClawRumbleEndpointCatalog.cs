using Windows.Devices.Enumeration;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class WindowsMsiClawRumbleEndpointCatalog
{
    // GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, OPEN_EXISTING -- matches
    // the access mode WindowsMsiClawRumbleTransport ultimately opens the same interface with, so
    // a candidate that fails to open here would also fail to open for the real rumble write.
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint GenericReadWrite = GenericRead | GenericWrite;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;

    private readonly IControllerDeviceEnumerator _devices;
    private readonly IMsiClawNativeHidApi _hidApi;

    internal WindowsMsiClawRumbleEndpointCatalog(IControllerDeviceEnumerator? devices = null, IMsiClawNativeHidApi? hidApi = null)
    {
        _devices = devices ?? new WindowsControllerDeviceEnumerator();
        _hidApi = hidApi ?? new WindowsMsiClawNativeHidApi();
    }

    internal IReadOnlyList<MsiClawRumbleEndpointCandidate> Find(MsiClawPhysicalInputIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.PnpInstanceId)) return [];
        const string hidInterfaceClassGuid = "{4D1E55B2-F16F-11CF-88CB-001111000030}";
        // Interface identity comes from the interface property bag. VID/PID authority remains
        // the existing SetupAPI-backed topology enumerator below; HardwareIds is a Device
        // property and is not requested from DeviceInterface objects.
        var devices = DeviceInformation.FindAllAsync($"System.Devices.InterfaceClassGuid:=\"{hidInterfaceClassGuid}\"", ["System.Devices.DeviceInstanceId"])
            .AsTask().GetAwaiter().GetResult();
        var pnpDevices = _devices.EnumeratePresentDevices();
        var candidates = new List<MsiClawRumbleEndpointCandidate>();
        foreach (var device in devices)
        {
            var pnp = device.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var value) ? value as string : null;
            if (string.IsNullOrWhiteSpace(pnp)) continue;
            var topology = pnpDevices.Where(candidate =>
                string.Equals(candidate.InstanceId, pnp, StringComparison.OrdinalIgnoreCase) &&
                candidate.VendorId == MsiClawHardware.VendorId && candidate.ProductId == MsiClawHardware.DirectInputProductId).ToArray();
            if (topology.Length != 1)
            {
                AppLog.Debug("Rumble", "Rumble endpoint candidate rejected: topology mismatch.", ("PnpInstanceId", pnp), ("MatchCount", topology.Length));
                continue;
            }
            var physicalRoot = topology[0].AncestorInstanceIds.FirstOrDefault(root => root.StartsWith("USB\\VID_0DB0&PID_1902\\", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(physicalRoot))
            {
                AppLog.Debug("Rumble", "Rumble endpoint candidate rejected: no physical root.", ("PnpInstanceId", pnp));
                continue;
            }
            if (!TryReadHidCapabilities(device.Id, out var inputLength, out var outputLength, out var usagePage, out var usage, out var openSucceeded, out var win32Error, out var hidStatus))
            {
                AppLog.Debug("Rumble", "Rumble endpoint candidate rejected: HID capability query failed.", ("PnpInstanceId", pnp), ("PhysicalRoot", physicalRoot), ("Win32Error", win32Error), ("HidStatus", hidStatus));
                continue;
            }
            var physicalRootMatch = MatchesPhysicalRoot(physicalRoot, identity.PhysicalIdentity);
            var isGamepadUsage = usagePage == MsiClawHardware.DirectInputUsagePage &&
                usage is MsiClawHardware.DirectInputUsage or MsiClawHardware.DirectInputJoystickUsage;
            AppLog.Debug("Rumble", "Rumble endpoint candidate", ("DevicePath", device.Id), ("PnpInstanceId", pnp), ("PhysicalRoot", physicalRoot), ("VID", MsiClawHardware.VendorId), ("PID", MsiClawHardware.DirectInputProductId), ("InputReportLength", inputLength), ("OutputReportLength", outputLength), ("UsagePage", usagePage), ("Usage", usage), ("IsGamepadUsage", isGamepadUsage), ("PhysicalRootMatch", physicalRootMatch), ("OpenSucceeded", openSucceeded));
            candidates.Add(new(device.Id, pnp!, physicalRoot, MsiClawHardware.VendorId,
                MsiClawHardware.DirectInputProductId, inputLength, outputLength, usagePage, usage, openSucceeded));
        }
        return candidates;
    }

    /// <summary>
    /// Opens the exact HID device interface and reads its real report-buffer capabilities via
    /// HidD_GetPreparsedData + HidP_GetCaps. Internal (not private) so the narrow HID-capability
    /// seam is directly testable with a fake IMsiClawNativeHidApi.
    /// </summary>
    internal bool TryReadHidCapabilities(string devicePath, out int inputLength, out int outputLength, out ushort usagePage, out ushort usage, out bool openSucceeded, out int win32Error, out int hidStatus)
    {
        inputLength = 0;
        outputLength = 0;
        usagePage = 0;
        usage = 0;
        openSucceeded = false;
        win32Error = 0;
        hidStatus = 0;
        // Some MSI HID collections deny GENERIC_READ while still allowing output writes -- proven
        // by ClawTweaks' ClawButtonMonitor.SharedHidWrite, which retries with GENERIC_WRITE only
        // after a read/write open fails. Without this fallback, a valid PID1902 gamepad collection
        // can be rejected here before the transport ever gets a chance to write it.
        var handle = _hidApi.Open(devicePath, GenericReadWrite, ShareReadWrite, OpenExisting);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = _hidApi.Open(devicePath, GenericWrite, ShareReadWrite, OpenExisting);
        }
        using var _ = handle;
        if (handle.IsInvalid)
        {
            win32Error = _hidApi.LastError;
            return false;
        }
        openSucceeded = true;
        if (!_hidApi.TryGetReportLengths(handle, out inputLength, out outputLength, out usagePage, out usage, out hidStatus))
        {
            win32Error = _hidApi.LastError;
            return false;
        }
        return true;
    }

    internal static bool MatchesPhysicalRoot(string candidateRoot, string expectedRoot) =>
        string.Equals(candidateRoot, expectedRoot, StringComparison.OrdinalIgnoreCase);

}
