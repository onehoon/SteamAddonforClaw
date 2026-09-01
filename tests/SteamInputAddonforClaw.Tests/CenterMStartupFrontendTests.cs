using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>PR #430 review: a successful MSI Center M startup mutation must NOT raise a global
/// <see cref="IAddonFrontendControl.StateInvalidated"/>. That notification queues
/// <c>DevicePage.RefreshAsync()</c>, which re-renders with <c>restartRequired: false</c> and would
/// erase the "Restart Windows to apply this change." notice immediately after the button click
/// showed it -- the one cue that the new startup state is not active until reboot.</summary>
[Collection("AppLog")]
public sealed class CenterMStartupFrontendTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Successful_mutation_does_not_self_invalidate_and_wipe_the_restart_notice()
    {
        var control = CreateControl(available: true, server: false, updater: false, service: false,
            helper: new FakeInvoker { Result = new(CenterMStartupHelperOutcome.Completed, Ok: true, false, false, false, null) });
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var result = await control.SetCenterMStartupEnabledAsync(false);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Disabled, result.Snapshot.State);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Failed_mutation_also_does_not_self_invalidate()
    {
        var control = CreateControl(available: true, server: true, updater: false, service: false,
            helper: new FakeInvoker { Result = new(CenterMStartupHelperOutcome.Completed, Ok: true, false, false, false, null) });
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        var result = await control.SetCenterMStartupEnabledAsync(false);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Capture_flows_through_the_frontend()
    {
        var control = CreateControl(available: true, server: true, updater: true, service: true, helper: new FakeInvoker());
        var snapshot = await control.CaptureCenterMStartupAsync();
        Assert.Equal(FrontendCenterMStartupState.Enabled, snapshot.State);
    }

    private InProcessAddonFrontendControl CreateControl(bool available, bool server, bool updater, bool service, ICenterMStartupHelperInvoker helper)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        var centerM = new CenterMStartupControl(available, ReaderFor(server, updater, service), helper);
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            "",
            captureRoutingStatus: () => new(true, RoutingOperationalState.Passive, false, false),
            centerMStartup: centerM);
    }

    private static CenterMStartupStateReader ReaderFor(bool server, bool updater, bool service) =>
        new(name => name == CenterMStartupStateReader.ServerTaskName ? server
            : name == CenterMStartupStateReader.UpdaterTaskName ? updater
            : null,
            () => service);

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakeInvoker : ICenterMStartupHelperInvoker
    {
        public CenterMStartupHelperResult Result { get; set; } = new(CenterMStartupHelperOutcome.Completed, true, true, true, true, null);
        public Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.FromResult(Result);
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
