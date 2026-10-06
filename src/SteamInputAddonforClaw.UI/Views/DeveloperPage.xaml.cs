using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SteamInputAddonforClaw.Views;

public sealed partial class DeveloperPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private Func<bool>? _isPrerequisiteSetupInProgress;
    private bool _isInitializingLogLevel;
    private int _isGeneratingEnvironmentDiscoveryReport;
    private int _isRunningPid1902InputCadenceDiagnostic;
    private int _isRunningIntelGpuFrequencyProbe;
    private FrontendIntelGpuFrequencyProbeSnapshot _intelGpuFrequencyProbeSnapshot =
        FrontendIntelGpuFrequencyProbeSnapshot.Unavailable("Press Refresh to read Intel IGCL GPU capabilities.");
    private FrontendLogLevel _lastKnownLogLevel;
    private string _logDirectoryPath = string.Empty;

    public event EventHandler? BackRequested;
    public event EventHandler? VibrationTestRequested;
    private void OpenVibrationTestButton_Click(object sender, RoutedEventArgs args) => VibrationTestRequested?.Invoke(this, EventArgs.Empty);
    public event EventHandler? SensorProbeRequested;
    public event EventHandler? FanHardwareProbeRequested;
    public event EventHandler? BatteryChargeLimitTestRequested;
    public event EventHandler? XboxSessionDiagnosticRequested;
    public event EventHandler? GameInputSystemButtonProbeRequested;
    private void OpenSensorProbeButton_Click(object sender, RoutedEventArgs args) => SensorProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenFanHardwareProbeButton_Click(object sender, RoutedEventArgs args) => FanHardwareProbeRequested?.Invoke(this, EventArgs.Empty);
    private void OpenBatteryChargeLimitTestButton_Click(object sender, RoutedEventArgs args) => BatteryChargeLimitTestRequested?.Invoke(this, EventArgs.Empty);
    private void OpenXboxSessionDiagnosticButton_Click(object sender, RoutedEventArgs args) => XboxSessionDiagnosticRequested?.Invoke(this, EventArgs.Empty);
    private void OpenGameInputSystemButtonProbeButton_Click(object sender, RoutedEventArgs args) => GameInputSystemButtonProbeRequested?.Invoke(this, EventArgs.Empty);

    public DeveloperPage()
    {
        InitializeComponent();
    }

    internal void Initialize(
        IAddonFrontendControl frontend,
        FrontendBootstrapSnapshot bootstrap,
        Func<bool> isPrerequisiteSetupInProgress)
    {
        _frontend = frontend;
        _lastKnownLogLevel = bootstrap.Settings.LogLevel;
        _logDirectoryPath = bootstrap.LogDirectoryPath;
        _isPrerequisiteSetupInProgress = isPrerequisiteSetupInProgress;

        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = bootstrap.Settings.LogLevel switch
        {
            FrontendLogLevel.Info => 1,
            FrontendLogLevel.Debug => 2,
            _ => 0,
        };
        _isInitializingLogLevel = false;
        RenderIntelGpuFrequencyProbe();
    }

    private void BackButton_Click(object sender, RoutedEventArgs args)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
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

    private async void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_isInitializingLogLevel || _frontend is null || LogLevelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string value) return;
        var level = value switch { "Debug" => FrontendLogLevel.Debug, "Info" => FrontendLogLevel.Info, _ => FrontendLogLevel.Off };
        try
        {
            var result = await _frontend.SetLogLevelAsync(level);
            _lastKnownLogLevel = result.LogLevel;
            SetLogLevel(_lastKnownLogLevel);
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Log level update failed.", exception);
            await RefreshAuthoritativeStateAsync(() => SetLogLevel(_lastKnownLogLevel));
        }
    }

    private async Task RefreshAuthoritativeStateAsync(Action fallback)
    {
        try
        {
            var bootstrap = await _frontend!.GetBootstrapAsync();
            _lastKnownLogLevel = bootstrap.Settings.LogLevel;
            _logDirectoryPath = bootstrap.LogDirectoryPath;
            SetLogLevel(_lastKnownLogLevel);
        }
        catch (Exception refreshException)
        {
            AppLog.Warn("DeveloperMenu", "Developer settings state refresh failed.", refreshException);
            fallback();
        }
    }

    private void SetLogLevel(FrontendLogLevel level)
    {
        _isInitializingLogLevel = true;
        LogLevelComboBox.SelectedIndex = level switch { FrontendLogLevel.Info => 1, FrontendLogLevel.Debug => 2, _ => 0 };
        _isInitializingLogLevel = false;
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

    private async void RefreshIntelGpuFrequencyProbeButton_Click(object sender, RoutedEventArgs args) =>
        await RunIntelGpuFrequencyProbeOperationAsync(
            () => _frontend!.CaptureIntelGpuFrequencyProbeAsync(), "Refreshing IGCL capabilities (read-only)...");

    private async void SetIntelGpuMaxMaxButton_Click(object sender, RoutedEventArgs args) =>
        await RunIntelGpuFrequencyProbeOperationAsync(
            () => _frontend!.RunIntelGpuFrequencyProbeAsync(FrontendIntelGpuFrequencyProbeOperation.SetMaxMax), "Applying GPU Max / Max and verifying range readback...");

    private async void RestoreIntelGpuFrequencyButton_Click(object sender, RoutedEventArgs args) =>
        await RunIntelGpuFrequencyProbeOperationAsync(
            () => _frontend!.RunIntelGpuFrequencyProbeAsync(FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalFrequency), "Restoring the original GPU frequency range...");

    private async void SetIntelGpuTestPl1Button_Click(object sender, RoutedEventArgs args)
    {
        if (!TryGetTestPl1(out var testPl1Mw))
        {
            SetIntelGpuFrequencyProbeStatus("PL1 must be a finite integer milliwatt value inside the driver-reported range.");
            return;
        }
        await RunIntelGpuFrequencyProbeOperationAsync(
            () => _frontend!.RunIntelGpuFrequencyProbeAsync(FrontendIntelGpuFrequencyProbeOperation.SetTestPl1, testPl1Mw),
            "Applying the test PL1 ceiling and verifying limits readback...");
    }

    private async void RestoreIntelGpuPowerButton_Click(object sender, RoutedEventArgs args) =>
        await RunIntelGpuFrequencyProbeOperationAsync(
            () => _frontend!.RunIntelGpuFrequencyProbeAsync(FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalPower), "Restoring the original GPU power limits...");

    private void IntelGpuTestPl1NumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateIntelGpuFrequencyProbeButtons();

    private async Task RunIntelGpuFrequencyProbeOperationAsync(
        Func<Task<FrontendIntelGpuFrequencyProbeSnapshot>> operation,
        string runningText)
    {
        if (_frontend is null || _isPrerequisiteSetupInProgress?.Invoke() == true
            || Interlocked.Exchange(ref _isRunningIntelGpuFrequencyProbe, 1) != 0)
            return;

        SetIntelGpuFrequencyProbeStatus(runningText);
        UpdateIntelGpuFrequencyProbeButtons();
        try
        {
            _intelGpuFrequencyProbeSnapshot = await operation();
            SetIntelGpuFrequencyProbeStatus(FormatIntelGpuFrequencyProbeStatus(_intelGpuFrequencyProbeSnapshot));
        }
        catch (Exception exception)
        {
            AppLog.Warn("DeveloperMenu", "Intel GPU IGCL probe operation failed.", exception);
            SetIntelGpuFrequencyProbeStatus("Operation failed. See the application log for details.");
        }
        finally
        {
            Volatile.Write(ref _isRunningIntelGpuFrequencyProbe, 0);
            RenderIntelGpuFrequencyProbe();
        }
    }

    private void RenderIntelGpuFrequencyProbe()
    {
        var snapshot = _intelGpuFrequencyProbeSnapshot;
        var text = new StringBuilder();
        text.AppendLine($"Adapter: {snapshot.AdapterName}  |  Vendor: 0x{snapshot.VendorId:X4}  |  PCI Device: 0x{snapshot.DeviceId:X4}");
        text.AppendLine($"GPU frequency domain: {(snapshot.Available ? "Available" : "Unavailable")}  |  Can control: {YesNo(snapshot.CanControl)}");
        text.AppendLine($"Hardware: {FormatMhz(snapshot.HardwareMinMhz)} min / {FormatMhz(snapshot.HardwareMaxMhz)} max");
        text.AppendLine($"External range: {FormatExternalRange(snapshot.CurrentMinMhz, snapshot.CurrentMinHasExternalLimit)} / {FormatExternalRange(snapshot.CurrentMaxMhz, snapshot.CurrentMaxHasExternalLimit)}");
        text.AppendLine($"Request: {FormatMhz(snapshot.RequestMhz)}  |  Actual: {FormatMhz(snapshot.ActualMhz)}  |  TDP max: {FormatMhz(snapshot.TdpMhz)}  |  Efficient: {FormatMhz(snapshot.EfficientMhz)}  |  Voltage: {FormatVoltage(snapshot.Voltage)}");
        text.AppendLine($"Throttle: {FormatThrottleReasons(snapshot.ThrottleReasons)}");
        text.AppendLine($"GPU power domain: {(snapshot.PowerAvailable ? "Available" : "Unavailable")}  |  Can control: {YesNo(snapshot.PowerCanControl)}");
        text.AppendLine($"Power default: {FormatMw(snapshot.PowerDefaultLimitMw)}  |  Allowed: {FormatMw(snapshot.PowerMinLimitMw)} - {FormatMw(snapshot.PowerMaxLimitMw)}");
        text.AppendLine($"PL1: {FormatEnabledLimit(snapshot.Pl1Enabled, snapshot.Pl1PowerMw)}  |  Tau: {FormatMilliseconds(snapshot.Pl1IntervalMs)}");
        text.AppendLine($"PL2: {FormatEnabledLimit(snapshot.Pl2Enabled, snapshot.Pl2PowerMw)}  |  PL4 AC/DC: {FormatMw(snapshot.Pl4AcPowerMw)} / {FormatMw(snapshot.Pl4DcPowerMw)}");
        text.Append($"Power limits are ceilings; requested PL1 does not reserve GPU power.");
        IntelGpuFrequencyProbeDetailsText.Text = text.ToString();

        if (snapshot.PowerMinLimitMw is { } min && snapshot.PowerMaxLimitMw is { } max && min >= 0 && max >= min)
        {
            IntelGpuTestPl1NumberBox.Minimum = 0;
            IntelGpuTestPl1NumberBox.Maximum = max;
            IntelGpuTestPl1NumberBox.Minimum = min;
        }
        else
        {
            IntelGpuTestPl1NumberBox.Minimum = 0;
            IntelGpuTestPl1NumberBox.Maximum = 100000;
        }

        UpdateIntelGpuFrequencyProbeButtons();
    }

    private void UpdateIntelGpuFrequencyProbeButtons()
    {
        var busy = Volatile.Read(ref _isRunningIntelGpuFrequencyProbe) != 0;
        var snapshot = _intelGpuFrequencyProbeSnapshot;
        RefreshIntelGpuFrequencyProbeButton.IsEnabled = !busy;
        SetIntelGpuMaxMaxButton.IsEnabled = !busy && snapshot.Available && snapshot.CanControl
            && snapshot.HardwareMinMhz is { } min && snapshot.HardwareMaxMhz is { } max
            && double.IsFinite(min) && double.IsFinite(max) && max > 0 && min <= max;
        RestoreIntelGpuFrequencyButton.IsEnabled = !busy && snapshot.OriginalRangeCaptured && snapshot.ModifiedByProbe;
        IntelGpuTestPl1NumberBox.IsEnabled = !busy && snapshot.PowerAvailable && snapshot.PowerCanControl;
        SetIntelGpuTestPl1Button.IsEnabled = !busy && snapshot.PowerAvailable && snapshot.PowerCanControl && TryGetTestPl1(out _);
        RestoreIntelGpuPowerButton.IsEnabled = !busy && snapshot.OriginalPowerLimitsCaptured && snapshot.PowerModifiedByProbe;
    }

    private bool TryGetTestPl1(out int value)
    {
        value = 0;
        var snapshot = _intelGpuFrequencyProbeSnapshot;
        var raw = IntelGpuTestPl1NumberBox.Value;
        return snapshot.PowerMinLimitMw is { } min
            && snapshot.PowerMaxLimitMw is { } max
            && double.IsFinite(raw)
            && raw == Math.Truncate(raw)
            && raw >= int.MinValue && raw <= int.MaxValue
            && raw >= min && raw <= max
            && (value = (int)raw) >= min;
    }

    private static string FormatIntelGpuFrequencyProbeStatus(FrontendIntelGpuFrequencyProbeSnapshot snapshot)
    {
        var lines = new List<string>();
        if (snapshot.LastOperation is { } operation)
            lines.Add($"Frequency — {operation}: {Verification(snapshot.LastOperationVerified, snapshot.LastNativeResult)}; native result {FormatResult(snapshot.LastNativeResult)}.");
        if (snapshot.FailureMessage is { Length: > 0 } frequencyFailure) lines.Add(frequencyFailure);
        if (snapshot.LastPowerOperation is { } powerOperation)
            lines.Add($"Power — {powerOperation}: {Verification(snapshot.LastPowerOperationVerified, snapshot.LastPowerNativeResult)}; native result {FormatResult(snapshot.LastPowerNativeResult)}.");
        if (snapshot.PowerFailureMessage is { Length: > 0 } powerFailure) lines.Add(powerFailure);
        if (lines.Count == 0) lines.Add(snapshot.Available || snapshot.PowerAvailable
            ? "Capture completed. Refresh is read-only; no settings were changed."
            : snapshot.FailureMessage ?? snapshot.PowerFailureMessage ?? "IGCL GPU capabilities are unavailable.");
        return string.Join("\r\n", lines);
    }

    private void SetIntelGpuFrequencyProbeStatus(string text) => IntelGpuFrequencyProbeStatusText.Text = text;

    private static string Verification(bool? verified, uint? nativeResult) => verified switch
    {
        true => "VERIFIED",
        false when nativeResult == 0 => "MISMATCH",
        false => "FAILED",
        _ => "not run"
    };
    private static string FormatResult(uint? result) => result is { } value ? $"0x{value:X8}" : "n/a";
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string FormatMhz(double? value) => value is { } mhz ? $"{mhz.ToString("F1", CultureInfo.InvariantCulture)} MHz" : "Unknown";
    private static string FormatVoltage(double? value) => value is { } volts ? $"{volts.ToString("F3", CultureInfo.InvariantCulture)} V" : "Unknown";
    private static string FormatExternalRange(double? value, bool hasLimit) => value is null
        ? "Unknown"
        : hasLimit ? FormatMhz(value) : "Factory / no external limit";
    private static string FormatMw(int? value) => value is { } mw ? $"{mw} mW" : "Unknown";
    private static string FormatMilliseconds(int? value) => value is { } ms ? $"{ms} ms" : "Unknown";
    private static string FormatEnabledLimit(bool? enabled, int? value) => enabled is null ? "Unknown" : $"{(enabled.Value ? "Enabled" : "Disabled")} / {FormatMw(value)}";

    private static string FormatThrottleReasons(uint? value)
    {
        if (value is not { } flags) return "Unknown";
        if (flags == 0) return "0x00000000 (none)";
        var known = new (uint Flag, string Name)[]
        {
            (1u << 0, "PL1"), (1u << 1, "PL2"), (1u << 2, "Current"), (1u << 3, "Thermal"),
            (1u << 4, "PSU"), (1u << 5, "Software range"), (1u << 6, "Hardware range")
        };
        var names = known.Where(item => (flags & item.Flag) != 0).Select(item => item.Name).ToList();
        var unknown = flags & ~known.Aggregate(0u, (mask, item) => mask | item.Flag);
        if (unknown != 0) names.Add($"other=0x{unknown:X8}");
        return $"0x{flags:X8} ({string.Join(", ", names)})";
    }
}
