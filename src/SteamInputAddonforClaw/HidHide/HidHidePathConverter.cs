using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.HidHide;

internal interface IHidHidePathConverter
{
    string ToFullImageName(string dosPath);
    string ToDosPath(string fullImageName);
}

internal interface IDosDeviceNameResolver
{
    string Resolve(string driveName);
}

internal sealed class HidHidePathConverter(IDosDeviceNameResolver? resolver = null) : IHidHidePathConverter
{
    private readonly IDosDeviceNameResolver _resolver = resolver ?? new DosDeviceNameResolver();

    public string ToFullImageName(string dosPath)
    {
        var fullPath = Path.GetFullPath(dosPath);
        if (!Path.IsPathFullyQualified(fullPath) || fullPath.StartsWith("\\\\", StringComparison.Ordinal) || Path.GetPathRoot(fullPath) is not { Length: 3 } root || root[1] != ':')
            throw new InvalidDataException("The HidHide application path must be an absolute drive-letter path.");
        var deviceName = _resolver.Resolve(root[..2]);
        if (!deviceName.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The DOS device mapping is not an absolute NT device path.");
        return deviceName.TrimEnd('\\') + fullPath[(root.Length - 1)..];
    }

    public string ToDosPath(string fullImageName)
    {
        if (!fullImageName.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The HidHide full image name is not an NT device path.");
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var drive = $"{letter}:";
            string deviceName;
            try { deviceName = _resolver.Resolve(drive).TrimEnd('\\'); }
            catch { continue; }
            if (fullImageName.StartsWith(deviceName + "\\", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(drive + fullImageName[deviceName.Length..]);
        }
        throw new InvalidDataException("No DOS drive maps to the HidHide full image name.");
    }
}

internal sealed class DosDeviceNameResolver : IDosDeviceNameResolver
{
    public string Resolve(string driveName)
    {
        var buffer = new StringBuilder(32768);
        if (QueryDosDevice(driveName, buffer, buffer.Capacity) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return buffer.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int max);
}
