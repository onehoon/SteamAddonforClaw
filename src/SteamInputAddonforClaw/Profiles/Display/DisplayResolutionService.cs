using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Profiles.Display;

internal readonly record struct DisplayModeSnapshot(int Width, int Height, int RefreshRate, int BitsPerPixel);

internal sealed class DisplayResolutionService
{
    private const int EnumCurrentSettings = -1, DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000, DmBitsPerPel = 0x40000, CdsTest = 2;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DevMode { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput, dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsEx(string? deviceName, int modeNum, ref DevMode devMode, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DevMode devMode, nint hwnd, int flags, nint param);
    internal bool TryCapture(out DisplayModeSnapshot snapshot) { var mode = new DevMode { dmDeviceName = string.Empty, dmFormName = string.Empty, dmSize = (short)Marshal.SizeOf<DevMode>() }; if (!OperatingSystem.IsWindows() || !EnumDisplaySettingsEx(null, EnumCurrentSettings, ref mode, 0)) { snapshot = default; return false; } snapshot = new(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, mode.dmBitsPerPel); return true; }
    internal bool TryApply(DisplayModeSnapshot current, int width, int height) => Change(current with { Width = width, Height = height }, CdsTest) == 0 && Change(current with { Width = width, Height = height }, 0) == 0;
    internal bool TryRestore(DisplayModeSnapshot original) { if (!TryCapture(out var current)) return false; return current == original || (Change(original, CdsTest) == 0 && Change(original, 0) == 0); }
    private static DevMode ToDev(DisplayModeSnapshot s) => new() { dmDeviceName = string.Empty, dmFormName = string.Empty, dmSize = (short)Marshal.SizeOf<DevMode>(), dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency | DmBitsPerPel, dmPelsWidth = s.Width, dmPelsHeight = s.Height, dmDisplayFrequency = s.RefreshRate, dmBitsPerPel = s.BitsPerPixel };
    private static int Change(DisplayModeSnapshot s, int flags) { var mode = ToDev(s); return ChangeDisplaySettingsEx(null, ref mode, 0, flags, 0); }
}
