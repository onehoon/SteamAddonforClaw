using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>Walks a Windows PnP device's ancestor chain (parent, grandparent, ...) as instance ID
/// strings, so a HID collection can be correlated back to the USB device instance that owns it.
/// Abstracted for testability -- the real walk needs live PnP state that a test cannot fabricate.</summary>
internal interface IDeviceAncestryWalker
{
    IReadOnlyList<string> GetAncestorInstanceIds(uint devInst, int maxDepth = 12);
}

internal sealed class Win32DeviceAncestryWalker : IDeviceAncestryWalker
{
    public IReadOnlyList<string> GetAncestorInstanceIds(uint devInst, int maxDepth = 12)
    {
        var ids = new List<string>();
        var current = devInst;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            if (NativeMethods.CM_Get_Parent(out var parent, current, 0) != 0) break;
            var buffer = new char[512];
            if (NativeMethods.CM_Get_Device_ID(parent, buffer, buffer.Length, 0) != 0) break;
            var nullIndex = Array.IndexOf(buffer, '\0');
            var id = new string(buffer, 0, nullIndex >= 0 ? nullIndex : buffer.Length);
            if (string.IsNullOrEmpty(id)) break;
            ids.Add(id);
            current = parent;
        }
        return ids;
    }

    private static class NativeMethods
    {
        [DllImport("cfgmgr32.dll")]
        internal static extern int CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern int CM_Get_Device_ID(uint devInst, char[] buffer, int bufferLength, uint flags);
    }
}
