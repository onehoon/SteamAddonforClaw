using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>A candidate Windows HID device interface that matches Gordon's VID/PID/usage.</summary>
internal readonly record struct GordonHidCandidate(string DevicePath, string? InstanceId, uint DevInst, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, int InputReportByteLength);

/// <summary>
/// Resolves the Windows HID device interface path(s) for any currently-present Classic Steam Controller
/// (Gordon) HID collection -- VID <c>0x28DE</c>, PID <c>0x1102</c>, usage page <c>0xFF00</c>, usage
/// <c>0x01</c> (VIIPER's own descriptor: <c>UsagePage{0xff00} Usage{0x01}</c>). Deliberately does not
/// itself decide which candidate is the Addon-owned one -- callers (see
/// <see cref="GordonDPadDiagnosticSession"/>) correlate against the PnP identity already resolved by the
/// existing ownership tracker, and must treat more than one candidate as ambiguous rather than guessing.
/// </summary>
internal interface IGordonHidDevicePathResolver
{
    IReadOnlyList<GordonHidCandidate> FindCandidates();
}

internal sealed class Win32GordonHidDevicePathResolver : IGordonHidDevicePathResolver
{
    internal const ushort GordonVendorId = 0x28DE;
    internal const ushort GordonProductId = 0x1102;
    internal const ushort GordonUsagePage = 0xFF00;
    internal const ushort GordonUsage = 0x01;

    public IReadOnlyList<GordonHidCandidate> FindCandidates()
    {
        var results = new List<GordonHidCandidate>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, NativeConstants.DigcfPresent | NativeConstants.DigcfDeviceInterface);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == NativeConstants.InvalidHandleValue) return results;

        try
        {
            var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>() };
            for (uint index = 0; NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
            {
                var candidate = TryResolveCandidate(deviceInfoSet, ref interfaceData);
                if (candidate is { } value) results.Add(value);
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
        return results;
    }

    private static GordonHidCandidate? TryResolveCandidate(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        if (!TryGetDevicePath(deviceInfoSet, ref interfaceData, out var devicePath, out var deviceInfoData)) return null;

        using var handle = NativeMethods.CreateFile(devicePath, 0, FileShare.Read | FileShare.Write, IntPtr.Zero, FileMode.Open, NativeConstants.FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attributes = new NativeMethods.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<NativeMethods.HIDD_ATTRIBUTES>() };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes)) return null;
        if (attributes.VendorID != GordonVendorId || attributes.ProductID != GordonProductId) return null;

        var usagePage = (ushort)0;
        var usage = (ushort)0;
        var inputReportByteLength = 0;
        if (NativeMethods.HidD_GetPreparsedData(handle, out var preparsedData))
        {
            try
            {
                if (NativeMethods.HidP_GetCaps(preparsedData, out var caps) == NativeConstants.HidpStatusSuccess)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inputReportByteLength = caps.InputReportByteLength;
                }
            }
            finally { NativeMethods.HidD_FreePreparsedData(preparsedData); }
        }
        if (usagePage != GordonUsagePage || usage != GordonUsage) return null;

        var instanceId = TryGetInstanceId(deviceInfoSet, ref deviceInfoData);
        return new GordonHidCandidate(devicePath, instanceId, deviceInfoData.DevInst, attributes.VendorID, attributes.ProductID, usagePage, usage, inputReportByteLength);
    }

    private static bool TryGetDevicePath(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVICE_INTERFACE_DATA interfaceData, out string devicePath, out NativeMethods.SP_DEVINFO_DATA deviceInfoData)
    {
        devicePath = string.Empty;
        deviceInfoData = new NativeMethods.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>() };

        NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        if (requiredSize == 0) return false;

        var buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // The detail struct's first field (cbSize) must be set to the fixed (not variable-length)
            // size of the struct on this platform before the call -- 6 on x64, 5 on x86 -- per the
            // documented SP_DEVICE_INTERFACE_DETAIL_DATA quirk.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, buffer, requiredSize, out _, ref deviceInfoData))
                return false;
            devicePath = Marshal.PtrToStringUni(buffer + 4) ?? string.Empty;
            return !string.IsNullOrEmpty(devicePath);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? TryGetInstanceId(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVINFO_DATA deviceInfoData)
    {
        var buffer = new char[512];
        if (!NativeMethods.SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, buffer, buffer.Length, out var required) || required == 0)
            return null;
        return new string(buffer, 0, required - 1);
    }

    private static class NativeConstants
    {
        internal const uint DigcfPresent = 0x00000002;
        internal const uint DigcfDeviceInterface = 0x00000010;
        internal const FileOptions FileFlagOverlapped = (FileOptions)0x40000000;
        internal const int HidpStatusSuccess = 0x00110000;
        internal static readonly IntPtr InvalidHandleValue = new(-1);
    }

    private static class NativeMethods
    {
        [DllImport("hid.dll")]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetAttributes(Microsoft.Win32.SafeHandles.SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetPreparsedData(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, char[] instanceId, int instanceIdSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode disposition, FileOptions flags, IntPtr template);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            internal int cbSize;
            internal Guid InterfaceClassGuid;
            internal uint Flags;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            internal int cbSize;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDD_ATTRIBUTES
        {
            internal int Size;
            internal ushort VendorID;
            internal ushort ProductID;
            internal ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_CAPS
        {
            internal ushort Usage;
            internal ushort UsagePage;
            internal ushort InputReportByteLength;
            internal ushort OutputReportByteLength;
            internal ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] internal ushort[] Reserved;
            internal ushort NumberLinkCollectionNodes;
            internal ushort NumberInputButtonCaps;
            internal ushort NumberInputValueCaps;
            internal ushort NumberInputDataIndices;
            internal ushort NumberOutputButtonCaps;
            internal ushort NumberOutputValueCaps;
            internal ushort NumberOutputDataIndices;
            internal ushort NumberFeatureButtonCaps;
            internal ushort NumberFeatureValueCaps;
            internal ushort NumberFeatureDataIndices;
        }
    }
}
