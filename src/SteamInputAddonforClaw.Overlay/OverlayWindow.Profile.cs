using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
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
    private CancellationTokenSource? _profileArtworkRequestCancellation;
    private uint? _profileArtworkCachedAppId;
    private int _profileArtworkCachedDecodePixelWidth;
    private double _profileArtworkCachedRasterizationScale;
    private BitmapImage? _profileArtworkCachedBitmap;
    private XamlRoot? _profileArtworkObservedXamlRoot;

    internal event Action? NoRunningGameProfileRecheckRequested;

    private void ProfileContentColumn_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var (width, height) = CalculateProfileArtworkSize(e.NewSize.Width);
        if (ShouldUpdateProfileArtworkSize(ProfileArtworkHost.Width, ProfileArtworkHost.Height, width, height))
        {
            ProfileArtworkHost.Width = width;
            ProfileArtworkHost.Height = height;
            ProfileArtworkHost.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, width, height)
            };
        }

        if (double.IsFinite(e.NewSize.Width) && e.NewSize.Width > 0)
            ReevaluateProfileArtworkForCurrentPage();
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

    internal static (double Width, double Height) CalculateProfileArtworkSize(double rightColumnWidth)
    {
        var width = double.IsFinite(rightColumnWidth)
            ? Math.Max(0, rightColumnWidth)
            : 0;
        return (width, width * 430.0 / 920.0);
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
        if (!_isVisible || !_profilePageReceivedThisShow)
        {
            HideProfileArtworkPreservingCache();
            return;
        }

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

        if (_profileArtworkRequestedAppId == appId
            && _profileArtworkRequestCancellation is { IsCancellationRequested: false }
            && _profileArtworkRequestedDecodePixelWidth == decodePixelWidth
            && Math.Abs(_profileArtworkRequestedRasterizationScale - rasterizationScale) < 0.0001)
            return;

        HideProfileArtworkPreservingCache();
        _profileArtworkRequestedAppId = appId;
        _profileArtworkRequestedDecodePixelWidth = decodePixelWidth;
        _profileArtworkRequestedRasterizationScale = rasterizationScale;
        var requestCancellation = new CancellationTokenSource();
        _profileArtworkRequestCancellation = requestCancellation;
        OverlayLog.Debug("Performance", "OverlayArtwork",
            ("AppId", appId.Value), ("Cache", "Miss"), ("DecodePixelWidth", decodePixelWidth),
            ("RasterizationScale", rasterizationScale), ("Stage", "Lookup"), ("RetryUsed", false));
        _ = ResolveAndApplyProfileArtworkAsync(appId.Value, page!, requestCancellation);
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

    private (double RasterizationScale, int? DecodePixelWidth) CurrentProfileArtworkDecodeKey()
    {
        if (ProfileArtworkHost.XamlRoot is not { } xamlRoot)
            return (0, null);

        var scale = xamlRoot.RasterizationScale;
        // SizeChanged assigns this explicit width from the current profile column. During that
        // callback ActualWidth can still describe the preceding layout pass.
        var explicitWidth = ProfileArtworkHost.Width;
        var width = double.IsFinite(explicitWidth)
            ? explicitWidth
            : ProfileArtworkHost.ActualWidth;
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
    }

    private void HideProfileArtworkPreservingCache()
    {
        CancelProfileArtworkRequest();
        _profileArtworkRequestedAppId = null;
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

    private void ClearProfileArtwork()
    {
        HideProfileArtworkPreservingCache();
        DiscardCachedProfileArtwork();
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
