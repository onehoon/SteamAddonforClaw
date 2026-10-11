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

    private async void RestoreMsiBackButtonDirectInputButton_Click(object sender, RoutedEventArgs args)
    {
        if (_isPrerequisiteSetupInProgress?.Invoke() == true || _frontend is null || XamlRoot is null
            || Volatile.Read(ref _msiBackButtonDiagnosticCancellation) is not null)
            return;

        var confirmation = new ContentDialog
        {
            Title = "Restore M1/M2 DirectInput (Experimental)",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "This writes the M1 and M2 controller profile and may persist across reboot and between SteamOS and Windows. This is not a read-only test. Only the exact recognized profile is eligible; unknown or custom mappings are left untouched. Keep the device connected and do not change controller mode during the operation.",
            },
            PrimaryButtonText = "Write M1/M2 Profile",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            return;

        var cancellation = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref _msiBackButtonDiagnosticCancellation, cancellation, null) is not null)
        {
            cancellation.Dispose();
            return;
        }

        RunMsiBackButtonDiagnosticButton.IsEnabled = false;
        RestoreMsiBackButtonDirectInputButton.IsEnabled = false;
        CancelMsiBackButtonDiagnosticButton.IsEnabled = true;
        SetMsiBackButtonDiagnosticStatus(string.Empty);
        SetMsiBackButtonManualRestoreStatus(
            "Checking both profile slots and current Runtime ownership. Cancel is honored before the first write; a write already sent cannot be undone.");
        try
        {
            var result = await _frontend.RunMsiBackButtonManualRestoreAsync(cancellation.Token);
            SetMsiBackButtonManualRestoreStatus(FormatMsiBackButtonManualRestoreResult(result));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetMsiBackButtonManualRestoreStatus("Cancelled before the first firmware write. No profile write was issued.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerBackButton", "Manual M1/M2 DirectInput restore failed in the Developer UI.", exception);
            SetMsiBackButtonManualRestoreStatus("Manual restore failed. Check the ControllerBackButton log for exact command and readback evidence.");
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _msiBackButtonDiagnosticCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
            RunMsiBackButtonDiagnosticButton.IsEnabled = true;
            RestoreMsiBackButtonDirectInputButton.IsEnabled = true;
            CancelMsiBackButtonDiagnosticButton.IsEnabled = false;
        }
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
        RestoreMsiBackButtonDirectInputButton.IsEnabled = false;
        CancelMsiBackButtonDiagnosticButton.IsEnabled = true;
        SetMsiBackButtonManualRestoreStatus(string.Empty);
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
            RestoreMsiBackButtonDirectInputButton.IsEnabled = true;
            CancelMsiBackButtonDiagnosticButton.IsEnabled = false;
        }
    }

    private void SetMsiBackButtonDiagnosticStatus(string text)
    {
        MsiBackButtonDiagnosticStatusText.Text = text;
        MsiBackButtonDiagnosticStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    internal static string FormatMsiBackButtonDiagnosticResult(FrontendMsiBackButtonDiagnosticResult result)
    {
        var firmware = result.FirmwareVersion is ushort version ? $"0x{version:X4}" : "Unavailable";
        var raw = result.RawInput;
        var sample = string.Join(", ", raw.Transitions.Take(6)
            .Select(item => $"{item.ButtonIndex}({item.RisingEdges}+/{item.FallingEdges}-)"));
        var indices = raw.Transitions.Count == 0
            ? "no changed indices"
            : $"{raw.Transitions.Count} changed index(es): {sample}{(raw.Transitions.Count > 6 ? ", …" : string.Empty)}";
        return $"Model: {result.ModelId} · Firmware: {firmware}\r\n" +
               $"M1 0x{result.M1.Address:X4}: {result.M1.Outcome} / {result.M1.Classification} · M2 0x{result.M2.Address:X4}: {result.M2.Outcome} / {result.M2.Classification}\r\n" +
               $"Raw: {raw.Outcome}, {raw.SuccessfulReadCount:N0} reads, {indices}\r\n" +
               $"Index 15={raw.Button15PressedAndReleasedObserved}, 16={raw.Button16PressedAndReleasedObserved}; detailed bytes/counts are in the log. Read-only; no firmware mutation." +
               (string.IsNullOrWhiteSpace(result.Status) ? string.Empty : $"\r\n{CompactStatus(result.Status)}");
    }

    internal static string FormatMsiBackButtonManualRestoreResult(FrontendMsiBackButtonManualRestoreResult result)
    {
        var firmware = result.FirmwareVersion is ushort version ? $"0x{version:X4}" : "Unavailable";
        var raw = result.RawInput;
        var m1Candidate = result.M1.BeforeClassification == FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate;
        var m2Candidate = result.M2.BeforeClassification == FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate;
        return $"{result.Outcome} · {result.ModelId} · FW {firmware}\r\n" +
               $"M1 candidate={m1Candidate}; {result.M1.BeforePayloadHex ?? "n/a"} → {result.M1.AfterPayloadHex ?? "n/a"}; write={result.M1.WriteIssued}/{result.M1.WriteTransportSucceeded}\r\n" +
               $"M2 candidate={m2Candidate}; {result.M2.BeforePayloadHex ?? "n/a"} → {result.M2.AfterPayloadHex ?? "n/a"}; write={result.M2.WriteIssued}/{result.M2.WriteTransportSucceeded}\r\n" +
               $"ROM sync issued/succeeded={result.RomSyncIssued}/{result.RomSyncTransportSucceeded}; both readbacks={result.ReadbackObserved}; raw 15/16 observed={result.RawInput15_16Observed} ({raw.Outcome})\r\n" +
               CompactStatus(result.Status);
    }

    private void SetMsiBackButtonManualRestoreStatus(string text)
    {
        MsiBackButtonManualRestoreStatusText.Text = text;
        MsiBackButtonManualRestoreStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string CompactStatus(string text) => text.Length <= 240 ? text : text[..237] + "…";

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
