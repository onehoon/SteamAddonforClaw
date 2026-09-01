using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMStartupControlTests
{
    private const CenterMFoundationServiceMode Auto = CenterMFoundationServiceMode.Automatic;
    private const CenterMFoundationServiceMode Off = CenterMFoundationServiceMode.Disabled;
    private const CenterMFoundationServiceMode Manual = CenterMFoundationServiceMode.Other;
    private const CenterMFoundationServiceMode NoService = CenterMFoundationServiceMode.Unavailable;

    // ---- State classification (work order PR1 section 7 / section 18 / PR #430 review) ----
    [Theory]
    [InlineData(true, true, "Automatic", FrontendCenterMStartupState.Enabled)]
    [InlineData(false, false, "Disabled", FrontendCenterMStartupState.Disabled)]
    [InlineData(false, true, "Disabled", FrontendCenterMStartupState.Partial)]
    [InlineData(true, true, "Disabled", FrontendCenterMStartupState.Partial)]
    [InlineData(true, true, "Other", FrontendCenterMStartupState.Partial)]  // Manual is NOT the enable baseline
    [InlineData(true, false, "Automatic", FrontendCenterMStartupState.Partial)]
    public void Classifies_the_three_roots(bool server, bool updater, string service, FrontendCenterMStartupState expected)
        => Assert.Equal(expected, CenterMStartupControl.Classify(server, updater, Enum.Parse<CenterMFoundationServiceMode>(service)));

    [Fact]
    public void Capture_is_unavailable_when_the_feature_does_not_apply()
    {
        var control = new CenterMStartupControl(available: false, Reader(_ => true, () => Auto), new FakeInvoker());
        Assert.Equal(FrontendCenterMStartupState.Unavailable, control.Capture().State);
    }

    [Fact]
    public void Capture_is_unavailable_when_a_root_cannot_be_read()
    {
        var control = new CenterMStartupControl(available: true, Reader(_ => null, () => Auto), new FakeInvoker());
        var snapshot = control.Capture();
        Assert.Equal(FrontendCenterMStartupState.Unavailable, snapshot.State);
        Assert.NotNull(snapshot.FailureMessage);
    }

    [Fact]
    public void Capture_does_not_report_settled_enabled_while_the_service_is_manual()
    {
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => Manual), new FakeInvoker());
        Assert.Equal(FrontendCenterMStartupState.Partial, control.Capture().State);
    }

    [Fact]
    public void Capture_reports_the_real_state_without_mutating()
    {
        var invoker = new FakeInvoker();
        var control = new CenterMStartupControl(available: true, Reader(_ => false, () => Off), invoker);
        Assert.Equal(FrontendCenterMStartupState.Disabled, control.Capture().State);
        Assert.Empty(invoker.Calls);
    }

    // ---- Disable mutation writes exactly one logical operation, never stop/kill ----
    [Fact]
    public async Task Disable_sends_one_set_enabled_false_and_nothing_else()
    {
        var invoker = new FakeInvoker { Result = Helper(ok: true, snapshotAvailable: true, false, false, Off) };
        var control = new CenterMStartupControl(available: true, Reader(_ => false, () => Off), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { false }, invoker.Calls);
    }

    [Fact]
    public async Task Enable_sends_one_set_enabled_true()
    {
        var invoker = new FakeInvoker { Result = Helper(ok: true, snapshotAvailable: true, true, true, Auto) };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => Auto), invoker);

        var result = await control.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { true }, invoker.Calls);
    }

    // ---- Read-back verification (work order PR1 section 8 / PR #430 review) ----
    [Fact]
    public async Task Mutation_that_writes_but_reads_back_mixed_is_not_a_success()
    {
        var invoker = new FakeInvoker { Result = Helper(ok: true, snapshotAvailable: true, false, false, Off) };
        var reader = Reader(name => name == CenterMStartupStateReader.ServerTaskName, () => Off); // server still enabled
        var control = new CenterMStartupControl(available: true, reader, invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Partial, result.Snapshot.State);
    }

    [Fact]
    public async Task Enable_that_leaves_the_service_manual_is_not_a_success()
    {
        // Tasks flipped on, but the service write left it Manual (incomplete restore / external change).
        var invoker = new FakeInvoker { Result = Helper(ok: true, snapshotAvailable: true, true, true, Manual) };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => Manual), invoker);

        var result = await control.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Partial, result.Snapshot.State);
    }

    // ---- Cancelled elevation never fabricates the requested state (Addendum E) ----
    [Fact]
    public async Task Cancelled_uac_returns_cancelled_with_the_last_real_snapshot()
    {
        var invoker = new FakeInvoker { Result = new(CenterMStartupHelperOutcome.Cancelled, false, false, false, false, NoService, null) };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => Auto), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Cancelled, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.Snapshot.State);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public async Task Helper_that_will_not_start_is_a_failure_not_a_success()
    {
        var invoker = new FakeInvoker { Result = new(CenterMStartupHelperOutcome.HelperUnavailable, false, false, false, false, NoService, "missing") };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => Auto), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Unreadable_root_plus_non_authoritative_helper_stays_unavailable_never_disabled()
    {
        // Helper could not observe a root (Ok=false, SnapshotAvailable=false, placeholder false
        // fields) AND the non-elevated Runtime re-read also fails. The placeholder tuple must NOT be
        // classified as Disabled (Addendum E / PR #430 review).
        var invoker = new FakeInvoker { Result = new(CenterMStartupHelperOutcome.Completed, false, false, false, false, NoService, "Unreadable: MSI_Center_M_Server") };
        var control = new CenterMStartupControl(available: true, Reader(_ => null, () => NoService), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Unavailable, result.Snapshot.State);
    }

    [Fact]
    public async Task Mutation_is_unavailable_when_the_feature_does_not_apply()
    {
        var invoker = new FakeInvoker();
        var control = new CenterMStartupControl(available: false, Reader(_ => true, () => Auto), invoker);

        var result = await control.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Unavailable, result.Outcome);
        Assert.Empty(invoker.Calls);
    }

    private static CenterMStartupStateReader Reader(Func<string, bool?> task, Func<CenterMFoundationServiceMode> service) => new(task, service);

    private static CenterMStartupHelperResult Helper(bool ok, bool snapshotAvailable, bool server, bool updater, CenterMFoundationServiceMode service, string? error = null)
        => new(CenterMStartupHelperOutcome.Completed, ok, snapshotAvailable, server, updater, service, error);

    private sealed class FakeInvoker : ICenterMStartupHelperInvoker
    {
        public List<bool> Calls { get; } = [];
        public CenterMStartupHelperResult Result { get; set; } =
            new(CenterMStartupHelperOutcome.Completed, true, true, true, true, CenterMFoundationServiceMode.Automatic, null);

        public Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            Calls.Add(enabled);
            return Task.FromResult(Result);
        }
    }
}
