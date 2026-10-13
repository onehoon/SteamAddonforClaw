using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Views;

public sealed partial class WindowsTweaksPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendWindowsTweaksSnapshot _snapshot = FrontendWindowsTweaksSnapshot.Unavailable;
    private DateOnly? _acceptedWarningDateThisProcess;
    private readonly HashSet<FrontendWindowsTweakId> _mutatingIds = [];
    private bool _applyingSnapshot;
    private bool _activationInProgress;
    private bool _refreshInProgress;
    private bool _refreshQueued;

    public WindowsTweaksPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));

    internal void Activate()
    {
        if (_frontend is null) return;
        if (_activationInProgress)
        {
            _refreshQueued = true;
            return;
        }
        _activationInProgress = true;
        _ = ActivateCoreAsync();
    }

    internal void RequestRefresh()
    {
        if (_frontend is null || TweaksPanel.Visibility != Visibility.Visible) return;
        if (_activationInProgress || _refreshInProgress || _mutatingIds.Count > 0)
        {
            _refreshQueued = true;
            return;
        }
        _ = RefreshCoreAsync();
    }

    internal static bool ShouldShowWarning(DateOnly localDate, DateOnly? persistedDate, DateOnly? acceptedThisProcess) =>
        persistedDate != localDate && acceptedThisProcess != localDate;

    internal static Visibility GetCardVisibility(FrontendWindowsTweaksSnapshot snapshot, FrontendWindowsTweakId id) =>
        snapshot.Tweaks.Any(candidate => candidate.Id == id) ? Visibility.Visible : Visibility.Collapsed;

    private async Task ActivateCoreAsync()
    {
        try
        {
            SetOperationMessage(null);
            EntryPromptText.Text = "Reading current Windows settings…";
            EntryPromptText.Visibility = Visibility.Visible;
            var today = DateOnly.FromDateTime(DateTime.Now);
            var snapshot = await CaptureAsync();
            if (ShouldShowWarning(today, snapshot.WarningAcknowledgedLocalDate, _acceptedWarningDateThisProcess))
            {
                TweaksPanel.Visibility = Visibility.Collapsed;
                EntryPromptText.Text = "Review and accept the Windows Tweaks safety notice to view these controls.";
                EntryPromptText.Visibility = Visibility.Visible;
                if (!await ShowDailyWarningAsync()) return;

                _acceptedWarningDateThisProcess = today;
                var saved = await _frontend!.AcknowledgeWindowsTweaksWarningAsync(today);
                if (!saved)
                    SetOperationMessage("The notice was accepted, but its date could not be saved. It may be shown again after the app restarts.");
                snapshot = await CaptureAsync();
            }

            EntryPromptText.Visibility = Visibility.Collapsed;
            TweaksPanel.Visibility = Visibility.Visible;
            ApplySnapshot(snapshot);
        }
        catch
        {
            EntryPromptText.Text = "Windows Tweaks could not be loaded. Select this tab again to retry.";
            EntryPromptText.Visibility = Visibility.Visible;
            TweaksPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _activationInProgress = false;
            DrainQueuedRefresh();
        }
    }

    private async Task<bool> ShowDailyWarningAsync()
    {
        var content = "Some changes need a Windows restart or Explorer restart to become fully effective. This app will not restart either automatically.\n\n"
            + "Memory Integrity strengthens kernel protection. Disabling it reduces protection. Enabling it can make incompatible drivers or apps malfunction and, rarely, prevent normal boot.\n\n"
            + "Virtual Machine Platform may be required by virtualization-dependent software; disabling it can stop that software from working.\n\n"
            + "Disabling password/sign-in on wake lets someone with physical access resume directly to the desktop.\n\n"
            + "These settings are reversible where Windows and policy permit. Windows policy or firmware locks can prevent a requested change.";
        var dialog = new ContentDialog
        {
            Title = "Windows Tweaks safety notice",
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
            PrimaryButtonText = "I understand",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<FrontendWindowsTweaksSnapshot> CaptureAsync()
    {
        try
        {
            _snapshot = await _frontend!.CaptureWindowsTweaksAsync();
            return _snapshot;
        }
        catch
        {
            _snapshot = FrontendWindowsTweaksSnapshot.Unavailable;
            return _snapshot;
        }
    }

    private async Task<FrontendWindowsTweaksSnapshot?> TryCaptureAfterMutationFailureAsync()
    {
        try { return await _frontend!.CaptureWindowsTweaksAsync(); }
        catch { return null; }
    }

    private async Task RefreshCoreAsync()
    {
        _refreshInProgress = true;
        try { ApplySnapshot(await CaptureAsync()); }
        finally
        {
            _refreshInProgress = false;
            DrainQueuedRefresh();
        }
    }

    private void DrainQueuedRefresh()
    {
        if (!_refreshQueued || _activationInProgress || _refreshInProgress || _mutatingIds.Count > 0)
            return;
        _refreshQueued = false;
        RequestRefresh();
    }

    private void ApplySnapshot(FrontendWindowsTweaksSnapshot snapshot)
    {
        _snapshot = snapshot;
        _applyingSnapshot = true;
        try
        {
            foreach (var definition in FrontendWindowsTweakCatalog.All)
            {
                var (card, toggle, stateText) = GetControls(definition.Id);
                var row = snapshot.Tweaks.SingleOrDefault(candidate => candidate.Id == definition.Id);
                card.Visibility = GetCardVisibility(snapshot, definition.Id);
                if (row is null)
                    continue;
                toggle.Visibility = row.ConfiguredEnabled.HasValue ? Visibility.Visible : Visibility.Collapsed;
                if (row.ConfiguredEnabled is { } configured)
                    toggle.IsOn = configured;
                toggle.IsEnabled = row.CanChange && !_mutatingIds.Contains(definition.Id);
                stateText.Text = FormatState(row);
            }
        }
        finally { _applyingSnapshot = false; }
    }

    private async void TweakSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingSnapshot || sender is not ToggleSwitch toggle || _frontend is null)
            return;
        var id = GetId(toggle);
        var current = _snapshot.Tweaks.SingleOrDefault(row => row.Id == id);
        if (current is null || !current.CanChange || !current.ConfiguredEnabled.HasValue)
        {
            ApplySnapshot(_snapshot);
            return;
        }

        var requested = toggle.IsOn;
        _mutatingIds.Add(id);
        _applyingSnapshot = true;
        toggle.IsOn = current.ConfiguredEnabled.Value;
        toggle.IsEnabled = false;
        _applyingSnapshot = false;
        var controls = GetControls(id);
        controls.State.Text = "Updating…";
        try
        {
            var result = await _frontend.SetWindowsTweakAsync(id, requested);
            ApplySnapshot(result.Snapshot);
            SetOperationMessage(result.Succeeded ? null : result.FailureMessage);
        }
        catch
        {
            var actual = await TryCaptureAfterMutationFailureAsync();
            if (actual is not null)
            {
                _snapshot = actual;
                ApplySnapshot(actual);
            }
            else
            {
                ApplySnapshot(_snapshot);
            }
            SetOperationMessage("The change could not be confirmed. The displayed value is the latest verified Windows state.");
        }
        finally
        {
            _mutatingIds.Remove(id);
            ApplySnapshot(_snapshot);
            DrainQueuedRefresh();
        }
    }

    private static string FormatState(FrontendWindowsTweakSnapshot row)
    {
        var label = row.State switch
        {
            FrontendWindowsTweakState.Enabled => "On",
            FrontendWindowsTweakState.Disabled => "Off",
            FrontendWindowsTweakState.EnablePendingRestart => "Enabled — restart required",
            FrontendWindowsTweakState.DisablePendingRestart => "Disabled — restart required",
            FrontendWindowsTweakState.ExplorerRestartPending => row.ConfiguredEnabled == true
                ? "Enabled — Explorer restart required"
                : "Disabled — Explorer restart required",
            FrontendWindowsTweakState.Unsupported => "Unsupported",
            FrontendWindowsTweakState.Managed => "Managed by Windows",
            FrontendWindowsTweakState.Mixed => "Mixed — turn On to apply both Faster Menus values",
            _ => "State unavailable",
        };
        return string.IsNullOrWhiteSpace(row.Detail)
            || label.Contains(row.Detail.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
            ? label
            : $"{label} · {row.Detail}";
    }

    private void SetOperationMessage(string? message)
    {
        OperationMessageText.Text = message ?? string.Empty;
        OperationMessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private (SettingsCard Card, ToggleSwitch Toggle, TextBlock State) GetControls(FrontendWindowsTweakId id) => id switch
    {
        FrontendWindowsTweakId.MemoryIntegrity => (MemoryIntegrityCard, MemoryIntegritySwitch, MemoryIntegrityState),
        FrontendWindowsTweakId.VirtualMachinePlatform => (VirtualMachinePlatformCard, VirtualMachinePlatformSwitch, VirtualMachinePlatformState),
        FrontendWindowsTweakId.InstantContextMenus => (InstantContextMenusCard, InstantContextMenusSwitch, InstantContextMenusState),
        FrontendWindowsTweakId.ClassicContextMenu => (ClassicContextMenuCard, ClassicContextMenuSwitch, ClassicContextMenuState),
        FrontendWindowsTweakId.DisablePasswordOnWake => (DisablePasswordOnWakeCard, DisablePasswordOnWakeSwitch, DisablePasswordOnWakeState),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private FrontendWindowsTweakId GetId(ToggleSwitch toggle) => toggle switch
    {
        var value when ReferenceEquals(value, MemoryIntegritySwitch) => FrontendWindowsTweakId.MemoryIntegrity,
        var value when ReferenceEquals(value, VirtualMachinePlatformSwitch) => FrontendWindowsTweakId.VirtualMachinePlatform,
        var value when ReferenceEquals(value, InstantContextMenusSwitch) => FrontendWindowsTweakId.InstantContextMenus,
        var value when ReferenceEquals(value, ClassicContextMenuSwitch) => FrontendWindowsTweakId.ClassicContextMenu,
        var value when ReferenceEquals(value, DisablePasswordOnWakeSwitch) => FrontendWindowsTweakId.DisablePasswordOnWake,
        _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
    };
}
