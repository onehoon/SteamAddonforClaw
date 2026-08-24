using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _isLoading;
    private bool _lastKnownSteamInputRoutingEnabled;
    private Oem1MappingSettings _oem1Mapping = Oem1MappingSettings.Default;
    private bool _oem1MappingAvailable;

    public ControllerPage() => InitializeComponent();

    internal event EventHandler<Oem1MappingSettings>? MappingEditRequested;

    internal void Initialize(IAddonFrontendControl frontend, FrontendBootstrapSnapshot bootstrap, Func<nint> windowHandleProvider)
    {
        _frontend = frontend;
        _isLoading = true;
        _lastKnownSteamInputRoutingEnabled = bootstrap.Settings.SteamInputRoutingEnabled;
        SteamInputRoutingToggleSwitch.IsOn = _lastKnownSteamInputRoutingEnabled;
        _oem1MappingAvailable = bootstrap.Oem1MappingAvailable;
        CenterMInlineContent.Visibility = _oem1MappingAvailable ? Visibility.Visible : Visibility.Collapsed;
        CenterMUnavailableText.Visibility = _oem1MappingAvailable ? Visibility.Collapsed : Visibility.Visible;
        CenterMInlineContent.Initialize(bootstrap, windowHandleProvider);
        CenterMInlineContent.MappingEditRequested += (_, mapping) => MappingEditRequested?.Invoke(this, mapping with { RemappingEnabled = true });
        ApplyOem1Mapping(bootstrap.Settings.Oem1Mapping);
        _isLoading = false;
    }

    internal void ApplyOem1Mapping(Oem1MappingSettings mapping)
    {
        _oem1Mapping = mapping with { RemappingEnabled = true };
        CenterMInlineContent.Apply(_oem1Mapping);
    }

    private async void SteamInputRoutingToggleSwitch_Toggled(object sender, RoutedEventArgs args)
    {
        if (_isLoading || _frontend is null) return;
        if (SteamInputRoutingToggleSwitch.IsOn && !_lastKnownSteamInputRoutingEnabled)
        {
            if (!await ConfirmRoutingEnableAsync())
            {
                SetRouteToggle(false);
                return;
            }
        }

        try
        {
            var result = await _frontend.SetSteamInputRoutingEnabledAsync(SteamInputRoutingToggleSwitch.IsOn);
            _lastKnownSteamInputRoutingEnabled = result.Settings.SteamInputRoutingEnabled;
            SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Controller", "Steam Input routing update failed.", exception);
            SetRouteToggle(_lastKnownSteamInputRoutingEnabled);
        }
    }

    private async Task<bool> ConfirmRoutingEnableAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Enable Steam Input Routing?",
            Content = "This feature manages HidHide configuration for Steam Input routing.\n\n" +
                      "If another application also uses HidHide, its controller hiding or remapping features may not work correctly while this feature is enabled.",
            PrimaryButtonText = "Enable",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetRouteToggle(bool value)
    {
        _isLoading = true;
        SteamInputRoutingToggleSwitch.IsOn = value;
        _isLoading = false;
    }

}
