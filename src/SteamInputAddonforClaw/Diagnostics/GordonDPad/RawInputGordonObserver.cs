using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>
/// Fallback path for when <see cref="IDirectHidReader"/> cannot open the Gordon device (e.g. Steam or
/// another process holds it in a way that blocks a second reader): observes the same reports passively
/// via the Windows Raw Input API instead. Registers only for Gordon's own usage page/usage, on the
/// existing WinUI main window's HWND, and never alters the registration in a way that could affect how
/// Steam itself receives input (no <c>RIDEV_NOLEGACY</c>/<c>RIDEV_EXCLUDE</c>/<c>RIDEV_REMOVE</c>).
/// </summary>
/// <remarks>
/// Self-contained: rather than requiring <c>MainWindow</c> to forward <c>WM_INPUT</c> from its own
/// message loop (which this codebase has no existing hook for), <see cref="Register"/> classically
/// subclasses the given window (swaps its <c>WNDPROC</c>, forwarding every other message to the original
/// unchanged via <c>CallWindowProc</c>) and restores the original procedure on <see cref="Unregister"/>.
/// This never removes or replaces the window's own message handling -- it only adds one more message
/// (<c>WM_INPUT</c>, 0x00FF) that this observer additionally inspects.
/// </remarks>
internal interface IRawInputGordonObserver : IDisposable
{
    /// <summary>Registers for WM_INPUT on <paramref name="ownerWindowHandle"/>, filtered to devices
    /// matching Gordon's VID/PID/usage; <paramref name="onReport"/> is invoked for each accepted report.
    /// Returns false (does not throw) if registration fails.</summary>
    bool Register(nint ownerWindowHandle, Action<byte[]> onReport);

    /// <summary>Unregisters raw input and restores the window's original WNDPROC. Safe to call multiple
    /// times and if <see cref="Register"/> was never called or failed.</summary>
    void Unregister();
}

internal sealed class Win32RawInputGordonObserver : IRawInputGordonObserver
{
    private const ushort GordonUsagePage = Win32GordonHidDevicePathResolver.GordonUsagePage;
    private const ushort GordonUsage = Win32GordonHidDevicePathResolver.GordonUsage;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RimTypeHid = 2;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceInfo = 0x2000000b;
    private const int GwlpWndproc = -4;
    private const uint WmInput = 0x00FF;

    private nint _windowHandle;
    private nint _originalWndProc;
    private NativeMethods.WndProcDelegate? _subclassProc; // kept rooted while subclassed
    private Action<byte[]>? _onReport;

