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
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly IntPtr InvalidDeviceInfoSet = new(-1);
    private static readonly DevPropKey DevpkeyDeviceContainerId = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
    {
        var deviceInfoSet = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidDeviceInfoSet)
        {
            throw new PnpDeviceReadException("SetupDiGetClassDevsW", Marshal.GetLastWin32Error());
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

                    throw new PnpDeviceReadException("SetupDiEnumDeviceInfo", error,
                        devInst: deviceInfo.DevInst == 0 ? null : deviceInfo.DevInst, operationContext: $"Index={index}");
                }

                var instanceId = GetDeviceInstanceId(deviceInfoSet, ref deviceInfo);
                var hardwareIds = GetRegistryMultiString(deviceInfoSet, ref deviceInfo, SpdrpHardwareId, instanceId);
                var compatibleIds = GetRegistryMultiString(deviceInfoSet, ref deviceInfo, SpdrpCompatibleIds, instanceId);
                var enumeratorName = GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpEnumeratorName, instanceId);
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
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpClass, instanceId),
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpClassGuid, instanceId),
                    GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpService, instanceId),
                    vendorProductId.VendorId,
                    vendorProductId.ProductId,
                    Present: true,
                    FriendlyName: GetRegistryString(deviceInfoSet, ref deviceInfo, SpdrpFriendlyName, instanceId),
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
        var light = ReadLightweightDevices(set.Handle);
        var productIds = new HashSet<ushort> { productId };
        return light.Values
            .Where(d => IsMatchingInstanceId(d.InstanceId, vendorId, productIds))
            .Select(d => d.InstanceId)
            .ToArray();
    }

    public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId) =>
        EnumeratePresentDevices(vendorId, [productId]);

    public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var wantedProductIds = productIds.ToHashSet();
        using var set = OpenPresentSet();
        var light = ReadLightweightDevices(set.Handle);
        var candidates = light.Values.Where(node => IsMatchingInstanceId(node.InstanceId, vendorId, wantedProductIds)).ToArray();
        var candidateIds = candidates.Select(node => node.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = SelectScopedInstanceIds(light.Values.Select(node => (node.InstanceId, node.DevInst)),
            vendorId, wantedProductIds, AncestorIds);

        return selected.Select(id =>
            {
                if (!light.TryGetValue(id, out var node)) return null;
                return Hydrate(set.Handle, node, isScopedCandidate: candidateIds.Contains(id));
            })
            .Where(device => device is not null).Cast<ControllerDeviceInfo>().ToArray();
    }

    public ControllerDeviceInfo? FindPresentDevice(string instanceId) =>
        FindNarrowDevice(instanceId);

    private ControllerDeviceInfo? FindNarrowDevice(string instanceId)
    {
        using var set = OpenPresentSet();
        var data = new SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
        if (!SetupDiOpenDeviceInfoW(set.Handle, instanceId, IntPtr.Zero, 0, ref data))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 433 or 1168) return null;
            throw new PnpDeviceReadException("SetupDiOpenDeviceInfoW", error, instanceId: instanceId);
        }
        if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess) return null;
        data.DevInst = devInst;
        return Hydrate(set.Handle, new LightDevice(instanceId, devInst, data));
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

    private static ControllerDeviceInfo Hydrate(IntPtr set, LightDevice node, bool? isScopedCandidate = null)
    {
        var data = node.Data;
        var hardwareIds = GetRegistryMultiString(set, ref data, SpdrpHardwareId, node.InstanceId);
        var compatibleIds = GetRegistryMultiString(set, ref data, SpdrpCompatibleIds, node.InstanceId);
        var ancestors = AncestorIds(node.DevInst).ToArray();
        var usage = ParseUsage(hardwareIds.Concat(compatibleIds));
        var identity = GetHydratedIdentity(node.InstanceId, hardwareIds, compatibleIds, isScopedCandidate);
        return new ControllerDeviceInfo(node.InstanceId, GetContainerId(set, ref data), ancestors.FirstOrDefault(), ancestors,
            GetRegistryString(set, ref data, SpdrpEnumeratorName, node.InstanceId), hardwareIds, compatibleIds,
            GetRegistryString(set, ref data, SpdrpClass, node.InstanceId), GetRegistryString(set, ref data, SpdrpClassGuid, node.InstanceId),
            GetRegistryString(set, ref data, SpdrpService, node.InstanceId),
            identity.VendorId, identity.ProductId, true,
            GetRegistryString(set, ref data, SpdrpFriendlyName, node.InstanceId), usage.Page, usage.Usage);
    }

    private static Dictionary<string, LightDevice> ReadLightweightDevices(IntPtr set)
    {
        var result = new Dictionary<string, LightDevice>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0; TryGetDevice(set, index, out var data); index++)
        {
            var instanceId = GetDeviceInstanceId(set, ref data);
            result[instanceId] = new LightDevice(instanceId, data.DevInst, data);
        }
        return result;
    }

    private static DeviceInfoSet OpenPresentSet()
    {
        var handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (handle == InvalidDeviceInfoSet)
        {
            var error = Marshal.GetLastWin32Error();
            throw new PnpDeviceReadException("SetupDiGetClassDevsW", error);
        }
        return new DeviceInfoSet(handle);
    }

    private static bool TryGetDevice(IntPtr set, uint index, out SpDevinfoData data)
    {
        data = new SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
        if (SetupDiEnumDeviceInfo(set, index, ref data)) return true;
        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNoMoreItems) return false;
        throw new PnpDeviceReadException("SetupDiEnumDeviceInfo", error,
            devInst: data.DevInst == 0 ? null : data.DevInst, operationContext: $"Index={index}");
    }

    private sealed record LightDevice(string InstanceId, uint DevInst, SpDevinfoData Data);
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
        var capacity = 1024;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = new StringBuilder(capacity);
            if (SetupDiGetDeviceInstanceIdW(deviceInfoSet, ref deviceInfo, buffer, buffer.Capacity, out var requiredSize))
                return buffer.ToString();

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorInsufficientBuffer && requiredSize > capacity && requiredSize > 0 && attempt == 0)
            {
                capacity = requiredSize;
                continue;
            }

            throw new PnpDeviceReadException("SetupDiGetDeviceInstanceIdW", error,
                requiredSize: requiredSize > 0 ? (uint)requiredSize : null,
                devInst: deviceInfo.DevInst == 0 ? null : deviceInfo.DevInst);
        }

        throw new InvalidOperationException("Unreachable PnP instance ID buffer state.");
    }

    private static IReadOnlyList<string> GetRegistryMultiString(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property, string instanceId)
    {
        var bytes = GetRegistryProperty(deviceInfoSet, ref deviceInfo, property, instanceId);
        return bytes.Length == 0
            ? []
            : Encoding.Unicode.GetString(bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? GetRegistryString(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property, string instanceId)
    {
        var bytes = GetRegistryProperty(deviceInfoSet, ref deviceInfo, property, instanceId);
        return bytes.Length == 0 ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static byte[] GetRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfo, uint property, string instanceId)
    {
        var bufferSize = 16384;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = new byte[bufferSize];
            if (SetupDiGetDeviceRegistryPropertyW(deviceInfoSet, ref deviceInfo, property, out _, buffer, (uint)buffer.Length, out var requiredSize))
            {
                return buffer[..(int)requiredSize];
            }

            var error = Marshal.GetLastWin32Error();
            if (error is 13 or 1168) return [];
            if (error == ErrorInsufficientBuffer && requiredSize > buffer.Length && requiredSize <= int.MaxValue && attempt == 0)
            {
                bufferSize = (int)requiredSize;
                continue;
            }

            var (propertyName, _) = GetPropertyName(property);
            throw new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", error, property,
                propertyName, requiredSize == 0 ? null : requiredSize, instanceId,
                deviceInfo.DevInst == 0 ? null : deviceInfo.DevInst);
        }

        throw new InvalidOperationException("Unreachable PnP property buffer state.");
    }

    private static bool IsMatchingInstanceId(string instanceId, ushort vendorId, IReadOnlySet<ushort> productIds)
    {
        var identity = ParseVendorProductId([instanceId]);
        return identity.VendorId == vendorId && identity.ProductId is { } productId && productIds.Contains(productId);
    }

    internal static IReadOnlySet<string> SelectScopedInstanceIds(
        IEnumerable<(string InstanceId, uint DevInst)> presentDevices,
        ushort vendorId,
        IReadOnlySet<ushort> productIds,
        Func<uint, IEnumerable<string>> getAncestors)
    {
        ArgumentNullException.ThrowIfNull(presentDevices);
        ArgumentNullException.ThrowIfNull(productIds);
        ArgumentNullException.ThrowIfNull(getAncestors);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in presentDevices)
        {
            if (!IsMatchingInstanceId(device.InstanceId, vendorId, productIds)) continue;
            selected.Add(device.InstanceId);
            foreach (var ancestorId in getAncestors(device.DevInst))
            {
                if (!string.IsNullOrWhiteSpace(ancestorId)) selected.Add(ancestorId);
            }
        }
        return selected;
    }

    internal static (ushort? VendorId, ushort? ProductId) GetHydratedIdentity(
        string instanceId,
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string> compatibleIds,
        bool? isScopedCandidate)
    {
        if (isScopedCandidate == true) return ParseVendorProductId([instanceId]);
        if (isScopedCandidate == false) return (null, null);
        return ParseVendorProductId(hardwareIds.Concat(compatibleIds));
    }

    private static (string Name, uint Id) GetPropertyName(uint property) => property switch
    {
        SpdrpHardwareId => ("SPDRP_HARDWAREID", property),
        SpdrpCompatibleIds => ("SPDRP_COMPATIBLEIDS", property),
        SpdrpService => ("SPDRP_SERVICE", property),
        SpdrpClass => ("SPDRP_CLASS", property),
        SpdrpClassGuid => ("SPDRP_CLASSGUID", property),
        SpdrpFriendlyName => ("SPDRP_FRIENDLYNAME", property),
        SpdrpEnumeratorName => ("SPDRP_ENUMERATOR_NAME", property),
        _ => ($"SPDRP_{property}", property),
    };

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
