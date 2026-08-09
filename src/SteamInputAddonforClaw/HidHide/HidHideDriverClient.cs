using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.HidHide;

// HidHide exposes this documented control device and IOCTL ABI for runtime clients.
internal sealed class HidHideDriverClient : IHidHideClient
{
    private const string ControlDevicePath = @"\\.\HidHide";
    private const uint DeviceType = 32769;
    private const uint MethodBuffered = 0;
    private const uint FileReadData = 0x0001;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorFileNotFound = 2;

    public HidHideInspection Inspect()
    {
        try
        {
            using var device = Open();
            var active = ReadBoolean(device, Ioctl(2052));
            var inverse = ReadBoolean(device, Ioctl(2054));
            var whitelist = ReadMultiString(device, Ioctl(2048));
            var blacklist = ReadMultiString(device, Ioctl(2050));
            var status = !active ? HidHideInspectionStatus.Disabled : inverse ? HidHideInspectionStatus.InverseWhitelist : HidHideInspectionStatus.Available;
            return new(status, new HashSet<string>(whitelist.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase), blacklist);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorFileNotFound)
        {
            return new(HidHideInspectionStatus.NotInstalled, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: exception.Message);
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide driver inspection failed.", exception, ("Action", "DoNotMutate"));
            return new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: exception.Message);
        }
    }

    public bool AddApplication(string executablePath) => UpdateWhitelist(executablePath, add: true);
    public bool RemoveApplication(string executablePath) => UpdateWhitelist(executablePath, add: false);

    private bool UpdateWhitelist(string executablePath, bool add)
    {
        try
        {
            using var device = Open();
            var entries = new HashSet<string>(ReadMultiString(device, Ioctl(2048)).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var changed = add ? entries.Add(Path.GetFullPath(executablePath)) : entries.Remove(Path.GetFullPath(executablePath));
            if (!changed) return true;
            WriteMultiString(device, Ioctl(2049), entries);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide whitelist mutation failed.", exception, ("ExecutablePath", executablePath), ("Action", "PreserveJournal"));
            return false;
        }
    }

    private static SafeFileHandle Open()
    {
        var handle = CreateFile(ControlDevicePath, FileReadData, FileShare.Read | FileShare.Write | FileShare.Delete, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    private static bool ReadBoolean(SafeFileHandle device, uint controlCode)
    {
        var buffer = new byte[1];
        Invoke(device, controlCode, null, buffer);
        return buffer[0] != 0;
    }

    private static string[] ReadMultiString(SafeFileHandle device, uint controlCode)
    {
        if (DeviceIoControl(device, controlCode, null, 0, null, 0, out var bytesNeeded, IntPtr.Zero) || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new byte[bytesNeeded];
        Invoke(device, controlCode, null, buffer);
        return Encoding.Unicode.GetString(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void WriteMultiString(SafeFileHandle device, uint controlCode, IEnumerable<string> entries)
    {
        var buffer = Encoding.Unicode.GetBytes(string.Join('\0', entries.OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)) + "\0\0");
        Invoke(device, controlCode, buffer, null);
    }

    private static void Invoke(SafeFileHandle device, uint controlCode, byte[]? input, byte[]? output)
    {
        if (!DeviceIoControl(device, controlCode, input, (uint)(input?.Length ?? 0), output, (uint)(output?.Length ?? 0), out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static uint Ioctl(uint function) => (DeviceType << 16) | (FileReadData << 14) | (function << 2) | MethodBuffered;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes, FileMode creationDisposition, FileAttributes flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[]? inputBuffer, uint inputBufferSize, byte[]? outputBuffer, uint outputBufferSize, out uint bytesReturned, IntPtr overlapped);
}
