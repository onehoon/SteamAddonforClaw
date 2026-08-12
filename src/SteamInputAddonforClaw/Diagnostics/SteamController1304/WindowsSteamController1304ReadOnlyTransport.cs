using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Diagnostics.SteamController1304;

internal sealed class WindowsSteamController1304ReadOnlyTransport : ISteamController1304ReadOnlyTransport
{
    private static readonly Guid HidInterfaceGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint ErrorNoMoreItems = 259;
    private const byte WirelessStateCommand = 0xB4;

    public byte[]? RequestConnectionStatus(ControllerDeviceInfo receiver, TimeSpan timeout)
    {
        var candidates = FindCandidates(receiver);
        if (candidates is null || candidates.Count == 0) return null;
        var started = Stopwatch.GetTimestamp();
        foreach (var candidate in candidates)
        {
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) break;
            AppLog.Debug("SteamController1304", "Receiver HID candidate query started.",
                ("HidInterface", candidate.Path), ("UsagePage", "0xFF00"), ("Usage", "0x0001"),
                ("InputReportByteLength", candidate.InputReportByteLength), ("OutputReportByteLength", candidate.OutputReportByteLength),
                ("FeatureReportByteLength", candidate.FeatureReportByteLength));
            try
            {
                var report = QueryCandidate(candidate, remaining);
                if (report is not null) return report;
            }
            catch (TimeoutException) when (Stopwatch.GetElapsedTime(started) < timeout) { }
        }
        return null;
    }

    private static byte[]? QueryCandidate(Candidate candidate, TimeSpan timeout)
    {
        using var handle = Open(candidate.Path);
        if (handle.IsInvalid) return null;
        var input = new byte[candidate.InputReportByteLength];
        using var stream = new FileStream(handle, FileAccess.Read, input.Length, isAsync: true);
        using var cancellation = new CancellationTokenSource(timeout);
        var read = stream.ReadExactlyAsync(input, cancellation.Token).AsTask();
        var feature = SteamController1304HidInteropContract.BuildWirelessStateFeatureReport(candidate.FeatureReportByteLength);
        if (feature is null || !HidD_SetFeature(handle, feature, feature.Length)) return null;
        try
        {
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
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw new TimeoutException("Steam Controller receiver wireless-state query timed out."); }
    }

    private static SafeFileHandle Open(string path) => CreateFile(path, 0xC0000000, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileOptions.Asynchronous, IntPtr.Zero);

    private static byte[] Normalize(byte[] report) => report.Length == SteamController1304ConnectionReportParser.ReportLength + 1 && report[1] == SteamController1304ConnectionReportParser.ProtocolMarker && report[2] == 0 ? report[1..] : report;

    private static List<Candidate>? FindCandidates(ControllerDeviceInfo receiver)
    {
        var result = new List<Candidate>();
        var interfaceGuid = HidInterfaceGuid;
        var set = SetupDiGetClassDevs(ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems) break;
                    return null;
                }
                var data = new SpDevinfoData { CbSize = Marshal.SizeOf<SpDevinfoData>() };
                uint required = 0;
                SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, ref required, ref data);
                if (required == 0) return null;
                var buffer = new byte[required];
                BitConverter.TryWriteBytes(buffer.AsSpan(), IntPtr.Size == 8 ? 8 : 5);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, buffer, (uint)buffer.Length, ref required, ref data)) return null;
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
                        if (caps.InputReportByteLength != 0 && caps.FeatureReportByteLength != 0)
                            result.Add(new Candidate(path, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength));
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
            var id = new System.Text.StringBuilder(512);
            if (CM_Get_Device_IDW(current, id, id.Capacity, 0) != 0) return false;
            if (string.Equals(id.ToString(), receiverId, StringComparison.OrdinalIgnoreCase)) return true;
            if (CM_Get_Parent(out var parent, current, 0) != 0) return false;
            current = parent;
        }
        return false;
    }

    private sealed record Candidate(string Path, ushort InputReportByteLength, ushort OutputReportByteLength, ushort FeatureReportByteLength);
    // HIDP_STATUS_SUCCESS is an HID parser NTSTATUS value, not a Win32 BOOL result.
    internal const int HidpStatusSuccess = 0x00110000;
    [StructLayout(LayoutKind.Sequential)] private struct SpDevinfoData { public int CbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct SpDeviceInterfaceData { public int CbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct HidpCaps
    {
        public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices;
    }
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr data, ref Guid guid, uint index, ref SpDeviceInterfaceData detail);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData interfaceData, IntPtr detail, uint size, ref uint required, ref SpDevinfoData devInfo);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData interfaceData, byte[] detail, uint size, ref uint required, ref SpDevinfoData devInfo);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint devInst, System.Text.StringBuilder buffer, int length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode disposition, FileOptions flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
}

internal static class SteamController1304HidInteropContract
{
    internal const int HidpStatusSuccess = 0x00110000;
    internal static byte[]? BuildWirelessStateFeatureReport(int length)
    {
        if (length < 2) return null;
        var report = new byte[length];
        report[1] = 0xB4;
        return report;
    }
}
