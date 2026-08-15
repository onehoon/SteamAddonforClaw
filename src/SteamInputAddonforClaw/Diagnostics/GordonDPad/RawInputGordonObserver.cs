using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>
/// Fallback path for when <see cref="IDirectHidReader"/> cannot open the Gordon device (e.g. Steam or
/// another process holds it in a way that blocks a second reader): observes the same reports passively
/// via the Windows Raw Input API instead. Registers only for Gordon's own usage page/usage, on the
/// existing WinUI main window's HWND, and never alters the registration in a way that could affect how
/// Steam itself receives input. <c>RIDEV_NOLEGACY</c>/<c>RIDEV_EXCLUDE</c> are never used;
/// <c>RIDEV_REMOVE</c> is used only for this observer's own explicit teardown in <see cref="Unregister"/>
/// (with <c>hwndTarget = NULL</c>, as the Win32 contract for that flag requires), which removes only this
/// process's own registration and does not affect any other application.
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
    /// <summary>Registers for WM_INPUT on <paramref name="ownerWindowHandle"/>, filtered to reports from
    /// exactly the device at <paramref name="expectedDevicePath"/> (the same Addon-owned Gordon
    /// <see cref="GordonHidCandidateSelector"/> already selected) -- not merely any VID/PID/usage-matching
    /// device, since a real Steam Controller or a stale Gordon node could otherwise also match and
    /// contaminate the capture. <paramref name="onReport"/> is invoked for each accepted report. Returns
    /// false (does not throw) if registration fails.</summary>
    bool Register(nint ownerWindowHandle, string expectedDevicePath, Action<byte[]> onReport);

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
    private const uint RidiDeviceName = 0x20000007;
    private const int GwlpWndproc = -4;
    private const uint WmInput = 0x00FF;

    private nint _windowHandle;
    private nint _originalWndProc;
    private NativeMethods.WndProcDelegate? _subclassProc; // kept rooted while subclassed
    private Action<byte[]>? _onReport;
    private string? _expectedDeviceNameNormalized;

    public bool Register(nint ownerWindowHandle, string expectedDevicePath, Action<byte[]> onReport)
    {
        try
        {
            _expectedDeviceNameNormalized = NormalizeDeviceName(expectedDevicePath);
            var device = new NativeMethods.RAWINPUTDEVICE
            {
                UsagePage = GordonUsagePage,
                Usage = GordonUsage,
                // RIDEV_INPUTSINK: receive input even when the owner window is not foreground -- needed
                // because Steam, not this Addon, is normally the foreground/focused consumer of Gordon
                // input. Deliberately not RIDEV_NOLEGACY/RIDEV_EXCLUDE: this must remain a passive,
                // additional observer, never altering how any other application (Steam) receives the same
                // input. (RIDEV_REMOVE is used separately, only in Unregister, for this observer's own
                // teardown.)
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
            if (!IsMatchingDevice(header.Device, _expectedDeviceNameNormalized)) return;

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

    /// <summary>
    /// True only for the exact device this observer was <see cref="Register"/>ed for -- checked in two
    /// steps: a cheap VID/PID/usage-page/usage pre-filter (rejects anything that isn't even Gordon-shaped
    /// quickly), then an exact device-path correlation against <paramref name="expectedDeviceNameNormalized"/>
    /// (rejects a second, different Gordon-shaped device -- a real Steam Controller or a stale node -- that
    /// happens to share the same VID/PID/usage).
    /// </summary>
    private static bool IsMatchingDevice(nint deviceHandle, string? expectedDeviceNameNormalized)
    {
        if (expectedDeviceNameNormalized is null) return false;
        try
        {
            // RIDI_DEVICEINFO's output is the fixed-size RID_DEVICE_INFO struct (32 bytes: cbSize +
            // dwType + a 24-byte mouse/keyboard/hid union), not a variable-length buffer -- per the Win32
            // contract, cbSize must already be set to sizeof(RID_DEVICE_INFO) *before* the call, so this
            // allocates the buffer at its known fixed size directly rather than using the two-call
            // size-query pattern (which is for RIDI_DEVICENAME's variable-length string, handled
            // separately below).
            var size = (uint)Marshal.SizeOf<NativeMethods.RID_DEVICE_INFO>();
            var infoBuffer = Marshal.AllocHGlobal((int)size);
            NativeMethods.RID_DEVICE_INFO info;
            try
            {
                Marshal.WriteInt32(infoBuffer, (int)size); // cbSize, at offset 0
                if (NativeMethods.GetRawInputDeviceInfo(deviceHandle, RidiDeviceInfo, infoBuffer, ref size) <= 0) return false;
                info = Marshal.PtrToStructure<NativeMethods.RID_DEVICE_INFO>(infoBuffer);
            }
            finally
            {
                Marshal.FreeHGlobal(infoBuffer);
            }
            if (info.dwType != RimTypeHid) return false;

            return IsExpectedGordonDevice(info.hid.dwVendorId, info.hid.dwProductId, info.hid.usUsagePage, info.hid.usUsage,
                GetDeviceName(deviceHandle), expectedDeviceNameNormalized, nameAlreadyNormalized: true);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pure decision logic, deliberately separated from the P/Invoke calls in <see cref="IsMatchingDevice"/>
    /// so it's directly unit-testable without faking native calls: given a device's already-fetched
    /// identity, is it (a) Gordon-shaped (VID/PID/usage page/usage) at all, and (b) the *specific* device
    /// this observer is looking for. Both checks matter -- (a) alone is not enough to distinguish the
    /// Addon-owned Gordon from a real Steam Controller or a stale node that happens to share the same
    /// VID/PID/usage, which is exactly the scenario this two-step check exists to reject.
    /// </summary>
    internal static bool IsExpectedGordonDevice(uint vendorId, uint productId, ushort usagePage, ushort usage,
        string? deviceName, string? expectedDeviceNameOrNormalized, bool nameAlreadyNormalized)
    {
        if (vendorId != Win32GordonHidDevicePathResolver.GordonVendorId
            || productId != Win32GordonHidDevicePathResolver.GordonProductId
            || usagePage != GordonUsagePage
            || usage != GordonUsage)
        {
            return false;
        }

        var expected = nameAlreadyNormalized ? expectedDeviceNameOrNormalized : NormalizeDeviceName(expectedDeviceNameOrNormalized);
        var name = NormalizeDeviceName(deviceName);
        return name is not null && expected is not null && name == expected;
    }

    /// <summary>RIDI_DEVICENAME is variable-length (a string), unlike RIDI_DEVICEINFO -- this uses the
    /// documented two-call size-query pattern. The returned size is in characters, per the Win32
    /// contract.</summary>
    private static string? GetDeviceName(nint deviceHandle)
    {
        var size = 0u;
        if (NativeMethods.GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, IntPtr.Zero, ref size) != 0) return null;
        if (size == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)size * sizeof(char));
        try
        {
            if (NativeMethods.GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, buffer, ref size) <= 0) return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// RIDI_DEVICENAME returns a kernel-namespace path (<c>\??\HID#VID_28DE&amp;PID_1102...</c>), while
    /// <see cref="GordonHidCandidate.DevicePath"/> (from SetupAPI) is the equivalent Win32-namespace path
    /// (<c>\\?\hid#vid_28de&amp;pid_1102...</c>) -- same device, different prefix convention. Normalizes
    /// both to the same form so they can be compared for an exact match.
    /// </summary>
    private static string? NormalizeDeviceName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var normalized = path.StartsWith(@"\??\", StringComparison.Ordinal) ? @"\\?\" + path[4..] : path;
        return normalized.ToUpperInvariant();
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
            _expectedDeviceNameNormalized = null;
        }
    }

    public void Dispose() => Unregister();

    /// <summary>Test-only: pins the native RID_DEVICE_INFO ABI layout this wrapper depends on (32-byte
    /// total size, hid union member at offset 8) without needing a real raw-input device.</summary>
    internal static int RidDeviceInfoSizeForTests => Marshal.SizeOf<NativeMethods.RID_DEVICE_INFO>();
    internal static int RidDeviceInfoHidOffsetForTests => Marshal.OffsetOf<NativeMethods.RID_DEVICE_INFO>(nameof(NativeMethods.RID_DEVICE_INFO.hid)).ToInt32();

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

        // Native RID_DEVICE_INFO is cbSize + dwType + a union of mouse/keyboard/hid variant structs, not
        // dwType followed directly by the hid variant. The union's size is that of its largest member --
        // RID_DEVICE_INFO_KEYBOARD (6 DWORDs = 24 bytes), not RID_DEVICE_INFO_HID (16 bytes) -- so the
        // total native size is 4 (cbSize) + 4 (dwType) + 24 (union) = 32 bytes. An explicit layout with
        // Size=32 and the hid variant placed at offset 8 matches that regardless of which union member is
        // actually populated (only the hid one is read here, since dwType is checked first).
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        internal struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] internal uint cbSize;
            [FieldOffset(4)] internal uint dwType;
            [FieldOffset(8)] internal RID_DEVICE_INFO_HID hid;
        }
    }
}
