using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.GordonDPad;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Views;

public sealed partial class GordonDPadDiagnosticPage : UserControl
{
    private GordonDPadDiagnosticSession? _session;
    private nint _ownerWindowHandle;
    private DispatcherQueueTimer? _uiTimer;

    public event EventHandler? BackRequested;

    public GordonDPadDiagnosticPage()
    {
        InitializeComponent();
    }

    internal void Initialize(nint ownerWindowHandle, AddonOwnedVirtualDeviceTracker ownedDeviceTracker)
    {
        _ownerWindowHandle = ownerWindowHandle;
        _session = new GordonDPadDiagnosticSession(
            new Win32GordonHidDevicePathResolver(),
            (path, length) => new Win32DirectHidReader(path, length),
            new Win32RawInputGordonObserver(),
            new Win32DeviceAncestryWalker(),
            () => new HashSet<string>(ownedDeviceTracker.OwnedInstanceIds, StringComparer.OrdinalIgnoreCase));
    }

    internal void PrepareForShow()
    {
        UpdateFromSnapshot(_session?.Snapshot);
        StartUiTimer();
    }

    internal async Task ShutdownAsync()
    {
        _uiTimer?.Stop();
        if (_session is not null) await _session.StopAsync();
    }

    private async void BackButton_Click(object sender, RoutedEventArgs args)
    {
        _uiTimer?.Stop();
        if (StopCaptureButton.IsEnabled && _session is not null) await _session.StopAsync();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StartCaptureButton_Click(object sender, RoutedEventArgs args)
    {
        if (_session is null) return;
        try
        {
            StartCaptureButton.IsEnabled = false;
            _session.Start(_ownerWindowHandle);
            StopCaptureButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("GordonDPadDiagnostic", "Failed to start capture.", exception);
            MessageText.Text = $"Failed to start capture: {exception.Message}";
            MessageText.Visibility = Visibility.Visible;
            StartCaptureButton.IsEnabled = true;
        }
    }

    private async void StopCaptureButton_Click(object sender, RoutedEventArgs args)
    {
        if (_session is null) return;
        try
        {
            StopCaptureButton.IsEnabled = false;
            await _session.StopAsync();
        }
        catch (Exception exception)
        {
            AppLog.Warn("GordonDPadDiagnostic", "Failed to stop capture cleanly.", exception);
        }
        finally
        {
            StartCaptureButton.IsEnabled = true;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs args)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw", "diagnostics");
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("GordonDPadDiagnostic", "Diagnostic folder could not be opened.", exception);
        }
    }

    private void StartUiTimer()
    {
        _uiTimer ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(300);
        _uiTimer.Tick -= UiTimer_Tick;
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    private void UiTimer_Tick(DispatcherQueueTimer sender, object args) => UpdateFromSnapshot(_session?.Snapshot);

    private void UpdateFromSnapshot(GordonDPadDiagnosticSnapshot? snapshot)
    {
        if (snapshot is null) return;
        GordonStatusText.Text = $"Gordon: {(snapshot.GordonState == GordonConnectionState.Connected ? "Connected" : "Not Available")}";
        NativeTraceStatusText.Text = $"Native Trace: {(snapshot.NativeTraceState == NativeTraceState.Active ? "Active" : "Not Available")}";
        WindowsHidStatusText.Text = "Windows HID: " + snapshot.WindowsHidMode switch
        {
            WindowsHidObservationMode.DirectHid => "Direct HID",
            WindowsHidObservationMode.RawInput => "Raw Input",
            WindowsHidObservationMode.Ambiguous => "Ambiguous (multiple candidates)",
            _ => "Not Available",
        };
        CaptureStatusText.Text = "Capture: " + snapshot.CaptureState switch
        {
            GordonDPadCaptureState.Active => "Running",
            GordonDPadCaptureState.WaitingForGordon => "Waiting for Gordon",
            GordonDPadCaptureState.GordonRemoved => "Gordon Removed",
            _ => "Stopped",
        };
        if (string.IsNullOrEmpty(snapshot.StatusMessage))
        {
            MessageText.Visibility = Visibility.Collapsed;
        }
        else
        {
            MessageText.Text = snapshot.StatusMessage;
            MessageText.Visibility = Visibility.Visible;
        }
        LastPhysicalText.Text = "Physical: " + (snapshot.LastPhysical ?? "(none observed yet)");
        LastCanonicalText.Text = "Canonical: " + (snapshot.LastCanonical ?? "(none observed yet)");
        LastAbiDecodedText.Text = "ABI Decoded: " + (snapshot.LastAbiDecoded ?? "(none observed yet)");
        LastGordonReportText.Text = "Gordon Report: " + (snapshot.LastGordonReport ?? "(none observed yet)");
        LastWindowsHidText.Text = "Windows HID: " + (snapshot.LastWindowsHid ?? "(none observed yet)");
    }
}
