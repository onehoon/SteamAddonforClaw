using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Globalization;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.FrontendTransport.XboxArtwork;
using SteamInputAddonforClaw.Overlay.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Windows.Foundation;
using Windows.Storage;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private const string NoActiveGameMessage = "No game is currently running. Start a game to configure its profile.";
    private const string NoActiveGameDisplayMessage = "No game is currently running.\nStart a game to configure its profile.";
    private const string UnavailableProfileMessage = "Profile settings are unavailable.";
    private const int ProfileArtworkMaximumAttempts = 2;
    private static readonly TimeSpan ProfileArtworkRetryDelay = TimeSpan.FromMilliseconds(750);

    private FrameworkElement? _profileDetailRoot;
    private TextBlock? _profileStatusMessage;
    private bool _activeProfileInitialLoadPending;
    private bool _noGameRecheckRequestedForThisEntry;
    private uint? _profileArtworkRequestedAppId;
    private int _profileArtworkRequestedDecodePixelWidth;
    private double _profileArtworkRequestedRasterizationScale;
    private bool _profileArtworkRequestIsPrewarm;
    private CancellationTokenSource? _profileArtworkRequestCancellation;
    private uint? _profileArtworkPrewarmAppId;
    private uint? _profileArtworkCachedAppId;
    private int _profileArtworkCachedDecodePixelWidth;
    private double _profileArtworkCachedRasterizationScale;
    private BitmapImage? _profileArtworkCachedBitmap;
    private XamlRoot? _profileArtworkObservedXamlRoot;
    private XboxStoreArtworkResolver? _xboxArtworkResolver;
    private string? _xboxProfileArtworkRequestedKey;
    private int _xboxProfileArtworkRequestedDecodePixelWidth;
    private double _xboxProfileArtworkRequestedRasterizationScale;
    private CancellationTokenSource? _xboxProfileArtworkRequestCancellation;
    private string? _xboxProfileArtworkCachedLookupKey;
    private string? _xboxProfileArtworkCachedSourceMarket;
    private string? _xboxProfileArtworkCachedPurpose;
    private int _xboxProfileArtworkCachedDecodePixelWidth;
    private double _xboxProfileArtworkCachedRasterizationScale;
    private BitmapImage? _xboxProfileArtworkCachedBitmap;

    internal event Action? NoRunningGameProfileRecheckRequested;

    private void ProfileContentColumn_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyProfileArtworkLayout(e.NewSize.Width, IsXboxProfileTarget(CurrentProfileArtworkPage()));

        if (double.IsFinite(e.NewSize.Width) && e.NewSize.Width > 0)
        {
            ReevaluateProfileArtworkForCurrentPage();
            ReevaluateHiddenProfileArtworkPrewarm();
        }
    }

    private void ProfileArtworkHost_Loaded(object sender, RoutedEventArgs e)
    {
        ObserveProfileArtworkXamlRoot(ProfileArtworkHost.XamlRoot);
        ReevaluateProfileArtworkForCurrentPage();
    }

    private void ProfileArtworkHost_Unloaded(object sender, RoutedEventArgs e) =>
        ObserveProfileArtworkXamlRoot(null);

    private void ProfileArtworkXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs e) =>
        ReevaluateProfileArtworkForCurrentPage();

    private void ObserveProfileArtworkXamlRoot(XamlRoot? xamlRoot)
    {
        if (ReferenceEquals(_profileArtworkObservedXamlRoot, xamlRoot)) return;
        if (_profileArtworkObservedXamlRoot is not null)
            _profileArtworkObservedXamlRoot.Changed -= ProfileArtworkXamlRoot_Changed;
        _profileArtworkObservedXamlRoot = xamlRoot;
        if (_profileArtworkObservedXamlRoot is not null)
            _profileArtworkObservedXamlRoot.Changed += ProfileArtworkXamlRoot_Changed;
    }

    private void ReevaluateProfileArtworkForCurrentPage()
    {
        if (!_isVisible || !_profilePageReceivedThisShow || !IsProfileArtworkPresentationTab(_tabState.SelectedTab)
            || !_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface))
            return;

        UpdateProfileArtwork(surface.Binding?.AuthoritativePage);
    }

    internal void PrewarmSteamArtwork(uint appId)
    {
        if (_isVisible)
        {
            OverlayLog.Debug("ProfileArtwork", "Ignored Steam artwork prewarm while the Overlay is visible.",
                ("AppId", appId), ("Admission", "SkippedVisible"));
            return;
        }

        _profileArtworkPrewarmAppId = appId > 0 ? appId : null;
        if (appId == 0)
        {
            if (_profileArtworkRequestIsPrewarm)
            {
                CancelProfileArtworkRequest();
                _profileArtworkRequestedAppId = null;
            }
            if (_profileArtworkCachedAppId is not null)
                DiscardCachedProfileArtwork();
            OverlayLog.Debug("ProfileArtwork", "Cleared the pending Steam artwork prewarm target.",
                ("AppId", 0), ("Admission", "ReadyHidden"));
            return;
        }

        OverlayLog.Debug("ProfileArtwork", "Accepted Steam artwork prewarm target.",
            ("AppId", appId), ("Admission", "ReadyHidden"));
        ReevaluateHiddenProfileArtworkPrewarm();
    }

    private void ReevaluateHiddenProfileArtworkPrewarm()
    {
        if (_isVisible || _profileArtworkPrewarmAppId is not { } appId)
            return;

        var (rasterizationScale, requestedDecodePixelWidth) = CurrentProfileArtworkDecodeKey();
        if (requestedDecodePixelWidth is not { } decodePixelWidth)
        {
            OverlayLog.Debug("ProfileArtwork", "Steam artwork prewarm is waiting for valid hidden-window geometry.",
                ("AppId", appId), ("Admission", "NoDecodeKey"));
            return;
        }

        var matchingRequest = _profileArtworkRequestIsPrewarm
            && _profileArtworkRequestedAppId == appId
            && _profileArtworkRequestCancellation is { IsCancellationRequested: false }
            && _profileArtworkRequestedDecodePixelWidth == decodePixelWidth
            && Math.Abs(_profileArtworkRequestedRasterizationScale - rasterizationScale) < 0.0001;
        if (!matchingRequest && _profileArtworkRequestCancellation is not null)
            CancelProfileArtworkRequest();

        if (_profileArtworkCachedBitmap is not null
            && IsProfileArtworkCacheCompatible(_profileArtworkCachedAppId, _profileArtworkCachedDecodePixelWidth,
                _profileArtworkCachedRasterizationScale, appId, decodePixelWidth, rasterizationScale))
        {
            OverlayLog.Debug("Performance", "OverlayArtwork",
                ("AppId", appId), ("Cache", "PrefetchReady"), ("DecodePixelWidth", decodePixelWidth),
                ("RasterizationScale", rasterizationScale), ("Stage", "Ready"));
            return;
        }

        if (_profileArtworkCachedBitmap is not null)
            DiscardCachedProfileArtwork();

        if (matchingRequest)
            return;
        _profileArtworkRequestedAppId = appId;
        _profileArtworkRequestedDecodePixelWidth = decodePixelWidth;
        _profileArtworkRequestedRasterizationScale = rasterizationScale;
        _profileArtworkRequestIsPrewarm = true;
        var requestCancellation = new CancellationTokenSource();
        _profileArtworkRequestCancellation = requestCancellation;
        OverlayLog.Debug("Performance", "OverlayArtwork",
            ("AppId", appId), ("Cache", "Miss"), ("DecodePixelWidth", decodePixelWidth),
            ("RasterizationScale", rasterizationScale), ("Stage", "PrefetchLookup"));
        _ = ResolveAndPrewarmProfileArtworkAsync(appId, decodePixelWidth, rasterizationScale, requestCancellation);
    }

    internal static (double Width, double Height) CalculateProfileArtworkSize(double rightColumnWidth)
    {
        var width = double.IsFinite(rightColumnWidth)
            ? Math.Max(0, rightColumnWidth)
            : 0;
        return (width, width * 430.0 / 920.0);
    }

    internal static (double Width, double Height) CalculateXboxProfileArtworkSize(double rightColumnWidth)
    {
        var width = double.IsFinite(rightColumnWidth) ? Math.Max(0, rightColumnWidth) : 0;
        return (width, width * 9.0 / 16.0);
    }

    private QuickSettingsPageSnapshot? CurrentProfileArtworkPage() =>
        _quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)
            ? surface.Binding?.AuthoritativePage
            : null;

    private static bool IsXboxProfileTarget(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, Available: true,
            ProfileTarget: { Kind: QuickSettingsProfileTargetKind.Xbox, IsStructurallyValid: true } };

    internal static string? EligibleXboxStoreGameKey(QuickSettingsPageSnapshot? page)
    {
        if (!IsXboxProfileTarget(page)
            || page?.ProfileTarget?.XboxGameKey is not { } gameKey
            || !XboxStoreArtworkResolver.TryGetStoreId(gameKey, out _))
            return null;
        return gameKey;
    }

    private void ApplyProfileArtworkLayout(double rightColumnWidth, bool xbox)
    {
        var (width, height) = xbox
            ? CalculateXboxProfileArtworkSize(rightColumnWidth)
            : CalculateProfileArtworkSize(rightColumnWidth);
        if (ShouldUpdateProfileArtworkSize(ProfileArtworkHost.Width, ProfileArtworkHost.Height, width, height))
        {
            ProfileArtworkHost.Width = width;
            ProfileArtworkHost.Height = height;
            ProfileArtworkHost.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        }

        var stretch = xbox ? Stretch.Uniform : Stretch.UniformToFill;
        if (ProfileArtworkImage.Stretch != stretch)
            ProfileArtworkImage.Stretch = stretch;
    }

    internal static bool ShouldUpdateProfileArtworkSize(
        double currentWidth,
        double currentHeight,
        double width,
        double height) =>
        !double.IsFinite(currentWidth)
        || !double.IsFinite(currentHeight)
        || Math.Abs(currentWidth - width) >= 0.0001
        || Math.Abs(currentHeight - height) >= 0.0001;

    private FrameworkElement BuildProfilePage()
    {
        var root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _profileDetailRoot = BuildQuickSettingsPage(AddonQuickSettingsTabId.Profile, QuickSettingsPageId.Profile);
        _profileDetailRoot.Visibility = Visibility.Collapsed;
        root.Children.Add(_profileDetailRoot);

        _profileStatusMessage = new TextBlock
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        OverlayQamResources.ApplyTextStyle(_profileStatusMessage, "QamBodyStrongTextStyle");
        _profileStatusMessage.FontSize = OverlayQamResources.Scale(16);
        root.Children.Add(_profileStatusMessage);
        return root;
    }

    internal bool TryHandleBack() => TryReturnFromDetailPage();

    private void OnProfileTabSelectionChanged(bool selected)
    {
        if (!selected)
        {
            _noGameRecheckRequestedForThisEntry = false;
            if (_tabState.SelectedTab != AddonQuickSettingsTabId.Shortcut)
                HideProfileArtworkPreservingCache();
            return;
        }

        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface))
            return;

        ApplyProfilePresentation(surface.Binding?.AuthoritativePage);
        UpdateProfileArtwork(surface.Binding?.AuthoritativePage);
        TryRequestNoRunningGameRecheck(surface.Binding?.AuthoritativePage);
    }

    private void PrepareActiveProfileFirstShow()
    {
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;
        _profilePageReceivedThisShow = false;
        _noGameRecheckRequestedForThisEntry = false;
        _activeProfileInitialLoadPending = true;
        var page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, "Loading the active game profile.");
        surface.Content.Visibility = Visibility.Collapsed;
        ApplyProfilePresentation(page);
        HideProfileArtworkPreservingCache();
    }

    internal string ApplyActiveProfilePage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != QuickSettingsPageId.Profile) return "Ignored";
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return "NoSurface";

        var preferredShortcutRowId = CaptureShortcutSelectionRowId();
        _profilePageReceivedThisShow = true;
        _activeProfileInitialLoadPending = false;
        surface.Binding?.ApplyAuthoritativePage(page);
        surface.Content.Visibility = HasRenderableActiveProfile(page) ? Visibility.Visible : Visibility.Collapsed;
        RenderQuickSettingsPage(surface);
        var renderPath = surface.LastRenderPath;
        ApplyProfilePresentation(page);
        if (_tabState.SelectedTab != AddonQuickSettingsTabId.Shortcut)
            UpdateProfileArtwork(page);
        TryRequestNoRunningGameRecheck(page);
        RefreshShortcutQuickSettingsView(preferredShortcutRowId);
        return renderPath;
    }

    private void UpdateProfileArtwork(QuickSettingsPageSnapshot? page)
    {
        var isXbox = IsXboxProfileTarget(page);
        var width = ProfileContentColumn.ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
            width = double.IsFinite(ProfileArtworkHost.Width) ? ProfileArtworkHost.Width : 0;
        ApplyProfileArtworkLayout(width, isXbox);

        if (!_isVisible || !_profilePageReceivedThisShow)
        {
            HideProfileArtworkPreservingCache();
            return;
        }

        if (isXbox)
        {
            UpdateXboxProfileArtwork(page);
            return;
        }

        CancelXboxProfileArtworkRequest();
        _xboxProfileArtworkRequestedKey = null;

        var appId = SteamGameArtworkResolver.EligibleSteamAppId(page);
        if (appId is null)
        {
            ClearProfileArtwork();
            return;
        }

        if (!IsProfileArtworkPresentationTab(_tabState.SelectedTab))
        {
            HideProfileArtworkPreservingCache();
            return;
        }

        var (rasterizationScale, requestedDecodePixelWidth) = CurrentProfileArtworkDecodeKey();
        if (requestedDecodePixelWidth is not { } decodePixelWidth)
        {
            HideProfileArtworkPreservingCache();
            return;
        }

        var cachedBitmap = _profileArtworkCachedBitmap;
        var cacheHit = cachedBitmap is not null
            && IsProfileArtworkCacheCompatible(
                _profileArtworkCachedAppId, _profileArtworkCachedDecodePixelWidth, _profileArtworkCachedRasterizationScale,
                appId.Value, decodePixelWidth, rasterizationScale);

        if (cacheHit)
        {
            CancelProfileArtworkRequest();
            _profileArtworkRequestedAppId = appId;
            _profileArtworkRequestedDecodePixelWidth = decodePixelWidth;
            _profileArtworkRequestedRasterizationScale = rasterizationScale;
            ApplyProfileArtwork(appId.Value, cachedBitmap!);
            OverlayLog.Debug("Performance", "OverlayArtwork",
                ("AppId", appId.Value), ("Cache", "Hit"), ("DecodePixelWidth", decodePixelWidth),
                ("RasterizationScale", rasterizationScale), ("Stage", "Ready"), ("RetryUsed", false));
            return;
        }

        if (_profileArtworkCachedBitmap is not null)
            DiscardCachedProfileArtwork();

        var matchingPrewarm = _profileArtworkRequestIsPrewarm
            && _profileArtworkPrewarmAppId == appId
            && _profileArtworkRequestedAppId == appId
            && _profileArtworkRequestCancellation is { IsCancellationRequested: false }
            && _profileArtworkRequestedDecodePixelWidth == decodePixelWidth
            && Math.Abs(_profileArtworkRequestedRasterizationScale - rasterizationScale) < 0.0001;
        if (_profileArtworkRequestIsPrewarm && !matchingPrewarm)
            CancelProfileArtworkRequest();

        if (matchingPrewarm)
        {
            OverlayLog.Debug("Performance", "OverlayArtwork",
                ("AppId", appId.Value), ("Cache", "PrefetchInFlight"), ("DecodePixelWidth", decodePixelWidth),
                ("RasterizationScale", rasterizationScale), ("Stage", "AwaitPrewarm"));
            return;
        }

        if (_profileArtworkRequestedAppId == appId
            && _profileArtworkRequestCancellation is { IsCancellationRequested: false }
            && _profileArtworkRequestedDecodePixelWidth == decodePixelWidth
            && Math.Abs(_profileArtworkRequestedRasterizationScale - rasterizationScale) < 0.0001)
            return;

        HideProfileArtworkPreservingCache();
        _profileArtworkRequestedAppId = appId;
        _profileArtworkRequestedDecodePixelWidth = decodePixelWidth;
        _profileArtworkRequestedRasterizationScale = rasterizationScale;
        _profileArtworkRequestIsPrewarm = false;
        var requestCancellation = new CancellationTokenSource();
        _profileArtworkRequestCancellation = requestCancellation;
        OverlayLog.Debug("Performance", "OverlayArtwork",
            ("AppId", appId.Value), ("Cache", "Miss"), ("DecodePixelWidth", decodePixelWidth),
            ("RasterizationScale", rasterizationScale), ("Stage", "Lookup"), ("RetryUsed", false));
        _ = ResolveAndApplyProfileArtworkAsync(appId.Value, page!, requestCancellation);
    }

    private void UpdateXboxProfileArtwork(QuickSettingsPageSnapshot? page)
    {
        var gameKey = EligibleXboxStoreGameKey(page);
        if (gameKey is null)
        {
            ClearProfileArtwork();
            return;
        }

        if (!IsProfileArtworkPresentationTab(_tabState.SelectedTab))
        {
            HideProfileArtworkPreservingCache();
            return;
        }

        CancelProfileArtworkRequest();
        _profileArtworkRequestedAppId = null;
        var (rasterizationScale, requestedDecodePixelWidth) = CurrentProfileArtworkDecodeKey();
        if (requestedDecodePixelWidth is not { } decodePixelWidth)
        {
            HideProfileArtworkPreservingCache();
            return;
        }

        var culture = CultureInfo.CurrentUICulture.Name;
        var (market, language) = XboxStoreArtworkResolver.ResolveMarketAndLanguage(culture);
        var lookupKey = BuildXboxArtworkLookupKey(gameKey, market, language);
        if (_xboxProfileArtworkCachedBitmap is { } cachedBitmap
            && IsXboxProfileArtworkCacheCompatible(
                _xboxProfileArtworkCachedLookupKey, _xboxProfileArtworkCachedDecodePixelWidth,
                _xboxProfileArtworkCachedRasterizationScale, lookupKey, decodePixelWidth, rasterizationScale))
        {
            CancelXboxProfileArtworkRequest();
            _xboxProfileArtworkRequestedKey = lookupKey;
            _xboxProfileArtworkRequestedDecodePixelWidth = decodePixelWidth;
            _xboxProfileArtworkRequestedRasterizationScale = rasterizationScale;
            ApplyXboxProfileArtwork(gameKey, cachedBitmap);
            OverlayLog.Debug("ProfileArtwork", "Applied cached Xbox Profile hero.",
                ("Market", _xboxProfileArtworkCachedSourceMarket ?? "Unknown"),
                ("Purpose", _xboxProfileArtworkCachedPurpose ?? "Unknown"),
                ("DecodePixelWidth", decodePixelWidth), ("RasterizationScale", rasterizationScale));
            return;
        }

        if (_xboxProfileArtworkCachedBitmap is not null)
            DiscardCachedXboxProfileArtwork();

        if (_xboxProfileArtworkRequestedKey == lookupKey
            && _xboxProfileArtworkRequestCancellation is { IsCancellationRequested: false }
            && _xboxProfileArtworkRequestedDecodePixelWidth == decodePixelWidth
            && Math.Abs(_xboxProfileArtworkRequestedRasterizationScale - rasterizationScale) < 0.0001)
            return;

        HideProfileArtworkPreservingCache();
        _xboxProfileArtworkRequestedKey = lookupKey;
        _xboxProfileArtworkRequestedDecodePixelWidth = decodePixelWidth;
        _xboxProfileArtworkRequestedRasterizationScale = rasterizationScale;
        var requestCancellation = new CancellationTokenSource();
        _xboxProfileArtworkRequestCancellation = requestCancellation;
        _xboxArtworkResolver ??= new XboxStoreArtworkResolver(
            XboxStoreArtworkResolver.ResolveCacheDirectory([FrontendLaunchArguments.LogDirectoryOption, OverlayLog.DirectoryPath]),
            diagnostic => OverlayLog.Debug("XboxArtwork", "Optional Xbox artwork lookup completed.",
                ("StoreId", diagnostic.StoreId), ("Market", diagnostic.Market), ("Purpose", diagnostic.Purpose),
                ("Outcome", diagnostic.Outcome), ("DurationMs", diagnostic.DurationMs), ("ErrorType", diagnostic.ErrorType ?? "None")));
        _ = ResolveAndApplyXboxProfileArtworkAsync(gameKey, lookupKey, _xboxArtworkResolver, requestCancellation);
    }

    internal static string BuildXboxArtworkLookupKey(string canonicalGameKey, string market, string language) =>
        $"{canonicalGameKey}|{market}|{language}";

    internal static bool IsXboxProfileArtworkCacheCompatible(
        string? cachedLookupKey,
        int cachedDecodePixelWidth,
        double cachedRasterizationScale,
        string requestedLookupKey,
        int requestedDecodePixelWidth,
        double requestedRasterizationScale) =>
        string.Equals(cachedLookupKey, requestedLookupKey, StringComparison.Ordinal)
        && cachedDecodePixelWidth == requestedDecodePixelWidth
        && Math.Abs(cachedRasterizationScale - requestedRasterizationScale) < 0.0001;

    private async Task ResolveAndApplyXboxProfileArtworkAsync(
        string gameKey,
        string lookupKey,
        XboxStoreArtworkResolver resolver,
        CancellationTokenSource requestCancellation)
    {
        var cancellationToken = requestCancellation.Token;
        try
        {
            var result = await resolver.ResolveProfileHeroAsync(gameKey, CultureInfo.CurrentUICulture.Name, cancellationToken);
            if (!IsCurrentXboxProfileArtworkRequest(gameKey, lookupKey, requestCancellation))
                return;
            if (result is null)
            {
                ProfileArtworkImage.Source = null;
                ProfileArtworkHost.Visibility = Visibility.Collapsed;
                PageTitle.Visibility = Visibility.Visible;
                OverlayLog.Debug("ProfileArtwork", "Xbox Profile hero unavailable; retaining the title fallback.",
                    ("Market", XboxStoreArtworkResolver.ResolveMarketAndLanguage(CultureInfo.CurrentUICulture.Name).Market));
                return;
            }

            var decodePixelWidth = _xboxProfileArtworkRequestedDecodePixelWidth;
            var bitmap = await TryLoadXboxProfileArtworkAsync(result.Path, decodePixelWidth, cancellationToken);
            if (!IsCurrentXboxProfileArtworkRequest(gameKey, lookupKey, requestCancellation))
                return;
            if (bitmap is null)
            {
                ProfileArtworkImage.Source = null;
                ProfileArtworkHost.Visibility = Visibility.Collapsed;
                PageTitle.Visibility = Visibility.Visible;
                OverlayLog.Debug("ProfileArtwork", "Xbox Profile hero could not be decoded; retaining the title fallback.",
                    ("Market", result.Market), ("Purpose", result.Purpose));
                return;
            }

            _xboxProfileArtworkCachedLookupKey = lookupKey;
            _xboxProfileArtworkCachedSourceMarket = result.Market;
            _xboxProfileArtworkCachedPurpose = result.Purpose;
            _xboxProfileArtworkCachedDecodePixelWidth = decodePixelWidth;
            _xboxProfileArtworkCachedRasterizationScale = _xboxProfileArtworkRequestedRasterizationScale;
            _xboxProfileArtworkCachedBitmap = bitmap;
            ApplyXboxProfileArtwork(gameKey, bitmap);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OverlayLog.Debug("ProfileArtwork", "Canceled stale Xbox Profile artwork lookup.");
        }
        catch (Exception exception)
        {
            if (IsCurrentXboxProfileArtworkRequest(gameKey, lookupKey, requestCancellation))
            {
                ProfileArtworkImage.Source = null;
                ProfileArtworkHost.Visibility = Visibility.Collapsed;
                PageTitle.Visibility = Visibility.Visible;
            }
            OverlayLog.Debug("ProfileArtwork", "Optional Xbox Profile artwork failed.",
                ("ErrorType", exception.GetType().Name));
        }
        finally
        {
            if (ReferenceEquals(_xboxProfileArtworkRequestCancellation, requestCancellation))
            {
                requestCancellation.Dispose();
                _xboxProfileArtworkRequestCancellation = null;
            }
        }
    }

    private static async Task<BitmapImage?> TryLoadXboxProfileArtworkAsync(
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = decodePixelWidth };
            await bitmap.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool IsCurrentXboxProfileArtworkRequest(
        string gameKey,
        string lookupKey,
        CancellationTokenSource requestCancellation) =>
        !requestCancellation.IsCancellationRequested
        && ReferenceEquals(_xboxProfileArtworkRequestCancellation, requestCancellation)
        && string.Equals(_xboxProfileArtworkRequestedKey, lookupKey, StringComparison.Ordinal)
        && IsCurrentXboxProfileArtworkTarget(gameKey);

    private bool IsCurrentXboxProfileArtworkTarget(string gameKey)
    {
        var currentGameKey = EligibleXboxStoreGameKey(CurrentProfileArtworkPage());
        var (market, language) = XboxStoreArtworkResolver.ResolveMarketAndLanguage(CultureInfo.CurrentUICulture.Name);
        var currentLookupKey = currentGameKey is null ? null : BuildXboxArtworkLookupKey(currentGameKey, market, language);
        return IsCurrentXboxProfileArtworkIdentity(
            gameKey, _xboxProfileArtworkRequestedKey, currentGameKey, currentLookupKey,
            _isVisible, _profilePageReceivedThisShow, _tabState.SelectedTab);
    }

    internal static bool IsCurrentXboxProfileArtworkIdentity(
        string requestedGameKey,
        string? requestedLookupKey,
        string? currentGameKey,
        string? currentLookupKey,
        bool isVisible,
        bool profilePageReceived,
        AddonQuickSettingsTabId selectedTab) =>
        isVisible
        && profilePageReceived
        && IsProfileArtworkPresentationTab(selectedTab)
        && string.Equals(requestedGameKey, currentGameKey, StringComparison.Ordinal)
        && string.Equals(requestedLookupKey, currentLookupKey, StringComparison.Ordinal);

    private void ApplyXboxProfileArtwork(string gameKey, BitmapImage bitmap)
    {
        if (!IsCurrentXboxProfileArtworkTarget(gameKey)) return;
        ProfileArtworkImage.Source = bitmap;
        ProfileArtworkImage.Stretch = Stretch.Uniform;
        ProfileArtworkHost.Visibility = Visibility.Visible;
        PageTitle.Visibility = Visibility.Collapsed;
    }

    private async Task ResolveAndApplyProfileArtworkAsync(
        uint appId,
        QuickSettingsPageSnapshot page,
        CancellationTokenSource requestCancellation)
    {
        var elapsed = Stopwatch.StartNew();
        var candidateLookupMs = 0.0;
        var decodeMs = 0.0;
        var cancellationToken = requestCancellation.Token;
        var retryUsed = false;
        var candidateCount = 0;
        try
        {
            for (var attempt = 0; attempt < ProfileArtworkMaximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentProfileArtworkRequest(appId, requestCancellation))
                {
                    if (ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation))
                        ClearProfileArtwork();
                    OverlayLog.Debug("ProfileArtwork", "Ignored stale local artwork lookup.",
                        ("AppId", appId), ("CandidateCount", candidateCount),
                        ("Stage", "StaleOrHidden"), ("RetryUsed", retryUsed));
                    return;
                }

                IReadOnlyList<string> paths = [];
                var lookup = Stopwatch.StartNew();
                try
                {
                    paths = await Task.Run(() => SteamGameArtworkResolver.ResolveArtworkCandidates(page));
                }
                catch (Exception exception) when (IsOptionalProfileArtworkFailure(exception))
                {
                    paths = [];
                    OverlayLog.Debug("ProfileArtwork", "Local artwork candidate lookup failed.",
                        ("AppId", appId), ("CandidateCount", 0),
                        ("Stage", "NoCandidate"), ("RetryUsed", retryUsed),
                        ("ExceptionType", exception.GetType().Name));
                }
                finally
                {
                    candidateLookupMs += lookup.Elapsed.TotalMilliseconds;
                    OverlayLog.Debug("Performance", "OverlayArtworkLookup",
                        ("AppId", appId), ("ElapsedMs", lookup.Elapsed.TotalMilliseconds),
                        ("CandidateCount", paths?.Count ?? 0), ("RetryUsed", retryUsed));
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentProfileArtworkRequest(appId, requestCancellation))
                {
                    if (ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation))
                        ClearProfileArtwork();
                    OverlayLog.Debug("ProfileArtwork", "Ignored stale local artwork candidates.",
                        ("AppId", appId), ("CandidateCount", paths.Count),
                        ("Stage", "StaleOrHidden"), ("RetryUsed", retryUsed));
                    return;
                }

                candidateCount = paths.Count;
                OverlayLog.Debug("ProfileArtwork", "Resolved local artwork candidates.",
                    ("AppId", appId), ("CandidateCount", candidateCount), ("RetryUsed", retryUsed));

                BitmapImage? bitmap;
                var decode = Stopwatch.StartNew();
                try
                {
                    bitmap = candidateCount == 0
                        ? null
                        : await TryLoadProfileArtworkAsync(paths, _profileArtworkRequestedDecodePixelWidth, cancellationToken);
                }
                finally
                {
                    decodeMs += decode.Elapsed.TotalMilliseconds;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentProfileArtworkRequest(appId, requestCancellation))
                {
                    if (ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation))
                        ClearProfileArtwork();
                    OverlayLog.Debug("ProfileArtwork", "Ignored stale local artwork result.",
                        ("AppId", appId), ("CandidateCount", candidateCount),
                        ("Stage", "StaleOrHidden"), ("RetryUsed", retryUsed));
                    return;
                }

                if (bitmap is not null)
                {
                    _profileArtworkCachedAppId = appId;
                    _profileArtworkCachedDecodePixelWidth = _profileArtworkRequestedDecodePixelWidth;
                    _profileArtworkCachedRasterizationScale = _profileArtworkRequestedRasterizationScale;
                    _profileArtworkCachedBitmap = bitmap;
                    ApplyProfileArtwork(appId, bitmap);
                    OverlayLog.Debug("Performance", "OverlayArtwork",
                        ("AppId", appId), ("Cache", "Miss"), ("CandidateLookupMs", candidateLookupMs),
                        ("DecodeMs", decodeMs), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                        ("CandidateCount", candidateCount), ("Stage", "Success"), ("RetryUsed", retryUsed));
                    OverlayLog.Debug("ProfileArtwork", "Loaded local Profile artwork.",
                        ("AppId", appId), ("CandidateCount", candidateCount),
                        ("Stage", "Success"), ("RetryUsed", retryUsed));
                    return;
                }

                var failureStage = candidateCount == 0 ? "NoCandidate" : "OpenOrDecodeFailed";
                if (attempt == 0)
                {
                    OverlayLog.Debug("ProfileArtwork", "Local artwork was unavailable; scheduling one bounded retry.",
                        ("AppId", appId), ("CandidateCount", candidateCount),
                        ("Stage", failureStage), ("RetryUsed", false));
                    retryUsed = true;
                    await Task.Delay(ProfileArtworkRetryDelay, cancellationToken);
                    continue;
                }

                ProfileArtworkImage.Source = null;
                ProfileArtworkHost.Visibility = Visibility.Collapsed;
                PageTitle.Visibility = Visibility.Visible;
                OverlayLog.Debug("Performance", "OverlayArtwork",
                    ("AppId", appId), ("Cache", "Miss"), ("CandidateLookupMs", candidateLookupMs),
                    ("DecodeMs", decodeMs), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                    ("CandidateCount", candidateCount), ("Stage", failureStage), ("RetryUsed", retryUsed));
                OverlayLog.Warn("ProfileArtwork", "Local artwork unavailable after the bounded retry; keeping the Profile title visible.",
                    null, ("AppId", appId), ("CandidateCount", candidateCount),
                    ("Stage", failureStage), ("RetryUsed", retryUsed));
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OverlayLog.Debug("Performance", "OverlayArtwork",
                ("AppId", appId), ("CandidateLookupMs", candidateLookupMs), ("DecodeMs", decodeMs),
                ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds), ("CandidateCount", candidateCount),
                ("Stage", "StaleOrHidden"), ("RetryUsed", retryUsed));
            OverlayLog.Debug("ProfileArtwork", "Stopped stale local artwork lookup.",
                ("AppId", appId), ("CandidateCount", candidateCount),
                ("Stage", "StaleOrHidden"), ("RetryUsed", retryUsed));
        }
        finally
        {
            if (ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation))
            {
                requestCancellation.Dispose();
                _profileArtworkRequestCancellation = null;
            }
        }
    }

    private async Task ResolveAndPrewarmProfileArtworkAsync(
        uint appId,
        int decodePixelWidth,
        double rasterizationScale,
        CancellationTokenSource requestCancellation)
    {
        var elapsed = Stopwatch.StartNew();
        var lookup = Stopwatch.StartNew();
        var candidateCount = 0;
        var prewarmSucceeded = false;
        var cancellationToken = requestCancellation.Token;
        try
        {
            IReadOnlyList<string> paths = [];
            try
            {
                paths = await Task.Run(() => SteamGameArtworkResolver.ResolveArtworkCandidates(appId), cancellationToken);
            }
            catch (Exception exception) when (IsOptionalProfileArtworkFailure(exception))
            {
                paths = [];
                OverlayLog.Debug("ProfileArtwork", "Hidden Steam artwork candidate lookup failed.",
                    ("AppId", appId), ("CandidateCount", 0), ("Stage", "NoCandidate"),
                    ("ExceptionType", exception.GetType().Name));
            }
            finally
            {
                lookup.Stop();
                OverlayLog.Debug("Performance", "OverlayArtworkLookup",
                    ("AppId", appId), ("ElapsedMs", lookup.Elapsed.TotalMilliseconds),
                    ("CandidateCount", paths.Count), ("Prefetch", true));
            }

            cancellationToken.ThrowIfCancellationRequested();
            candidateCount = paths.Count;
            if (!IsCurrentProfileArtworkPrewarmRequest(appId, requestCancellation))
            {
                LogStaleSteamArtworkPrewarm(appId, candidateCount, "AfterLookup", elapsed.Elapsed.TotalMilliseconds);
                return;
            }

            OverlayLog.Debug("ProfileArtwork", "Resolved hidden Steam artwork candidates.",
                ("AppId", appId), ("CandidateCount", candidateCount), ("Prefetch", true));
            if (candidateCount == 0)
            {
                OverlayLog.Debug("Performance", "OverlayArtwork",
                    ("AppId", appId), ("Cache", "Miss"), ("CandidateLookupMs", lookup.Elapsed.TotalMilliseconds),
                    ("DecodeMs", 0), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                    ("CandidateCount", 0), ("Stage", "NoCandidate"), ("Prefetch", true));
                return;
            }

            BitmapImage? bitmap;
            var decode = Stopwatch.StartNew();
            try
            {
                bitmap = await TryLoadProfileArtworkAsync(paths, decodePixelWidth, cancellationToken);
            }
            finally
            {
                decode.Stop();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentProfileArtworkPrewarmRequest(appId, requestCancellation))
            {
                LogStaleSteamArtworkPrewarm(appId, candidateCount, "AfterDecode", elapsed.Elapsed.TotalMilliseconds);
                return;
            }

            if (bitmap is null)
            {
                OverlayLog.Debug("Performance", "OverlayArtwork",
                    ("AppId", appId), ("Cache", "Miss"), ("CandidateLookupMs", lookup.Elapsed.TotalMilliseconds),
                    ("DecodeMs", decode.Elapsed.TotalMilliseconds), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                    ("CandidateCount", candidateCount), ("Stage", "OpenOrDecodeFailed"), ("Prefetch", true));
                OverlayLog.Debug("ProfileArtwork", "Hidden Steam artwork was not ready; visible fallback remains enabled.",
                    ("AppId", appId), ("CandidateCount", candidateCount), ("Stage", "OpenOrDecodeFailed"));
                return;
            }

            _profileArtworkCachedAppId = appId;
            _profileArtworkCachedDecodePixelWidth = decodePixelWidth;
            _profileArtworkCachedRasterizationScale = rasterizationScale;
            _profileArtworkCachedBitmap = bitmap;
            prewarmSucceeded = true;
            if (IsCurrentProfileArtworkTarget(appId))
            {
                ApplyProfileArtwork(appId, bitmap);
                OverlayLog.Debug("Performance", "OverlayArtwork",
                    ("AppId", appId), ("Cache", "Hit"), ("DecodePixelWidth", decodePixelWidth),
                    ("RasterizationScale", rasterizationScale), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                    ("CandidateCount", candidateCount), ("Stage", "PrefetchCompletedWhileVisible"));
            }

            OverlayLog.Debug("Performance", "OverlayArtwork",
                ("AppId", appId), ("Cache", "PrefetchReady"), ("DecodePixelWidth", decodePixelWidth),
                ("RasterizationScale", rasterizationScale), ("CandidateLookupMs", lookup.Elapsed.TotalMilliseconds),
                ("DecodeMs", decode.Elapsed.TotalMilliseconds), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                ("CandidateCount", candidateCount), ("Stage", "Ready"), ("Prefetch", true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStaleSteamArtworkPrewarm(appId, candidateCount, "Canceled", elapsed.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception) when (IsOptionalProfileArtworkFailure(exception))
        {
            OverlayLog.Debug("ProfileArtwork", "Hidden Steam artwork prewarm failed; visible fallback remains enabled.",
                ("AppId", appId), ("CandidateCount", candidateCount), ("ElapsedMs", elapsed.Elapsed.TotalMilliseconds),
                ("ExceptionType", exception.GetType().Name));
        }
        finally
        {
            if (ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation))
            {
                var wasCanceled = requestCancellation.IsCancellationRequested;
                requestCancellation.Dispose();
                _profileArtworkRequestCancellation = null;
                _profileArtworkRequestIsPrewarm = false;

                if (ShouldStartVisibleArtworkFallbackAfterPrewarmFailure(
                        candidateCount, prewarmSucceeded, wasCanceled, IsCurrentProfileArtworkTarget(appId)))
                {
                    OverlayLog.Debug("ProfileArtwork", "Prewarm did not produce artwork for the visible Profile; starting the existing visible fallback.",
                        ("AppId", appId), ("CandidateCount", candidateCount),
                        ("Stage", candidateCount == 0 ? "NoCandidate" : "OpenOrDecodeFailed"));
                    UpdateProfileArtwork(CurrentProfileArtworkPage());
                }
            }
        }
    }

    private bool IsCurrentProfileArtworkPrewarmRequest(uint appId, CancellationTokenSource requestCancellation)
    {
        var authoritativeAppId = _profilePageReceivedThisShow
            ? SteamGameArtworkResolver.EligibleSteamAppId(CurrentProfileArtworkPage())
            : null;
        return !requestCancellation.IsCancellationRequested
            && ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation)
            && _profileArtworkRequestIsPrewarm
            && _profileArtworkRequestedAppId == appId
            && IsProfileArtworkPrewarmIdentityCurrent(appId, _profileArtworkPrewarmAppId,
                _isVisible, _profilePageReceivedThisShow, authoritativeAppId);
    }

    private void LogStaleSteamArtworkPrewarm(uint appId, int candidateCount, string stage, double elapsedMs) =>
        OverlayLog.Debug("Performance", "OverlayArtwork",
            ("AppId", appId), ("Cache", "Miss"), ("CandidateCount", candidateCount),
            ("ElapsedMs", elapsedMs), ("Stage", "StaleOrHidden"), ("PrefetchStage", stage));

    private async Task<BitmapImage?> TryLoadProfileArtworkAsync(
        IReadOnlyList<string> paths,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage { DecodePixelWidth = decodePixelWidth };
                await bitmap.SetSourceAsync(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return bitmap;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // File access and image decoding are optional; try the next bounded candidate.
            }
        }

        return null;
    }

    private static bool IsOptionalProfileArtworkFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            ArgumentException or NotSupportedException or InvalidOperationException or COMException;

    private bool IsCurrentProfileArtworkRequest(uint appId, CancellationTokenSource requestCancellation) =>
        !requestCancellation.IsCancellationRequested
        && ReferenceEquals(_profileArtworkRequestCancellation, requestCancellation)
        && IsCurrentProfileArtworkTarget(appId);

    private bool IsCurrentProfileArtworkTarget(uint appId)
    {
        if (!_isVisible || !_profilePageReceivedThisShow || !IsProfileArtworkPresentationTab(_tabState.SelectedTab)
            || _profileArtworkRequestedAppId != appId
            || !_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface))
            return false;

        return SteamGameArtworkResolver.EligibleSteamAppId(surface.Binding?.AuthoritativePage) == appId;
    }

    internal static bool IsProfileArtworkPresentationTab(AddonQuickSettingsTabId tab) =>
        tab is AddonQuickSettingsTabId.Profile or AddonQuickSettingsTabId.Shortcut;

    internal static int? ProfileArtworkDecodePixelWidth(double widthDip, double rasterizationScale)
    {
        if (!double.IsFinite(widthDip) || widthDip <= 0
            || !double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            return null;

        var pixelWidth = Math.Ceiling(widthDip * rasterizationScale);
        return double.IsFinite(pixelWidth)
            ? (int)Math.Clamp(pixelWidth, 1, 1200)
            : null;
    }

    internal static bool IsProfileArtworkCacheCompatible(
        uint? cachedAppId,
        int cachedDecodePixelWidth,
        double cachedRasterizationScale,
        uint requestedAppId,
        int requestedDecodePixelWidth,
        double requestedRasterizationScale) =>
        cachedAppId == requestedAppId
        && cachedDecodePixelWidth == requestedDecodePixelWidth
        && Math.Abs(cachedRasterizationScale - requestedRasterizationScale) < 0.0001;

    internal static double? ProfileArtworkWidthFromConfiguredWindow(int windowWidthPx, double rasterizationScale)
    {
        if (windowWidthPx <= 0 || !double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
            return null;

        var windowWidthDip = windowWidthPx / rasterizationScale;
        var tabRailWidthDip = OverlayUiScale.TabRailWidthDip
            * OverlayUiScale.SizeFactorForRasterizationScale(rasterizationScale);
        var profileWidthDip = windowWidthDip - tabRailWidthDip;
        return double.IsFinite(profileWidthDip) && profileWidthDip > 0 ? profileWidthDip : null;
    }

    internal static bool IsProfileArtworkPrewarmIdentityCurrent(
        uint requestedAppId,
        uint? currentPrewarmAppId,
        bool isVisible,
        bool profilePageReceived,
        uint? authoritativeProfileAppId) =>
        currentPrewarmAppId == requestedAppId
        && (!isVisible || !profilePageReceived || authoritativeProfileAppId == requestedAppId);

    internal static bool ShouldStartVisibleArtworkFallbackAfterPrewarmFailure(
        int candidateCount,
        bool prewarmSucceeded,
        bool requestCanceled,
        bool isCurrentVisibleTarget) =>
        (candidateCount == 0 || !prewarmSucceeded) && !requestCanceled && isCurrentVisibleTarget;

    private (double RasterizationScale, int? DecodePixelWidth) CurrentProfileArtworkDecodeKey()
    {
        var scale = ProfileArtworkHost.XamlRoot?.RasterizationScale ?? _lastConfiguredDpi / 96.0;
        if (!double.IsFinite(scale) || scale <= 0)
            return (0, null);

        double width;
        if (!_isVisible)
        {
            // Hidden layout is not guaranteed to have run. Use its measured width when present,
            // otherwise derive the same tab-rail/content geometry from the configured HWND size.
            var measuredWidth = ProfileContentColumn.ActualWidth;
            width = double.IsFinite(measuredWidth) && measuredWidth > 0
                ? measuredWidth
                : ProfileArtworkWidthFromConfiguredWindow(_lastConfiguredOverlayWidthPx, scale) ?? 0;
        }
        else
        {
            // SizeChanged assigns this explicit width from the current profile column. During that
            // callback ActualWidth can still describe the preceding layout pass.
            var explicitWidth = ProfileArtworkHost.Width;
            width = double.IsFinite(explicitWidth) && explicitWidth > 0
                ? explicitWidth
                : ProfileArtworkHost.ActualWidth;
            if ((!double.IsFinite(width) || width <= 0)
                && ProfileArtworkWidthFromConfiguredWindow(_lastConfiguredOverlayWidthPx, scale) is { } configuredWidth)
                width = configuredWidth;
        }
        return (scale, ProfileArtworkDecodePixelWidth(width, scale));
    }

    private void ApplyProfileArtwork(uint appId, BitmapImage bitmap)
    {
        if (!IsCurrentProfileArtworkTarget(appId)) return;
        ProfileArtworkImage.Source = bitmap;
        ProfileArtworkHost.Visibility = Visibility.Visible;
        PageTitle.Visibility = Visibility.Collapsed;
    }

    private void CancelProfileArtworkRequest()
    {
        _profileArtworkRequestCancellation?.Cancel();
        _profileArtworkRequestCancellation?.Dispose();
        _profileArtworkRequestCancellation = null;
        _profileArtworkRequestIsPrewarm = false;
    }

    private void CancelXboxProfileArtworkRequest()
    {
        _xboxProfileArtworkRequestCancellation?.Cancel();
        _xboxProfileArtworkRequestCancellation?.Dispose();
        _xboxProfileArtworkRequestCancellation = null;
    }

    private void HideProfileArtworkPreservingCache()
    {
        var keepPrewarm = _profileArtworkRequestIsPrewarm
            && _profileArtworkPrewarmAppId == _profileArtworkRequestedAppId
            && _profileArtworkRequestCancellation is { IsCancellationRequested: false };
        if (!keepPrewarm)
        {
            CancelProfileArtworkRequest();
            _profileArtworkRequestedAppId = null;
        }
        CancelXboxProfileArtworkRequest();
        _xboxProfileArtworkRequestedKey = null;
        ProfileArtworkImage.Source = null;
        ProfileArtworkHost.Visibility = Visibility.Collapsed;
        PageTitle.Visibility = Visibility.Visible;
    }

    private void DiscardCachedProfileArtwork()
    {
        _profileArtworkCachedAppId = null;
        _profileArtworkCachedDecodePixelWidth = 0;
        _profileArtworkCachedRasterizationScale = 0;
        _profileArtworkCachedBitmap = null;
    }

    private void DiscardCachedXboxProfileArtwork()
    {
        _xboxProfileArtworkCachedLookupKey = null;
        _xboxProfileArtworkCachedSourceMarket = null;
        _xboxProfileArtworkCachedPurpose = null;
        _xboxProfileArtworkCachedDecodePixelWidth = 0;
        _xboxProfileArtworkCachedRasterizationScale = 0;
        _xboxProfileArtworkCachedBitmap = null;
    }

    private void DisposeXboxArtworkResolver()
    {
        CancelXboxProfileArtworkRequest();
        _xboxArtworkResolver?.Dispose();
        _xboxArtworkResolver = null;
    }

    private void ClearProfileArtwork()
    {
        _profileArtworkPrewarmAppId = null;
        CancelProfileArtworkRequest();
        HideProfileArtworkPreservingCache();
        DiscardCachedProfileArtwork();
        DiscardCachedXboxProfileArtwork();
    }

    private void ApplyProfilePresentation(QuickSettingsPageSnapshot? page)
    {
        if (_profileDetailRoot is null || _profileStatusMessage is null) return;

        if (_activeProfileInitialLoadPending)
        {
            _profileDetailRoot.Visibility = Visibility.Collapsed;
            _profileStatusMessage.Text = "Loading the active game profile.";
            _profileStatusMessage.Visibility = Visibility.Visible;
            return;
        }

        var activeProfileReady = HasRenderableActiveProfile(page);
        if (activeProfileReady)
        {
            _profileStatusMessage.Visibility = Visibility.Collapsed;
            _profileDetailRoot.Visibility = Visibility.Visible;
            return;
        }

        _profileDetailRoot.Visibility = Visibility.Collapsed;
        _profileStatusMessage.Text = ResolveProfileStatusMessage(page);
        _profileStatusMessage.Visibility = Visibility.Visible;
    }

    private void TryRequestNoRunningGameRecheck(QuickSettingsPageSnapshot? page)
    {
        if (!ShouldRequestNoRunningGameRecheck(
                page,
                _tabState.SelectedTab == AddonQuickSettingsTabId.Profile,
                _activeProfileInitialLoadPending,
                _noGameRecheckRequestedForThisEntry))
            return;

        _noGameRecheckRequestedForThisEntry = true;
        NoRunningGameProfileRecheckRequested?.Invoke();
    }

    internal static bool ShouldRequestNoRunningGameRecheck(
        QuickSettingsPageSnapshot? page,
        bool profileSelected,
        bool initialLoadPending,
        bool alreadyRequestedForEntry) =>
        profileSelected && !initialLoadPending && !alreadyRequestedForEntry && IsExactNoRunningGamePage(page);

    internal static bool IsExactNoRunningGamePage(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, Available: false, ProfileTarget: null, Message: NoActiveGameMessage };

    internal static bool HasRenderableActiveProfile(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, Available: true, ProfileTarget: { IsStructurallyValid: true } };

    internal static string ResolveProfileStatusMessage(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, ProfileTarget: null, Message: NoActiveGameMessage }
            ? NoActiveGameDisplayMessage
            : page?.Message ?? UnavailableProfileMessage;
}
