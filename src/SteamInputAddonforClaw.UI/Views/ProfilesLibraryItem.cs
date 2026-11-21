using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;

namespace SteamInputAddonforClaw.Views;

internal enum ProfilesLibraryFilter
{
    All,
    Steam,
    Xbox,
}

internal sealed class ProfilesLibraryItem : INotifyPropertyChanged
{
    private BitmapImage? _artwork;
    private string? _artworkPurpose;

    private ProfilesLibraryItem(FrontendProfileGameCatalogEntry game)
    {
        SteamGame = game;
        Identity = GetIdentity(game);
    }

    private ProfilesLibraryItem(FrontendXboxGameCatalogEntry game)
    {
        XboxGame = game;
        Identity = GetIdentity(game);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal string Identity { get; }
    internal FrontendProfileGameCatalogEntry? SteamGame { get; private set; }
    internal FrontendXboxGameCatalogEntry? XboxGame { get; private set; }
    internal bool IsXbox => XboxGame is not null;
    public string Name => SteamGame?.Name ?? XboxGame?.DisplayName ?? string.Empty;
    public string PlatformLabel => MainUiStrings.Get(IsXbox ? "Profiles_XboxLabel" : "Profiles_SteamLabel", IsXbox ? "XBOX" : "Steam");
    public string AutomationName => string.Format(CultureInfo.CurrentUICulture,
        MainUiStrings.Get("Profiles_GameAutomationName", "{0}, {1}"), Name, PlatformLabel);
    internal bool Favorite => SteamGame?.Favorite ?? XboxGame?.Favorite ?? false;
    public bool FavoriteStateKnown => SteamGame?.FavoriteStateKnown ?? XboxGame?.FavoriteStateKnown ?? false;
    public string FavoriteGlyph => !FavoriteStateKnown ? "—" : Favorite ? "★" : "☆";
    public string FavoriteAutomationName
    {
        get
        {
            var key = !FavoriteStateKnown
                ? "Profiles_FavoriteUnknownAutomationName"
                : Favorite ? "Profiles_FavoriteRemoveAutomationName" : "Profiles_FavoriteAddAutomationName";
            var fallback = !FavoriteStateKnown
                ? "Favorite state unavailable for {0}"
                : Favorite ? "Remove {0} from favorites" : "Add {0} to favorites";
            return string.Format(CultureInfo.CurrentUICulture, MainUiStrings.Get(key, fallback), Name);
        }
    }
    public BitmapImage? Artwork => _artwork;
    public Visibility PosterVisibility => _artwork is not null && (!IsXbox || _artworkPurpose == "Poster")
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BoxArtVisibility => _artwork is not null && IsXbox && _artworkPurpose == "BoxArt"
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FallbackVisibility => _artwork is null ? Visibility.Visible : Visibility.Collapsed;

    internal static ProfilesLibraryItem FromSteam(FrontendProfileGameCatalogEntry game) => new(game);
    internal static ProfilesLibraryItem FromXbox(FrontendXboxGameCatalogEntry game) => new(game);

    internal static string GetIdentity(FrontendProfileGameCatalogEntry game) =>
        $"steam:{game.Source}:{game.AppId.ToString(CultureInfo.InvariantCulture)}";

    internal static string GetIdentity(FrontendXboxGameCatalogEntry game) => $"xbox:{game.Key}";

    internal void Update(FrontendProfileGameCatalogEntry game)
    {
        if (SteamGame == game) return;
        var oldName = Name;
        var oldFavorite = Favorite;
        var oldFavoriteKnown = FavoriteStateKnown;
        SteamGame = game;
        NotifyIfChanged(nameof(Name), oldName, Name);
        Notify(nameof(AutomationName));
        NotifyFavoriteIfChanged(oldFavorite, oldFavoriteKnown);
    }

    internal void Update(FrontendXboxGameCatalogEntry game)
    {
        if (XboxGame == game) return;
        var oldName = Name;
        var oldFavorite = Favorite;
        var oldFavoriteKnown = FavoriteStateKnown;
        XboxGame = game;
        NotifyIfChanged(nameof(Name), oldName, Name);
        Notify(nameof(AutomationName));
        NotifyFavoriteIfChanged(oldFavorite, oldFavoriteKnown);
    }

    internal void SetArtwork(BitmapImage? artwork, string? purpose)
    {
        if (ReferenceEquals(_artwork, artwork) && _artworkPurpose == purpose) return;
        _artwork = artwork;
        _artworkPurpose = purpose;
        Notify(nameof(Artwork));
        Notify(nameof(PosterVisibility));
        Notify(nameof(BoxArtVisibility));
        Notify(nameof(FallbackVisibility));
    }

    private void NotifyFavoriteIfChanged(bool oldFavorite, bool oldFavoriteKnown)
    {
        if (oldFavorite == Favorite && oldFavoriteKnown == FavoriteStateKnown) return;
        Notify(nameof(Favorite));
        Notify(nameof(FavoriteStateKnown));
        Notify(nameof(FavoriteGlyph));
        Notify(nameof(FavoriteAutomationName));
    }

    private void NotifyIfChanged(string propertyName, string oldValue, string newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            Notify(propertyName);
    }

    private void Notify(string propertyName) => PropertyChanged?.Invoke(this, new(propertyName));
}

internal static class ProfilesCatalogPolicy
{
    internal static IReadOnlyList<string> SynchronizeItems(
        Dictionary<string, ProfilesLibraryItem> itemsByIdentity,
        IReadOnlyList<FrontendProfileGameCatalogEntry> steamGames,
        IReadOnlyList<FrontendXboxGameCatalogEntry> xboxGames)
    {
        var currentIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var game in steamGames)
        {
            var identity = ProfilesLibraryItem.GetIdentity(game);
            currentIdentities.Add(identity);
            if (itemsByIdentity.TryGetValue(identity, out var existing)) existing.Update(game);
            else itemsByIdentity.Add(identity, ProfilesLibraryItem.FromSteam(game));
        }
        foreach (var game in xboxGames)
        {
            var identity = ProfilesLibraryItem.GetIdentity(game);
            currentIdentities.Add(identity);
            if (itemsByIdentity.TryGetValue(identity, out var existing)) existing.Update(game);
            else itemsByIdentity.Add(identity, ProfilesLibraryItem.FromXbox(game));
        }

