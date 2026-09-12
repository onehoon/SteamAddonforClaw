using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class BatteryChargeLimitTestPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _active;
    private bool _busy;

    public event EventHandler? BackRequested;

    public BatteryChargeLimitTestPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate()
    {
        _active = true;
        ErrorText.Text = string.Empty;
        ResultText.Text = "Refreshing...";
        _ = RefreshAsync();
    }

    internal void Deactivate() => _active = false;

    private async void Refresh_Click(object sender, RoutedEventArgs args) => await RefreshAsync();

    private async void ApplyLimit_Click(object sender, RoutedEventArgs args)
    {
        if (LimitComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string text ||
            !int.TryParse(text.TrimEnd('%'), out var percent))
            return;

        await RunAsync(() => _frontend!.SetBatteryChargeLimitTestPercentAsync(percent));
    }

    private async void Enable_Click(object sender, RoutedEventArgs args) =>
        await RunAsync(() => _frontend!.SetBatteryChargeLimitTestEnabledAsync(true));

    private async void Disable_Click(object sender, RoutedEventArgs args) =>
        await RunAsync(() => _frontend!.SetBatteryChargeLimitTestEnabledAsync(false));

    private async Task RefreshAsync()
    {
        if (_frontend is null || _busy) return;
        SetBusy(true);
        try
        {
            Render(await _frontend.CaptureBatteryChargeLimitTestAsync());
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            ResultText.Text = "Result: Failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunAsync(Func<Task<FrontendBatteryChargeLimitTestMutationResult>> operation)
    {
        if (_frontend is null || _busy) return;
        SetBusy(true);
        try
        {
            var result = await operation();
            Render(result.Snapshot);
            ResultText.Text = result.Succeeded ? "Result: Succeeded" : $"Result: {result.Outcome}";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            ResultText.Text = "Result: Failed";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs args)
    {
        _active = false;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        ApplyLimitButton.IsEnabled = !busy;
        LimitComboBox.IsEnabled = !busy;
        EnableButton.IsEnabled = !busy;
        DisableButton.IsEnabled = !busy;
        if (busy) ResultText.Text = "Result: Running...";
    }

    private void Render(FrontendBatteryChargeLimitTestSnapshot snapshot)
    {
        if (!_active) return;
        ManufacturerText.Text = $"Manufacturer: {ValueOrUnknown(snapshot.Manufacturer)}";
        ModelText.Text = $"Model: {ValueOrUnknown(snapshot.Model)}";
        BoardText.Text = $"Board: {ValueOrUnknown(snapshot.BaseBoard)}";
        EnabledText.Text = $"Enabled: {(snapshot.Enabled is true ? "Yes" : snapshot.Enabled is false ? "No" : "Unknown")}";
        LimitText.Text = $"Limit: {(snapshot.LimitPercent is int percent ? $"{percent}%" : "Unknown")}";
        RawText.Text = $"Raw: {(snapshot.RawValue is byte raw ? $"0x{raw:X2}" : "Unknown")}";
        ProductValueText.Text = $"Product value: {(snapshot.RawValue is null ? "Unknown" : snapshot.ProductValueValid ? "Valid" : "Outside Addon range")}";
        LimitComboBox.SelectedIndex = snapshot.LimitPercent is int value && value is >= 60 and <= 100 && (value - 60) % 5 == 0 ? (value - 60) / 5 : -1;
        ErrorText.Text = snapshot.FailureMessage ?? string.Empty;
        ResultText.Text = snapshot.FailureMessage is not null
            ? snapshot.Available ? "Result: Failed" : "Result: Unavailable"
            : snapshot.Available ? "Result: State captured" : "Result: Unavailable";
    }

    private static string ValueOrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}
