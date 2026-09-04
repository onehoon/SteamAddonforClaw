using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ControllerPage : UserControl
{
    private Oem1MappingSettings _oem1Mapping = Oem1MappingSettings.Default;
    private bool _oem1MappingAvailable;

    public ControllerPage() => InitializeComponent();

    internal event EventHandler<Oem1MappingSettings>? MappingEditRequested;

    internal void Initialize(FrontendBootstrapSnapshot bootstrap, Func<nint> windowHandleProvider)
    {
        _oem1MappingAvailable = bootstrap.Oem1MappingAvailable;
        CenterMInlineContent.Visibility = _oem1MappingAvailable ? Visibility.Visible : Visibility.Collapsed;
        CenterMUnavailableText.Visibility = _oem1MappingAvailable ? Visibility.Collapsed : Visibility.Visible;
        CenterMInlineContent.Initialize(bootstrap, windowHandleProvider);
        CenterMInlineContent.MappingEditRequested += (_, mapping) => MappingEditRequested?.Invoke(this, mapping with { RemappingEnabled = true });
        ApplyOem1Mapping(bootstrap.Settings.Oem1Mapping);
    }

    internal void ApplyOem1Mapping(Oem1MappingSettings mapping)
    {
        _oem1Mapping = mapping with { RemappingEnabled = true };
        CenterMInlineContent.Apply(_oem1Mapping);
    }
}
