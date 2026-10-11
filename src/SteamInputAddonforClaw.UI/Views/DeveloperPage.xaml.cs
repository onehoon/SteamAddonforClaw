using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Views;

public sealed partial class DeveloperPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private Func<bool>? _isPrerequisiteSetupInProgress;
    private int _isGeneratingEnvironmentDiscoveryReport;
    private int _isRunningPid1902InputCadenceDiagnostic;
    private CancellationTokenSource? _msiBackButtonDiagnosticCancellation;
    private string _logDirectoryPath = string.Empty;

    public event EventHandler? BackRequested;
    public event EventHandler? VibrationTestRequested;
    private void OpenVibrationTestButton_Click(object sender, RoutedEventArgs args) => VibrationTestRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? SensorProbeRequested;
    public event EventHandler? FanHardwareProbeRequested;
    public event EventHandler? BatteryChargeLimitTestRequested;
    public event EventHandler? GameInputSystemButtonProbeRequested;
    private void OpenSensorProbeButton_Click(object sender, RoutedEventArgs args) => SensorProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenFanHardwareProbeButton_Click(object sender, RoutedEventArgs args) => FanHardwareProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenBatteryChargeLimitTestButton_Click(object sender, RoutedEventArgs args) => BatteryChargeLimitTestRequested?.Invoke(this, EventArgs.Empty);
    private void OpenGameInputSystemButtonProbeButton_Click(object sender, RoutedEventArgs args) => GameInputSystemButtonProbeRequested?.Invoke(this, EventArgs.Empty);

    public DeveloperPage()
    {
        InitializeComponent();
        Unloaded += DeveloperPage_Unloaded;
    }

    internal void Initialize(
        IAddonFrontendControl frontend,
        FrontendBootstrapSnapshot bootstrap,
        Func<bool> isPrerequisiteSetupInProgress)
    {
        _frontend = frontend;
        _logDirectoryPath = bootstrap.LogDirectoryPath;
        _isPrerequisiteSetupInProgress = isPrerequisiteSetupInProgress;
    }

    private void BackButton_Click(object sender, RoutedEventArgs args)
    {
        CancelMsiBackButtonDiagnostic();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeveloperPage_Unloaded(object sender, RoutedEventArgs args) => CancelMsiBackButtonDiagnostic();

    private void CancelMsiBackButtonDiagnosticButton_Click(object sender, RoutedEventArgs args) => CancelMsiBackButtonDiagnostic();

    private void CancelMsiBackButtonDiagnostic()
    {
        try { _msiBackButtonDiagnosticCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_logDirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Log folder could not be opened.", exception);
        }
    }

    private async void GenerateEnvironmentDiscoveryReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true) return;
        if (Interlocked.Exchange(ref _isGeneratingEnvironmentDiscoveryReport, 1) != 0) return;
        GenerateEnvironmentDiscoveryReportButton.IsEnabled = false;
        SetEnvironmentDiscoveryStatus("Generating...");
        try
        {
            var result = await _frontend!.GenerateEnvironmentReportAsync();
            SetEnvironmentDiscoveryStatus(result.Succeeded ? string.Empty : "Report generation failed.\r\nSee the application log for details.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("EnvironmentDiscovery", "Environment discovery report generation failed.", exception, ("Reason", exception.GetType().Name));
            SetEnvironmentDiscoveryStatus("Report generation failed.\r\nSee the application log for details.");
        }
        finally
        {
            GenerateEnvironmentDiscoveryReportButton.IsEnabled = true;
            Volatile.Write(ref _isGeneratingEnvironmentDiscoveryReport, 0);
        }
    }

    private void SetEnvironmentDiscoveryStatus(string text)
    {
        EnvironmentDiscoveryReportStatusText.Text = text;
        EnvironmentDiscoveryReportStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void RunPid1902InputCadenceButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true || _frontend is null) return;
        if (Interlocked.Exchange(ref _isRunningPid1902InputCadenceDiagnostic, 1) != 0) return;

        RunPid1902InputCadenceButton.IsEnabled = false;
        SetPid1902InputCadenceStatus("Running...\r\nContinuously move both sticks and LT/RT for 10 seconds.");
        try
        {
            var result = await _frontend.RunPid1902InputCadenceDiagnosticAsync();
            SetPid1902InputCadenceStatus(FormatPid1902InputCadenceResult(result));
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "PID1902 input cadence diagnostic failed.", exception);
            SetPid1902InputCadenceStatus("Diagnostic failed. See the application log for details.");
        }
        finally
        {
            RunPid1902InputCadenceButton.IsEnabled = true;
            Volatile.Write(ref _isRunningPid1902InputCadenceDiagnostic, 0);
        }
    }

    private void SetPid1902InputCadenceStatus(string text)
    {
        Pid1902InputCadenceStatusText.Text = text;
        Pid1902InputCadenceStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void RunMsiBackButtonDiagnosticButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true || _frontend is null) return;
        var cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _msiBackButtonDiagnosticCancellation, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return;
        }

        RunMsiBackButtonDiagnosticButton.IsEnabled = false;
        CancelMsiBackButtonDiagnosticButton.IsEnabled = true;
        SetMsiBackButtonDiagnosticStatus("Running… Press and release M1 only, then M2 only; do not touch any other controls.");
        try
        {
            var result = await _frontend.RunMsiBackButtonProfileAndRawInputDiagnosticAsync(cancellation.Token);
            SetMsiBackButtonDiagnosticStatus(FormatMsiBackButtonDiagnosticResult(result));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetMsiBackButtonDiagnosticStatus("Cancelled. No firmware mutation was issued.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerBackButton", "M1/M2 profile and raw-input diagnostic failed in the Developer UI.", exception);
            SetMsiBackButtonDiagnosticStatus("Diagnostic failed. See the application log for details.");
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _msiBackButtonDiagnosticCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
            RunMsiBackButtonDiagnosticButton.IsEnabled = true;
            CancelMsiBackButtonDiagnosticButton.IsEnabled = false;
        }
    }

    private void SetMsiBackButtonDiagnosticStatus(string text)
    {
        MsiBackButtonDiagnosticStatusText.Text = text;
        MsiBackButtonDiagnosticStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatMsiBackButtonDiagnosticResult(FrontendMsiBackButtonDiagnosticResult result)
    {
        var firmware = result.FirmwareVersion is ushort version ? $"0x{version:X4}" : "Unavailable";
        var raw = result.RawInput;
        var indices = raw.Transitions.Count == 0
            ? "None"
            : string.Join(", ", raw.Transitions.Select(item => $"{item.ButtonIndex}: {item.RisingEdges}+/{item.FallingEdges}-"));
        return $"Model: {result.ModelId} · Firmware: {firmware}\r\n" +
               $"M1 0x{result.M1.Address:X4}: {result.M1.Outcome} / {result.M1.Classification}\r\n" +
               $"M2 0x{result.M2.Address:X4}: {result.M2.Outcome} / {result.M2.Classification}\r\n" +
               $"Raw input: {raw.Outcome}; buttons={raw.ButtonCount?.ToString() ?? "n/a"}; indices={indices}\r\n" +
               $"Index 15 press+release={raw.Button15PressedAndReleasedObserved}, index 16 press+release={raw.Button16PressedAndReleasedObserved}\r\n" +
               $"Index 15 changed while 16 released={raw.Button15ChangedWhileButton16Released}, index 16 changed while 15 released={raw.Button16ChangedWhileButton15Released}\r\n" +
               $"{result.Status}\r\nDetailed bounded bytes and counts are in the Addon log. Index changes do not identify a physical paddle without the instructed press sequence. No firmware mutation was issued.";
    }

    private static string FormatPid1902InputCadenceResult(FrontendPid1902InputCadenceResult result)
    {
        if (result.Outcome != FrontendPid1902InputCadenceOutcome.Completed)
            return result.Status;

        var text = $"Completed\r\nReads: {result.SuccessfulReadCount:N0} ({FormatHz(result.ObservedReadHz)})\r\n" +
                   $"Distinct states: {result.DistinctStateCount:N0} ({FormatHz(result.DistinctStateHz)})\r\n" +
                   $"Duplicates: {result.DuplicatePercent?.ToString("F1") ?? "n/a"}%\r\n\r\n" +
                   "Distinct interval:\r\n" +
                   $"Min       {FormatMilliseconds(result.MinDistinctIntervalMs)}\r\n" +
                   $"Mean      {FormatMilliseconds(result.MeanDistinctIntervalMs)}\r\n" +
                   $"Median    {FormatMilliseconds(result.MedianDistinctIntervalMs)}\r\n" +
                   $"P95       {FormatMilliseconds(result.P95DistinctIntervalMs)}\r\n" +
                   $"Max       {FormatMilliseconds(result.MaxDistinctIntervalMs)}";

        if (result.DistinctStateCount < 2)
            text += "\r\n\r\nCompleted, but too few changing raw states were observed. Run again while continuously moving both sticks and triggers.";
        else if (result.ObservedReadHz is not > 250)
            text += $"\r\n\r\nSampling rate was too low for a reliable 125 Hz vs 250 Hz comparison. Observed read rate: {FormatHz(result.ObservedReadHz)}.";

        return text;
    }

    private static string FormatHz(double? value) => value is { } hz ? $"{hz:F1} Hz" : "n/a";
    private static string FormatMilliseconds(double? value) => value is { } milliseconds ? $"{milliseconds:F2} ms" : "n/a";

}
