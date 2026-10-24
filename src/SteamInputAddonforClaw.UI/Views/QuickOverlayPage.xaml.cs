using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI;

namespace SteamInputAddonforClaw.Views;

public sealed partial class QuickOverlayPage : UserControl
{
    private sealed record TabOrderRowControls(SettingsCard Card, Button MoveEarlier, Button MoveLater);

    private static readonly AddonQuickSettingsTabId[] TabOrderTabIds = Enum.GetValues<AddonQuickSettingsTabId>();

    private IAddonFrontendControl? _frontend;
    private bool _applyingPowerSourcePreference;
    private bool _powerSourceMutationInProgress;
    private bool _powerSourceRefreshInProgress;
    private bool _discardInFlightPowerSourceRefresh;
    private bool _lastKnownQuickSettingsCurrentPowerSourceOnly;
    private readonly Dictionary<AddonQuickSettingsTabId, TabOrderRowControls> _tabOrderRows = new();
    private AddonQuickSettingsTabOrderSnapshot? _lastConfirmedTabOrder;
    private bool _tabOrderStateAvailable;
    private bool _tabOrderRefreshInProgress;
    private bool _tabOrderMoveInProgress;

    public QuickOverlayPage()
    {
        InitializeComponent();
        foreach (var tabId in TabOrderTabIds)
            _tabOrderRows.Add(tabId, CreateTabOrderRow(tabId));
    }

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, IAddonFrontendControl frontend)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
        SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
    }

    internal void Activate() => RequestRefresh();

    internal void RequestRefresh()
    {
        _ = RefreshQuickSettingsPowerSourcePreferenceAsync();
        _ = RefreshTabOrderAsync();
    }

    private async Task RefreshTabOrderAsync()
    {
        if (_frontend is null || _tabOrderMoveInProgress || _tabOrderRefreshInProgress) return;

        _tabOrderRefreshInProgress = true;
        UpdateTabOrderButtonStates();
        try
        {
            var snapshot = await _frontend.CaptureAddonQuickSettingsTabOrderAsync().ConfigureAwait(true);
            if (_tabOrderMoveInProgress)
                return;

            ApplyAuthoritativeTabOrder(snapshot);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Tab-order refresh failed.", exception);
            if (!_tabOrderMoveInProgress)
                MarkTabOrderUnavailable();
        }
        finally
        {
            _tabOrderRefreshInProgress = false;
            UpdateTabOrderButtonStates();
        }
    }

    private async Task MoveTabOrderAsync(AddonQuickSettingsTabId tabId, int delta)
    {
        if (_frontend is null || !_tabOrderStateAvailable || _tabOrderMoveInProgress || _tabOrderRefreshInProgress ||
            !CanMoveTab(tabId, delta))
        {
            return;
        }

        _tabOrderMoveInProgress = true;
        UpdateTabOrderButtonStates();
        try
        {
            var result = await _frontend.MoveAddonQuickSettingsTabAsync(
                new AddonQuickSettingsTabOrderMoveIntent(tabId, delta)).ConfigureAwait(true);
            if (!result.Succeeded)
                AppLog.Warn("QuickSettings", $"Tab-order move rejected: {result.FailureMessage ?? "no failure detail"}");

            ApplyAuthoritativeTabOrder(result.State);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Tab-order move failed; capturing the current Runtime order.", exception);
            try
            {
                // Deliberately call the capture seam directly while the move flag is set. The normal
                // refresh method short-circuits during a move and cannot provide this recovery readback.
                var snapshot = await _frontend.CaptureAddonQuickSettingsTabOrderAsync().ConfigureAwait(true);
                ApplyAuthoritativeTabOrder(snapshot);
            }
            catch (Exception readbackException)
            {
                AppLog.Warn("QuickSettings", "Tab-order recovery readback failed; keeping the last confirmed order.", readbackException);
                MarkTabOrderUnavailable();
            }
        }
        finally
        {
            _tabOrderMoveInProgress = false;
            UpdateTabOrderButtonStates();
        }
    }

    private TabOrderRowControls CreateTabOrderRow(AddonQuickSettingsTabId tabId)
    {
        var moveEarlier = CreateTabOrderButton(tabId, -1);
        var moveLater = CreateTabOrderButton(tabId, 1);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        buttons.Children.Add(moveEarlier);
        buttons.Children.Add(moveLater);

        return new TabOrderRowControls(new SettingsCard { Content = buttons }, moveEarlier, moveLater);
    }

    private Button CreateTabOrderButton(AddonQuickSettingsTabId tabId, int delta)
    {
        var isEarlier = delta < 0;
        var label = AddonQuickSettingsShellContract.LabelFor(tabId);
        var accessibleName = MainUiStrings.Format(
            isEarlier ? "QuickOverlay_TabOrder_MoveUp_A11y" : "QuickOverlay_TabOrder_MoveDown_A11y",
            isEarlier ? "Move {0} up" : "Move {0} down",
            label);
        var button = new Button
        {
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = isEarlier ? "\uE74A" : "\uE74B",
                FontSize = 20,
            },
            Width = 40,
            Height = 32,
            Tag = tabId,
        };
        AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, accessibleName);
        button.Click += (_, _) => _ = MoveTabOrderAsync(tabId, delta);
        return button;
    }

    private void ApplyAuthoritativeTabOrder(AddonQuickSettingsTabOrderSnapshot? snapshot)
    {
        if (snapshot is null || !snapshot.Available || snapshot.Rows is null ||
            snapshot.Rows.Count != TabOrderTabIds.Length ||
            !AddonQuickSettingsTabOrderContract.TryNormalize(
                snapshot.Rows.Select(row => row.TabId).ToArray(), out _) ||
            snapshot.Rows.Any(row => string.IsNullOrWhiteSpace(row.Label)))
        {
            MarkTabOrderUnavailable();
            return;
        }

        _lastConfirmedTabOrder = snapshot;
        _tabOrderStateAvailable = true;
        QuickOverlayTabOrderExpander.Items.Clear();
        QuickOverlayTabOrderStatusText.Visibility = Visibility.Collapsed;
        foreach (var row in snapshot.Rows)
        {
            var controls = _tabOrderRows[row.TabId];
            controls.Card.Header = row.Label;
            UpdateTabOrderButtonNames(row.TabId, row.Label, controls);
            controls.MoveEarlier.IsEnabled = row.CanMoveEarlier && !_tabOrderMoveInProgress && !_tabOrderRefreshInProgress;
            controls.MoveLater.IsEnabled = row.CanMoveLater && !_tabOrderMoveInProgress && !_tabOrderRefreshInProgress;
            QuickOverlayTabOrderExpander.Items.Add(controls.Card);
        }
    }

    private void UpdateTabOrderButtonNames(
        AddonQuickSettingsTabId tabId,
        string label,
        TabOrderRowControls controls)
    {
        var earlierName = MainUiStrings.Format("QuickOverlay_TabOrder_MoveUp_A11y", "Move {0} up", label);
        var laterName = MainUiStrings.Format("QuickOverlay_TabOrder_MoveDown_A11y", "Move {0} down", label);
        AutomationProperties.SetName(controls.MoveEarlier, earlierName);
        AutomationProperties.SetName(controls.MoveLater, laterName);
        ToolTipService.SetToolTip(controls.MoveEarlier, earlierName);
        ToolTipService.SetToolTip(controls.MoveLater, laterName);
        controls.MoveEarlier.Tag = tabId;
        controls.MoveLater.Tag = tabId;
    }

    private bool CanMoveTab(AddonQuickSettingsTabId tabId, int delta)
    {
        var row = _lastConfirmedTabOrder?.Rows.FirstOrDefault(candidate => candidate.TabId == tabId);
        return row is not null && (delta < 0 ? row.CanMoveEarlier : delta > 0 && row.CanMoveLater);
    }

    private void MarkTabOrderUnavailable()
    {
        _tabOrderStateAvailable = false;
        if (_lastConfirmedTabOrder is null)
        {
            QuickOverlayTabOrderExpander.Items.Clear();
            QuickOverlayTabOrderStatusText.Text = MainUiStrings.Get(
                "QuickOverlay_TabOrder_Unavailable",
                "Tab order is currently unavailable.");
            QuickOverlayTabOrderStatusText.Visibility = Visibility.Visible;
            QuickOverlayTabOrderExpander.Items.Add(QuickOverlayTabOrderStatusText);
        }
        UpdateTabOrderButtonStates();
    }

    private void UpdateTabOrderButtonStates()
    {
        foreach (var (tabId, controls) in _tabOrderRows)
        {
            var row = _lastConfirmedTabOrder?.Rows.FirstOrDefault(candidate => candidate.TabId == tabId);
            var available = _tabOrderStateAvailable && row is not null &&
                            !_tabOrderMoveInProgress && !_tabOrderRefreshInProgress;
            controls.MoveEarlier.IsEnabled = available && row!.CanMoveEarlier;
            controls.MoveLater.IsEnabled = available && row!.CanMoveLater;
        }
    }

    private async Task RefreshQuickSettingsPowerSourcePreferenceAsync()
    {
        if (_frontend is null || _powerSourceMutationInProgress || _powerSourceRefreshInProgress) return;

        _powerSourceRefreshInProgress = true;
        try
        {
            var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
            if (_powerSourceMutationInProgress || _discardInFlightPowerSourceRefresh)
                return;

            _lastKnownQuickSettingsCurrentPowerSourceOnly = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Current power-source preference refresh failed.", exception);
        }
        finally
        {
            _powerSourceRefreshInProgress = false;
            _discardInFlightPowerSourceRefresh = false;
        }
    }

    private void SetQuickSettingsPowerSourceToggle(bool enabled)
    {
        _applyingPowerSourcePreference = true;
        try { QuickSettingsPowerSourceToggleSwitch.IsOn = enabled; }
        finally { _applyingPowerSourcePreference = false; }
    }

    private async void QuickSettingsPowerSourceToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingPowerSourcePreference || _frontend is null || _powerSourceMutationInProgress) return;

        // A snapshot started before this edit must not overwrite its authoritative mutation result.
        _discardInFlightPowerSourceRefresh = _powerSourceRefreshInProgress;
        _powerSourceMutationInProgress = true;
        QuickSettingsPowerSourceToggleSwitch.IsEnabled = false;
        try
        {
            var settings = await _frontend.SetQuickSettingsCurrentPowerSourceOnlyAsync(
                QuickSettingsPowerSourceToggleSwitch.IsOn).ConfigureAwait(true);
            _lastKnownQuickSettingsCurrentPowerSourceOnly = settings.QuickSettingsCurrentPowerSourceOnly;
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
        catch (Exception exception)
        {
            AppLog.Warn("QuickSettings", "Current power-source preference update failed.", exception);
            bool? refreshedValue = null;
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
                refreshedValue = bootstrap.Settings.QuickSettingsCurrentPowerSourceOnly;
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("QuickSettings", "Current power-source preference rollback failed.", refreshException);
            }

            _lastKnownQuickSettingsCurrentPowerSourceOnly = ResolveQuickSettingsPowerSourcePreference(
                _lastKnownQuickSettingsCurrentPowerSourceOnly,
                refreshedValue);
            SetQuickSettingsPowerSourceToggle(_lastKnownQuickSettingsCurrentPowerSourceOnly);
        }
        finally
        {
            _powerSourceMutationInProgress = false;
            QuickSettingsPowerSourceToggleSwitch.IsEnabled = true;
        }
    }

    internal static bool ResolveQuickSettingsPowerSourcePreference(bool lastKnownValue, bool? refreshedValue) =>
        refreshedValue ?? lastKnownValue;
}
