using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;
using System.Text.Json;

namespace SteamInputAddonforClaw.Steam;

/// <summary>
/// Ensures Steam's built-in CEF remote-debugging bootstrap marker exists before a future
/// steamwebhelper launch. QamHost remains GamepadUI-session scoped; this is only the persistent Steam startup
/// prerequisite that makes Steam expose its loopback DevTools endpoint without manual launch flags.
/// </summary>
internal static class SteamCefDebugBootstrap
{
    private const string SteamRegistryPath = "Software\\Valve\\Steam";
    private const string SteamPathValueName = "SteamPath";
    private const string InstallPathValueName = "InstallPath";
    internal const string MarkerFileName = ".cef-enable-remote-debugging";

    internal static bool Ensure()
    {
        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(SteamRegistryPath, writable: false);
            var steamDirectory = steamKey?.GetValue(SteamPathValueName) as string;
            if (string.IsNullOrWhiteSpace(steamDirectory))
                steamDirectory = steamKey?.GetValue(InstallPathValueName) as string;

            if (string.IsNullOrWhiteSpace(steamDirectory))
            {
                AppLog.Warn("QAM.Bootstrap", "Steam install path was not found; CEF debugging bootstrap skipped.");
                return false;
            }

            return EnsureForSteamDirectory(steamDirectory);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QAM.Bootstrap", "Steam CEF debugging bootstrap failed; Runtime remains available.", exception);
            return false;
        }
    }

    internal static bool EnsureForSteamDirectory(string steamDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(steamDirectory)) return false;

            var normalizedDirectory = Path.GetFullPath(steamDirectory.Trim().Trim('"'));
            var steamExecutable = Path.Combine(normalizedDirectory, "steam.exe");
            if (!File.Exists(steamExecutable))
            {
                AppLog.Warn(
                    "QAM.Bootstrap",
                    "Steam install path did not contain steam.exe; CEF debugging bootstrap skipped.",
                    null,
                    ("SteamDirectory", normalizedDirectory));
                return false;
            }

            var markerPath = Path.Combine(normalizedDirectory, MarkerFileName);
            if (File.Exists(markerPath))
            {
                AppLog.Info("QAM.Bootstrap", "Steam CEF remote-debugging marker already present.", ("Path", markerPath));
                return true;
            }

            try
            {
                using var marker = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(markerPath))
            {
                // Another same-user process won the create race. The required end state is satisfied.
                AppLog.Info("QAM.Bootstrap", "Steam CEF remote-debugging marker already present.", ("Path", markerPath));
                return true;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OwnershipPath)!);
                File.WriteAllText(OwnershipPath, JsonSerializer.Serialize(new CefMarkerOwnership(normalizedDirectory)));
            }
            catch
            {
                try { File.Delete(markerPath); }
                catch (Exception cleanupException)
                {
                    AppLog.Warn("QAM.Bootstrap", "CEF marker rollback after ownership persistence failure failed.", cleanupException);
                }
                throw;
            }

            AppLog.Info(
                "QAM.Bootstrap",
                "Steam CEF remote-debugging marker created. Restart Steam once if it is already running.",
                ("Path", markerPath));
            return true;
        }
        catch (Exception exception)
        {
            try
            {
                var markerPath = Path.Combine(Path.GetFullPath(steamDirectory.Trim().Trim('\"')), MarkerFileName);
                if (File.Exists(markerPath) && !File.Exists(OwnershipPath)) File.Delete(markerPath);
            }
            catch { }
            AppLog.Warn(
                "QAM.Bootstrap",
                "Steam CEF remote-debugging marker could not be prepared; Runtime remains available.",
                exception,
                ("SteamDirectory", steamDirectory));
            return false;
        }
    }

    internal static bool RemoveOwnedMarker()
    {
        if (!File.Exists(OwnershipPath)) return true;
        try
        {
            var ownership = JsonSerializer.Deserialize<CefMarkerOwnership>(File.ReadAllText(OwnershipPath));
            if (ownership is not { SteamDirectory.Length: > 0 }) return false;
            var markerPath = Path.Combine(ownership.SteamDirectory, MarkerFileName);
            if (File.Exists(markerPath)) File.Delete(markerPath);
            File.Delete(OwnershipPath);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Uninstall", "Owned Steam CEF marker cleanup failed; ownership evidence was preserved.", exception);
            return false;
        }
    }

    internal static Func<string> OwnershipPathProvider { get; set; } = static () => AddonDataPaths.CefMarkerOwnershipPath;
    private static string OwnershipPath => OwnershipPathProvider();

    private sealed record CefMarkerOwnership(string SteamDirectory);
}
