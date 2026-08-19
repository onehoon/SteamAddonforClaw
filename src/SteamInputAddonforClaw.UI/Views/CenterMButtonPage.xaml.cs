using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;
using Windows.Storage.Pickers;

namespace SteamInputAddonforClaw.Views;

/// <summary>
/// The one Center M Button detail page: the global remapping switch plus the four mapping slots,
/// grouped by mapping domain.
/// </summary>
/// <remarks>
/// Every action ComboBox is populated from <see cref="Oem1ActionCapabilities.ActionsFor"/> -- the
/// SAME table the runtime dispatcher validates persisted bindings against -- so the UI can never
/// offer a combination the runtime would refuse, and neither side restates the matrix.
///
/// <para>
/// Editing is whole-record: any change sends the complete <see cref="Oem1MappingSettings"/> back, so
/// flipping the remapping switch off carries the four bindings through untouched instead of relying
/// on the backend to remember them.
/// </para>
/// </remarks>
public sealed partial class CenterMButtonPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private Func<nint>? _windowHandleProvider;
    private Oem1MappingSettings _mapping = Oem1MappingSettings.Default;
    private SlotEditor[] _slots = [];
    /// <summary>Suppresses the change handlers while the page is writing the persisted state INTO
    /// the controls, so restoring the UI never looks like a user edit and re-saves.</summary>
    private bool _isLoading;

    // Review fix (BLOCKER): every edit used to fire its own independent fire-and-forget SaveAsync,
    // all reading _mapping as it stood BEFORE any of them completed. Two edits issued back-to-back
    // (easy to trigger from the hotkey modifier checkboxes) both derived from the same stale
    // snapshot, so the second request could silently drop the first edit, and whichever RPC response
    // landed last could overwrite the controls with an older whole-record snapshot. _saveChain
    // orders every save behind its predecessor; _editVersion lets a completing save recognize it is
    // no longer the newest edit and skip re-applying a stale result to the controls.
    private Task _saveChain = Task.CompletedTask;
    private long _editVersion;
    private Oem1MappingSettings _lastPersistedMapping = Oem1MappingSettings.Default;

    public CenterMButtonPage()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user asks to go back; the host owns navigation.</summary>
    internal event EventHandler? BackRequested;

    /// <summary>Raised after a successful save so the host can refresh the Controller page's own
    /// remapping toggle without re-fetching bootstrap.</summary>
    internal event EventHandler<Oem1MappingSettings>? MappingChanged;

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap, Func<nint> windowHandleProvider)
    {
        _frontend = frontend;
        _windowHandleProvider = windowHandleProvider;
        _slots =
        [
            new SlotEditor(Oem1BindingSlot.NormalSingle, NormalSingleActionComboBox, NormalSingleConfigCard, NormalSingleConfigPanel, this),
            new SlotEditor(Oem1BindingSlot.NormalDouble, NormalDoubleActionComboBox, NormalDoubleConfigCard, NormalDoubleConfigPanel, this),
            new SlotEditor(Oem1BindingSlot.RoutingSingle, RoutingSingleActionComboBox, RoutingSingleConfigCard, RoutingSingleConfigPanel, this),
            new SlotEditor(Oem1BindingSlot.RoutingDouble, RoutingDoubleActionComboBox, RoutingDoubleConfigCard, RoutingDoubleConfigPanel, this)
        ];

        Apply(bootstrap.Settings.Oem1Mapping);
    }

    /// <summary>Writes a persisted mapping into every control. Never tears the editors down and
    /// rebuilds them -- the toggle changing must not disturb what the user is looking at.</summary>
    internal void Apply(Oem1MappingSettings mapping)
    {
        _isLoading = true;
        try
        {
            _mapping = mapping;
            _lastPersistedMapping = mapping;
            RemappingToggleSwitch.IsOn = mapping.RemappingEnabled;
            foreach (var slot in _slots) slot.Load(mapping.Resolve(slot.Slot));
            ApplyRemappingEnabledState(mapping.RemappingEnabled);
        }
        finally { _isLoading = false; }
    }

    /// <summary>
    /// Global-off presentation: the mapping editors are disabled but stay visible with their saved
    /// values, and the explanation of what "off" means lives on the switch's own card. Nothing is
    /// cleared or hidden.
    /// </summary>
    private void ApplyRemappingEnabledState(bool enabled)
    {
        NormalExpander.IsEnabled = enabled;
        RoutingExpander.IsEnabled = enabled;
    }

    private void BackButton_Click(object sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void RemappingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading) return;
        QueueSave(_mapping with { RemappingEnabled = RemappingToggleSwitch.IsOn });
    }

    private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isLoading) return;
        foreach (var slot in _slots)
        {
            if (!ReferenceEquals(slot.ActionComboBox, sender)) continue;
            slot.ShowConfigurationFor(slot.SelectedAction);
            QueueSave(_mapping.With(slot.Slot, slot.Capture()));
            return;
        }
    }

    /// <summary>Called by a slot editor whenever its action-specific configuration changed.</summary>
    private void OnSlotConfigurationChanged(SlotEditor slot)
    {
        if (_isLoading) return;
        QueueSave(_mapping.With(slot.Slot, slot.Capture()));
    }

    /// <summary>Advances the page's local mapping synchronously (so the very next edit -- even one
    /// issued before this save's RPC returns -- is built on top of it) and chains the actual save
    /// behind whatever is already in flight.</summary>
    private void QueueSave(Oem1MappingSettings next)
    {
        _mapping = next;
        var version = ++_editVersion;
        _saveChain = SaveAfterAsync(_saveChain, next, version);
    }

    private async Task SaveAfterAsync(Task previous, Oem1MappingSettings next, long version)
    {
        // The predecessor's own failure (if any) was already handled where it happened; this chain
        // only needs it to have FINISHED before this save starts, so a faulted predecessor must not
        // abort the newly queued one.
        try { await previous; }
        catch { /* observed above */ }

        if (_frontend is null) return;
        try
        {
            var result = await _frontend.SetOem1MappingAsync(next);
            _lastPersistedMapping = result.Oem1Mapping;

            // A newer edit was queued while this request was in flight -- its own save is already
            // chained behind this one and will apply the true final state; applying this now-stale
            // response to the controls would visibly revert what the user just typed.
            if (version != _editVersion) return;

            Apply(result.Oem1Mapping);
            MappingChanged?.Invoke(this, result.Oem1Mapping);
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterMButton", "Center M mapping save failed.", exception);
            // Put the controls back to what is actually persisted rather than leaving the page
            // showing an edit that never landed -- but only if nothing newer is already queued to
            // resolve this itself.
            if (version == _editVersion)
                Apply(_lastPersistedMapping);
        }
    }

    private async Task BrowseForExecutableAsync(SlotEditor slot)
    {
        if (_windowHandleProvider is not { } handleProvider) return;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".lnk");
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handleProvider());
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            slot.SetExecutablePath(file.Path);
            OnSlotConfigurationChanged(slot);
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterMButton", "Application picker failed.", exception);
        }
    }

    /// <summary>Human-readable label for an action; the ComboBox stores the enum value itself as the
    /// item tag so nothing has to parse these strings back.</summary>
    private static string DescribeAction(Oem1Action action) => action switch
    {
        Oem1Action.None => "None",
        Oem1Action.SteamBigPicture => "Steam Big Picture",
        Oem1Action.SteamQuickAccess => "Steam Quick Access",
        Oem1Action.KeyboardHotkey => "Keyboard / Hotkey",
        Oem1Action.LaunchApplication => "Launch Application",
        _ => action.ToString()
    };

    /// <summary>
    /// The controls for one mapping slot: the action ComboBox plus the inline hotkey and
    /// launch-application editors that appear directly beneath it. Deliberately one small class
    /// instantiated four times rather than four hand-copied blocks of event handlers.
    /// </summary>
    private sealed class SlotEditor
    {
        private readonly CenterMButtonPage _page;
        private readonly SettingsCard _configCard;
        private readonly StackPanel _hotkeyPanel;
        private readonly CheckBox _control = new() { Content = "Ctrl" };
        private readonly CheckBox _shift = new() { Content = "Shift" };
        private readonly CheckBox _alt = new() { Content = "Alt" };
        private readonly CheckBox _windows = new() { Content = "Win" };
        private readonly ComboBox _key = new() { MinWidth = 140 };
        private readonly StackPanel _launchPanel;
        private readonly TextBox _path = new() { PlaceholderText = "Application path", MinWidth = 320 };
        private readonly TextBox _arguments = new() { PlaceholderText = "Arguments (optional)", MinWidth = 320 };

        internal SlotEditor(Oem1BindingSlot slot, ComboBox actionComboBox, SettingsCard configCard, StackPanel configPanel, CenterMButtonPage page)
        {
            Slot = slot;
            ActionComboBox = actionComboBox;
            _configCard = configCard;
            _page = page;

            foreach (var action in Oem1ActionCapabilities.ActionsFor(slot))
                actionComboBox.Items.Add(new ComboBoxItem { Content = DescribeAction(action), Tag = action });

            foreach (var key in Enum.GetValues<Oem1HotkeyKey>())
            {
                if (key == Oem1HotkeyKey.None) continue;
                _key.Items.Add(new ComboBoxItem { Content = key.ToString(), Tag = key });
            }

            _hotkeyPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Visibility = Visibility.Collapsed,
                Children = { _control, _shift, _alt, _windows, _key }
            };

            var browse = new Button { Content = "Browse…" };
            browse.Click += (_, _) => _ = page.BrowseForExecutableAsync(this);
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

            foreach (var modifier in new[] { _control, _shift, _alt, _windows })
            {
                modifier.Checked += (_, _) => page.OnSlotConfigurationChanged(this);
                modifier.Unchecked += (_, _) => page.OnSlotConfigurationChanged(this);
            }
            _key.SelectionChanged += (_, _) => page.OnSlotConfigurationChanged(this);
            // LostFocus, not TextChanged: saving on every keystroke would write the settings file
            // once per character.
            _path.LostFocus += (_, _) => page.OnSlotConfigurationChanged(this);
            _arguments.LostFocus += (_, _) => page.OnSlotConfigurationChanged(this);
        }

        internal Oem1BindingSlot Slot { get; }
        internal ComboBox ActionComboBox { get; }

        internal Oem1Action SelectedAction =>
            ActionComboBox.SelectedItem is ComboBoxItem { Tag: Oem1Action action } ? action : Oem1Action.None;

        internal void Load(Oem1SlotBinding binding)
        {
            SelectAction(binding.Action);
            _control.IsChecked = binding.Hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Control);
            _shift.IsChecked = binding.Hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Shift);
            _alt.IsChecked = binding.Hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Alt);
            _windows.IsChecked = binding.Hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Windows);
            SelectKey(binding.Hotkey.Key);
            _path.Text = binding.Launch.ExecutablePath;
            _arguments.Text = binding.Launch.Arguments;
            ShowConfigurationFor(binding.Action);
        }

        /// <summary>Builds the binding the controls currently describe. The hotkey and launch
        /// configurations are always carried, whichever action is selected, so switching action back
        /// and forth does not discard what the user already typed.</summary>
        internal Oem1SlotBinding Capture() => new()
        {
            Action = SelectedAction,
            Hotkey = new Oem1HotkeyBinding(CaptureModifiers(), CaptureKey()),
            Launch = new Oem1LaunchApplicationBinding(_path.Text ?? string.Empty, _arguments.Text ?? string.Empty)
        };

        internal void SetExecutablePath(string path) => _path.Text = path;

        internal void ShowConfigurationFor(Oem1Action action)
        {
            _hotkeyPanel.Visibility = action == Oem1Action.KeyboardHotkey ? Visibility.Visible : Visibility.Collapsed;
            _launchPanel.Visibility = action == Oem1Action.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
            // The whole row disappears for actions with nothing to configure, rather than leaving an
            // empty card behind.
            _configCard.Visibility = action is Oem1Action.KeyboardHotkey or Oem1Action.LaunchApplication
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private Oem1HotkeyModifiers CaptureModifiers()
        {
            var modifiers = Oem1HotkeyModifiers.None;
            if (_control.IsChecked == true) modifiers |= Oem1HotkeyModifiers.Control;
            if (_shift.IsChecked == true) modifiers |= Oem1HotkeyModifiers.Shift;
            if (_alt.IsChecked == true) modifiers |= Oem1HotkeyModifiers.Alt;
            if (_windows.IsChecked == true) modifiers |= Oem1HotkeyModifiers.Windows;
            return modifiers;
        }

        private Oem1HotkeyKey CaptureKey() =>
            _key.SelectedItem is ComboBoxItem { Tag: Oem1HotkeyKey key } ? key : Oem1HotkeyKey.None;

        /// <summary>A persisted action this slot does not support (an older build, a hand-edited
        /// settings file) is not offered here; the ComboBox falls back to no selection and the
        /// runtime independently refuses to execute it.</summary>
        private void SelectAction(Oem1Action action)
        {
            ActionComboBox.SelectedItem = null;
            foreach (var item in ActionComboBox.Items)
                if (item is ComboBoxItem { Tag: Oem1Action candidate } && candidate == action)
                {
                    ActionComboBox.SelectedItem = item;
                    return;
                }
        }

        private void SelectKey(Oem1HotkeyKey key)
        {
            _key.SelectedItem = null;
            foreach (var item in _key.Items)
                if (item is ComboBoxItem { Tag: Oem1HotkeyKey candidate } && candidate == key)
                {
                    _key.SelectedItem = item;
                    return;
                }
        }
    }
}
