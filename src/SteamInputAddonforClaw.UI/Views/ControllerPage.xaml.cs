using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;

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
    private bool _available;
    private BindingEditor[] _editors = [];
    /// <summary>Suppresses change handlers while the page writes persisted state INTO the controls,
    /// so restoring the UI never looks like a user edit and re-saves.</summary>
    private bool _isLoading;

    public ControllerPage() => InitializeComponent();

    internal event EventHandler<FrontButtonMappingSettings>? MappingEditRequested;

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, Func<nint> windowHandleProvider)
    {
        _available = bootstrap.FrontButtonMappingAvailable;
        MappingContent.Visibility = _available ? Visibility.Visible : Visibility.Collapsed;
        MappingUnavailableText.Visibility = _available ? Visibility.Collapsed : Visibility.Visible;

        _editors =
        [
            new BindingEditor(FrontButtonKind.Gamebar, FrontButtonDomain.Normal, GamebarNormalActionComboBox, GamebarNormalConfigCard, GamebarNormalConfigPanel, this, windowHandleProvider),
            new BindingEditor(FrontButtonKind.Gamebar, FrontButtonDomain.Steam, GamebarSteamActionComboBox, GamebarSteamConfigCard, GamebarSteamConfigPanel, this, windowHandleProvider),
            new BindingEditor(FrontButtonKind.CenterM, FrontButtonDomain.Normal, CenterMNormalActionComboBox, CenterMNormalConfigCard, CenterMNormalConfigPanel, this, windowHandleProvider),
            new BindingEditor(FrontButtonKind.CenterM, FrontButtonDomain.Steam, CenterMSteamActionComboBox, CenterMSteamConfigCard, CenterMSteamConfigPanel, this, windowHandleProvider),
        ];

        ApplyFrontButtonMapping(bootstrap.Settings.FrontButtonMapping);
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
