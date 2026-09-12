using SteamInputAddonforClaw.Contracts.Frontend;
using System.Text;

namespace SteamInputAddonforClaw.Views;

internal enum BatteryChargeLimitValidationRestoreOutcome
{
    NotAttempted,
    Skipped,
    Succeeded,
    Failed
}

internal sealed record BatteryChargeLimitValidationFailure(
    string Step,
    FrontendBatteryChargeLimitTestMutationOutcome? Outcome,
    string Message);

internal sealed record BatteryChargeLimitValidationRestoreResult(
    BatteryChargeLimitValidationRestoreOutcome Outcome,
    string? FailureMessage)
{
    internal static readonly BatteryChargeLimitValidationRestoreResult NotAttempted =
        new(BatteryChargeLimitValidationRestoreOutcome.NotAttempted, null);
}

internal sealed record BatteryChargeLimitValidationRunResult(
    string? ReportPath,
    int CompletedSteps,
    BatteryChargeLimitValidationFailure? PrimaryFailure,
    BatteryChargeLimitValidationRestoreResult Restore)
{
    internal bool Passed => PrimaryFailure is null && Restore.Outcome != BatteryChargeLimitValidationRestoreOutcome.Failed;
}

/// <summary>Runs the one Developer-page battery validation sequence over the existing typed PR1 API.
/// This is intentionally a small page-specific helper rather than a general diagnostic framework.</summary>
internal sealed class BatteryChargeLimitValidationRunner
{
    internal static readonly int[] ValidationLimits = [60, 65, 70, 75, 80, 85, 90, 95, 100];
    internal const int TotalSteps = 22;

    private readonly Func<Task<FrontendBatteryChargeLimitTestSnapshot>> _capture;
    private readonly Func<bool, Task<FrontendBatteryChargeLimitTestMutationResult>> _setEnabled;
    private readonly Func<int, Task<FrontendBatteryChargeLimitTestMutationResult>> _setPercent;
    private readonly string _logDirectory;
    private readonly Action<int, int, string>? _progress;
    private readonly Action<string>? _reportCreated;
    private readonly Action<FrontendBatteryChargeLimitTestSnapshot>? _stateUpdated;
    private readonly Func<string, StreamWriter> _reportWriterFactory;

    internal BatteryChargeLimitValidationRunner(
        Func<Task<FrontendBatteryChargeLimitTestSnapshot>> capture,
        Func<bool, Task<FrontendBatteryChargeLimitTestMutationResult>> setEnabled,
        Func<int, Task<FrontendBatteryChargeLimitTestMutationResult>> setPercent,
        string logDirectory,
        Action<int, int, string>? progress = null,
        Action<string>? reportCreated = null,
        Action<FrontendBatteryChargeLimitTestSnapshot>? stateUpdated = null,
        Func<string, StreamWriter>? reportWriterFactory = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
        _setPercent = setPercent ?? throw new ArgumentNullException(nameof(setPercent));
        _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
        _progress = progress;
        _reportCreated = reportCreated;
        _stateUpdated = stateUpdated;
        _reportWriterFactory = reportWriterFactory ?? CreateReportWriter;
    }

