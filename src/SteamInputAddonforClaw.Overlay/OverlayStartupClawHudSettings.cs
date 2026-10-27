using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

internal sealed record OverlayStartupClawHudSettings(
    bool AddonDesiredEnabled,
    FrontendClawHudFont Font,
    int SizeOffset)
{
    private const string DataDirectoryName = "SteamInputAddonforClaw-Data";
    private const string LogDirectoryName = "logs";
    private const string IniSection = "HUD";
    private const string DefaultFontToken = "SegoeUIVariable";

    internal static OverlayStartupClawHudSettings Default { get; } = new(
        false,
        FrontendClawHudFont.SegoeUiVariable,
        0);

    internal static OverlayStartupClawHudSettings Load(string? logDirectoryPath)
    {
        string? localAppDataPath = null;
        try { localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        catch (Exception exception) when (IsBootstrapFailure(exception)) { }
        return Load(logDirectoryPath, localAppDataPath);
    }

    internal static OverlayStartupClawHudSettings Load(string? logDirectoryPath, string? localAppDataPath)
    {
        var enabled = ReadAddonDesiredEnabled(logDirectoryPath);
        var font = FrontendClawHudFont.SegoeUiVariable;
        var sizeOffset = 0;

        try
        {
            if (!string.IsNullOrWhiteSpace(localAppDataPath))
            {
                var iniPath = Path.Combine(localAppDataPath, "ClawHUD", "settings.ini");
                var fontToken = ReadProfileValue(IniSection, "Font", DefaultFontToken, iniPath);
                font = string.Equals(fontToken, DefaultFontToken, StringComparison.Ordinal)
                    ? FrontendClawHudFont.SegoeUiVariable
                    : FrontendClawHudFont.Unispace;

                var rawSize = ReadProfileValue(IniSection, "Size", "0", iniPath);
                if (int.TryParse(rawSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
                    sizeOffset = Math.Clamp(parsedSize, -2, 2);
            }
        }
        catch (Exception exception) when (IsBootstrapFailure(exception))
        {
            // Startup geometry is best-effort; malformed or unavailable user settings must not
            // prevent the Runtime-owned Overlay from starting.
        }

        return new(enabled, font, sizeOffset);
    }

    internal int ResolveTopInsetPx(
        bool? authoritativeEnabled,
        FrontendClawHudSettingsSnapshot? authoritativeSettings,
        FrontendClawHudFont? pendingFont = null,
        int? pendingSizeOffset = null) =>
        OverlayWindowGeometry.ClawHudTopInsetPx(
            authoritativeEnabled ?? AddonDesiredEnabled,
            pendingFont ?? authoritativeSettings?.Font ?? Font,
            pendingSizeOffset ?? authoritativeSettings?.HudSizeOffset ?? SizeOffset);

    private static bool ReadAddonDesiredEnabled(string? logDirectoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logDirectoryPath))
                return false;

            var fullLogDirectoryPath = Path.GetFullPath(logDirectoryPath);
            if (!string.Equals(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(fullLogDirectoryPath)),
                    LogDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            var dataRoot = Directory.GetParent(fullLogDirectoryPath);
            if (dataRoot is null || !string.Equals(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(dataRoot.FullName)),
                    DataDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            var settingsPath = Path.Combine(dataRoot.FullName, "settings.json");
            if (!File.Exists(settingsPath))
                return false;

            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ClawHudEnabled", out var enabled)
                && enabled.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (IsBootstrapFailure(exception))
        {
            return false;
        }
    }

    private static string ReadProfileValue(string section, string key, string defaultValue, string iniPath)
    {
        var buffer = new StringBuilder(128);
        _ = GetPrivateProfileStringW(section, key, defaultValue, buffer, (uint)buffer.Capacity, iniPath);
        return buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPrivateProfileStringW")]
    private static extern uint GetPrivateProfileStringW(
        string section,
        string key,
        string defaultValue,
        StringBuilder returnedString,
        uint size,
        string fileName);

    private static bool IsBootstrapFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or SecurityException or JsonException or DllNotFoundException or EntryPointNotFoundException;
}
