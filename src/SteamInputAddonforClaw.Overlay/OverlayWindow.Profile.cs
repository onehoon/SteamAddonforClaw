using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private CancellationTokenSource? _profileArtworkRequestCancellation;

    internal event Action? NoRunningGameProfileRecheckRequested;

    private void ProfileContentColumn_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var (width, height) = CalculateProfileArtworkSize(e.NewSize.Width);
        if (Math.Abs(ProfileArtworkHost.Width - width) < 0.0001
            && Math.Abs(ProfileArtworkHost.Height - height) < 0.0001)
            return;

        ProfileArtworkHost.Width = width;
        ProfileArtworkHost.Height = height;
        ProfileArtworkHost.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, width, height)
        };
    }

    internal static (double Width, double Height) CalculateProfileArtworkSize(double rightColumnWidth)
    {
        var width = double.IsFinite(rightColumnWidth)
            ? Math.Max(0, rightColumnWidth)
            : 0;
        return (width, width * 430.0 / 920.0);
    }

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
        _profileStatusMessage.FontSize = 16;
        root.Children.Add(_profileStatusMessage);
        return root;
    }

    internal bool TryHandleBack() => TryHandleSettingBack();

    private void OnProfileTabSelectionChanged(bool selected)
    {
        if (!selected)
        {
            _noGameRecheckRequestedForThisEntry = false;
            ClearProfileArtwork();
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
        _noGameRecheckRequestedForThisEntry = false;
        _activeProfileInitialLoadPending = true;
        var page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, "Loading the active game profile.");
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ApplyProfilePresentation(page);
        UpdateProfileArtwork(page);
    }

    internal void ApplyActiveProfilePage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != QuickSettingsPageId.Profile) return;
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;

        _activeProfileInitialLoadPending = false;
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ApplyProfilePresentation(page);
        UpdateProfileArtwork(page);
        TryRequestNoRunningGameRecheck(page);
    }

    private void UpdateProfileArtwork(QuickSettingsPageSnapshot? page)
    {
        var appId = _tabState.SelectedTab == AddonQuickSettingsTabId.Profile
            ? SteamGameArtworkResolver.EligibleSteamAppId(page)
            : null;
        if (appId is null)
        {
            ClearProfileArtwork();
            return;
        }

        if (_profileArtworkRequestedAppId == appId)
            return;

        ClearProfileArtwork();
        _profileArtworkRequestedAppId = appId;
        var requestCancellation = new CancellationTokenSource();
        _profileArtworkRequestCancellation = requestCancellation;
        _ = ResolveAndApplyProfileArtworkAsync(appId.Value, page!, requestCancellation);
    }

    private async Task ResolveAndApplyProfileArtworkAsync(
        uint appId,
        QuickSettingsPageSnapshot page,
        CancellationTokenSource requestCancellation)
    {
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

                IReadOnlyList<string> paths;
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

                var bitmap = candidateCount == 0
                    ? null
                    : await TryLoadProfileArtworkAsync(paths, cancellationToken);

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
                    ProfileArtworkImage.Source = bitmap;
                    ProfileArtworkHost.Visibility = Visibility.Visible;
                    PageTitle.Visibility = Visibility.Collapsed;
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
                OverlayLog.Warn("ProfileArtwork", "Local artwork unavailable after the bounded retry; keeping the Profile title visible.",
                    null, ("AppId", appId), ("CandidateCount", candidateCount),
                    ("Stage", failureStage), ("RetryUsed", retryUsed));
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
        CancellationToken cancellationToken)
    {
        var scale = ProfileArtworkHost.XamlRoot?.RasterizationScale ?? 1.0;
        var widthDip = ProfileArtworkHost.ActualWidth;
        var decodePixelWidth = widthDip > 0
            ? Math.Clamp((int)Math.Ceiling(widthDip * scale), 1, 1200)
            : 960;

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
        if (!_isVisible || _tabState.SelectedTab != AddonQuickSettingsTabId.Profile
            || _profileArtworkRequestedAppId != appId
            || !_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface))
            return false;

        return SteamGameArtworkResolver.EligibleSteamAppId(surface.Binding?.AuthoritativePage) == appId;
    }

    private void ClearProfileArtwork()
    {
        _profileArtworkRequestCancellation?.Cancel();
        _profileArtworkRequestCancellation?.Dispose();
        _profileArtworkRequestCancellation = null;
        _profileArtworkRequestedAppId = null;
        ProfileArtworkImage.Source = null;
        ProfileArtworkHost.Visibility = Visibility.Collapsed;
        PageTitle.Visibility = Visibility.Visible;
    }

    private void ApplyProfilePresentation(QuickSettingsPageSnapshot? page)
    {
        if (_profileDetailRoot is null || _profileStatusMessage is null) return;

        if (_activeProfileInitialLoadPending)
        {
            _profileDetailRoot.Visibility = Visibility.Collapsed;
            _profileStatusMessage.Visibility = Visibility.Collapsed;
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
