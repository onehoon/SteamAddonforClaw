using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI;

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

    public WindowsTweaksPage()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(
            DisableEdgeBackgroundDescriptionText,
            MainUiStrings.Get(
                "Tweaks_DisableEdgeBackgroundDescription/Text",
                "Disable Edge Startup Boost and background mode; existing processes may continue until closed."));
    }

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
            EntryPromptText.Text = MainUiStrings.Get("Tweaks_Loading", "Reading current Windows settings…");
            EntryPromptText.Visibility = Visibility.Visible;
            var today = DateOnly.FromDateTime(DateTime.Now);
            var snapshot = await CaptureAsync();
            if (ShouldShowWarning(today, snapshot.WarningAcknowledgedLocalDate, _acceptedWarningDateThisProcess))
            {
                TweaksPanel.Visibility = Visibility.Collapsed;
                EntryPromptText.Text = MainUiStrings.Get(
                    "Tweaks_AcceptSafetyPrompt",
                    "Review and accept the Windows Tweaks safety notice to view these controls.");
                EntryPromptText.Visibility = Visibility.Visible;
                if (!await ShowDailyWarningAsync()) return;

                _acceptedWarningDateThisProcess = today;
                var saved = await _frontend!.AcknowledgeWindowsTweaksWarningAsync(today);
                if (!saved)
                    SetOperationMessage(MainUiStrings.Get(
                        "Tweaks_AcknowledgementSaveFailed",
                        "The notice was accepted, but its date could not be saved. It may be shown again after the app restarts."));
                snapshot = await CaptureAsync();
            }

            EntryPromptText.Visibility = Visibility.Collapsed;
            TweaksPanel.Visibility = Visibility.Visible;
            ApplySnapshot(snapshot);
        }
        catch
        {
            EntryPromptText.Text = MainUiStrings.Get(
                "Tweaks_LoadFailed",
                "Windows Tweaks could not be loaded. Select this tab again to retry.");
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
        var content = MainUiStrings.Get(
            "Tweaks_SafetyNotice_Body",
            "Some changes need a Windows restart or Explorer restart to become fully effective. This app will not restart either automatically.\n\n"
            + "Memory Integrity strengthens kernel protection. Disabling it reduces protection. Enabling it can make incompatible drivers or apps malfunction and, rarely, prevent normal boot.\n\n"
            + "Virtual Machine Platform may be required by virtualization-dependent software; disabling it can stop that software from working.\n\n"
            + "Disabling password/sign-in on wake lets someone with physical access resume directly to the desktop.\n\n"
            + "These settings are reversible where Windows and policy permit. Windows policy or firmware locks can prevent a requested change. Registry policy controls may be restricted, and removing startup delay can make startup apps launch closer together.");
        var dialog = new ContentDialog
        {
            Title = MainUiStrings.Get("Tweaks_SafetyNotice_Title", "Windows Tweaks safety notice"),
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
            PrimaryButtonText = MainUiStrings.Get("Tweaks_SafetyNotice_Accept", "I understand"),
            CloseButtonText = MainUiStrings.Get("Tweaks_SafetyNotice_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
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
                ApplyTweakRow(row);
            }
        }
        finally { _applyingSnapshot = false; }
    }

    private void ApplyTweakRow(FrontendWindowsTweakSnapshot row)
    {
        var (card, toggle, stateText) = GetControls(row.Id);
        card.Visibility = Visibility.Visible;
        toggle.Visibility = row.ConfiguredEnabled.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (row.ConfiguredEnabled is { } configured)
            toggle.IsOn = configured;
        toggle.IsEnabled = row.CanChange && !_mutatingIds.Contains(row.Id);
        SetStateLabel(stateText, FormatState(row));
    }

    internal static FrontendWindowsTweaksSnapshot ReplaceTweak(
        FrontendWindowsTweaksSnapshot snapshot,
        FrontendWindowsTweakSnapshot updatedTweak) =>
        snapshot with
        {
            Tweaks = snapshot.Tweaks.Select(row => row.Id == updatedTweak.Id ? updatedTweak : row).ToArray(),
        };

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
        SetStateLabel(controls.State, MainUiStrings.Get("Tweaks_Updating", "Updating…"));
        try
        {
            var result = await _frontend.SetWindowsTweakAsync(id, requested);
            _snapshot = ReplaceTweak(_snapshot, result.UpdatedTweak);
            _applyingSnapshot = true;
            ApplyTweakRow(result.UpdatedTweak);
            _applyingSnapshot = false;
            SetOperationMessage(result.Succeeded ? null : result.FailureMessage);
        }
        catch
        {
            _applyingSnapshot = true;
            ApplyTweakRow(current);
            _applyingSnapshot = false;
            SetOperationMessage(MainUiStrings.Get(
                "Tweaks_ChangeUnconfirmed",
                "The change could not be confirmed. Reopen the Tweaks tab to refresh Windows state."));
        }
        finally
        {
            _mutatingIds.Remove(id);
            var row = _snapshot.Tweaks.SingleOrDefault(candidate => candidate.Id == id);
            if (row is not null)
            {
                _applyingSnapshot = true;
                ApplyTweakRow(row);
                _applyingSnapshot = false;
            }
            DrainQueuedRefresh();
        }
    }

    internal static string? FormatState(FrontendWindowsTweakSnapshot row)
    {
        return row.State switch
        {
            FrontendWindowsTweakState.Enabled or FrontendWindowsTweakState.Disabled => FormatVerifiedState(row),
            FrontendWindowsTweakState.EnablePendingRestart
                when row.Id == FrontendWindowsTweakId.MemoryIntegrity
                    && row.ConfiguredEnabled == true
                    && row.EffectiveEnabled == false => MainUiStrings.Get(
                        "Tweaks_State_ConfiguredNotRunning",
                        "Configured — not running"),
            FrontendWindowsTweakState.EnablePendingRestart or FrontendWindowsTweakState.DisablePendingRestart =>
                MainUiStrings.Get("Tweaks_State_RestartRequired", "Restart required"),
            FrontendWindowsTweakState.ExplorerRestartPending =>
                MainUiStrings.Get("Tweaks_State_ExplorerRestartRequired", "Explorer restart required"),
            FrontendWindowsTweakState.Unsupported => MainUiStrings.Get("Tweaks_State_Unsupported", "Unsupported"),
            FrontendWindowsTweakState.Managed => MainUiStrings.Get("Tweaks_State_Managed", "Managed"),
            FrontendWindowsTweakState.Mixed => MainUiStrings.Get("Tweaks_State_MixedApply", "Mixed — turn On to apply"),
            _ => MainUiStrings.Get("Tweaks_State_Unavailable", "Unavailable"),
        };
    }

    private static string? FormatVerifiedState(FrontendWindowsTweakSnapshot row)
    {
        if (row.Id is FrontendWindowsTweakId.MemoryIntegrity or FrontendWindowsTweakId.VirtualMachinePlatform)
            return null;

        var configuredBehaviorEnabled = row.State == FrontendWindowsTweakState.Enabled;
        var titleDisablesBehavior = row.Id is FrontendWindowsTweakId.DisablePasswordOnWake
            or FrontendWindowsTweakId.DisableEdgeBackground
            or FrontendWindowsTweakId.DisableStartupAppDelay;
        var behaviorEnabled = titleDisablesBehavior ? !configuredBehaviorEnabled : configuredBehaviorEnabled;
        return MainUiStrings.Get(
            behaviorEnabled ? "Tweaks_State_Enabled" : "Tweaks_State_Disabled",
            behaviorEnabled ? "Enabled" : "Disabled");
    }

    private static void SetStateLabel(TextBlock stateText, string? message)
    {
        var hasMessage = !string.IsNullOrWhiteSpace(message);
        stateText.Text = hasMessage ? message! : string.Empty;
        stateText.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(stateText, hasMessage ? message! : string.Empty);
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
        FrontendWindowsTweakId.DisableEdgeBackground => (DisableEdgeBackgroundCard, DisableEdgeBackgroundSwitch, DisableEdgeBackgroundState),
        FrontendWindowsTweakId.DisableStartupAppDelay => (DisableStartupAppDelayCard, DisableStartupAppDelaySwitch, DisableStartupAppDelayState),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private FrontendWindowsTweakId GetId(ToggleSwitch toggle) => toggle switch
    {
        var value when ReferenceEquals(value, MemoryIntegritySwitch) => FrontendWindowsTweakId.MemoryIntegrity,
        var value when ReferenceEquals(value, VirtualMachinePlatformSwitch) => FrontendWindowsTweakId.VirtualMachinePlatform,
        var value when ReferenceEquals(value, InstantContextMenusSwitch) => FrontendWindowsTweakId.InstantContextMenus,
        var value when ReferenceEquals(value, ClassicContextMenuSwitch) => FrontendWindowsTweakId.ClassicContextMenu,
        var value when ReferenceEquals(value, DisablePasswordOnWakeSwitch) => FrontendWindowsTweakId.DisablePasswordOnWake,
        var value when ReferenceEquals(value, DisableEdgeBackgroundSwitch) => FrontendWindowsTweakId.DisableEdgeBackground,
        var value when ReferenceEquals(value, DisableStartupAppDelaySwitch) => FrontendWindowsTweakId.DisableStartupAppDelay,
        _ => throw new ArgumentOutOfRangeException(nameof(toggle)),
    };
}
