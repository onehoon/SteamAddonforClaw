using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Diagnostics.SteamController1304;

internal sealed class WindowsSteamController1304ReadOnlyTransport : ISteamController1304ReadOnlyTransport
{
    private static readonly Guid HidInterfaceGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint ErrorNoMoreItems = 259;
    private const byte WirelessStateCommand = 0xB4;

    public byte[]? RequestConnectionStatus(ControllerDeviceInfo receiver, TimeSpan timeout)
    {
        var candidates = FindCandidates(receiver);
        if (candidates.Count != 1) return null;
        using var handle = Open(candidates[0].Path);
        if (handle.IsInvalid) return null;
        if (!HidD_GetPreparsedData(handle, out var preparsed)) return null;
        try
        {
            if (HidP_GetCaps(preparsed, out var caps) != HidpStatusSuccess || caps.InputReportByteLength == 0 || caps.FeatureReportByteLength == 0)
                return null;
            var input = new byte[caps.InputReportByteLength];
            using var stream = new FileStream(handle, FileAccess.Read, input.Length, isAsync: true);
            using var cancellation = new CancellationTokenSource(timeout);
            var read = stream.ReadExactlyAsync(input, cancellation.Token).AsTask();
            var feature = new byte[caps.FeatureReportByteLength];
            feature[0] = 0;
            if (feature.Length < 2) return null;
            feature[1] = WirelessStateCommand;
            if (!HidD_SetFeature(handle, feature, feature.Length)) return null;
            while (true)
            {
                read.GetAwaiter().GetResult();
                var normalized = Normalize(input);
                if (normalized.Length == SteamController1304ConnectionReportParser.ReportLength && normalized[2] == SteamController1304ConnectionReportParser.MessageTypeWireless)
                    return normalized;
                Array.Clear(input);
                read = stream.ReadExactlyAsync(input, cancellation.Token).AsTask();
            }
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    private static SafeFileHandle Open(string path) => CreateFile(path, 0xC0000000, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileOptions.Asynchronous, IntPtr.Zero);

    private static byte[] Normalize(byte[] report) => report.Length == SteamController1304ConnectionReportParser.ReportLength + 1 && report[1] == SteamController1304ConnectionReportParser.ProtocolMarker && report[2] == 0 ? report[1..] : report;

    private static List<Candidate> FindCandidates(ControllerDeviceInfo receiver)
    {
        var result = new List<Candidate>();
        var interfaceGuid = HidInterfaceGuid;
        var set = SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new SpDevinfoData { cbSize = Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref interfaceGuid, index, IntPtr.Zero))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems) break;
                    continue;
                }
                uint required = 0;
                SetupDiGetDeviceInterfaceDetail(set, IntPtr.Zero, IntPtr.Zero, 0, ref required, ref data);
                if (required == 0) break;
                var buffer = new byte[required];
                BitConverter.TryWriteBytes(buffer.AsSpan(), IntPtr.Size == 8 ? 8 : 5);
                if (!SetupDiGetDeviceInterfaceDetail(set, IntPtr.Zero, buffer, (uint)buffer.Length, ref required, ref data)) continue;
                var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                string? path;
                try { path = Marshal.PtrToStringUni(pinned.AddrOfPinnedObject() + (IntPtr.Size == 8 ? 8 : 4)); }
                finally { pinned.Free(); }
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!IsDescendantOfReceiver(data.DevInst, receiver.InstanceId)) continue;
                using var handle = Open(path);
                if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var preparsed)) continue;
                try
                {
                    if (HidP_GetCaps(preparsed, out var caps) == HidpStatusSuccess && caps.UsagePage == 0xFF00 && caps.Usage == 0x0001)
                        result.Add(new Candidate(path));
                }
                finally { HidD_FreePreparsedData(preparsed); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static bool IsDescendantOfReceiver(uint devInst, string receiverId)
    {
        for (var current = devInst; current != 0; )
        {
            var id = new string('\0', 512);
            if (CM_Get_Device_ID(current, id, id.Length, 0) != 0) return false;
            id = id.TrimEnd('\0');
            if (string.Equals(id, receiverId, StringComparison.OrdinalIgnoreCase)) return true;
            if (CM_Get_Parent(out var parent, current, 0) != 0) return false;
            current = parent;
        }
        return false;
    }

    private sealed record Candidate(string Path);
    private const int HidpStatusSuccess = 0;
    [StructLayout(LayoutKind.Sequential)] private struct SpDevinfoData { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct SpDeviceInterfaceDetailData { public int cbSize; }
    [StructLayout(LayoutKind.Sequential)] private struct HidpCaps { public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved; }
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr data, ref Guid guid, uint index, IntPtr detail);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, IntPtr data, IntPtr detail, uint size, ref uint required, ref SpDevinfoData devInfo);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, IntPtr data, ref SpDeviceInterfaceDetailData detail, uint size, ref uint required, ref SpDevinfoData devInfo);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, IntPtr data, byte[] detail, uint size, ref uint required, ref SpDevinfoData devInfo);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID(uint devInst, string buffer, int length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode disposition, FileOptions flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
}