    internal async Task<BatteryChargeLimitValidationRunResult> RunAsync()
    {
        string? reportPath = null;
        StreamWriter? writer = null;
        FrontendBatteryChargeLimitTestSnapshot? initial = null;
        BatteryChargeLimitValidationFailure? primaryFailure = null;
        var restore = BatteryChargeLimitValidationRestoreResult.NotAttempted;
        var completedSteps = 0;
        var mutationStarted = false;
        var reportWriteFailed = false;

        void MarkReportFailure(BatteryChargeLimitValidationFailure failure)
        {
            reportWriteFailed = true;
            primaryFailure ??= failure;
        }

        BatteryChargeLimitValidationFailure? TryWriteReport(string step, Action write)
        {
            if (reportWriteFailed)
                return primaryFailure;

            try
            {
                write();
                return null;
            }
            catch (Exception exception)
            {
                var failure = new BatteryChargeLimitValidationFailure(
                    step, null, $"Validation report write failed: {exception.Message}");
                MarkReportFailure(failure);
                return failure;
            }
        }

        try
        {
            var candidateReportPath = BuildReportPath();
            writer = _reportWriterFactory(candidateReportPath);
            reportPath = candidateReportPath;
            _reportCreated?.Invoke(reportPath);
            TryWriteReport("Report header", () => WriteHeader(writer!));
            AppLog.Info("BatteryValidation", "Automated validation started.", ("ReportPath", reportPath));

            try
            {
                initial = await _capture();
                _stateUpdated?.Invoke(initial);
            }
            catch (Exception exception)
            {
                primaryFailure ??= new("Initial capture", null, exception.Message);
                TryWriteReport("Initial capture", () => writer!.Write($"INITIAL FAILURE\nMessage={exception.Message}\n"));
            }

            if (primaryFailure is null && initial is not null)
            {
                TryWriteReport("Initial capture", () => WriteInitial(writer!, initial));
            }

            if (primaryFailure is null && initial is not null && !CanBegin(initial))
            {
                var failure = new BatteryChargeLimitValidationFailure("Initial capture", null,
                    initial.FailureMessage ?? "Initial battery state is unavailable or incomplete.");
                primaryFailure = failure;
                TryWriteReport("Initial capture", () => WriteFailure(writer!, failure));
            }

            if (primaryFailure is null && initial is not null)
            {
                mutationStarted = true;
                var disabled = await RunMutationStepAsync(
                    "Disable", expectedEnabled: false, expectedPercent: null, requireProductValue: false,
                    () => _setEnabled(false), writer!, ++completedSteps, tryWriteReport: TryWriteReport);
                _progress?.Invoke(completedSteps, TotalSteps, "Disable");
                if (disabled.Failure is not null)
                {
                    primaryFailure = disabled.Failure;
                }
                else
                {
                    foreach (var limit in ValidationLimits)
                    {
                        var step = await RunMutationStepAsync(
                            $"Set {limit}% while Disabled", expectedEnabled: false, expectedPercent: limit, requireProductValue: true,
                            () => _setPercent(limit), writer!, ++completedSteps, tryWriteReport: TryWriteReport);
                        _progress?.Invoke(completedSteps, TotalSteps, $"Set {limit}% while Disabled");
                        if (step.Failure is not null)
                        {
                            primaryFailure = step.Failure;
                            break;
                        }
                    }
                }

                if (primaryFailure is null)
                {
                    mutationStarted = true;
                    var enabled = await RunMutationStepAsync(
                        "Enable", expectedEnabled: true, expectedPercent: null, requireProductValue: true,
                        () => _setEnabled(true), writer!, ++completedSteps, tryWriteReport: TryWriteReport);
                    _progress?.Invoke(completedSteps, TotalSteps, "Enable");
                    if (enabled.Failure is not null)
                    {
                        primaryFailure = enabled.Failure;
                    }
                    else
                    {
                        StepResult? enabled100 = null;
                        foreach (var limit in ValidationLimits)
                        {
                            var step = await RunMutationStepAsync(
                                $"Set {limit}% while Enabled", expectedEnabled: true, expectedPercent: limit, requireProductValue: true,
                                () => _setPercent(limit), writer!, ++completedSteps, tryWriteReport: TryWriteReport);
                            _progress?.Invoke(completedSteps, TotalSteps, $"Set {limit}% while Enabled");
                            if (limit == 100) enabled100 = step;
                            if (step.Failure is not null)
                            {
                                primaryFailure = step.Failure;
                                break;
                            }
                        }

                        if (primaryFailure is null && enabled100 is not null)
                        {
                            var enabled100Failure = Validate100EnabledObservation(enabled100.Result);
                            completedSteps++;
                            enabled100Failure = TryWriteReport(
                                "Verify 100% Enabled",
                                () => WriteObservationStep(writer!, completedSteps, "Verify 100% Enabled", enabled100.Result, enabled100Failure))
                                ?? enabled100Failure;
                            _progress?.Invoke(completedSteps, TotalSteps, "Verify 100% Enabled");
                            if (enabled100Failure is not null)
                            {
                                primaryFailure = enabled100Failure;
                            }
                            else
                            {
                                mutationStarted = true;
                                var disabled100 = await RunMutationStepAsync(
                                    "Verify 100% Disabled", expectedEnabled: false, expectedPercent: 100, requireProductValue: true,
                                    () => _setEnabled(false), writer!, ++completedSteps,
                                    result => Validate100Identity(enabled100.Result, result),
                                    TryWriteReport);
                                _progress?.Invoke(completedSteps, TotalSteps, "Verify 100% Disabled");
                                if (disabled100.Failure is not null && primaryFailure is null)
                                    primaryFailure = disabled100.Failure;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure ??= new("Validation runner", null, exception.Message);
            TryWriteReport("Validation runner", () => writer?.Write($"RUNNER FAILURE\nMessage={exception.Message}\n"));
        }
        finally
        {
            if (initial is not null && mutationStarted && !reportWriteFailed)
            {
                try
                {
                    restore = await RestoreInitialStateAsync(initial, writer!, TryWriteReport);
                }
                catch (Exception exception)
                {
                    restore = new(BatteryChargeLimitValidationRestoreOutcome.Failed, exception.Message);
                    primaryFailure ??= new("Restore", null, exception.Message);
                    TryWriteReport("Restore", () => writer?.Write($"Restore=FAIL\nFailureMessage={exception.Message}\n"));
                }
            }
            else if (initial is not null && mutationStarted && reportWriteFailed)
            {
                restore = new(BatteryChargeLimitValidationRestoreOutcome.Skipped,
                    "Restore skipped because the validation report is no longer writable.");
            }

            var passed = primaryFailure is null &&
                         restore.Outcome != BatteryChargeLimitValidationRestoreOutcome.Failed &&
                         !reportWriteFailed;
            if (!reportWriteFailed)
            {
                try
                {
                    if (primaryFailure is not null)
                    {
                        writer?.Write($"PrimaryFailureStep={primaryFailure.Step}\n");
                        writer?.Write($"PrimaryFailureOutcome={primaryFailure.Outcome?.ToString() ?? "Exception"}\n");
                        writer?.Write($"PrimaryFailureMessage={primaryFailure.Message}\n");
                    }
                    writer?.Write(primaryFailure is null ? "Sequence completed.\n" : "Sequence stopped after first failure.\n");
                    writer?.Write($"FINAL RESULT: {(passed ? "PASS" : "FAIL")}\n");
                    writer?.Write($"Completed: {DateTimeOffset.Now:O}\n");
                    writer?.Flush();
                }
                catch (Exception exception)
                {
                    MarkReportFailure(new(
                        "Report finalization", null,
                        $"Validation report finalization failed: {exception.Message}"));
                    passed = false;
                }
            }
            try
            {
                writer?.Dispose();
            }
            catch (Exception exception)
            {
                MarkReportFailure(new(
                    "Report finalization", null,
                    $"Validation report finalization failed: {exception.Message}"));
                passed = false;
            }

            if (passed)
                AppLog.Info("BatteryValidation", "Automated validation completed.", ("ReportPath", reportPath ?? "Unavailable"));
            else
                AppLog.Warn("BatteryValidation", "Automated validation failed.", null, ("ReportPath", reportPath ?? "Unavailable"));
        }

        return new(reportPath, completedSteps, primaryFailure, restore);
    }

    private async Task<StepResult> RunMutationStepAsync(
        string label,
        bool expectedEnabled,
        int? expectedPercent,
        bool requireProductValue,
        Func<Task<FrontendBatteryChargeLimitTestMutationResult>> operation,
        StreamWriter writer,
        int stepNumber,
        Func<FrontendBatteryChargeLimitTestMutationResult?, BatteryChargeLimitValidationFailure?>? additionalValidation = null,
        Func<string, Action, BatteryChargeLimitValidationFailure?>? tryWriteReport = null)
    {
        FrontendBatteryChargeLimitTestMutationResult? result = null;
        BatteryChargeLimitValidationFailure? failure = null;
        try
        {
            result = await operation();
            var snapshot = result.Snapshot;
            var passed = result.Succeeded && snapshot.Available && snapshot.Enabled == expectedEnabled &&
                (expectedPercent is null || snapshot.LimitPercent == expectedPercent) &&
                (!requireProductValue || snapshot.RawValue is not null && snapshot.ProductValueValid);
            if (!passed)
            {
                var detail = result.FailureMessage ??
                    $"Returned state did not match expected Enabled={expectedEnabled}, Limit={expectedPercent?.ToString() ?? "preserved"}.";
                failure = new(label, result.Outcome, detail);
            }
            if (failure is null && additionalValidation is not null)
                failure = additionalValidation(result);
            _stateUpdated?.Invoke(snapshot);
        }
        catch (Exception exception)
        {
            failure = new(label, null, exception.Message);
        }

        if (tryWriteReport is not null)
            failure = tryWriteReport(label, () => WriteMutationStep(writer, stepNumber, label, result, failure)) ?? failure;
        else
            WriteMutationStep(writer, stepNumber, label, result, failure);
        return new(result, failure);
    }

    private async Task<BatteryChargeLimitValidationRestoreResult> RestoreInitialStateAsync(
        FrontendBatteryChargeLimitTestSnapshot initial,
        StreamWriter writer,
        Func<string, Action, BatteryChargeLimitValidationFailure?> tryWriteReport)
    {
        if (tryWriteReport("Restore", () =>
        {
            WriteLine(writer, "RESTORE");
            WriteLine(writer, $"Initial ProductValueValid={initial.ProductValueValid}");
        }) is not null)
        {
            return ReportWriteFailureRestoreResult();
        }

        if (!CanRestore(initial))
        {
            if (tryWriteReport("Restore", () =>
            {
                WriteLine(writer, "Restore=SKIPPED");
                WriteLine(writer, "Reason=Initial remembered limit is outside Addon supported product values.");
            }) is not null)
            {
                return ReportWriteFailureRestoreResult();
            }

            return new(BatteryChargeLimitValidationRestoreOutcome.Skipped,
                "Initial remembered limit is outside Addon supported product values.");
        }

        var percent = await _setPercent(initial.LimitPercent!.Value);
        _stateUpdated?.Invoke(percent.Snapshot);
        if (tryWriteReport("Restore", () => WriteRestoreMutation(writer, $"SetPercent({initial.LimitPercent.Value})", percent)) is not null)
            return ReportWriteFailureRestoreResult();
        if (!percent.Succeeded)
        {
            if (tryWriteReport("Restore", () => WriteLine(writer, "Restore=FAIL")) is not null)
                return ReportWriteFailureRestoreResult();
            return new(BatteryChargeLimitValidationRestoreOutcome.Failed,
                percent.FailureMessage ?? "Initial percentage restore failed.");
        }

        var enabled = await _setEnabled(initial.Enabled!.Value);
        _stateUpdated?.Invoke(enabled.Snapshot);
        if (tryWriteReport("Restore", () => WriteRestoreMutation(writer, $"SetEnabled({initial.Enabled.Value})", enabled)) is not null)
            return ReportWriteFailureRestoreResult();
        if (!enabled.Succeeded)
        {
            if (tryWriteReport("Restore", () => WriteLine(writer, "Restore=FAIL")) is not null)
                return ReportWriteFailureRestoreResult();
            return new(BatteryChargeLimitValidationRestoreOutcome.Failed,
                enabled.FailureMessage ?? "Initial enabled-state restore failed.");
        }

        if (tryWriteReport("Restore", () =>
        {
            WriteLine(writer, $"Final Enabled={Format(enabled.Snapshot.Enabled)}");
            WriteLine(writer, $"Final Limit={Format(enabled.Snapshot.LimitPercent)}");
            WriteLine(writer, $"Final Raw={Format(enabled.Snapshot.RawValue)}");
            WriteLine(writer, "Restore=PASS");
        }) is not null)
        {
            return ReportWriteFailureRestoreResult();
        }

        return new(BatteryChargeLimitValidationRestoreOutcome.Succeeded, null);
    }

    private static BatteryChargeLimitValidationRestoreResult ReportWriteFailureRestoreResult() =>
        new(BatteryChargeLimitValidationRestoreOutcome.Skipped,
            "Restore skipped because the validation report is no longer writable.");

    private static bool CanBegin(FrontendBatteryChargeLimitTestSnapshot snapshot) =>
        snapshot.Available && snapshot.FailureMessage is null && snapshot.RawValue is not null &&
        snapshot.Enabled is not null && snapshot.LimitPercent is not null;

    private static bool CanRestore(FrontendBatteryChargeLimitTestSnapshot snapshot) =>
        snapshot.Available && snapshot.ProductValueValid && snapshot.Enabled is not null &&
        snapshot.LimitPercent is int percent && IsProductValue(percent);

    private static bool IsProductValue(int percent) => percent is >= 60 and <= 100 && (percent - 60) % 5 == 0;

    private static BatteryChargeLimitValidationFailure? Validate100EnabledObservation(FrontendBatteryChargeLimitTestMutationResult? result)
    {
        if (result is null || !result.Succeeded || result.Snapshot.Enabled != true || result.Snapshot.LimitPercent != 100 ||
            result.Snapshot.RawValue is null || !result.Snapshot.ProductValueValid)
            return new("Verify 100% Enabled", FrontendBatteryChargeLimitTestMutationOutcome.VerificationFailed,
                "The enabled 100% state was not returned as a valid authoritative snapshot.");
        return null;
    }

    private static BatteryChargeLimitValidationFailure? Validate100Identity(
        FrontendBatteryChargeLimitTestMutationResult? enabled,
        FrontendBatteryChargeLimitTestMutationResult? disabled)
    {
        if (disabled is null || disabled.Snapshot.Enabled != false || disabled.Snapshot.LimitPercent != 100 ||
            disabled.Snapshot.RawValue is null || !disabled.Snapshot.ProductValueValid)
            return disabled is null ? null : new("Verify 100% Disabled", FrontendBatteryChargeLimitTestMutationOutcome.VerificationFailed,
                "The disabled 100% state was not returned as a valid authoritative snapshot.");

        if (enabled?.Snapshot.RawValue == disabled.Snapshot.RawValue)
            return new("Verify 100% Disabled", FrontendBatteryChargeLimitTestMutationOutcome.VerificationFailed,
                "100% enabled and disabled states returned the same raw byte.");
        return null;
    }

    private static string BuildReportPath(string directory)
    {
        var fileName = $"BatteryChargeLimitValidation-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss.fff}-P{Environment.ProcessId}.txt";
        return Path.Combine(directory, fileName);
    }

    private string BuildReportPath() => BuildReportPath(_logDirectory);

    private static StreamWriter CreateReportWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    private static void WriteHeader(StreamWriter writer)
    {
        WriteLine(writer, "Battery Charge Limit Validation");
        WriteLine(writer, $"Started: {DateTimeOffset.Now:O}");
        WriteLine(writer, $"Process: {Environment.ProcessId}");
        WriteLine(writer);
    }

    private static void WriteInitial(StreamWriter writer, FrontendBatteryChargeLimitTestSnapshot snapshot)
    {
        WriteLine(writer, "INITIAL");
        WriteLine(writer, $"Available={snapshot.Available}");
        WriteLine(writer, $"Device Manufacturer: {snapshot.Manufacturer}");
        WriteLine(writer, $"Device Model: {snapshot.Model}");
        WriteLine(writer, $"BaseBoard: {snapshot.BaseBoard}");
        WriteLine(writer, $"Enabled={Format(snapshot.Enabled)}");
        WriteLine(writer, $"Limit={Format(snapshot.LimitPercent)}");
        WriteLine(writer, $"Raw={Format(snapshot.RawValue)}");
        WriteLine(writer, $"ProductValueValid={snapshot.ProductValueValid}");
        if (snapshot.FailureMessage is not null) WriteLine(writer, $"FailureMessage={snapshot.FailureMessage}");
        WriteLine(writer);
    }

    private static void WriteMutationStep(
        StreamWriter writer,
        int number,
        string label,
        FrontendBatteryChargeLimitTestMutationResult? result,
        BatteryChargeLimitValidationFailure? failure)
    {
        WriteLine(writer, $"[{number:00}] {label}");
        WriteResult(writer, result);
        if (failure is not null) WriteLine(writer, $"RunnerFailure={failure.Message}");
        WriteLine(writer, failure is null ? "PASS" : "FAIL");
        WriteLine(writer);
        writer.Flush();
    }

    private static void WriteObservationStep(
        StreamWriter writer,
        int number,
        string label,
        FrontendBatteryChargeLimitTestMutationResult? result,
        BatteryChargeLimitValidationFailure? failure)
    {
        WriteLine(writer, $"[{number:00}] {label}");
        WriteResult(writer, result);
        if (failure is not null) WriteLine(writer, $"RunnerFailure={failure.Message}");
        WriteLine(writer, failure is null ? "PASS" : "FAIL");
        WriteLine(writer);
        writer.Flush();
    }

    private static void WriteRestoreMutation(
        StreamWriter? writer,
        string label,
        FrontendBatteryChargeLimitTestMutationResult result)
    {
        WriteLine(writer, $"{label}={result.Outcome}");
        if (result.FailureMessage is not null) WriteLine(writer, $"FailureMessage={result.FailureMessage}");
        WriteLine(writer, $"Enabled={Format(result.Snapshot.Enabled)} Limit={Format(result.Snapshot.LimitPercent)} Raw={Format(result.Snapshot.RawValue)}");
        writer?.Flush();
    }

    private static void WriteResult(StreamWriter writer, FrontendBatteryChargeLimitTestMutationResult? result)
    {
        WriteLine(writer, $"Outcome={result?.Outcome.ToString() ?? "Exception"}");
        if (result?.FailureMessage is not null) WriteLine(writer, $"FailureMessage={result.FailureMessage}");
        if (result is not null)
        {
            WriteLine(writer, $"Enabled={Format(result.Snapshot.Enabled)}");
            WriteLine(writer, $"Limit={Format(result.Snapshot.LimitPercent)}");
            WriteLine(writer, $"Raw={Format(result.Snapshot.RawValue)}");
            WriteLine(writer, $"ProductValueValid={result.Snapshot.ProductValueValid}");
        }
    }

    private static void WriteFailure(StreamWriter writer, BatteryChargeLimitValidationFailure failure)
    {
        WriteLine(writer, "FAILURE");
        WriteLine(writer, $"Step={failure.Step}");
        WriteLine(writer, $"Outcome={failure.Outcome?.ToString() ?? "Exception"}");
        WriteLine(writer, $"Message={failure.Message}");
        WriteLine(writer);
    }

    private static void WriteLine(StreamWriter? writer, string text = "") => writer?.WriteLine(text);

    private static string Format(bool? value) => value?.ToString() ?? "Unknown";
    private static string Format(int? value) => value?.ToString() ?? "Unknown";
    private static string Format(byte? value) => value is byte raw ? $"0x{raw:X2}" : "Unknown";

    private sealed record StepResult(
        FrontendBatteryChargeLimitTestMutationResult? Result,
        BatteryChargeLimitValidationFailure? Failure);
}
