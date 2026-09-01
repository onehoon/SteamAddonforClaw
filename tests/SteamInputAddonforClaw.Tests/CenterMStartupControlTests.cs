using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMStartupControlTests
{
    // ---- State classification (work order PR1 section 7 / section 18) ----
    [Theory]
    [InlineData(true, true, true, FrontendCenterMStartupState.Enabled)]
    [InlineData(false, false, false, FrontendCenterMStartupState.Disabled)]
    [InlineData(false, true, false, FrontendCenterMStartupState.Partial)]
    [InlineData(true, true, false, FrontendCenterMStartupState.Partial)]
    [InlineData(true, false, true, FrontendCenterMStartupState.Partial)]
    public void Classifies_the_three_roots(bool server, bool updater, bool service, FrontendCenterMStartupState expected)
        => Assert.Equal(expected, CenterMStartupControl.Classify(server, updater, service));

    [Fact]
    public void Capture_is_unavailable_when_the_feature_does_not_apply()
    {
        var control = new CenterMStartupControl(available: false, Reader(_ => true, () => true), new FakeInvoker());
        Assert.Equal(FrontendCenterMStartupState.Unavailable, control.Capture().State);
    }

    [Fact]
    public void Capture_is_unavailable_when_a_root_cannot_be_read()
    {
        var control = new CenterMStartupControl(available: true, Reader(_ => null, () => true), new FakeInvoker());
        var snapshot = control.Capture();
        Assert.Equal(FrontendCenterMStartupState.Unavailable, snapshot.State);
        Assert.NotNull(snapshot.FailureMessage);
    }

    [Fact]
    public void Capture_reports_the_real_state_without_mutating()
    {
        var invoker = new FakeInvoker();
        var control = new CenterMStartupControl(available: true, Reader(_ => false, () => false), invoker);
        Assert.Equal(FrontendCenterMStartupState.Disabled, control.Capture().State);
        Assert.Empty(invoker.Calls); // capture never touches the privileged helper
    }

    // ---- Disable mutation writes exactly one logical operation, never stop/kill ----
    [Fact]
    public async Task Disable_sends_one_set_enabled_false_and_nothing_else()
    {
        var invoker = new FakeInvoker { Result = Helper(CenterMStartupHelperOutcome.Completed, ok: true, false, false, false) };
        var control = new CenterMStartupControl(available: true, Reader(_ => false, () => false), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { false }, invoker.Calls);
        // The only privileged operation the helper interface exposes is SetEnabled -- there is no
        // Stop/Start/Kill path anywhere in this component (work order PR1 sections 1/13/18).
    }

    [Fact]
    public async Task Enable_sends_one_set_enabled_true()
    {
        var invoker = new FakeInvoker { Result = Helper(CenterMStartupHelperOutcome.Completed, ok: true, true, true, true) };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => true), invoker);

        var result = await control.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { true }, invoker.Calls);
    }

    // ---- Read-back verification (work order PR1 section 8) ----
    [Fact]
    public async Task Mutation_that_writes_but_reads_back_mixed_is_not_a_success()
    {
        // Helper claims OK, but the authoritative re-read shows a mixed state.
        var invoker = new FakeInvoker { Result = Helper(CenterMStartupHelperOutcome.Completed, ok: true, false, false, false) };
        var reader = Reader(name => name == CenterMStartupStateReader.ServerTaskName, () => false); // server still enabled
        var control = new CenterMStartupControl(available: true, reader, invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Partial, result.Snapshot.State);
    }

    // ---- Cancelled elevation never fabricates the requested state (Addendum E) ----
    [Fact]
    public async Task Cancelled_uac_returns_cancelled_with_the_last_real_snapshot()
    {
        var invoker = new FakeInvoker { Result = Helper(CenterMStartupHelperOutcome.Cancelled, ok: false, false, false, false) };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => true), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Cancelled, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.Snapshot.State); // real pre-mutation state, not "Disabled"
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public async Task Helper_that_will_not_start_is_a_failure_not_a_success()
    {
        var invoker = new FakeInvoker { Result = Helper(CenterMStartupHelperOutcome.HelperUnavailable, ok: false, false, false, false, "missing") };
        var control = new CenterMStartupControl(available: true, Reader(_ => true, () => true), invoker);

        var result = await control.SetEnabledAsync(false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Mutation_is_unavailable_when_the_feature_does_not_apply()
    {
        var invoker = new FakeInvoker();
        var control = new CenterMStartupControl(available: false, Reader(_ => true, () => true), invoker);

        var result = await control.SetEnabledAsync(true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Unavailable, result.Outcome);
        Assert.Empty(invoker.Calls);
    }

    private static CenterMStartupStateReader Reader(Func<string, bool?> task, Func<bool?> service) => new(task, service);

    private static CenterMStartupHelperResult Helper(CenterMStartupHelperOutcome outcome, bool ok, bool server, bool updater, bool service, string? error = null)
        => new(outcome, ok, server, updater, service, error);

    private sealed class FakeInvoker : ICenterMStartupHelperInvoker
    {
        public List<bool> Calls { get; } = [];
        public CenterMStartupHelperResult Result { get; set; } = new(CenterMStartupHelperOutcome.Completed, true, true, true, true, null);

        public Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            Calls.Add(enabled);
            return Task.FromResult(Result);
        }
    }
}
