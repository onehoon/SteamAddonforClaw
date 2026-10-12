namespace SteamInputAddonforClaw.Contracts.Frontend;

public sealed record ShortcutIconChoice(string Name, string Glyph);

/// <summary>Named Segoe Fluent Icons available to Shortcut tiles and their action defaults.</summary>
public static class ShortcutIconCatalog
{
    public const string FallbackGlyph = "\uE71D";

    private const string SettingsGlyph = "\uE713";
    private const string VideoGlyph = "\uE714";
    private const string MailGlyph = "\uE715";
    private const string ShopGlyph = "\uE719";
    private const string MicrophoneGlyph = "\uE720";
    private const string SearchGlyph = "\uE721";
    private const string CameraGlyph = "\uE722";
    private const string LinkGlyph = "\uE71B";
    private const string FavoriteGlyph = "\uE734";
    private const string SaveGlyph = "\uE74E";
    private const string CloudGlyph = "\uE753";
    private const string CommandPromptGlyph = "\uE756";
    private const string KeyboardGlyph = "\uE765";
    private const string PlayGlyph = "\uE768";
    private const string SystemGlyph = "\uE770";
    private const string DevicesGlyph = "\uE772";
    private const string GlobeGlyph = "\uE774";
    private const string ContactGlyph = "\uE77B";
    private const string CalendarGlyph = "\uE787";
    private const string PhotoCollectionGlyph = "\uE7AA";
    private const string PackageGlyph = "\uE7B8";
    private const string TaskViewGlyph = "\uE7C4";
    private const string BrowsePhotosGlyph = "\uE7C5";
    private const string PowerButtonGlyph = "\uE7E8";
    private const string TvMonitorGlyph = "\uE7F4";
    private const string SpeakersGlyph = "\uE7F5";
    private const string HeadphoneGlyph = "\uE7F6";
    private const string LaptopGlyph = "\uE7F8";
    private const string GameGlyph = "\uE7FC";
    private const string FolderGlyph = "\uE838";
    private const string XboxConsoleGlyph = "\uE990";

    private static readonly ShortcutIconChoice[] ChoiceArray =
    [
        new("All apps", FallbackGlyph),
        new("Settings", SettingsGlyph),
        new("Video", VideoGlyph),
        new("Mail", MailGlyph),
        new("Shop", ShopGlyph),
        new("Microphone", MicrophoneGlyph),
        new("Search", SearchGlyph),
        new("Camera", CameraGlyph),
        new("Link", LinkGlyph),
        new("Favorite", FavoriteGlyph),
        new("Save", SaveGlyph),
        new("Cloud", CloudGlyph),
        new("Command prompt", CommandPromptGlyph),
        new("Keyboard", KeyboardGlyph),
        new("Play", PlayGlyph),
        new("System", SystemGlyph),
        new("Devices", DevicesGlyph),
        new("Globe", GlobeGlyph),
        new("Contact", ContactGlyph),
        new("Calendar", CalendarGlyph),
        new("Photo collection", PhotoCollectionGlyph),
        new("Package", PackageGlyph),
        new("Task view", TaskViewGlyph),
        new("Browse photos", BrowsePhotosGlyph),
        new("Power", PowerButtonGlyph),
        new("TV / monitor", TvMonitorGlyph),
        new("Speakers", SpeakersGlyph),
        new("Headphones", HeadphoneGlyph),
        new("Laptop", LaptopGlyph),
        new("Game", GameGlyph),
        new("Folder open", FolderGlyph),
        new("Xbox console", XboxConsoleGlyph),
    ];

    private static readonly IReadOnlyList<ShortcutIconChoice> ReadOnlyChoices = Array.AsReadOnly(ChoiceArray);
    private static readonly IReadOnlyDictionary<string, ShortcutIconChoice> ChoicesByGlyph =
        ChoiceArray.ToDictionary(choice => choice.Glyph, StringComparer.Ordinal);

    public static IReadOnlyList<ShortcutIconChoice> Choices => ReadOnlyChoices;

    public static string GetDefaultGlyph(FrontendShortcutEditorActionKind kind) => kind switch
    {
        FrontendShortcutEditorActionKind.ScreenshotFullscreen => CameraGlyph,
        FrontendShortcutEditorActionKind.SteamClient => GameGlyph,
        FrontendShortcutEditorActionKind.SteamBigPicture => TvMonitorGlyph,
        FrontendShortcutEditorActionKind.XboxApp => XboxConsoleGlyph,
        FrontendShortcutEditorActionKind.PowerShell => CommandPromptGlyph,
        FrontendShortcutEditorActionKind.Url => GlobeGlyph,
        _ => FallbackGlyph,
    };

    public static string GetEffectiveGlyph(FrontendShortcutEditorActionKind kind, string? overrideGlyph) =>
        TryGetChoice(overrideGlyph, out _) ? overrideGlyph! : GetDefaultGlyph(kind);

    public static bool TryGetChoice(string? glyph, out ShortcutIconChoice choice)
    {
        if (glyph is { Length: 1 } && ChoicesByGlyph.TryGetValue(glyph, out var found))
        {
            choice = found;
            return true;
        }

        choice = null!;
        return false;
    }

    public static bool IsKnownGlyph(string? glyph) => TryGetChoice(glyph, out _);

    public static string GetEffectiveName(FrontendShortcutEditorActionKind kind, string? overrideGlyph) =>
        TryGetChoice(overrideGlyph, out var selected)
            ? selected.Name
            : TryGetChoice(GetDefaultGlyph(kind), out var fallback) ? fallback.Name : "All apps";
}
