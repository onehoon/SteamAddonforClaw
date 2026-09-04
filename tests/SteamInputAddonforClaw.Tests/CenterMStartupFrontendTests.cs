using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR3: the frontend delegates the reboot-bound authority transition straight to the
/// Runtime-owned transition owner and returns its authoritative result verbatim. It must NOT raise a
/// global <see cref="IAddonFrontendControl.StateInvalidated"/> -- that would queue
/// <c>DevicePage.RefreshAsync()</c> and race the just-returned snapshot; the feature has no QAM
/// surface and a successful transition restarts Windows anyway (PR #430 review, carried forward).</summary>
[Collection("AppLog")]
public sealed class CenterMStartupFrontendTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Transition_request_flows_through_to_the_owner_verbatim(bool centerMEnabled)
    {
        var owner = new FakeTransition
        {
            Result = new(FrontendCenterMStartupMutationOutcome.Succeeded,
                new FrontendCenterMStartupSnapshot(FrontendCenterMStartupState.Disabled, false, false, false, null), null),
        };
        var control = CreateControl(owner);
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var result = await control.RequestCenterMAuthorityTransitionAsync(centerMEnabled);

        Assert.Same(owner.Result, result);
        Assert.Equal(centerMEnabled, owner.LastRequest);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Failed_transition_also_does_not_self_invalidate()
    {
        var owner = new FakeTransition
        {
            Result = new(FrontendCenterMStartupMutationOutcome.Failed, FrontendCenterMStartupSnapshot.Unavailable, "nope"),
        };
        var control = CreateControl(owner);
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var result = await control.RequestCenterMAuthorityTransitionAsync(false);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Transition_is_unavailable_when_no_owner_is_wired()
    {
        var control = CreateControl(owner: null);

        var result = await control.RequestCenterMAuthorityTransitionAsync(false);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Capture_flows_through_the_frontend()
    {
        var centerM = new CenterMStartupControl(true, ReaderFor(true, true, CenterMFoundationServiceMode.Automatic), new FakeInvoker());
        var control = CreateControl(owner: null, centerM: centerM);

        var snapshot = await control.CaptureCenterMStartupAsync();

        Assert.Equal(FrontendCenterMStartupState.Enabled, snapshot.State);
    }

    private InProcessAddonFrontendControl CreateControl(ICenterMRebootAuthorityTransition? owner, CenterMStartupControl? centerM = null)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            centerMStartup: centerM,
            centerMAuthorityTransition: owner);
    }

    private static CenterMStartupStateReader ReaderFor(bool server, bool updater, CenterMFoundationServiceMode service) =>
        new(name => name == CenterMStartupStateReader.ServerTaskName ? server
            : name == CenterMStartupStateReader.UpdaterTaskName ? updater
            : null,
            () => service);

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakeTransition : ICenterMRebootAuthorityTransition
    {
        public FrontendCenterMStartupMutationResult Result { get; set; } =
            new(FrontendCenterMStartupMutationOutcome.Succeeded, FrontendCenterMStartupSnapshot.Unavailable, null);
        public bool? LastRequest { get; private set; }

        public Task<FrontendCenterMStartupMutationResult> RequestAsync(bool centerMEnabled, CancellationToken cancellationToken)
        {
            LastRequest = centerMEnabled;
            return Task.FromResult(Result);
        }

        public Task<SteamInputAddonforClaw.CenterMStartup.StockUninstallPrepareResult> PrepareForUninstallAsync(CancellationToken cancellationToken)
            => Task.FromResult(SteamInputAddonforClaw.CenterMStartup.StockUninstallPrepareResult.Ok());
    }

    private sealed class FakeInvoker : ICenterMStartupHelperInvoker
    {
        public Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
            Task.FromResult(new CenterMStartupHelperResult(CenterMStartupHelperOutcome.Completed, true, true, true, true, CenterMFoundationServiceMode.Automatic, null));
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status capture is not part of these tests.");
    }
}
