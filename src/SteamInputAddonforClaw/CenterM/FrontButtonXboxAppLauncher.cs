using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal static class FrontButtonXboxAppLauncher
{
    internal const string XboxAppAumid = "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    private static readonly Guid ActivationManagerClsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    internal static void Launch()
    {
        var activationType = Type.GetTypeFromCLSID(ActivationManagerClsid, throwOnError: true)!;
        var instance = Activator.CreateInstance(activationType)
            ?? throw new InvalidOperationException("Windows application activation manager was unavailable.");

        try
        {
            var manager = (IApplicationActivationManager)instance;
            var hr = manager.ActivateApplication(XboxAppAumid, null, ActivateOptions.None, out var processId);
            Marshal.ThrowExceptionForHR(hr);

            AppLog.Info(
                "FrontButtons.XboxApp",
                "Xbox app activation requested.",
                ("Aumid", XboxAppAumid),
                ("ProcessId", processId));
        }
        finally
        {
            if (Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);
    }

    private enum ActivateOptions
    {
        None = 0
    }
}
