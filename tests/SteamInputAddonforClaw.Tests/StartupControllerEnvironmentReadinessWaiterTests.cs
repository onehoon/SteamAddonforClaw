using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupControllerEnvironmentReadinessWaiterTests
{
    [Fact]
    public async Task ManualLaunch_DoesNotWaitEvenWhenNotReady()
    {
        var provider = new FakeAssessmentProvider([Starting()]);
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: false, CancellationToken.None);

        Assert.Equal(1, provider.CaptureCount);
        Assert.Empty(delay.Calls);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.MsiCenterMStarting, result.Compatibility.Reason);
    }

    [Fact]
    public async Task BackgroundStartup_CenterMAlreadyReady_AddsNoDelay()
    {
        var provider = new FakeAssessmentProvider([Ready()]);
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(1, provider.CaptureCount);
        Assert.Empty(delay.Calls);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Supported, result.Compatibility.Status);
    }

    [Fact]
    public async Task BackgroundStartup_CenterMTransitionsDuringBoot_UsesFinalFreshAssessment()
    {
        var provider = new FakeAssessmentProvider([NotRunning(), Starting(), Ready()]);
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(3, provider.CaptureCount);
        Assert.Equal(2, delay.Calls.Count);
        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Supported, result.Compatibility.Status);
    }

    [Fact]
    public async Task BackgroundStartup_CenterMStaysStarting_TimesOutAndReturnsLatestAssessment()
    {
        var provider = new FakeAssessmentProvider(() => Starting());
        var delay = new FakeDelay();
        // A short real timeout with a non-waiting fake delay exercises genuine timeout logic
        // (the waiter's own Stopwatch) without ever sleeping for a real 30 seconds.
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, pollInterval: TimeSpan.Zero, timeout: TimeSpan.FromMilliseconds(1000), delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, result.Compatibility.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.MsiCenterMStarting, result.Compatibility.Reason);
        Assert.True(provider.CaptureCount > 1);
    }

    [Fact]
    public async Task BackgroundStartup_CenterMStaysNotRunning_TimesOutAndReturnsLatestAssessment()
    {
        var provider = new FakeAssessmentProvider(() => NotRunning());
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, pollInterval: TimeSpan.Zero, timeout: TimeSpan.FromMilliseconds(1000), delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational, result.Compatibility.Reason);
        Assert.True(provider.CaptureCount > 1);
    }

    [Fact]
    public async Task BackgroundStartup_CenterMNotInstalled_DoesNotWait()
    {
        var provider = new FakeAssessmentProvider([NotInstalled()]);
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(1, provider.CaptureCount);
        Assert.Empty(delay.Calls);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.MsiCenterMRequired, result.Compatibility.Reason);
    }

    [Theory]
    [InlineData((int)ControllerManagerKind.ClawTweaks, (int)ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion)]
    [InlineData((int)ControllerManagerKind.HandheldCompanion, (int)ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion)]
    [InlineData((int)ControllerManagerKind.Winhanced, (int)ControllerEnvironmentCompatibilityReason.WinhancedNotSupportedByCurrentVersion)]
    [InlineData((int)ControllerManagerKind.Multiple, (int)ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion)]
    public async Task BackgroundStartup_ThirdPartyManagerDetected_DoesNotWaitForCenterM(int kindValue, int reasonValue)
    {
        var kind = (ControllerManagerKind)kindValue;
        var reason = (ControllerEnvironmentCompatibilityReason)reasonValue;
        var provider = new FakeAssessmentProvider([ThirdParty(kind, reason)]);
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(1, provider.CaptureCount);
        Assert.Empty(delay.Calls);
        Assert.Equal(kind, result.Manager.Kind);
    }

    [Fact]
    public async Task BackgroundStartup_IndeterminateState_RetriesWithinBoundedWindowThenTimesOutPassive()
    {
        var provider = new FakeAssessmentProvider(() => Indeterminate());
        var delay = new FakeDelay();
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, pollInterval: TimeSpan.Zero, timeout: TimeSpan.FromMilliseconds(1000), delay: delay.DelayAsync);

        var result = await waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, CancellationToken.None);

        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, result.Compatibility.Status);
        Assert.True(provider.CaptureCount > 1);
    }

    [Fact]
    public async Task Cancellation_DuringReadinessDelay_PropagatesWithoutFurtherAssessment()
    {
        var provider = new FakeAssessmentProvider(() => Starting());
        using var cancellation = new CancellationTokenSource();
        var delay = new FakeDelay { OnDelay = () => cancellation.Cancel() };
        var waiter = new StartupControllerEnvironmentReadinessWaiter(provider, delay: delay.DelayAsync);

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter.WaitForReadyAssessmentAsync(isBackgroundStartup: true, cancellation.Token));

        Assert.Equal(1, provider.CaptureCount);
    }

    private static ControllerEnvironmentAssessmentSnapshot Ready() =>
        new([], new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager),
            new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported));

    private static ControllerEnvironmentAssessmentSnapshot Starting() =>
        new([], new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager),
            new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.MsiCenterMStarting));

    private static ControllerEnvironmentAssessmentSnapshot NotRunning() =>
        new([], new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager),
            new(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational));

    private static ControllerEnvironmentAssessmentSnapshot NotInstalled() =>
        new([], new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager),
            new(ControllerEnvironmentCompatibilityStatus.Unsupported, ControllerEnvironmentCompatibilityReason.MsiCenterMRequired));

    private static ControllerEnvironmentAssessmentSnapshot Indeterminate() =>
        new([], new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate),
            new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate));

    private static ControllerEnvironmentAssessmentSnapshot ThirdParty(ControllerManagerKind kind, ControllerEnvironmentCompatibilityReason reason) =>
        new([], new(kind, ControllerManagerClassificationReason.ClawTweaksDetected), new(ControllerEnvironmentCompatibilityStatus.Unsupported, reason));

    private sealed class FakeAssessmentProvider : IControllerEnvironmentAssessmentProvider
    {
        private readonly Queue<ControllerEnvironmentAssessmentSnapshot>? _sequence;
        private readonly Func<ControllerEnvironmentAssessmentSnapshot>? _factory;
        public int CaptureCount { get; private set; }

        public FakeAssessmentProvider(IEnumerable<ControllerEnvironmentAssessmentSnapshot> sequence) => _sequence = new(sequence);
        public FakeAssessmentProvider(Func<ControllerEnvironmentAssessmentSnapshot> factory) => _factory = factory;

        public ControllerEnvironmentAssessmentSnapshot Capture()
        {
            CaptureCount++;
            if (_sequence is not null) return _sequence.Count > 1 ? _sequence.Dequeue() : _sequence.Peek();
            return _factory!();
        }
    }

    private sealed class FakeDelay
    {
        public List<TimeSpan> Calls { get; } = [];
        public Action? OnDelay { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Calls.Add(delay);
            OnDelay?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
