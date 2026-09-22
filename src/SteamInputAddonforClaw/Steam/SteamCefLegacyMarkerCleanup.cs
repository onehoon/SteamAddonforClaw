using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;
using System.Text.Json;

namespace SteamInputAddonforClaw.Steam;

/// <summary>Removes only the legacy CEF marker previously created by this Addon.
/// New product code never creates or prepares the marker.</summary>
internal static class SteamCefLegacyMarkerCleanup
{
    internal const string MarkerFileName = ".cef-enable-remote-debugging";

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
            AppLog.Warn("Startup", "Owned legacy Steam CEF marker cleanup failed; ownership evidence was preserved.", exception);
            return false;
        }
    }

    internal static Func<string> OwnershipPathProvider { get; set; } = static () => AddonDataPaths.CefMarkerOwnershipPath;
    private static string OwnershipPath => OwnershipPathProvider();

    private sealed record CefMarkerOwnership(string SteamDirectory);
}
