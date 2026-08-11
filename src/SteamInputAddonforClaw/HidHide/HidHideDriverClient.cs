using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.HidHide;

internal interface IHidHideNativeApi
{
    IHidHideControlDevice Open(uint desiredAccess);
}

internal interface IHidHideControlDevice : IDisposable
{
    bool DeviceIoControl(uint controlCode, byte[]? input, byte[]? output, out uint bytesReturned, out int errorCode);
}

internal sealed class HidHideDriverClient(IHidHideNativeApi? nativeApi = null, IHidHidePathConverter? pathConverter = null) : IHidHideClient
{
    internal const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeviceType = 32769;
    private const uint FileReadData = 0x0001;
    private readonly IHidHideNativeApi _nativeApi = nativeApi ?? new HidHideNativeApi();
    private readonly IHidHidePathConverter _pathConverter = pathConverter ?? new HidHidePathConverter();

    public HidHideInspection Inspect()
    {
        try
        {
            using var device = _nativeApi.Open(GenericRead);
            var active = ReadBoolean(device, Ioctl(2052));
            var inverse = ReadBoolean(device, Ioctl(2054));
            var rawWhitelist = ReadMultiString(device, Ioctl(2048));
            var blacklist = ReadMultiString(device, Ioctl(2050));
            var normalizedWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fullImageName in rawWhitelist)
            {
                try { normalizedWhitelist.Add(_pathConverter.ToDosPath(fullImageName)); }
                catch (Exception exception) { AppLog.Warn("HidHide", "A HidHide whitelist entry could not be normalized.", exception, ("Action", "PreserveRawEntry")); }
            }
            var status = !active ? HidHideInspectionStatus.Disabled : inverse ? HidHideInspectionStatus.InverseWhitelist : HidHideInspectionStatus.Available;
            return new(status, normalizedWhitelist, blacklist, rawWhitelist);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
        {
            return new(HidHideInspectionStatus.NotInstalled, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: exception.Message);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            AppLog.Warn("HidHide", "HidHide control device access was denied.", exception, ("Operation", "OpenControlDevice"), ("Win32Error", 5), ("Reason", "HidHideControlDeviceAccessDenied"), ("Action", "RemainPassive"));
            return new(HidHideInspectionStatus.AccessDenied, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: exception.Message);
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide driver inspection failed.", exception, ("Action", "DoNotMutate"));
            return new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(StringComparer.OrdinalIgnoreCase), Reason: exception.Message);
        }
    }

    public bool AddApplication(string executablePath) => UpdateWhitelist(executablePath, add: true);
    public bool RemoveApplication(string executablePath) => UpdateWhitelist(executablePath, add: false);
    public bool AddHiddenDevice(string deviceEntry) => UpdateHiddenDevices(deviceEntry, add: true);
    public bool RemoveHiddenDevice(string deviceEntry) => UpdateHiddenDevices(deviceEntry, add: false);

    private bool UpdateHiddenDevices(string deviceEntry, bool add)
    {
        if (string.IsNullOrWhiteSpace(deviceEntry)) return false;
        try
        {
            var inspection = Inspect();
            if (!inspection.IsConfigurationReadable) return false;
            var entries = (inspection.HiddenDeviceEntries ?? []).ToList();
            var index = entries.FindIndex(entry => string.Equals(entry, deviceEntry, StringComparison.OrdinalIgnoreCase));
            if (add ? index >= 0 : index < 0) return true;
            if (add) entries.Add(deviceEntry);
            else entries.RemoveAt(index);

            using var device = _nativeApi.Open(GenericRead | GenericWrite);
            WriteMultiString(device, Ioctl(2051), entries);
            var verification = Inspect();
            if (!verification.IsConfigurationReadable) return false;
            var present = (verification.HiddenDeviceEntries ?? [])
                .Any(entry => string.Equals(entry, deviceEntry, StringComparison.OrdinalIgnoreCase));
            return add == present;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide hidden-device mutation failed.", exception,
                ("DeviceEntry", deviceEntry), ("Action", "PreserveJournal"));
            return false;
        }
    }

    private bool UpdateWhitelist(string executablePath, bool add)
    {
        try
        {
            var fullImageName = _pathConverter.ToFullImageName(executablePath);
            using var device = _nativeApi.Open(GenericRead);
            var entries = ReadMultiString(device, Ioctl(2048));
            var changed = add
                ? !entries.Contains(fullImageName, StringComparer.OrdinalIgnoreCase)
                : entries.RemoveAll(entry => string.Equals(entry, fullImageName, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!changed) return true;
            if (add) entries.Add(fullImageName);
            WriteMultiString(device, Ioctl(2049), entries);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HidHide", "HidHide whitelist mutation failed.", exception, ("ExecutablePath", executablePath), ("Action", "PreserveJournal"));
            return false;
        }
    }

    internal static bool ReadBoolean(IHidHideControlDevice device, uint controlCode)
    {
        var buffer = new byte[1];
        Invoke(device, controlCode, null, buffer, out var returned);
        if (returned != 1) throw new InvalidDataException("The HidHide boolean response length is invalid.");
        return buffer[0] != 0;
    }

    internal static List<string> ReadMultiString(IHidHideControlDevice device, uint controlCode)
    {
        if (!device.DeviceIoControl(controlCode, null, null, out var bytesNeeded, out var errorCode))
            throw new Win32Exception(errorCode);
        if (bytesNeeded == 0 || (bytesNeeded & 1) != 0) throw new InvalidDataException("The HidHide MULTI_SZ size is invalid.");
        var buffer = new byte[bytesNeeded];
        Invoke(device, controlCode, null, buffer, out var returned);
        if (returned == 0 || returned > bytesNeeded || (returned & 1) != 0) throw new InvalidDataException("The HidHide MULTI_SZ response length is invalid.");
        return DeserializeMultiString(buffer.AsSpan(0, (int)returned));
    }

    internal static void WriteMultiString(IHidHideControlDevice device, uint controlCode, IEnumerable<string> entries)
    {
        var buffer = SerializeMultiString(entries);
        Invoke(device, controlCode, buffer, null, out _);
    }

    internal static byte[] SerializeMultiString(IEnumerable<string> entries)
    {
        var values = entries.ToArray();
        var multiString = values.Length == 0
            ? "\0"
            : string.Join('\0', values) + "\0\0";
        return System.Text.Encoding.Unicode.GetBytes(multiString);
    }

    internal static List<string> DeserializeMultiString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || (bytes.Length & 1) != 0)
            throw new InvalidDataException("The HidHide MULTI_SZ length is invalid.");

        var text = new System.Text.UnicodeEncoding(false, false, true).GetString(bytes);
        if (text[^1] != '\0')
            throw new InvalidDataException("The HidHide MULTI_SZ is not NUL terminated.");

        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void Invoke(IHidHideControlDevice device, uint controlCode, byte[]? input, byte[]? output, out uint bytesReturned)
    {
        if (!device.DeviceIoControl(controlCode, input, output, out bytesReturned, out var errorCode)) throw new Win32Exception(errorCode);
    }

    // HidHide control contract: hidden-device list write (SET_BLACKLIST), verified against
    // DS4Windows HidHideAPIDevice.cs, reference SHA 3579450dc8f50a74d9532e711249589c732c460b.
    private static uint Ioctl(uint function) => (DeviceType << 16) | (FileReadData << 14) | (function << 2);
}

internal sealed class HidHideNativeApi : IHidHideNativeApi
{
    public IHidHideControlDevice Open(uint desiredAccess)
    {
        var handle = CreateFile(@"\\.\HidHide", desiredAccess, FileShare.Read | FileShare.Write | FileShare.Delete, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new HidHideControlDevice(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode disposition, FileAttributes flags, IntPtr template);
}

internal sealed class HidHideControlDevice(SafeFileHandle handle) : IHidHideControlDevice
{
    public bool DeviceIoControl(uint controlCode, byte[]? input, byte[]? output, out uint bytesReturned, out int errorCode)
    {
        var result = NativeDeviceIoControl(handle, controlCode, input, (uint)(input?.Length ?? 0), output, (uint)(output?.Length ?? 0), out bytesReturned, IntPtr.Zero);
        errorCode = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }
    public void Dispose() => handle.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeDeviceIoControl(SafeFileHandle device, uint code, byte[]? input, uint inputLength, byte[]? output, uint outputLength, out uint bytesReturned, IntPtr overlapped);
}
