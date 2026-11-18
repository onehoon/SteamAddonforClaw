using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Display;

internal readonly record struct DisplayScaleSnapshot(
    string PrimaryGdiDeviceName,
    string MonitorDevicePath,
    int OriginalRelativeScale);

internal interface IDisplayScalingService
{
    bool TryCapture(out DisplayScaleSnapshot snapshot);
    bool TrySetTo100(DisplayScaleSnapshot original);
    bool TryRestore(DisplayScaleSnapshot original);
}

/// <summary>
/// Uses the undocumented DisplayConfig -3/-4 packets for the desktop scale value. The packets are
/// deliberately isolated here, and every mutation is checked through -3 packet readback.
/// Unsupported systems fail closed.
/// </summary>
internal sealed class DisplayScalingService : IDisplayScalingService
{
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint MonitorInfoPrimary = 0x00000001;
    private const int GetDpiScaleType = -3;
    private const int SetDpiScaleType = -4;
    private const int DisplayConfigModeInfoSize = 64;
    private const int MaxDisplayPaths = 256;
    private const int MaxDisplayModes = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { internal uint LowPart; internal int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIndex;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational { internal uint Numerator; internal uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIndex;
        internal uint OutputTechnology;
        internal uint Rotation;
        internal uint Scaling;
        internal Rational RefreshRate;
        internal uint ScanLineOrdering;
        internal uint TargetAvailable;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        internal DisplayConfigPathSourceInfo Source;
        internal DisplayConfigPathTargetInfo Target;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        internal int Type;
        internal uint Size;
        internal Luid AdapterId;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        internal DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        internal DeviceInfoHeader Header;
        internal uint Flags;
        internal uint OutputTechnology;
        internal ushort EdidManufactureId;
        internal ushort EdidProductCodeId;
        internal uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DpiScaleGetInfo
    {
        internal NativeHeader Header;
        internal int MinimumRelativeScale;
        internal int CurrentRelativeScale;
        internal int MaximumRelativeScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DpiScaleSetInfo
    {
        internal NativeHeader Header;
        internal int RelativeScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeHeader
    {
        internal int Type;
        internal uint Size;
        internal uint AdapterLowPart;
        internal int AdapterHighPart;
        internal uint Id;
    }

    private readonly record struct PrimaryPath(string GdiDeviceName, string MonitorDevicePath, Luid AdapterId, uint SourceId);
    private readonly record struct ScaleReadback(PrimaryPath Path, DpiScaleGetInfo Info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths,
        ref uint modeCount, IntPtr modes, IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", SetLastError = true)]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", SetLastError = true)]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", SetLastError = true)]
    private static extern int DisplayConfigGetDeviceInfo(ref DpiScaleGetInfo request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo", SetLastError = true)]
    private static extern int DisplayConfigSetDeviceInfo(ref DpiScaleSetInfo request);

    internal static int DpiScaleGetInfoSize => Marshal.SizeOf<DpiScaleGetInfo>();
    internal static int DpiScaleSetInfoSize => Marshal.SizeOf<DpiScaleSetInfo>();
    internal static int SourceDeviceNameSize => Marshal.SizeOf<SourceDeviceName>();

    public bool TryCapture(out DisplayScaleSnapshot snapshot)
    {
        snapshot = default;
        if (!OperatingSystem.IsWindows() || !TryReadPrimaryScale(out var readback)) return false;
        snapshot = new(readback.Path.GdiDeviceName, readback.Path.MonitorDevicePath,
            readback.Info.CurrentRelativeScale);
        return true;
    }

    public bool TrySetTo100(DisplayScaleSnapshot original)
    {
        if (!TryReadMatchingScale(original, out var current)) return false;
        var minimum = current.Info.MinimumRelativeScale;
        if (current.Info.CurrentRelativeScale == minimum)
            return true;

        if (!TrySetRelativeScale(current.Path, minimum)) return false;
        if (!TryReadMatchingScale(original, out var verified)
            || verified.Info.CurrentRelativeScale != verified.Info.MinimumRelativeScale)
        {
            AppLog.Warn("Profiles.Display", "Display scaling to 100% failed readback verification.", null,
                ("GdiDevice", original.PrimaryGdiDeviceName));
            return false;
        }
        return true;
    }

    public bool TryRestore(DisplayScaleSnapshot original)
    {
        if (!TryReadMatchingScale(original, out var current)) return false;
        if (original.OriginalRelativeScale < current.Info.MinimumRelativeScale
            || original.OriginalRelativeScale > current.Info.MaximumRelativeScale)
        {
            AppLog.Warn("Profiles.Display", "Captured display scale is outside the current supported range; restoration was left pending.", null,
                ("GdiDevice", original.PrimaryGdiDeviceName),
                ("CapturedRelativeScale", original.OriginalRelativeScale));
            return false;
        }
        if (current.Info.CurrentRelativeScale == original.OriginalRelativeScale)
            return true;

        if (!TrySetRelativeScale(current.Path, original.OriginalRelativeScale)) return false;
        if (!TryReadMatchingScale(original, out var verified)
            || verified.Info.CurrentRelativeScale != original.OriginalRelativeScale)
        {
            AppLog.Warn("Profiles.Display", "Captured display scaling failed readback verification; recovery remains pending.", null,
                ("GdiDevice", original.PrimaryGdiDeviceName));
            return false;
        }
        return true;
    }

    private static bool TrySetRelativeScale(PrimaryPath path, int relativeScale)
    {
        var request = new DpiScaleSetInfo
        {
            Header = CreateNativeHeader(SetDpiScaleType, Marshal.SizeOf<DpiScaleSetInfo>(), path.AdapterId, path.SourceId),
            RelativeScale = relativeScale,
        };
        if (Marshal.SizeOf<DpiScaleSetInfo>() != 0x18)
        {
            AppLog.Error("Profiles.Display", "Unexpected native DPI set packet size; scaling was not changed.", null,
                ("PacketSize", Marshal.SizeOf<DpiScaleSetInfo>()));
            return false;
        }
        var result = DisplayConfigSetDeviceInfo(ref request);
        if (result == 0) return true;
        AppLog.Warn("Profiles.Display", "Windows rejected the undocumented display scaling request.", null,
            ("GdiDevice", path.GdiDeviceName), ("Status", result));
        return false;
    }

    private static bool TryReadMatchingScale(DisplayScaleSnapshot expected, out ScaleReadback readback)
    {
        if (!TryReadPrimaryScale(out readback)) return false;
        if (string.Equals(readback.Path.GdiDeviceName, expected.PrimaryGdiDeviceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(readback.Path.MonitorDevicePath, expected.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))
            return true;

        AppLog.Warn("Profiles.Display", "Primary display identity changed; captured scaling was not applied to another display.", null,
            ("ExpectedGdiDevice", expected.PrimaryGdiDeviceName), ("CurrentGdiDevice", readback.Path.GdiDeviceName));
        readback = default;
        return false;
    }

    private static bool TryReadPrimaryScale(out ScaleReadback readback)
    {
        readback = default;
        if (!TryGetPrimaryPath(out var path)) return false;

        var info = new DpiScaleGetInfo
        {
            Header = CreateNativeHeader(GetDpiScaleType, Marshal.SizeOf<DpiScaleGetInfo>(), path.AdapterId, path.SourceId),
        };
        if (Marshal.SizeOf<DpiScaleGetInfo>() != 0x20)
        {
            AppLog.Error("Profiles.Display", "Unexpected native DPI query packet size; scaling is unavailable.", null,
                ("PacketSize", Marshal.SizeOf<DpiScaleGetInfo>()));
            return false;
        }
        var result = DisplayConfigGetDeviceInfo(ref info);
        if (result != 0 || info.MinimumRelativeScale > info.CurrentRelativeScale
            || info.CurrentRelativeScale > info.MaximumRelativeScale)
        {
            AppLog.Warn("Profiles.Display", "Windows could not provide a valid display scaling snapshot.", null,
                ("GdiDevice", path.GdiDeviceName), ("Status", result));
            return false;
        }
        readback = new(path, info);
        return true;
    }

    private static bool TryGetPrimaryPath(out PrimaryPath primaryPath)
    {
        primaryPath = default;
        IntPtr primaryMonitor = IntPtr.Zero;
        var primaryDeviceName = string.Empty;
        var callback = new MonitorEnumProc((monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx
            {
                Size = (uint)Marshal.SizeOf<MonitorInfoEx>(),
                DeviceName = string.Empty,
            };
            if (GetMonitorInfo(monitor, ref info) && (info.Flags & MonitorInfoPrimary) != 0)
            {
                primaryMonitor = monitor;
                primaryDeviceName = info.DeviceName;
                return true;
            }
            return true;
        });
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)
            || primaryMonitor == IntPtr.Zero || string.IsNullOrWhiteSpace(primaryDeviceName))
            return false;

        var status = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
        if (status != 0 || pathCount is 0 or > MaxDisplayPaths || modeCount > MaxDisplayModes)
        {
            AppLog.Warn("Profiles.Display", "The active display paths could not be enumerated for DPI scaling.", null,
                ("GdiDevice", primaryDeviceName), ("Status", status));
            return false;
        }
        var pathSize = Marshal.SizeOf<DisplayConfigPathInfo>();
        if (pathSize != 72) return false;
        var paths = IntPtr.Zero;
        var modes = IntPtr.Zero;
        try
        {
            paths = Marshal.AllocHGlobal(checked((int)pathCount * pathSize));
            modes = Marshal.AllocHGlobal(checked((int)Math.Max(1, modeCount) * DisplayConfigModeInfoSize));
            var returnedPaths = pathCount;
            var returnedModes = modeCount;
            status = QueryDisplayConfig(QdcOnlyActivePaths, ref returnedPaths, paths, ref returnedModes, modes, IntPtr.Zero);
            if (status != 0 || returnedPaths > pathCount) return false;

            PrimaryPath? match = null;
            for (var i = 0; i < returnedPaths; i++)
            {
                var path = Marshal.PtrToStructure<DisplayConfigPathInfo>(IntPtr.Add(paths, checked((int)i * pathSize)));
                var source = new SourceDeviceName
                {
                    Header = CreateDeviceInfoHeader(1, Marshal.SizeOf<SourceDeviceName>(), path.Source.AdapterId, path.Source.Id),
                    ViewGdiDeviceName = string.Empty,
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0
                    || !string.Equals(source.ViewGdiDeviceName, primaryDeviceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (match is not null) return false;
                var target = new TargetDeviceName
                {
                    Header = CreateDeviceInfoHeader(2, Marshal.SizeOf<TargetDeviceName>(), path.Target.AdapterId, path.Target.Id),
                    MonitorFriendlyDeviceName = string.Empty,
                    MonitorDevicePath = string.Empty,
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0
                    || string.IsNullOrWhiteSpace(target.MonitorDevicePath))
                    return false;
                match = new PrimaryPath(primaryDeviceName, target.MonitorDevicePath,
                    path.Source.AdapterId, path.Source.Id);
            }
            if (match is not { } found) return false;
            primaryPath = found;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Profiles.Display", "Active primary display identity could not be resolved for DPI scaling.", exception,
                ("GdiDevice", primaryDeviceName));
            return false;
        }
        finally
        {
            if (paths != IntPtr.Zero) Marshal.FreeHGlobal(paths);
            if (modes != IntPtr.Zero) Marshal.FreeHGlobal(modes);
        }
    }

    private static DeviceInfoHeader CreateDeviceInfoHeader(int type, int size, Luid adapterId, uint id) => new()
    {
        Type = type,
        Size = (uint)size,
        AdapterId = adapterId,
        Id = id,
    };

    private static NativeHeader CreateNativeHeader(int type, int size, Luid adapterId, uint id) => new()
    {
        Type = type,
        Size = (uint)size,
        AdapterLowPart = adapterId.LowPart,
        AdapterHighPart = adapterId.HighPart,
        Id = id,
    };
}