        var removed = itemsByIdentity.Keys.Where(identity => !currentIdentities.Contains(identity)).ToArray();
        foreach (var identity in removed) itemsByIdentity.Remove(identity);
        return removed;
    }

    internal static IReadOnlyList<ProfilesLibraryItem> FilterAndSort(
        IEnumerable<ProfilesLibraryItem> items,
        ProfilesLibraryFilter filter,
        string? query)
    {
        var normalizedQuery = query?.Trim();
        return items
            .Where(item => filter == ProfilesLibraryFilter.All
                || (filter == ProfilesLibraryFilter.Xbox) == item.IsXbox)
            .Where(item => MatchesSearch(item, normalizedQuery))
            .OrderByDescending(item => item.FavoriteStateKnown && item.Favorite)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.IsXbox ? "xbox" : item.SteamGame!.Source.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool MatchesSearch(ProfilesLibraryItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return true;
        return item.SteamGame is { } steam
            && steam.AppId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsVisibleInFilter(bool isXbox, ProfilesLibraryFilter filter) =>
        filter == ProfilesLibraryFilter.All
        || (filter == ProfilesLibraryFilter.Xbox) == isXbox;

    internal static void ReconcileVisibleCards(
        System.Collections.ObjectModel.ObservableCollection<ProfilesLibraryItem> current,
        IReadOnlyList<ProfilesLibraryItem> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var desiredItem = desired[index];
            if (index < current.Count && ReferenceEquals(current[index], desiredItem)) continue;
            var existingIndex = -1;
            for (var candidate = index + 1; candidate < current.Count; candidate++)
            {
                if (ReferenceEquals(current[candidate], desiredItem))
                {
                    existingIndex = candidate;
                    break;
                }
            }
            if (existingIndex >= 0) current.Move(existingIndex, index);
            else current.Insert(index, desiredItem);
        }
        while (current.Count > desired.Count) current.RemoveAt(current.Count - 1);
    }
}
