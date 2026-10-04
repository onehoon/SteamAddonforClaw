using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Windows.Storage.Pickers;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
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
    private bool _isRenderingVibration;
    private bool _vibrationMutationInProgress;
    private bool _vibrationTestInProgress;
    /// <summary>Suppresses change handlers while the page writes persisted state INTO the controls,
    /// so restoring the UI never looks like a user edit and re-saves.</summary>
    private bool _isLoading;

    public ControllerPage() => InitializeComponent();

    internal event EventHandler<FrontButtonMappingSettings>? MappingEditRequested;
    internal event EventHandler<BackButtonMappingSettings>? BackButtonMappingEditRequested;
    internal event EventHandler<ControllerLedSettings>? ControllerLedEditRequested;

    internal void Initialize(
        FrontendBootstrapSnapshot bootstrap,
        IAddonFrontendControl frontend,
        Func<nint> windowHandleProvider)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _available = bootstrap.FrontButtonMappingAvailable;
        _backButtonAvailable = bootstrap.BackButtonMappingAvailable;
        _controllerLedAvailable = bootstrap.ControllerLedAvailable;
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
            (left, right) => _ = CommitVibrationStrengthAsync(left, right));
        ApplyVibrationStrengthSnapshot(FrontendControllerVibrationStrengthSnapshot.Unavailable(), preserveDraft: false);
        ApplyControllerLedSettings(bootstrap.Settings.ControllerLed);
    }

    internal void Activate()
    {
        if (_frontend is not null)
            _ = RefreshVibrationStrengthAsync();
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
            SelectBackButtonTarget(M1TargetComboBox, mapping.M1);
            SelectBackButtonTarget(M2TargetComboBox, mapping.M2);
        }
        finally { _isLoading = false; }
    }

    private void PopulateBackButtonTargets()
    {
        foreach (var target in Enum.GetValues<Xbox360BackButtonTarget>())
        {
            M1TargetComboBox.Items.Add(new ComboBoxItem { Content = DescribeBackButtonTarget(target), Tag = target });
            M2TargetComboBox.Items.Add(new ComboBoxItem { Content = DescribeBackButtonTarget(target), Tag = target });
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

    private void VibrationStrengthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_isRenderingVibration
            || _frontend is null
            || !_vibrationSnapshot.Available
            || !_vibrationSnapshot.Writable
            || _vibrationMutationInProgress
            || _vibrationTestInProgress)
            return;

        var left = ToPercent(LeftVibrationStrengthSlider.Value);
        var right = ToPercent(RightVibrationStrengthSlider.Value);
        LeftVibrationStrengthPercentText.Text = $"{left}%";
        RightVibrationStrengthPercentText.Text = $"{right}%";
        _vibrationDebounce?.Schedule(left, right);
        UpdateVibrationControls();
    }

    private async void LeftVibrationTestButton_Click(object sender, RoutedEventArgs args) =>
        await TestVibrationMotorAsync(FrontendControllerVibrationMotor.Left);

    private async void RightVibrationTestButton_Click(object sender, RoutedEventArgs args) =>
        await TestVibrationMotorAsync(FrontendControllerVibrationMotor.Right);

    private async Task CommitVibrationStrengthAsync(int leftPercent, int rightPercent)
    {
        if (_frontend is null || !_vibrationSnapshot.Available || !_vibrationSnapshot.Writable)
            return;

        _vibrationMutationInProgress = true;
        UpdateVibrationControls();
        try
        {
            var result = await _frontend.SetControllerVibrationStrengthAsync(leftPercent, rightPercent);
            ApplyVibrationStrengthSnapshot(result.Snapshot, preserveDraft: false);
            if (!result.Succeeded)
            {
                ShowVibrationMessage(result.FailureMessage ?? "Firmware vibration values could not be verified.", InfoBarSeverity.Error);
                await RefreshVibrationStrengthAsync(preserveFailure: true);
                return;
            }

            if (!result.Snapshot.Writable && result.Snapshot.Available)
                ShowVibrationMessage(result.Snapshot.Status, InfoBarSeverity.Informational);
            else
                HideVibrationMessage();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerVibration", "Controller vibration firmware mutation transport failed.", exception);
            ShowVibrationMessage("The firmware result is uncertain. Reading the current values again.", InfoBarSeverity.Error);
            await RefreshVibrationStrengthAsync(preserveFailure: true);
        }
        finally
        {
            _vibrationMutationInProgress = false;
            UpdateVibrationControls();
        }
    }

    private async Task TestVibrationMotorAsync(FrontendControllerVibrationMotor motor)
    {
        if (_frontend is null
            || !_vibrationSnapshot.Available
            || !_vibrationSnapshot.TestAvailable
            || _vibrationDebounce?.HasPendingDraft == true
            || _vibrationMutationInProgress
            || _vibrationTestInProgress)
            return;

        _vibrationTestInProgress = true;
        UpdateVibrationControls();
        try
        {
            var result = await _frontend.TestControllerVibrationMotorAsync(motor);
            if (result.Succeeded)
                HideVibrationMessage();
            else
                ShowVibrationMessage(result.FailureMessage ?? "The physical vibration test did not complete safely.", InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerVibration", "Controller vibration test transport failed.", exception,
                ("Motor", motor));
            ShowVibrationMessage("The test result is uncertain. Refreshing firmware values.", InfoBarSeverity.Error);
            await RefreshVibrationStrengthAsync(preserveFailure: true);
        }
        finally
        {
            _vibrationTestInProgress = false;
            UpdateVibrationControls();
        }
    }

    private async Task RefreshVibrationStrengthAsync(bool preserveFailure = false)
    {
        if (_frontend is null)
            return;

        try
        {
            var snapshot = await _frontend.CaptureControllerVibrationStrengthAsync();
            var preserveDraft = _vibrationMutationInProgress || _vibrationDebounce?.HasPendingDraft == true;
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
            AppLog.Warn("ControllerVibration", "Controller vibration firmware capture transport failed.", exception);
            _vibrationDebounce?.CancelPending();
            ApplyVibrationStrengthSnapshot(
                FrontendControllerVibrationStrengthSnapshot.Unavailable("The firmware vibration values could not be read."),
                preserveDraft: false);
            ShowVibrationMessage("The firmware vibration values could not be read.", InfoBarSeverity.Error);
        }
    }

    private void ApplyVibrationStrengthSnapshot(
        FrontendControllerVibrationStrengthSnapshot snapshot,
        bool preserveDraft)
    {
        _vibrationSnapshot = snapshot;
        if (preserveDraft && !snapshot.Available)
        {
            _vibrationDebounce?.CancelPending();
            preserveDraft = false;
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
        var operationInProgress = _vibrationMutationInProgress || _vibrationTestInProgress;
        LeftVibrationStrengthSlider.IsEnabled = _vibrationSnapshot.Available
            && _vibrationSnapshot.Writable && !operationInProgress;
        RightVibrationStrengthSlider.IsEnabled = _vibrationSnapshot.Available
            && _vibrationSnapshot.Writable && !operationInProgress;

        var testsEnabled = _vibrationSnapshot.TestAvailable
            && _vibrationDebounce?.HasPendingDraft != true
            && !operationInProgress;
        LeftVibrationTestButton.IsEnabled = testsEnabled;
        RightVibrationTestButton.IsEnabled = testsEnabled;
    }

    private void ShowVibrationMessage(string message, InfoBarSeverity severity)
    {
        VibrationStrengthInfoBar.Severity = severity;
        VibrationStrengthInfoBar.Message = message;
        VibrationStrengthInfoBar.IsOpen = true;
    }

    private void HideVibrationMessage() => VibrationStrengthInfoBar.IsOpen = false;

    private static int ToPercent(double value) => Math.Clamp((int)Math.Round(value), 0, 100);

    private static void SelectBackButtonTarget(ComboBox comboBox, Xbox360BackButtonTarget target)
    {
        comboBox.SelectedItem = null;
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem { Tag: Xbox360BackButtonTarget candidate } && candidate == target)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private static string DescribeBackButtonTarget(Xbox360BackButtonTarget target) => target switch
    {
        Xbox360BackButtonTarget.Disabled => "Disabled",
        Xbox360BackButtonTarget.A => "A",
        Xbox360BackButtonTarget.B => "B",
        Xbox360BackButtonTarget.X => "X",
        Xbox360BackButtonTarget.Y => "Y",
        Xbox360BackButtonTarget.DPadUp => "D-Pad Up",
        Xbox360BackButtonTarget.DPadRight => "D-Pad Right",
        Xbox360BackButtonTarget.DPadDown => "D-Pad Down",
        Xbox360BackButtonTarget.DPadLeft => "D-Pad Left",
        Xbox360BackButtonTarget.LeftBumper => "Left Bumper (LB)",
        Xbox360BackButtonTarget.RightBumper => "Right Bumper (RB)",
        Xbox360BackButtonTarget.LeftTrigger => "Left Trigger (LT)",
        Xbox360BackButtonTarget.RightTrigger => "Right Trigger (RT)",
        Xbox360BackButtonTarget.LeftStickClick => "Left Stick Click (L3)",
        Xbox360BackButtonTarget.RightStickClick => "Right Stick Click (R3)",
        Xbox360BackButtonTarget.View => "View",
        Xbox360BackButtonTarget.Menu => "Menu",
        Xbox360BackButtonTarget.XboxGuide => "Xbox Guide",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
    };

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
        FrontButtonAction.QuickSettingsOverlay => "Quick Settings Overlay",
        FrontButtonAction.SteamBigPicture => "Steam Big Picture",
        FrontButtonAction.SteamButton => "Steam Button",
        FrontButtonAction.SteamQuickAccess => "Steam Quick Access",
        FrontButtonAction.KeyboardHotkey => "Keyboard / Hotkey",
        FrontButtonAction.LaunchApplication => "Launch Application",
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
        private readonly TextBox _path = new() { PlaceholderText = "Application path", MinWidth = 320 };
        private readonly TextBox _arguments = new() { PlaceholderText = "Arguments (optional)", MinWidth = 320 };

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

            var browse = new Button { Content = "Browse…" };
            browse.Click += (_, _) => _ = page.BrowseForExecutableAsync(this, windowHandleProvider);
            _launchPanel = new StackPanel
            {
                Spacing = 8,
                Visibility = Visibility.Collapsed,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _path, browse } },
                    _arguments
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
            ShowConfigurationFor(binding.Action);
        }

        internal FrontButtonBinding Capture() => new()
        {
            Action = SelectedAction,
            Hotkey = new FrontButtonHotkeyBinding(CaptureModifiers(), CaptureKey()),
            Launch = new FrontButtonLaunchApplicationBinding(_path.Text ?? string.Empty, _arguments.Text ?? string.Empty)
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
