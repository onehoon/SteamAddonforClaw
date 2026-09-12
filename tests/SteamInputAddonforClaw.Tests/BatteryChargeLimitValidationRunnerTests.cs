using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class BatteryChargeLimitValidationRunnerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"BatteryValidation.{Guid.NewGuid():N}");

    public BatteryChargeLimitValidationRunnerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Runs_the_documented_sequence_and_restores_the_initial_state()
    {
        var frontend = new FakeFrontend(enabled: true, limit: 80);
        var result = await CreateRunner(frontend).RunAsync();

        Assert.True(result.Passed);
        Assert.Equal(BatteryChargeLimitValidationRestoreOutcome.Succeeded, result.Restore.Outcome);
        Assert.Equal(22, result.CompletedSteps);
        Assert.Matches(
            @"^BatteryChargeLimitValidation-\d{4}-\d{2}-\d{2}-\d{6}\.\d{3}-P\d+\.txt$",
            Path.GetFileName(result.ReportPath));
        Assert.Equal(
            ["Enabled:False", "Percent:60", "Percent:65", "Percent:70", "Percent:75", "Percent:80", "Percent:85", "Percent:90", "Percent:95", "Percent:100",
             "Enabled:True", "Percent:60", "Percent:65", "Percent:70", "Percent:75", "Percent:80", "Percent:85", "Percent:90", "Percent:95", "Percent:100",
             "Enabled:False", "Percent:80", "Enabled:True"],
            frontend.Calls);

        var report = File.ReadAllText(result.ReportPath!);
        Assert.Contains("Device Manufacturer: Micro-Star International", report, StringComparison.Ordinal);
        Assert.Contains("[01] Disable", report, StringComparison.Ordinal);
        Assert.Contains("[10] Set 100% while Disabled", report, StringComparison.Ordinal);
        Assert.Contains("[11] Enable", report, StringComparison.Ordinal);
        Assert.Contains("[20] Set 100% while Enabled", report, StringComparison.Ordinal);
        Assert.Contains("[21] Verify 100% Enabled", report, StringComparison.Ordinal);
        Assert.Contains("[22] Verify 100% Disabled", report, StringComparison.Ordinal);
        Assert.Contains("SetPercent(80)=Succeeded", report, StringComparison.Ordinal);
        Assert.Contains("SetEnabled(True)=Succeeded", report, StringComparison.Ordinal);
        Assert.Contains("FINAL RESULT: PASS", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_at_the_first_failed_step_and_restores_without_running_later_steps()
    {
        var frontend = new FakeFrontend(enabled: true, limit: 80) { FailAt85WhileDisabled = true };
        var result = await CreateRunner(frontend).RunAsync();

        Assert.False(result.Passed);
        Assert.Equal("Set 85% while Disabled", result.PrimaryFailure!.Step);
        Assert.Equal(FrontendBatteryChargeLimitTestMutationOutcome.VerificationFailed, result.PrimaryFailure.Outcome);
        Assert.Equal(BatteryChargeLimitValidationRestoreOutcome.Succeeded, result.Restore.Outcome);
        Assert.DoesNotContain("Percent:90", frontend.Calls);
        var failureIndex = frontend.Calls.IndexOf("Percent:85");
        Assert.True(failureIndex >= 0);
        Assert.Equal(["Percent:80", "Enabled:True"], frontend.Calls.Skip(failureIndex + 1));

        var report = File.ReadAllText(result.ReportPath!);
        Assert.Contains("Sequence stopped after first failure.", report, StringComparison.Ordinal);
        Assert.Contains("PrimaryFailureStep=Set 85% while Disabled", report, StringComparison.Ordinal);
        Assert.Contains("FINAL RESULT: FAIL", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restores_an_initial_disabled_100_percent_state_with_percent_then_enabled_order()
    {
        var frontend = new FakeFrontend(enabled: false, limit: 100);
        var result = await CreateRunner(frontend).RunAsync();

        Assert.True(result.Passed);
        Assert.Equal(["Percent:100", "Enabled:False"], frontend.Calls.TakeLast(2));
    }

    [Fact]
    public async Task Skips_restore_for_an_initial_non_product_value_without_attempting_unsupported_write()
    {
        var frontend = new FakeFrontend(enabled: true, limit: 83);
        var result = await CreateRunner(frontend).RunAsync();

        Assert.True(result.Passed);
        Assert.Equal(BatteryChargeLimitValidationRestoreOutcome.Skipped, result.Restore.Outcome);
        Assert.DoesNotContain("Percent:83", frontend.Calls);
        Assert.Contains("Restore=SKIPPED", File.ReadAllText(result.ReportPath!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_primary_failure_when_restore_fails()
    {
        var frontend = new FakeFrontend(enabled: true, limit: 80)
        {
            FailAt85WhileDisabled = true,
            FailInitialPercentRestore = true
        };
        var result = await CreateRunner(frontend).RunAsync();

        Assert.False(result.Passed);
        Assert.Equal("Set 85% while Disabled", result.PrimaryFailure!.Step);
        Assert.Equal(BatteryChargeLimitValidationRestoreOutcome.Failed, result.Restore.Outcome);
        Assert.Contains("Restore=FAIL", File.ReadAllText(result.ReportPath!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_when_100_percent_enabled_and_disabled_raw_states_are_not_distinct()
    {
        var frontend = new FakeFrontend(enabled: true, limit: 80) { ForceSame100Raw = true };
        var result = await CreateRunner(frontend).RunAsync();

        Assert.False(result.Passed);
        Assert.Equal("Verify 100% Disabled", result.PrimaryFailure!.Step);
        Assert.Equal(FrontendBatteryChargeLimitTestMutationOutcome.VerificationFailed, result.PrimaryFailure.Outcome);
        Assert.Equal(BatteryChargeLimitValidationRestoreOutcome.Succeeded, result.Restore.Outcome);
        Assert.Contains("same raw byte", result.PrimaryFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_creation_failure_aborts_before_any_battery_mutation()
    {
        var blockingFile = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blockingFile, "block directory creation");
        var frontend = new FakeFrontend(enabled: true, limit: 80);

        var result = await new BatteryChargeLimitValidationRunner(
            frontend.CaptureAsync,
            frontend.SetEnabledAsync,
            frontend.SetPercentAsync,
            blockingFile).RunAsync();

        Assert.False(result.Passed);
        Assert.NotNull(result.PrimaryFailure);
        Assert.Null(result.ReportPath);
        Assert.Empty(frontend.Calls);
    }

    private BatteryChargeLimitValidationRunner CreateRunner(FakeFrontend frontend) => new(
        frontend.CaptureAsync,
        frontend.SetEnabledAsync,
        frontend.SetPercentAsync,
        _directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeFrontend
    {
        private FrontendBatteryChargeLimitTestSnapshot _snapshot;

        internal FakeFrontend(bool enabled, int limit) => _snapshot = Snapshot(enabled, limit);

        internal List<string> Calls { get; } = [];
        internal bool FailAt85WhileDisabled { get; init; }
        internal bool FailInitialPercentRestore { get; init; }
        internal bool ForceSame100Raw { get; init; }

        internal Task<FrontendBatteryChargeLimitTestSnapshot> CaptureAsync() => Task.FromResult(_snapshot);

        internal Task<FrontendBatteryChargeLimitTestMutationResult> SetEnabledAsync(bool value)
        {
            Calls.Add($"Enabled:{value}");
            _snapshot = Snapshot(value, _snapshot.LimitPercent!.Value);
            return Task.FromResult(Success());
        }

        internal Task<FrontendBatteryChargeLimitTestMutationResult> SetPercentAsync(int value)
        {
            Calls.Add($"Percent:{value}");
            if (FailAt85WhileDisabled && value == 85 && _snapshot.Enabled == false)
                return Task.FromResult(Failure(FrontendBatteryChargeLimitTestMutationOutcome.VerificationFailed, "simulated validation failure"));
            if (FailInitialPercentRestore && value == 80 && Calls.Count(call => call == "Percent:80") >= 2 && _snapshot.Enabled == false)
                return Task.FromResult(Failure(FrontendBatteryChargeLimitTestMutationOutcome.ReadFailed, "simulated restore failure"));

            _snapshot = Snapshot(_snapshot.Enabled!.Value, value);
            return Task.FromResult(Success());
        }

        private FrontendBatteryChargeLimitTestMutationResult Success() =>
            new(FrontendBatteryChargeLimitTestMutationOutcome.Succeeded, null, _snapshot);

        private FrontendBatteryChargeLimitTestMutationResult Failure(
            FrontendBatteryChargeLimitTestMutationOutcome outcome,
            string message) => new(outcome, message, _snapshot);

        private FrontendBatteryChargeLimitTestSnapshot Snapshot(bool enabled, int limit)
        {
            var raw = (byte)((enabled ? 0x80 : 0) | (limit & 0x7F));
            if (ForceSame100Raw && limit == 100) raw = 0x64;
            return new(true, "Micro-Star International", "Claw 8 EX AI+", "MS-1T91", enabled, limit,
                raw, limit is >= 60 and <= 100 && (limit - 60) % 5 == 0, null);
        }
    }
}
