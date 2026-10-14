using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SteamInputAddonforClaw.Views;

/// <summary>
/// The Controller page's Button Mapping surface (App UI PR-C). Both physical front buttons -- Gamebar
/// Button first, Center M Button second -- each expose one action for the Normal domain and one for
/// the Steam Game / Big Picture domain. Every ComboBox is populated from
/// <see cref="FrontButtonActionCapabilities.ActionsFor"/> (the same table the runtime dispatcher
/// validates against) and the partner button's currently selected action is disabled in the same
/// domain so a same-domain duplicate can never be selected. There is no <c>None</c>, no blank steady
/// state, and no per-button on/off switch.
/// </summary>
/// <remarks>
/// Editing is whole-record: any change raises the complete desired <see cref="FrontButtonMappingSettings"/>
/// and the host (MainWindow) owns the single ordered save chain -- this page never persists.
/// </remarks>
public sealed partial class ControllerPage : UserControl
{
    private FrontButtonMappingSettings _mapping = FrontButtonMappingSettings.Default;
    private BackButtonMappingSettings _backButtonMapping = BackButtonMappingSettings.Default;
    private ControllerLedSettings _controllerLed = ControllerLedSettings.Default;
    private bool _available;
    private bool _backButtonAvailable;
    private bool _controllerLedAvailable;
    private BindingEditor[] _editors = [];
    private IAddonFrontendControl? _frontend;
    private FrontendControllerVibrationStrengthSnapshot _vibrationSnapshot =
        FrontendControllerVibrationStrengthSnapshot.Unavailable();
    private ControllerVibrationStrengthDebounce? _vibrationDebounce;
    private (int Left, int Right)? _vibrationDraft;
    private Slider? _vibrationPointerSlider;
    private uint? _vibrationPointerId;
    private bool _isRenderingVibration;
    private bool _vibrationPointerGestureInProgress;
    private bool _vibrationMutationInProgress;
    private bool _vibrationTestInProgress;
    private bool _vibrationRearmInProgress;
    private bool _vibrationPageUnloaded;
    private bool _controllerPageActive;
    private bool _applyingSteamVirtualControllerState;
    private int _steamVirtualControllerMutationInProgress;
    private SteamVirtualControllerPreference _lastKnownSteamVirtualController = SteamVirtualControllerPreference.SteamDeck;
    private FrontendControllerForSteamSnapshot _controllerForSteam = FrontendControllerForSteamSnapshot.Unavailable(SteamVirtualControllerPreference.SteamDeck);
    private bool _applyingGyroscopeState;
    private int _gyroscopeMutationInProgress;
    private bool _lastKnownGyroscopeEnabled = true;
    /// <summary>Suppresses change handlers while the page writes persisted state INTO the controls,
    /// so restoring the UI never looks like a user edit and re-saves.</summary>
    private bool _isLoading;

