using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class FanHardwareProbePage : UserControl
{
    private IAddonFrontendControl? _frontend; private FrontendFanProbeSnapshot? _latest; private bool _active;
    public event EventHandler? BackRequested;
    public FanHardwareProbePage() => InitializeComponent();
    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;
    internal void Activate() { _active = true; ResetUi(); _ = OpenAsync(); }
    internal void Deactivate() => _active = false;
    private async Task OpenAsync() { try { if (_frontend is not null) Render(await _frontend.OpenFanProbeAsync()); } catch (Exception e) { ErrorText.Text = e.Message; } }
    private async void Capture_Click(object s, RoutedEventArgs e) => await Run(FrontendFanProbeOperation.Capture);
    private async void Automatic_Click(object s, RoutedEventArgs e) => await Run(FrontendFanProbeOperation.AutomaticTest);
    private async void Restore_Click(object s, RoutedEventArgs e) => await Run(FrontendFanProbeOperation.RestoreAuto);
    private async void Physical_Click(object s, RoutedEventArgs e) => await Run(FrontendFanProbeOperation.PhysicalResponse);
    private async void Suspend_Click(object s, RoutedEventArgs e) => await Run(FrontendFanProbeOperation.ArmSuspendResume);
    private async Task Run(FrontendFanProbeOperation operation)
    { if (_frontend is null) return; SetBusy(true); try { Render(await _frontend.RunFanProbeAsync(operation)); } catch (FrontendTransportException ex) { ErrorText.Text = ex.Message; } catch (Exception ex) { ErrorText.Text = ex.Message; } finally { SetBusy(false); } }
    private void Back_Click(object s, RoutedEventArgs e) { _active = false; BackRequested?.Invoke(this, EventArgs.Empty); }
    private void Report_Click(object s, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(_latest?.ReportPath)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_latest.ReportPath}\"") { UseShellExecute = true }); }
    private void ResetUi() { _latest = null; StatusText.Text = "Ready"; ResultText.Text = "Last Result: Not run"; ErrorText.Text = ""; ReportButton.IsEnabled = false; }
    private void SetBusy(bool busy) { var armed = _latest?.Status == "ARMED"; CaptureButton.IsEnabled = !busy && !armed; AutomaticButton.IsEnabled = !busy && !armed; PhysicalButton.IsEnabled = !busy && !armed; SuspendButton.IsEnabled = !busy && !armed; RestoreButton.IsEnabled = !busy; if (busy) StatusText.Text = "Running..."; }
    private void Render(FrontendFanProbeSnapshot s) { if (!_active) return; _latest = s; DeviceText.Text = $"Device: {s.Model}"; BoardText.Text = $"Board: {s.BaseBoard}"; ModelText.Text = $"Probe profile: {s.ProbeModel}"; StatusText.Text = $"Status: {s.Status}"; ResultText.Text = $"Result: {s.Status}\nReport: {s.ReportPath ?? "Not generated"}"; ErrorText.Text = s.ErrorMessage ?? ""; ReportButton.IsEnabled = s.HasReport; SetBusy(false); }
}