    public bool Register(nint ownerWindowHandle, Action<byte[]> onReport)
    {
        try
        {
            var device = new NativeMethods.RAWINPUTDEVICE
            {
                UsagePage = GordonUsagePage,
                Usage = GordonUsage,
                // RIDEV_INPUTSINK: receive input even when the owner window is not foreground -- needed
                // because Steam, not this Addon, is normally the foreground/focused consumer of Gordon
                // input. Deliberately not RIDEV_NOLEGACY/RIDEV_EXCLUDE/RIDEV_REMOVE: this must remain a
                // passive, additional observer, never altering how any other application (Steam) receives
                // the same input.
                Flags = RidevInputSink,
                TargetWindow = ownerWindowHandle,
            };
            if (!NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
                return false;

            _onReport = onReport;
            _subclassProc = SubclassWndProc;
            _windowHandle = ownerWindowHandle;
            _originalWndProc = NativeMethods.SetWindowLongPtr(ownerWindowHandle, GwlpWndproc, Marshal.GetFunctionPointerForDelegate(_subclassProc));
            if (_originalWndProc == 0)
            {
                NativeMethods.RegisterRawInputDevices([device with { Flags = RidevRemove, TargetWindow = 0 }], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
                _onReport = null;
                _subclassProc = null;
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private nint SubclassWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmInput)
        {
            try { HandleRawInput(lParam); }
            catch { /* a malformed/unexpected WM_INPUT payload must never crash the window's message loop */ }
        }
        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void HandleRawInput(nint lParam)
    {
        var onReport = _onReport;
        if (onReport is null) return;

        var size = 0u;
        if (NativeMethods.GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>()) != 0) return;
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(lParam, RidInput, buffer, ref size, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>()) != size) return;
            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.Type != RimTypeHid) return;
            if (!IsMatchingDevice(header.Device)) return;

            // RAWHID: dwSizeHid = size of ONE report, dwCount = number of reports packed into this
            // message; a single WM_INPUT can carry more than one report if the device produced several
            // between window messages.
            var hidOffset = Marshal.OffsetOf<NativeMethods.RAWINPUT>(nameof(NativeMethods.RAWINPUT.hid));
            var hid = Marshal.PtrToStructure<NativeMethods.RAWHID>(buffer + (int)hidOffset);
            var reportSize = (int)hid.dwSizeHid;
            var reportCount = (int)hid.dwCount;
            if (reportSize <= 0 || reportCount <= 0) return;

            var dataOffset = hidOffset + Marshal.OffsetOf<NativeMethods.RAWHID>(nameof(NativeMethods.RAWHID.bRawData)).ToInt32();
            for (var i = 0; i < reportCount; i++)
            {
                var reportBuffer = new byte[reportSize];
                Marshal.Copy(buffer + (int)dataOffset + i * reportSize, reportBuffer, 0, reportSize);
                onReport(reportBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsMatchingDevice(nint deviceHandle)
    {
        try
        {
            var size = 0u;
            if (NativeMethods.GetRawInputDeviceInfo(deviceHandle, RidiDeviceInfo, IntPtr.Zero, ref size) != 0) return false;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (NativeMethods.GetRawInputDeviceInfo(deviceHandle, RidiDeviceInfo, buffer, ref size) <= 0) return false;
                var info = Marshal.PtrToStructure<NativeMethods.RID_DEVICE_INFO>(buffer);
                if (info.dwType != RimTypeHid) return false;
                return info.hid.dwVendorId == Win32GordonHidDevicePathResolver.GordonVendorId
                    && info.hid.dwProductId == Win32GordonHidDevicePathResolver.GordonProductId
                    && info.hid.usUsagePage == GordonUsagePage
                    && info.hid.usUsage == GordonUsage;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return false;
        }
    }

    public void Unregister()
    {
        if (_windowHandle != 0 && _originalWndProc != 0)
        {
            try { NativeMethods.SetWindowLongPtr(_windowHandle, GwlpWndproc, _originalWndProc); }
            catch { /* best-effort restore */ }
        }
        try
        {
            var device = new NativeMethods.RAWINPUTDEVICE { UsagePage = GordonUsagePage, Usage = GordonUsage, Flags = RidevRemove, TargetWindow = 0 };
            NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        }
        catch { /* best-effort unregister */ }
        finally
        {
            _windowHandle = 0;
            _originalWndProc = 0;
            _subclassProc = null;
            _onReport = null;
        }
    }

    public void Dispose() => Unregister();

    private static class NativeMethods
    {
        internal delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputData(nint hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetRawInputDeviceInfo(nint deviceHandle, uint command, IntPtr data, ref uint size);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr(nint hWnd, int index, nint newProc);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static extern nint CallWindowProc(nint previousProc, nint hWnd, uint msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTDEVICE
        {
            internal ushort UsagePage;
            internal ushort Usage;
            internal uint Flags;
            internal nint TargetWindow;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTHEADER
        {
            internal uint Type;
            internal uint Size;
            internal nint Device;
            internal nint WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWHID
        {
            internal uint dwSizeHid;
            internal uint dwCount;
            internal byte bRawData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUT
        {
            internal RAWINPUTHEADER header;
            internal RAWHID hid;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RID_DEVICE_INFO_HID
        {
            internal uint dwVendorId;
            internal uint dwProductId;
            internal uint dwVersionNumber;
            internal ushort usUsagePage;
            internal ushort usUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RID_DEVICE_INFO
        {
            internal uint cbSize;
            internal uint dwType;
            internal RID_DEVICE_INFO_HID hid;
        }
    }
}