    public ControllerPage()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(
            RestoreVibrationButton,
            MainUiStrings.Get(
                "Controller_RestoreVibrationButton_Tooltip",
                "Temporarily disconnects virtual controller input. Save and close your game first."));
        RegisterVibrationPointerHandlers(LeftVibrationStrengthSlider);
        RegisterVibrationPointerHandlers(RightVibrationStrengthSlider);
        Loaded += ControllerPage_Loaded;
        Unloaded += ControllerPage_Unloaded;
    }

    internal event EventHandler<FrontButtonMappingSettings>? MappingEditRequested;
    internal event EventHandler<BackButtonMappingSettings>? BackButtonMappingEditRequested;
    internal event EventHandler<ControllerLedSettings>? ControllerLedEditRequested;

    internal void Initialize(
        FrontendBootstrapSnapshot bootstrap,
        IAddonFrontendControl frontend,
        Func<nint> windowHandleProvider)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _vibrationPageUnloaded = false;
        _available = bootstrap.FrontButtonMappingAvailable;
        _backButtonAvailable = bootstrap.BackButtonMappingAvailable;
        _controllerLedAvailable = bootstrap.ControllerLedAvailable;
        ApplyControllerForSteamSettings(bootstrap.Settings);
        FrontButtonMappingContent.Visibility = _available ? Visibility.Visible : Visibility.Collapsed;
        BackButtonMappingExpander.Visibility = _backButtonAvailable ? Visibility.Visible : Visibility.Collapsed;
        ControllerSettingsHeader.Visibility = _controllerLedAvailable ? Visibility.Visible : Visibility.Collapsed;
        ControllerLedExpander.Visibility = _controllerLedAvailable ? Visibility.Visible : Visibility.Collapsed;
        MappingContent.Visibility = _available || _backButtonAvailable || _controllerLedAvailable ? Visibility.Visible : Visibility.Collapsed;
        MappingUnavailableText.Visibility = _available || _backButtonAvailable || _controllerLedAvailable ? Visibility.Collapsed : Visibility.Visible;
        VibrationStrengthExpander.Visibility = _available ? Visibility.Visible : Visibility.Collapsed;

        _editors =
        [
            new BindingEditor(FrontButtonKind.Gamebar, FrontButtonDomain.Normal, GamebarNormalActionComboBox, GamebarNormalConfigCard, GamebarNormalConfigPanel, this, windowHandleProvider),
            new BindingEditor(FrontButtonKind.Gamebar, FrontButtonDomain.Steam, GamebarSteamActionComboBox, GamebarSteamConfigCard, GamebarSteamConfigPanel, this, windowHandleProvider),
            new BindingEditor(FrontButtonKind.CenterM, FrontButtonDomain.Normal, CenterMNormalActionComboBox, CenterMNormalConfigCard, CenterMNormalConfigPanel, this, windowHandleProvider),
            new BindingEditor(FrontButtonKind.CenterM, FrontButtonDomain.Steam, CenterMSteamActionComboBox, CenterMSteamConfigCard, CenterMSteamConfigPanel, this, windowHandleProvider),
        ];

        ApplyFrontButtonMapping(bootstrap.Settings.FrontButtonMapping);
        _backButtonMapping = bootstrap.Settings.BackButtonMapping;
        if (_backButtonAvailable)
        {
            PopulateBackButtonTargets();
            ApplyBackButtonMapping(_backButtonMapping);
        }
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _vibrationDebounce = new ControllerVibrationStrengthDebounce(
            Task.Delay,
            action => dispatcher.TryEnqueue(() => action()),
            VibrationStrengthDebounce_Settled);
        ApplyVibrationStrengthSnapshot(FrontendControllerVibrationStrengthSnapshot.Unavailable(), preserveDraft: false);
        ApplyControllerLedSettings(bootstrap.Settings.ControllerLed);
    }

    internal void Activate()
    {
        if (!_controllerPageActive && _frontend is not null)
        {
            _frontend.StateInvalidated += OnFrontendStateInvalidated;
            _controllerPageActive = true;
        }
        _vibrationPageUnloaded = false;
        if (_frontend is not null) _ = RefreshControllerForSteamAsync();
        if (_frontend is not null)
            _ = RefreshVibrationStrengthAsync();
    }

    internal static bool CanEditSteamVirtualController(FrontendControllerForSteamSnapshot state, bool mutationInProgress) =>
        state.Available && state.CanChangeController && !mutationInProgress;

    internal void ApplyControllerForSteamSettings(FrontendSettingsSnapshot settings)
    {
        if (Volatile.Read(ref _steamVirtualControllerMutationInProgress) == 0)
        {
            _controllerForSteam = settings.ControllerForSteam;
            _lastKnownSteamVirtualController = settings.SteamVirtualController;
        }
        if (Volatile.Read(ref _gyroscopeMutationInProgress) == 0)
        {
            _lastKnownGyroscopeEnabled = settings.GyroscopeEnabled;
            ApplyGyroscopeEnabled(settings.GyroscopeEnabled);
        }
        RenderSteamVirtualController();
    }

    private void ApplyGyroscopeEnabled(bool enabled)
    {
        _applyingGyroscopeState = true;
        try { GyroscopeEnabledToggle.IsOn = enabled; }
        finally { _applyingGyroscopeState = false; }
        UpdateGyroscopeControls();
    }

    private void RenderSteamVirtualController()
    {
        var available = _controllerForSteam.Available;
        var tag = (_controllerForSteam.ActiveSteamPreference ?? _controllerForSteam.SavedPreference).ToString();
        _applyingSteamVirtualControllerState = true;
        try
        {
            SteamVirtualControllerComboBox.SelectedItem = available
                ? SteamVirtualControllerComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
                : null;
            SteamVirtualControllerComboBox.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            SteamVirtualControllerUnavailableText.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _applyingSteamVirtualControllerState = false; }
        SteamVirtualControllerActivityText.Text = !_controllerForSteam.Available
            ? MainUiStrings.Get("Controller_SteamActivity_Unknown", "Controller state is unavailable. Selection is locked.")
            : _controllerForSteam.CanChangeController
                ? MainUiStrings.Get("Controller_SteamActivity_Idle", "Controller selection can be changed outside Steam games and Big Picture Mode.")
                : MainUiStrings.Get("Controller_SteamActivity_Active", "Steam is active. The applied controller is shown; controller selection is locked.");
        UpdateSteamVirtualControllerControls();
    }

    private void UpdateSteamVirtualControllerControls() =>
        SteamVirtualControllerComboBox.IsEnabled = CanEditSteamVirtualController(
            _controllerForSteam,
            Volatile.Read(ref _steamVirtualControllerMutationInProgress) != 0);

    private void UpdateGyroscopeControls() =>
        GyroscopeEnabledToggle.IsEnabled = Volatile.Read(ref _gyroscopeMutationInProgress) == 0;

    private void OnFrontendStateInvalidated(object? sender, EventArgs args)
    {
        if (_controllerPageActive)
            DispatcherQueue.TryEnqueue(() => _ = RefreshControllerForSteamAsync());
    }

    private async Task RefreshControllerForSteamAsync()
    {
        if (_frontend is null || Volatile.Read(ref _steamVirtualControllerMutationInProgress) != 0
            || Volatile.Read(ref _gyroscopeMutationInProgress) != 0) return;
        try
        {
            var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
            if (Volatile.Read(ref _steamVirtualControllerMutationInProgress) != 0
                || Volatile.Read(ref _gyroscopeMutationInProgress) != 0) return;
            ApplyControllerForSteamSettings(bootstrap.Settings);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Steam virtual controller preference refresh failed.", exception);
        }
    }

    private async void SteamVirtualControllerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_applyingSteamVirtualControllerState || _frontend is null
            || SteamVirtualControllerComboBox.SelectedItem is not ComboBoxItem { Tag: string selectionText }
            || !Enum.TryParse<SteamVirtualControllerPreference>(selectionText, ignoreCase: false, out var requested)
            || !Enum.IsDefined(requested))
            return;

        if (!CanEditSteamVirtualController(_controllerForSteam, Volatile.Read(ref _steamVirtualControllerMutationInProgress) != 0))
        {
            RenderSteamVirtualController();
            _ = RefreshControllerForSteamAsync();
            return;
        }
        if (requested == _controllerForSteam.SavedPreference) return;
        if (Interlocked.Exchange(ref _steamVirtualControllerMutationInProgress, 1) != 0)
        {
            RenderSteamVirtualController();
            return;
        }

        RenderSteamVirtualController();
        SteamVirtualControllerInfoBar.IsOpen = false;
        UpdateSteamVirtualControllerControls();
        try
        {
            var settings = await _frontend.SetSteamVirtualControllerAsync(requested).ConfigureAwait(true);
            _controllerForSteam = settings.ControllerForSteam;
            _lastKnownSteamVirtualController = settings.SteamVirtualController;
            RenderSteamVirtualController();
            if (_lastKnownSteamVirtualController != requested)
            {
                SteamVirtualControllerInfoBar.Message = MainUiStrings.Get(
                    "Controller_SteamChoice_NotChanged",
                    "The saved controller choice was not changed.");
                SteamVirtualControllerInfoBar.IsOpen = true;
                _ = RefreshControllerForSteamAsync();
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Steam virtual controller preference update failed.", exception);
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
                _controllerForSteam = bootstrap.Settings.ControllerForSteam;
                _lastKnownSteamVirtualController = bootstrap.Settings.SteamVirtualController;
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("Controller", "Steam virtual controller rollback could not refresh Runtime settings.", refreshException);
            }
            RenderSteamVirtualController();
            SteamVirtualControllerInfoBar.Message = MainUiStrings.Get(
                "Controller_SteamChoice_Uncertain",
                "The change could not be confirmed. The last confirmed choice is selected.");
            SteamVirtualControllerInfoBar.IsOpen = true;
            _ = RefreshControllerForSteamAsync();
        }
        finally
        {
            Volatile.Write(ref _steamVirtualControllerMutationInProgress, 0);
            UpdateSteamVirtualControllerControls();
        }
    }

    private async void GyroscopeEnabledToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_applyingGyroscopeState || _frontend is null
            || Interlocked.Exchange(ref _gyroscopeMutationInProgress, 1) != 0)
            return;

        var requested = GyroscopeEnabledToggle.IsOn;
        bool? confirmedValue = null;
        SteamVirtualControllerInfoBar.IsOpen = false;
        UpdateGyroscopeControls();
        try
        {
            var settings = await _frontend.SetGyroscopeEnabledAsync(requested).ConfigureAwait(true);
            confirmedValue = settings.GyroscopeEnabled;
            ApplyControllerForSteamSettings(settings);
            if (confirmedValue != requested) ShowGyroscopeUpdateFailure();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerMotion", "Gyroscope preference update failed.", exception);
            ShowGyroscopeUpdateFailure();
            try
            {
                var bootstrap = await _frontend.GetBootstrapAsync().ConfigureAwait(true);
                confirmedValue = bootstrap.Settings.GyroscopeEnabled;
                ApplyControllerForSteamSettings(bootstrap.Settings);
            }
            catch (Exception refreshException)
            {
                AppLog.Warn("ControllerMotion", "Gyroscope preference rollback could not refresh Runtime settings.", refreshException);
            }
        }
        finally
        {
            Volatile.Write(ref _gyroscopeMutationInProgress, 0);
            if (confirmedValue is { } enabled) _lastKnownGyroscopeEnabled = enabled;
            ApplyGyroscopeEnabled(confirmedValue ?? _lastKnownGyroscopeEnabled);
            UpdateGyroscopeControls();
        }
    }

    private void ShowGyroscopeUpdateFailure()
    {
        SteamVirtualControllerInfoBar.Message = MainUiStrings.Get(
            "Controller_GyroscopeUpdateFailed",
            "The Gyroscope setting could not be saved. The last confirmed value is shown.");
        SteamVirtualControllerInfoBar.IsOpen = true;
    }

    internal void Deactivate()
    {
        if (_controllerPageActive && _frontend is not null)
            _frontend.StateInvalidated -= OnFrontendStateInvalidated;
        _controllerPageActive = false;
        if (_vibrationPageUnloaded)
            return;

        if (_vibrationPointerGestureInProgress)
        {
            CompleteVibrationPointerGesture(pointerId: null, slider: null);
            return;
        }

        if (_vibrationDebounce?.FlushPending() != true)
            TryCommitVibrationDraftIfSettled();
    }

    internal void ApplyControllerLedSettings(ControllerLedSettings settings)
    {
        _isLoading = true;
        try
        {
            _controllerLed = settings;
            ControllerLedEnabledToggle.IsOn = settings.Enabled;
            ControllerLedBrightnessSlider.Value = settings.Brightness;
            ControllerLedBrightnessValue.Text = settings.Brightness.ToString();
            ControllerLedColorPicker.Color = Color.FromArgb(255, settings.Red, settings.Green, settings.Blue);
            ControllerLedColorSwatch.Background = new SolidColorBrush(Color.FromArgb(255, settings.Red, settings.Green, settings.Blue));
            ControllerLedBrightnessSlider.IsEnabled = settings.Enabled;
            ControllerLedColorButton.IsEnabled = settings.Enabled;
        }
        finally { _isLoading = false; }
    }

    private void ControllerLedEnabled_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || !_controllerLedAvailable) return;
        _controllerLed = _controllerLed with { Enabled = ControllerLedEnabledToggle.IsOn };
        ControllerLedBrightnessSlider.IsEnabled = _controllerLed.Enabled;
        ControllerLedColorButton.IsEnabled = _controllerLed.Enabled;
        ControllerLedEditRequested?.Invoke(this, _controllerLed);
    }

    private void ControllerLedBrightness_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs args)
    {
        var rounded = (int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero);
        ControllerLedBrightnessValue.Text = rounded.ToString();
        if (_isLoading || !_controllerLedAvailable || !_controllerLed.Enabled) return;
        if (_controllerLed.Brightness == rounded) return;
        _controllerLed = _controllerLed with { Brightness = rounded };
        ControllerLedEditRequested?.Invoke(this, _controllerLed);
    }

    private void ControllerLedColor_Changed(ColorPicker sender, ColorChangedEventArgs args)
    {
        ControllerLedColorSwatch.Background = new SolidColorBrush(Color.FromArgb(255, args.NewColor.R, args.NewColor.G, args.NewColor.B));
        if (_isLoading || !_controllerLedAvailable || !_controllerLed.Enabled) return;
        var next = _controllerLed with { Red = args.NewColor.R, Green = args.NewColor.G, Blue = args.NewColor.B };
        if (_controllerLed == next) return;
        _controllerLed = next;
        ControllerLedEditRequested?.Invoke(this, _controllerLed);
    }

    /// <summary>Writes a persisted mapping into every control. Never tears the editors down.</summary>
    internal void ApplyFrontButtonMapping(FrontButtonMappingSettings mapping)
    {
        _isLoading = true;
        try
        {
            _mapping = mapping;
            foreach (var editor in _editors)
                editor.Load(mapping.Resolve(editor.Kind, editor.Domain));
            RefreshPartnerAvailability();
        }
        finally { _isLoading = false; }
    }

    private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isLoading) return;
        foreach (var editor in _editors)
        {
            if (!ReferenceEquals(editor.ActionComboBox, sender)) continue;
            editor.ShowConfigurationFor(editor.SelectedAction);
            RaiseEdit(editor);
            return;
        }
    }

    /// <summary>Writes the Runtime-owned Xbox360 rear-button mapping into both selectors.</summary>
    internal void ApplyBackButtonMapping(BackButtonMappingSettings mapping)
    {
        _isLoading = true;
        try
        {
            _backButtonMapping = mapping;
            BackButtonMappingUiOptions.SelectTarget(M1TargetComboBox, mapping.M1);
            BackButtonMappingUiOptions.SelectTarget(M2TargetComboBox, mapping.M2);
        }
        finally { _isLoading = false; }
    }

    private void PopulateBackButtonTargets()
    {
        BackButtonMappingUiOptions.AddTargets(M1TargetComboBox);
        BackButtonMappingUiOptions.AddTargets(M2TargetComboBox);
        LocalizeControllerBackTargets(M1TargetComboBox);
        LocalizeControllerBackTargets(M2TargetComboBox);
    }

    private static void LocalizeControllerBackTargets(ComboBox comboBox)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is not Xbox360BackButtonTarget target) continue;
            item.Content = MainUiStrings.Get(
                $"Controller_BackTarget_{target}",
                BackButtonMappingLabels.GetDisplayName(target));
        }
    }

    private void BackButtonTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isLoading || !_backButtonAvailable) return;

        var target = (sender as ComboBox)?.SelectedItem is ComboBoxItem { Tag: Xbox360BackButtonTarget value }
            ? value
            : (Xbox360BackButtonTarget?)null;
        if (target is null) return;

        if (ReferenceEquals(sender, M1TargetComboBox))
            _backButtonMapping = new BackButtonMappingSettings(target.Value, _backButtonMapping.M2);
        else if (ReferenceEquals(sender, M2TargetComboBox))
            _backButtonMapping = new BackButtonMappingSettings(_backButtonMapping.M1, target.Value);
        else
            return;

        BackButtonMappingEditRequested?.Invoke(this, _backButtonMapping);
    }

    private void RegisterVibrationPointerHandlers(Slider slider)
    {
        slider.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(VibrationStrengthSlider_PointerPressed), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(VibrationStrengthSlider_PointerReleased), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler(VibrationStrengthSlider_PointerCanceled), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(VibrationStrengthSlider_PointerCaptureLost), handledEventsToo: true);
    }

    private void ControllerPage_Loaded(object sender, RoutedEventArgs args) => _vibrationPageUnloaded = false;

    private void ControllerPage_Unloaded(object sender, RoutedEventArgs args)
    {
        _vibrationPageUnloaded = true;
        _vibrationDebounce?.CancelPending();
        _vibrationDraft = null;
        _vibrationPointerGestureInProgress = false;
        _vibrationPointerId = null;
        _vibrationPointerSlider = null;
    }

    private void VibrationStrengthSlider_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_vibrationPageUnloaded
            || _frontend is null
            || _vibrationPointerGestureInProgress
            || !CanEditVibrationSliders(
                _vibrationSnapshot.Available,
                _vibrationSnapshot.Writable,
                _vibrationTestInProgress || _vibrationRearmInProgress))
            return;

        var slider = sender as Slider;
        if (slider is null)
            return;

        var properties = args.GetCurrentPoint(slider).Properties;
        var isContact = args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
            ? properties.IsLeftButtonPressed
            : args.Pointer.IsInContact;
        if (!isContact)
            return;

        _vibrationPointerGestureInProgress = true;
        _vibrationPointerId = args.Pointer.PointerId;
        _vibrationPointerSlider = slider;
        _vibrationDebounce?.CancelPending();
        UpdateVibrationControls();
    }

    private void VibrationStrengthSlider_PointerReleased(object sender, PointerRoutedEventArgs args) =>
        CompleteVibrationPointerGesture(args.Pointer.PointerId, sender as Slider);

    private void VibrationStrengthSlider_PointerCanceled(object sender, PointerRoutedEventArgs args) =>
        CompleteVibrationPointerGesture(args.Pointer.PointerId, sender as Slider);

    private void VibrationStrengthSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        CompleteVibrationPointerGesture(args.Pointer.PointerId, sender as Slider);

    private bool CompleteVibrationPointerGesture(uint? pointerId, Slider? slider)
    {
        if (!_vibrationPointerGestureInProgress
            || (pointerId is { } observedPointerId
                && !IsCurrentVibrationPointerGesture(_vibrationPointerGestureInProgress, _vibrationPointerId, observedPointerId))
            || (slider is not null && !ReferenceEquals(_vibrationPointerSlider, slider)))
            return false;

        _vibrationPointerGestureInProgress = false;
        _vibrationPointerId = null;
        _vibrationPointerSlider = null;
        TryCommitVibrationDraftIfSettled();
        UpdateVibrationControls();
        return true;
    }

    private void VibrationStrengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_isRenderingVibration
            || _vibrationPageUnloaded
            || _frontend is null
            || !_vibrationSnapshot.Available
            || !_vibrationSnapshot.Writable
            || _vibrationTestInProgress
            || _vibrationRearmInProgress)
            return;

        var pair = (Left: ToPercent(LeftVibrationStrengthSlider.Value), Right: ToPercent(RightVibrationStrengthSlider.Value));
        LeftVibrationStrengthPercentText.Text = $"{pair.Left}%";
        RightVibrationStrengthPercentText.Text = $"{pair.Right}%";

        _vibrationDraft = !_vibrationMutationInProgress && MatchesVibrationSnapshot(pair, _vibrationSnapshot)
            ? null
            : pair;

        if (_vibrationPointerGestureInProgress)
            _vibrationDebounce?.CancelPending();
        else if (_vibrationDraft is { } draft)
            _vibrationDebounce?.Schedule(draft.Left, draft.Right);
        else
            _vibrationDebounce?.CancelPending();

        UpdateVibrationControls();
    }

    private void VibrationStrengthDebounce_Settled(int leftPercent, int rightPercent)
    {
        if (_vibrationPageUnloaded
            || _vibrationPointerGestureInProgress
            || _vibrationDraft is not { } draft
            || draft != (leftPercent, rightPercent))
            return;

        TryCommitVibrationDraftIfSettled();
    }

    private void TryCommitVibrationDraftIfSettled()
    {
        if (_vibrationDraft is not { } draft
            || !CanSubmitVibrationDraft(
                _vibrationSnapshot.Available,
                _vibrationSnapshot.Writable,
                hasDraft: true,
                _vibrationPointerGestureInProgress,
                _vibrationDebounce?.HasPendingDraft == true,
                _vibrationMutationInProgress,
                pageUnloaded: _vibrationPageUnloaded))
            return;

        if (MatchesVibrationSnapshot(draft, _vibrationSnapshot))
        {
            _vibrationDraft = null;
            UpdateVibrationControls();
            return;
        }

        _ = CommitVibrationStrengthAsync(draft.Left, draft.Right);
    }

    private async void LeftVibrationTestButton_Click(object sender, RoutedEventArgs args) =>
        await TestVibrationMotorAsync(FrontendControllerVibrationMotor.Left);

    private async void RightVibrationTestButton_Click(object sender, RoutedEventArgs args) =>
        await TestVibrationMotorAsync(FrontendControllerVibrationMotor.Right);

    private async void RestoreVibrationButton_Click(object sender, RoutedEventArgs args)
    {
        var frontend = _frontend;
        if (frontend is null)
            return;
        if (!CanStartVibrationRearm(
                HasUncommittedVibrationDraft,
                _vibrationPointerGestureInProgress,
                _vibrationMutationInProgress,
                _vibrationTestInProgress,
                _vibrationRearmInProgress))
            return;

        _vibrationRearmInProgress = true;
        RestoreVibrationButton.Content = MainUiStrings.Get("Controller_RestoreVibrationButton_BusyContent", "Restoring…");
        UpdateVibrationControls();
        try
        {
            var result = await frontend.RunDeveloperRumbleRearmAsync();
            AppLog.Info("ControllerOwnership", "Controller-page rumble recovery UI result.",
                ("Event", result.Outcome == FrontendDeveloperRumbleRearmOutcome.Completed
                    ? "ControllerRumbleRecoveryUiCompleted" : "ControllerRumbleRecoveryUiResult"),
                ("Outcome", result.Outcome), ("Status", result.Status),
                ("XInputTransitionVerified", result.XInputTransitionVerified),
                ("DirectInputTransitionVerified", result.DirectInputTransitionVerified),
                ("PhysicalOwnershipRestored", result.PhysicalOwnershipRestored),
                ("PresentationRestored", result.PresentationRestored),
                ("PhysicalMotorEffectVerified", result.PhysicalMotorEffectVerified));
            if (result.Succeeded)
            {
                var preserveFailure = VibrationStrengthInfoBar.IsOpen
                    && VibrationStrengthInfoBar.Severity == InfoBarSeverity.Error;
                await RefreshVibrationStrengthAsync(preserveFailure);
                ShowRumbleRearmMessageUnlessFailure(
                    MainUiStrings.Get(
                        "Controller_RumbleRearm_Succeeded",
                        "Controller restored. Test both motors to confirm vibration."),
                    InfoBarSeverity.Informational);
            }
            else if (result.Outcome == FrontendDeveloperRumbleRearmOutcome.Unavailable)
                ShowRumbleRearmMessageUnlessFailure(
                    MainUiStrings.Get("Controller_RumbleRearm_Unavailable", "Vibration recovery is unavailable right now."),
                    InfoBarSeverity.Warning);
            else
                ShowRumbleRearmMessageUnlessFailure(
                    MainUiStrings.Get("Controller_RumbleRearm_Failed", "Could not restore the controller. See the application log."),
                    InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "Controller-page rumble recovery request failed.", exception,
                ("Event", "ControllerRumbleRecoveryUiFailed"), ("Reason", exception.GetType().Name));
            ShowRumbleRearmMessageUnlessFailure(
                MainUiStrings.Get("Controller_RumbleRearm_Failed", "Could not restore the controller. See the application log."),
                InfoBarSeverity.Error);
        }
        finally
        {
            _vibrationRearmInProgress = false;
            RestoreVibrationButton.Content = MainUiStrings.Get("Controller_RestoreVibrationButton/Content", "Restore Vibration");
            UpdateVibrationControls();
        }
    }

    private void ShowRumbleRearmMessageUnlessFailure(string message, InfoBarSeverity severity)
    {
        if (!CanShowRumbleRearmMessage(
                VibrationStrengthInfoBar.IsOpen,
                VibrationStrengthInfoBar.Severity == InfoBarSeverity.Error))
            return;

        ShowVibrationMessage(message, severity);
    }

    internal static bool CanShowRumbleRearmMessage(bool existingMessageOpen, bool existingError) =>
        !existingMessageOpen || !existingError;

    private async Task CommitVibrationStrengthAsync(int leftPercent, int rightPercent)
    {
        var submittedPair = (Left: leftPercent, Right: rightPercent);
        var frontend = _frontend;
        if (frontend is null
            || _vibrationDraft != submittedPair
            || !CanSubmitVibrationDraft(
                _vibrationSnapshot.Available,
                _vibrationSnapshot.Writable,
                hasDraft: true,
                _vibrationPointerGestureInProgress,
                _vibrationDebounce?.HasPendingDraft == true,
                _vibrationMutationInProgress,
                pageUnloaded: _vibrationPageUnloaded))
            return;

        _vibrationMutationInProgress = true;
        UpdateVibrationControls();
        try
        {
            var result = await frontend.SetControllerVibrationStrengthAsync(leftPercent, rightPercent);
            if (_vibrationPageUnloaded)
                return;

            if (ShouldClearSubmittedVibrationDraft(_vibrationDraft, submittedPair))
                _vibrationDraft = null;
            ApplyVibrationStrengthSnapshot(
                result.Snapshot,
                ShouldPreserveVibrationDraft(
                    result.Snapshot.Available,
                    result.Snapshot.Writable,
                    hasDraft: _vibrationDraft is not null,
                    _vibrationPointerGestureInProgress,
                    _vibrationDebounce?.HasPendingDraft == true));
            if (!result.Succeeded)
            {
                ShowVibrationMessage(
                    result.FailureMessage ?? MainUiStrings.Get(
                        "Controller_Vibration_ApplyFailed",
                        "The saved vibration setting could not be applied now."),
                    InfoBarSeverity.Error);
                await RefreshVibrationStrengthAsync(preserveFailure: true);
                return;
            }

            if (_vibrationDraft is not null)
            {
                // A newer local edit is still waiting for its own settle/release boundary.
            }
            else if (!result.Snapshot.Writable && result.Snapshot.Available)
                ShowVibrationMessage(result.Snapshot.Status, InfoBarSeverity.Informational);
            else
                HideVibrationMessage();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerVibration", "Controller vibration setting update transport failed.", exception);
            if (ShouldClearSubmittedVibrationDraft(_vibrationDraft, submittedPair))
                _vibrationDraft = null;
            ShowVibrationMessage(
                MainUiStrings.Get(
                    "Controller_Vibration_SaveUncertain",
                    "The saved setting could not be confirmed. Refreshing the current setting."),
                InfoBarSeverity.Error);
            await RefreshVibrationStrengthAsync(preserveFailure: true);
        }
        finally
        {
            _vibrationMutationInProgress = false;
            if (!_vibrationPageUnloaded)
            {
                UpdateVibrationControls();
                TryCommitVibrationDraftIfSettled();
            }
        }
    }

    private async Task TestVibrationMotorAsync(FrontendControllerVibrationMotor motor)
    {
        var frontend = _frontend;
        if (frontend is null)
            return;

        var operationStarted = false;
        try
        {
            var request = TryStartVibrationMotorTest(
                _vibrationSnapshot,
                HasUncommittedVibrationDraft,
                _vibrationPointerGestureInProgress,
                _vibrationMutationInProgress || _vibrationRearmInProgress,
                _vibrationTestInProgress,
                () =>
                {
                    operationStarted = true;
                    _vibrationTestInProgress = true;
                    UpdateVibrationControls();
                },
                () => frontend.TestControllerVibrationMotorAsync(motor));
            if (request is null)
                return;

            var result = await request;
            if (result.Succeeded)
                HideVibrationMessage();
            else
                ShowVibrationMessage(
                    result.FailureMessage ?? MainUiStrings.Get(
                        "Controller_Vibration_TestFailed",
                        "The physical vibration test did not complete safely."),
                    InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerVibration", "Controller vibration test transport failed.", exception,
                ("Motor", motor));
            ShowVibrationMessage(
                MainUiStrings.Get(
                    "Controller_Vibration_TestUncertain",
                    "The test result is uncertain. Refreshing the saved strength values."),
                InfoBarSeverity.Error);
            await RefreshVibrationStrengthAsync(preserveFailure: true);
        }
        finally
        {
            if (operationStarted)
            {
                _vibrationTestInProgress = false;
                UpdateVibrationControls();
            }
        }
    }

    internal static bool CanRunVibrationMotorTest(
        FrontendControllerVibrationStrengthSnapshot snapshot,
        bool hasPendingDraft,
        bool pointerGestureInProgress,
        bool mutationInProgress,
        bool testInProgress) =>
        snapshot.TestAvailable
        && !hasPendingDraft
        && !pointerGestureInProgress
        && !mutationInProgress
        && !testInProgress;

    internal static Task<FrontendControllerVibrationTestResult>? TryStartVibrationMotorTest(
        FrontendControllerVibrationStrengthSnapshot snapshot,
        bool hasPendingDraft,
        bool pointerGestureInProgress,
        bool mutationInProgress,
        bool testInProgress,
        Action markStarted,
        Func<Task<FrontendControllerVibrationTestResult>> dispatch)
    {
        if (!CanRunVibrationMotorTest(snapshot, hasPendingDraft, pointerGestureInProgress, mutationInProgress, testInProgress))
            return null;

        markStarted();
        return dispatch();
    }

    private async Task RefreshVibrationStrengthAsync(bool preserveFailure = false)
    {
        if (_frontend is null || _vibrationPageUnloaded)
            return;

        try
        {
            var snapshot = await _frontend.CaptureControllerVibrationStrengthAsync();
            if (_vibrationPageUnloaded)
                return;

            var preserveDraft = ShouldPreserveVibrationDraft(
                snapshot.Available,
                snapshot.Writable,
                _vibrationDraft is not null,
                _vibrationPointerGestureInProgress,
                _vibrationDebounce?.HasPendingDraft == true);
            ApplyVibrationStrengthSnapshot(snapshot, preserveDraft);
            if (preserveFailure)
                return;
            if (!snapshot.Available)
                ShowVibrationMessage(snapshot.Status, InfoBarSeverity.Error);
            else if (!snapshot.Writable)
                ShowVibrationMessage(snapshot.Status, InfoBarSeverity.Informational);
            else
                HideVibrationMessage();
        }
        catch (Exception exception)
        {
            if (_vibrationPageUnloaded)
                return;

            AppLog.Warn("ControllerVibration", "Controller vibration setting capture transport failed.", exception);
            _vibrationDebounce?.CancelPending();
            _vibrationDraft = null;
            _vibrationPointerGestureInProgress = false;
            _vibrationPointerId = null;
            _vibrationPointerSlider = null;
            ApplyVibrationStrengthSnapshot(
                FrontendControllerVibrationStrengthSnapshot.Unavailable(MainUiStrings.Get(
                    "Controller_Vibration_LoadFailed",
                    "The saved vibration setting could not be loaded.")),
                preserveDraft: false);
            if (!preserveFailure)
                ShowVibrationMessage(
                    MainUiStrings.Get("Controller_Vibration_LoadFailed", "The saved vibration setting could not be loaded."),
                    InfoBarSeverity.Error);
        }
    }

    private void ApplyVibrationStrengthSnapshot(
        FrontendControllerVibrationStrengthSnapshot snapshot,
        bool preserveDraft)
    {
        _vibrationSnapshot = snapshot;
        if (!snapshot.Available || !snapshot.Writable)
        {
            _vibrationDebounce?.CancelPending();
            _vibrationDraft = null;
            _vibrationPointerGestureInProgress = false;
            _vibrationPointerId = null;
            _vibrationPointerSlider = null;
            preserveDraft = false;
        }
        else
        {
            preserveDraft = preserveDraft && ShouldPreserveVibrationDraft(
                snapshot.Available,
                snapshot.Writable,
                _vibrationDraft is not null,
                _vibrationPointerGestureInProgress,
                _vibrationDebounce?.HasPendingDraft == true);
        }

        if (!preserveDraft)
        {
            _isRenderingVibration = true;
            try
            {
                LeftVibrationStrengthSlider.Visibility = snapshot.Available ? Visibility.Visible : Visibility.Collapsed;
                RightVibrationStrengthSlider.Visibility = snapshot.Available ? Visibility.Visible : Visibility.Collapsed;
                LeftVibrationStrengthSlider.Value = snapshot.LeftPercent ?? 0;
                RightVibrationStrengthSlider.Value = snapshot.RightPercent ?? 0;
                LeftVibrationStrengthPercentText.Text = snapshot.LeftPercent is { } left ? $"{left}%" : "—";
                RightVibrationStrengthPercentText.Text = snapshot.RightPercent is { } right ? $"{right}%" : "—";
            }
            finally { _isRenderingVibration = false; }
        }

        UpdateVibrationControls();
    }

    private void UpdateVibrationControls()
    {
        var slidersEnabled = CanEditVibrationSliders(
            _vibrationSnapshot.Available,
            _vibrationSnapshot.Writable,
            _vibrationTestInProgress || _vibrationRearmInProgress);
        LeftVibrationStrengthSlider.IsEnabled = slidersEnabled;
        RightVibrationStrengthSlider.IsEnabled = slidersEnabled;

        var testsEnabled = CanRunVibrationMotorTest(
            _vibrationSnapshot,
            HasUncommittedVibrationDraft,
            _vibrationPointerGestureInProgress,
            _vibrationMutationInProgress || _vibrationRearmInProgress,
            _vibrationTestInProgress);
        LeftVibrationTestButton.IsEnabled = testsEnabled;
        RightVibrationTestButton.IsEnabled = testsEnabled;
        RestoreVibrationButton.IsEnabled = _frontend is not null && CanStartVibrationRearm(
            HasUncommittedVibrationDraft,
            _vibrationPointerGestureInProgress,
            _vibrationMutationInProgress,
            _vibrationTestInProgress,
            _vibrationRearmInProgress);
    }

    private bool HasUncommittedVibrationDraft =>
        _vibrationDraft is not null || _vibrationDebounce?.HasPendingDraft == true;

    internal static bool CanStartVibrationRearm(
        bool hasUncommittedDraft,
        bool pointerGestureInProgress,
        bool mutationInProgress,
        bool testInProgress,
        bool rearmInProgress) =>
        !hasUncommittedDraft
        && !pointerGestureInProgress
        && !mutationInProgress
        && !testInProgress
        && !rearmInProgress;

    internal static bool CanEditVibrationSliders(bool available, bool writable, bool testInProgress) =>
        available && writable && !testInProgress;

    internal static bool CanSubmitVibrationDraft(
        bool available,
        bool writable,
        bool hasDraft,
        bool pointerGestureInProgress,
        bool debouncePending,
        bool mutationInProgress,
        bool pageUnloaded) =>
        !pageUnloaded
        && available
        && writable
        && hasDraft
        && !pointerGestureInProgress
        && !debouncePending
        && !mutationInProgress;

    internal static bool ShouldPreserveVibrationDraft(
        bool snapshotAvailable,
        bool snapshotWritable,
        bool hasDraft,
        bool pointerGestureInProgress,
        bool debouncePending) =>
        snapshotAvailable
        && snapshotWritable
        && (hasDraft || pointerGestureInProgress || debouncePending);

    internal static bool ShouldClearSubmittedVibrationDraft(
        (int Left, int Right)? latestDraft,
        (int Left, int Right) submittedPair) =>
        latestDraft is { } draft && draft == submittedPair;

    internal static bool IsCurrentVibrationPointerGesture(
        bool gestureInProgress,
        uint? activePointerId,
        uint observedPointerId) =>
        gestureInProgress && activePointerId == observedPointerId;

    private static bool MatchesVibrationSnapshot(
        (int Left, int Right) pair,
        FrontendControllerVibrationStrengthSnapshot snapshot) =>
        snapshot.Available
        && snapshot.Writable
        && snapshot.LeftPercent == pair.Left
        && snapshot.RightPercent == pair.Right;

    private void ShowVibrationMessage(string message, InfoBarSeverity severity)
    {
        VibrationStrengthInfoBar.Severity = severity;
        VibrationStrengthInfoBar.Message = message;
        VibrationStrengthInfoBar.IsOpen = true;
    }

    private void HideVibrationMessage() => VibrationStrengthInfoBar.IsOpen = false;

    private static int ToPercent(double value) => Math.Clamp((int)Math.Round(value), 0, 100);

    private void OnEditorConfigurationChanged(BindingEditor editor)
    {
        if (_isLoading) return;
        RaiseEdit(editor);
    }

    private void RaiseEdit(BindingEditor editor)
    {
        _mapping = _mapping.With(editor.Kind, editor.Domain, editor.Capture());
        RefreshPartnerAvailability();
        MappingEditRequested?.Invoke(this, _mapping);
    }

    /// <summary>§12.2 / §20: in each domain, the action the other button currently uses is disabled
    /// in this button's ComboBox so a same-domain duplicate can never be selected.</summary>
    private void RefreshPartnerAvailability()
    {
        foreach (var editor in _editors)
        {
            var partner = _mapping.Resolve(
                editor.Kind == FrontButtonKind.Gamebar ? FrontButtonKind.CenterM : FrontButtonKind.Gamebar,
                editor.Domain);
            editor.DisablePartnerAction(partner.Action);
        }
    }

    private async Task BrowseForExecutableAsync(BindingEditor editor, Func<nint> windowHandleProvider)
    {
        try
        {
            var hwnd = windowHandleProvider();
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var picker = new FileOpenPicker(windowId) { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            editor.SetExecutablePath(file.Path);
            OnEditorConfigurationChanged(editor);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Application picker failed.", exception);
        }
    }

    private static string DescribeAction(FrontButtonAction action) => action switch
    {
        FrontButtonAction.QuickSettingsOverlay => MainUiStrings.Get("Controller_Action_QuickSettingsOverlay", "Quick Settings Overlay"),
        FrontButtonAction.SteamBigPicture => MainUiStrings.Get("Controller_Action_SteamBigPicture", "Steam Big Picture"),
        FrontButtonAction.SteamButton => MainUiStrings.Get("Controller_Action_SteamButton", "Steam Button"),
        FrontButtonAction.SteamQuickAccess => MainUiStrings.Get("Controller_Action_SteamQuickAccess", "Steam Quick Access"),
        FrontButtonAction.KeyboardHotkey => MainUiStrings.Get("Controller_Action_KeyboardHotkey", "Keyboard / Hotkey"),
        FrontButtonAction.LaunchApplication => MainUiStrings.Get("Controller_Action_LaunchApplication", "Launch Application"),
        FrontButtonAction.XboxApp => MainUiStrings.Get("Controller_Action_Xbox", "Xbox"),
        _ => action.ToString()
    };

    /// <summary>The controls for one (button, domain) binding: the action ComboBox plus the inline
    /// hotkey and launch-application editors beneath it.</summary>
    private sealed class BindingEditor
    {
        private readonly ControllerPage _page;
        private readonly SettingsCard _configCard;
        private readonly CheckBox _control = new() { Content = "Ctrl" };
        private readonly CheckBox _shift = new() { Content = "Shift" };
        private readonly CheckBox _alt = new() { Content = "Alt" };
        private readonly CheckBox _windows = new() { Content = "Win" };
        private readonly ComboBox _key = new() { MinWidth = 140 };
        private readonly StackPanel _hotkeyPanel;
        private readonly StackPanel _launchPanel;
        private readonly TextBox _path = new()
        {
            PlaceholderText = MainUiStrings.Get("Controller_BindingEditor_ApplicationPath", "Application path"),
            MinWidth = 320
        };
        private readonly TextBox _arguments = new()
        {
            PlaceholderText = MainUiStrings.Get("Controller_BindingEditor_Arguments", "Arguments (optional)"),
            MinWidth = 320
        };
        private readonly CheckBox _runAsAdministrator = new()
        {
            Content = MainUiStrings.Get("Controller_BindingEditor_RunAsAdministrator", "Run as administrator")
        };

        internal BindingEditor(FrontButtonKind kind, FrontButtonDomain domain, ComboBox actionComboBox, SettingsCard configCard, StackPanel configPanel, ControllerPage page, Func<nint> windowHandleProvider)
        {
            Kind = kind;
            Domain = domain;
            ActionComboBox = actionComboBox;
            _configCard = configCard;
            _page = page;

            foreach (var action in FrontButtonActionCapabilities.ActionsFor(domain))
                actionComboBox.Items.Add(new ComboBoxItem { Content = DescribeAction(action), Tag = action });

            foreach (var vkey in Enum.GetValues<FrontButtonHotkeyKey>())
            {
                if (vkey == FrontButtonHotkeyKey.None) continue;
                _key.Items.Add(new ComboBoxItem { Content = vkey.ToString(), Tag = vkey });
            }
            RefreshHotkeyAvailability();

            _hotkeyPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Visibility = Visibility.Collapsed,
                Children = { _control, _shift, _alt, _windows, _key }
            };

            var browse = new Button
            {
                Content = MainUiStrings.Get("Controller_BindingEditor_Browse", "Browse…")
            };
            browse.Click += (_, _) => _ = page.BrowseForExecutableAsync(this, windowHandleProvider);
            _launchPanel = new StackPanel
            {
                Spacing = 8,
                Visibility = Visibility.Collapsed,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _path, browse } },
                    _arguments,
                    _runAsAdministrator,
                    new TextBlock
                    {
                        Text = MainUiStrings.Get(
                            "Controller_BindingEditor_PrivilegeExplanation",
                            "Runs with administrator privileges; otherwise requests normal user privileges."),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };

            configPanel.Children.Add(_hotkeyPanel);
            configPanel.Children.Add(_launchPanel);

            foreach (var modifier in new[] { _control, _shift, _alt })
            {
                modifier.Checked += (_, _) => page.OnEditorConfigurationChanged(this);
                modifier.Unchecked += (_, _) => page.OnEditorConfigurationChanged(this);
            }
            // §7 / Policy B: a Gamebar editor must not be able to leave Win+G selected. Toggling Win
            // re-evaluates whether the G key is offered and clears it if it was chosen.
            _windows.Checked += (_, _) => { RefreshHotkeyAvailability(); page.OnEditorConfigurationChanged(this); };
            _windows.Unchecked += (_, _) => { RefreshHotkeyAvailability(); page.OnEditorConfigurationChanged(this); };
            _key.SelectionChanged += (_, _) => page.OnEditorConfigurationChanged(this);
            _path.TextChanged += (_, _) => page.OnEditorConfigurationChanged(this);
            _arguments.TextChanged += (_, _) => page.OnEditorConfigurationChanged(this);
            _runAsAdministrator.Checked += (_, _) => page.OnEditorConfigurationChanged(this);
            _runAsAdministrator.Unchecked += (_, _) => page.OnEditorConfigurationChanged(this);
        }

        internal FrontButtonKind Kind { get; }
        internal FrontButtonDomain Domain { get; }
        internal ComboBox ActionComboBox { get; }

        internal FrontButtonAction SelectedAction =>
            ActionComboBox.SelectedItem is ComboBoxItem { Tag: FrontButtonAction action } ? action : FrontButtonAction.QuickSettingsOverlay;

        internal void Load(FrontButtonBinding binding)
        {
            SelectAction(binding.Action);
            _control.IsChecked = binding.Hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Control);
            _shift.IsChecked = binding.Hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Shift);
            _alt.IsChecked = binding.Hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Alt);
            _windows.IsChecked = binding.Hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Windows);
            SelectKey(binding.Hotkey.Key);
            RefreshHotkeyAvailability();
            _path.Text = binding.Launch.ExecutablePath;
            _arguments.Text = binding.Launch.Arguments;
            _runAsAdministrator.IsChecked = binding.Launch.RunAsAdministrator;
            ShowConfigurationFor(binding.Action);
        }

        internal FrontButtonBinding Capture() => new()
        {
            Action = SelectedAction,
            Hotkey = new FrontButtonHotkeyBinding(CaptureModifiers(), CaptureKey()),
            Launch = new FrontButtonLaunchApplicationBinding(
                _path.Text ?? string.Empty,
                _arguments.Text ?? string.Empty,
                _runAsAdministrator.IsChecked == true)
        };

        internal void SetExecutablePath(string path) => _path.Text = path;

        internal void ShowConfigurationFor(FrontButtonAction action)
        {
            _hotkeyPanel.Visibility = action == FrontButtonAction.KeyboardHotkey ? Visibility.Visible : Visibility.Collapsed;
            _launchPanel.Visibility = action == FrontButtonAction.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
            _configCard.Visibility = action is FrontButtonAction.KeyboardHotkey or FrontButtonAction.LaunchApplication
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>Disable the item matching <paramref name="partnerAction"/> so the two buttons in
        /// this domain cannot select the same action; re-enable everything else.</summary>
        internal void DisablePartnerAction(FrontButtonAction partnerAction)
        {
            foreach (var item in ActionComboBox.Items)
                if (item is ComboBoxItem comboItem && comboItem.Tag is FrontButtonAction candidate)
                    comboItem.IsEnabled = candidate != partnerAction || candidate == SelectedAction;
        }

        /// <summary>§7 / Policy B: on a Gamebar editor the <c>G</c> key is unavailable while the Win
        /// modifier is checked, and a G selection is cleared if Win becomes checked -- so the editor
        /// can never leave Win+G selected. Center M is unaffected.</summary>
        private void RefreshHotkeyAvailability()
        {
            var blockG = Kind == FrontButtonKind.Gamebar && _windows.IsChecked == true;
            foreach (var item in _key.Items)
                if (item is ComboBoxItem { Tag: FrontButtonHotkeyKey key } comboItem)
                    comboItem.IsEnabled = !(blockG && key == FrontButtonHotkeyKey.G);
            if (blockG && CaptureKey() == FrontButtonHotkeyKey.G)
                _key.SelectedItem = null;
        }

        private FrontButtonHotkeyModifiers CaptureModifiers()
        {
            var modifiers = FrontButtonHotkeyModifiers.None;
            if (_control.IsChecked == true) modifiers |= FrontButtonHotkeyModifiers.Control;
            if (_shift.IsChecked == true) modifiers |= FrontButtonHotkeyModifiers.Shift;
            if (_alt.IsChecked == true) modifiers |= FrontButtonHotkeyModifiers.Alt;
            if (_windows.IsChecked == true) modifiers |= FrontButtonHotkeyModifiers.Windows;
            return modifiers;
        }

        private FrontButtonHotkeyKey CaptureKey() =>
            _key.SelectedItem is ComboBoxItem { Tag: FrontButtonHotkeyKey key } ? key : FrontButtonHotkeyKey.None;

        private void SelectAction(FrontButtonAction action)
        {
            ActionComboBox.SelectedItem = null;
            foreach (var item in ActionComboBox.Items)
                if (item is ComboBoxItem { Tag: FrontButtonAction candidate } && candidate == action)
                {
                    ActionComboBox.SelectedItem = item;
                    return;
                }
        }

        private void SelectKey(FrontButtonHotkeyKey key)
        {
            _key.SelectedItem = null;
            foreach (var item in _key.Items)
                if (item is ComboBoxItem { Tag: FrontButtonHotkeyKey candidate } && candidate == key)
                {
                    _key.SelectedItem = item;
                    return;
                }
        }
    }
}
