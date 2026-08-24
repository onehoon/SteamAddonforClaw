using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.Controllers.Detection;

public sealed class WindowsControllerDeviceEnumerator : IControllerDeviceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpCompatibleIds = 0x00000002;
    private const uint SpdrpService = 0x00000004;
    private const uint SpdrpClass = 0x00000007;
    private const uint SpdrpClassGuid = 0x00000008;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint SpdrpEnumeratorName = 0x00000016;
    private const int CrSuccess = 0;
    private const uint DevpropTypeGuid = 0x0000000D;

    private static readonly IntPtr InvalidDeviceInfoSet = new(-1);
    private static readonly DevPropKey DevpkeyDeviceContainerId = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
    {
        var deviceInfoSet = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidDeviceInfoSet)
        {
            throw new InvalidOperationException("Unable to enumerate present PnP devices.");
        }

        try
        {
            var devices = new List<ControllerDeviceInfo>();
            for (uint index = 0; ; index++)
            {
                var deviceInfo = new SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259)
                    {
                        break;
                    }

                    throw new InvalidOperationException($"Unable to enumerate PnP device at index {index}.");
                }

                var instanceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfo);
                var hardwareIds = GetRegistryMultiString(deviceInfoSet, ref deviceInfo, SpdrpHardwareId);
                var compatibleIds = GetRegistryMultiString(deviceInfoSet, ref deviceInfo, SpdrpCompatibleIds);
                var enumeratorName = GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpEnumeratorName);
                var vendorProductId = ParseVendorProductId(hardwareIds.Concat(compatibleIds));
                var ancestorInstanceIds = GetAncestorInstanceIds(deviceInfo.DevInst);

                devices.Add(new ControllerDeviceInfo(
                    instanceId,
                    GetContainerId(deviceInfoSet, ref deviceInfo),
                    ancestorInstanceIds.FirstOrDefault(),
                    ancestorInstanceIds,
                    enumeratorName,
                    hardwareIds,
                    compatibleIds,
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpClass),
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpClassGuid),
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpService),
                    vendorProductId.VendorId,
                    vendorProductId.ProductId,
                    Present: true,
                    FriendlyName: GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpFriendlyName),
                    UsagePage: ParseUsage(hardwareIds.Concat(compatibleIds)).Page,
                    Usage: ParseUsage(hardwareIds.Concat(compatibleIds)).Usage));
            }

            return devices;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public bool IsPresent(ushort vendorId, ushort productId)
    {
        using var set = OpenPresentSet();
        for (uint index = 0; TryGetDevice(set.Handle, index, out var data); index++)
        {
            var parsed = ParseVendorProductId([GetDeviceInstanceId(set.Handle, ref data)]);
            if (parsed.VendorId == vendorId && parsed.ProductId == productId) return true;
        }
        return false;
    }

    public IReadOnlyList<string> EnumeratePresentInstanceIds(ushort vendorId, ushort productId)
    {
        using var set = OpenPresentSet();
        var light = ReadLightweightDevices(set.Handle, vendorId, productId);
        return light.Values
            .Where(d => ParseVendorProductId([d.InstanceId]).VendorId == vendorId
                && ParseVendorProductId([d.InstanceId]).ProductId == productId)
            .Select(d => d.InstanceId)
            .ToArray();
    }

    public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId)
    {
        using var set = OpenPresentSet();
        var light = ReadLightweightDevices(set.Handle, vendorId, productId);
        var target = light.Values.Where(d => ParseVendorProductId(d.HardwareIds).VendorId == vendorId && ParseVendorProductId(d.HardwareIds).ProductId == productId).ToArray();
        var selected = target.SelectMany(d => AncestorIds(d.DevInst)).Concat(target.Select(d => d.InstanceId))
            .Where(id => id.Length != 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selected.Select(id => light.TryGetValue(id, out var node) ? Hydrate(set.Handle, node) : null)
            .Where(device => device is not null).Cast<ControllerDeviceInfo>().ToArray();
    }

    public ControllerDeviceInfo? FindPresentDevice(string instanceId) =>
        FindNarrowDevice(instanceId);

    private ControllerDeviceInfo? FindNarrowDevice(string instanceId)
    {
        using var set = OpenPresentSet();
        var data = new SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
        if (!SetupDiOpenDeviceInfoW(set.Handle, instanceId, IntPtr.Zero, 0, ref data)) return null;
        if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess) return null;
        data.DevInst = devInst;
        var hardwareIds = GetRegistryMultiString(set.Handle, ref data, SpdrpHardwareId);
        return Hydrate(set.Handle, new LightDevice(instanceId, devInst, hardwareIds, data));
    }

    private static IEnumerable<string> AncestorIds(uint devInst)
    {
        var current = devInst;
        var visited = new HashSet<uint>();
        while (CM_Get_Parent(out var parent, current, 0) == CrSuccess)
        {
            if (!visited.Add(parent)) yield break;
            var buffer = new StringBuilder(1024);
            if (CM_Get_Device_IDW(parent, buffer, buffer.Capacity, 0) != CrSuccess) yield break;
            yield return buffer.ToString();
            current = parent;
        }
    }

    private static ControllerDeviceInfo Hydrate(IntPtr set, LightDevice node)
    {
        var data = node.Data;
        var hardwareIds = GetRegistryMultiString(set, ref data, SpdrpHardwareId);
        var compatibleIds = GetRegistryMultiString(set, ref data, SpdrpCompatibleIds);
        var ancestors = AncestorIds(node.DevInst).ToArray();
        var usage = ParseUsage(hardwareIds.Concat(compatibleIds));
        return new ControllerDeviceInfo(node.InstanceId, GetContainerId(set, ref data), ancestors.FirstOrDefault(), ancestors,
            GetRegistryString(set, ref data, SpdrpEnumeratorName), hardwareIds, compatibleIds,
            GetRegistryString(set, ref data, SpdrpClass), GetRegistryString(set, ref data, SpdrpClassGuid),
            GetRegistryString(set, ref data, SpdrpService), ParseVendorProductId(hardwareIds.Concat(compatibleIds)).VendorId,
            ParseVendorProductId(hardwareIds.Concat(compatibleIds)).ProductId, true,
            GetRegistryString(set, ref data, SpdrpFriendlyName), usage.Page, usage.Usage);
    }

    private static Dictionary<string, LightDevice> ReadLightweightDevices(IntPtr set, ushort vendorId, ushort productId)
    {
        var result = new Dictionary<string, LightDevice>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; TryGetDevice(set, index, out var data); index++)
        {
            var instanceId = GetDeviceInstanceId(set, ref data);
            // InstanceId is the cheap first discriminator for the common USB/HID case.
            var instanceIdentity = ParseVendorProductId([instanceId]);
            var ids = instanceIdentity.VendorId == vendorId && instanceIdentity.ProductId == productId
                ? (IReadOnlyList<string>)[instanceId]
                : [];
            result[instanceId] = new LightDevice(instanceId, data.DevInst, ids, data);
        }
        return result;
    }

    private static DeviceInfoSet OpenPresentSet()
    {
        var handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (handle == InvalidDeviceInfoSet) throw new InvalidOperationException("Unable to enumerate present PnP devices.");
        return new DeviceInfoSet(handle);
    }

    private static bool TryGetDevice(IntPtr set, uint index, out SpDevinfoData data)
    {
        data = new SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
        if (SetupDiEnumDeviceInfo(set, index, ref data)) return true;
        var error = Marshal.GetLastWin32Error();
        if (error == 259) return false;
        throw new InvalidOperationException($"Unable to enumerate PnP device at index {index}.");
    }

    private sealed record LightDevice(string InstanceId, uint DevInst, IReadOnlyList<string> HardwareIds, SpDevinfoData Data);
    private sealed class DeviceInfoSet(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => SetupDiDestroyDeviceInfoList(Handle);
    }

    private static (ushort? Page, ushort? Usage) ParseUsage(IEnumerable<string> identifiers)
    {
        foreach (var identifier in identifiers)
        {
            var marker = identifier.IndexOf("UP:", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) marker = identifier.IndexOf("UP_", StringComparison.OrdinalIgnoreCase);
            var usageMarker = identifier.IndexOf("_U:", StringComparison.OrdinalIgnoreCase);
            if (usageMarker < 0) usageMarker = identifier.IndexOf("_U_", StringComparison.OrdinalIgnoreCase);
            if (marker < 0 || usageMarker < 0) continue;
            var pageText = identifier[(marker + 3)..].Split('_', StringSplitOptions.RemoveEmptyEntries)[0];
            var usageText = identifier[(usageMarker + 3)..].Split('_', StringSplitOptions.RemoveEmptyEntries)[0];
            if (ushort.TryParse(pageText, System.Globalization.NumberStyles.HexNumber, null, out var page) && ushort.TryParse(usageText, System.Globalization.NumberStyles.HexNumber, null, out var usage)) return (page, usage);
        }
        return (null, null);
    }

    private static string GetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo)
    {
        var buffer = new StringBuilder(1024);
        if (!SetupDiGetDeviceInstanceIdW(deviceInfoSet, ref deviceInfo, buffer, buffer.Capacity, out _))
        {
            throw new InvalidOperationException("Unable to read a PnP device instance ID.");
        }

        return buffer.ToString();
    }

    private static IReadOnlyList<string> GetRegistryMultiString(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property)
    {
        var bytes = GetRegistryProperty(deviceInfoSet, ref deviceInfo, property);
        return bytes.Length == 0
            ? []
            : Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? GetRegistryString(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property)
    {
        var bytes = GetRegistryProperty(deviceInfoSet, ref deviceInfo, property);
        return bytes.Length == 0 ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static byte[] GetRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property)
    {
        var buffer = new byte[16384];
        if (!SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref deviceInfo, property, out _, buffer, (uint)buffer.Length, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 13 || error == 1168)
            {
                return [];
            }

            throw new InvalidOperationException($"Unable to read PnP property {property}.");
        }

        return buffer[..(int)requiredSize];
    }

    private static Guid? GetContainerId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo)
    {
        var buffer = new byte[16];
        var propertyKey = DevpkeyDeviceContainerId;
        if (!SetupDiGetDevicePropertyW(deviceInfoSet, ref deviceInfo, ref propertyKey, out var propertyType, buffer, (uint)buffer.Length, out _, 0))
        {
            return null;
        }

        return propertyType == DevpropTypeGuid ? new Guid(buffer) : null;
    }

    private static IReadOnlyList<string> GetAncestorInstanceIds(uint deviceInstance)
    {
        var ancestors = new List<string>();
        var currentInstance = deviceInstance;
        while (CM_Get_Parent(out var parentInstance, currentInstance, 0) == CrSuccess)
        {
            var buffer = new StringBuilder(1024);
            if (CM_Get_Device_IDW(parentInstance, buffer, buffer.Capacity, 0) != CrSuccess)
            {
                break;
            }

            ancestors.Add(buffer.ToString());
            currentInstance = parentInstance;
        }

        return ancestors;
    }

    private static (ushort? VendorId, ushort? ProductId) ParseVendorProductId(IEnumerable<string> identifiers)
    {
        foreach (var identifier in identifiers)
        {
            var match = System.Text.RegularExpressions.Regex.Match(identifier, "VID_([0-9A-Fa-f]{4}).*PID_([0-9A-Fa-f]{4})");
            if (match.Success)
            {
                return (Convert.ToUInt16(match.Groups[1].Value, 16), Convert.ToUInt16(match.Groups[2].Value, 16));
            }
        }

        return (null, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr parentWindow, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInfoW(IntPtr deviceInfoSet, string deviceInstanceId, IntPtr hwndParent, uint openFlags, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, ref DevPropKey propertyKey, out uint propertyType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Parent(out uint parentDeviceInstance, uint deviceInstance, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint deviceInstance, StringBuilder buffer, int bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceInstance, string deviceInstanceId, uint flags);
}
