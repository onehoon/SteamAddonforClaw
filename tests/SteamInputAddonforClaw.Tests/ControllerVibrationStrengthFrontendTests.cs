using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Contracts.ControllerVibration;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerVibrationStrengthFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.VibrationFrontend.{Guid.NewGuid():N}");

    [Fact]
    public async Task Capture_returns_persisted_pair_without_enumerating_or_reading_HID()
    {
        var persisted = new ControllerVibrationSettings(30, 70);
        var (settings, _) = CreateSettings(persisted);
        var devices = new CountingEnumerator([]);
        var client = CreateClient("msi.claw.cg3em", devices, new NoOpVibrationProfileIo());
        var control = CreateControl(settings, CenterM(FrontendCenterMStartupState.Disabled), client);

        var snapshot = await control.CaptureControllerVibrationStrengthAsync();

        Assert.True(snapshot.Available);
        Assert.True(snapshot.Writable);
        Assert.Equal(30, snapshot.LeftPercent);
        Assert.Equal(70, snapshot.RightPercent);
        Assert.Equal(0, devices.EnumerationCount);
    }

    [Fact]
    public async Task Set_persists_the_pair_before_one_best_effort_host_apply_and_keeps_it_on_failure()
    {
        var (settings, store) = CreateSettings(ControllerVibrationSettings.Default);
        var devices = new CountingEnumerator([]);
        var client = CreateClient("msi.claw.cg3em", devices, new NoOpVibrationProfileIo());
        var requested = new ControllerVibrationSettings(20, 80);
        var applyCount = 0;
        var control = CreateControl(
            settings,
            CenterM(FrontendCenterMStartupState.Disabled),
            client,
            apply: (pair, _) =>
            {
                applyCount++;
                Assert.Equal(requested, store.Load().ControllerVibration);
                Assert.Equal(pair, settings.ControllerVibration);
                return Task.FromResult(false);
            });

        var result = await control.SetControllerVibrationStrengthAsync(20, 80);

        Assert.Equal(1, applyCount);
        Assert.Equal(FrontendControllerVibrationStrengthMutationOutcome.Failed, result.Outcome);
        Assert.Equal(20, result.Snapshot.LeftPercent);
        Assert.Equal(80, result.Snapshot.RightPercent);
        Assert.Contains("Saved, but not applied now", result.FailureMessage, StringComparison.Ordinal);
        Assert.Equal(requested, settings.ControllerVibration);
        Assert.Equal(requested, store.Load().ControllerVibration);
        Assert.Equal(0, devices.EnumerationCount);
    }

    [Fact]
    public async Task Successful_set_applies_once_without_using_the_client_for_frontend_HID_access()
    {
        var (settings, store) = CreateSettings(ControllerVibrationSettings.Default);
        var devices = new CountingEnumerator([]);
        var client = CreateClient("msi.claw.cg3em", devices, new NoOpVibrationProfileIo());
        var applyCount = 0;
        var control = CreateControl(
            settings,
            CenterM(FrontendCenterMStartupState.Disabled),
            client,
            apply: (pair, _) =>
            {
                applyCount++;
                Assert.Equal(pair, store.Load().ControllerVibration);
                return Task.FromResult(true);
            });

        var result = await control.SetControllerVibrationStrengthAsync(40, 60);

        Assert.True(result.Succeeded);
        Assert.Equal(1, applyCount);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Equal(40, result.Snapshot.LeftPercent);
        Assert.Equal(60, result.Snapshot.RightPercent);
    }

    [Fact]
    public async Task Unsupported_model_and_stock_authority_preserve_saved_values_without_apply()
    {
        var original = new ControllerVibrationSettings(25, 75);
        var (settings, store) = CreateSettings(original);
        var devices = new CountingEnumerator([]);
        var unsupportedClient = CreateClient("msi.claw.a2vm.8", devices, new NoOpVibrationProfileIo());
        var applyCount = 0;
        var testCount = 0;
        var control = CreateControl(
            settings,
            CenterM(FrontendCenterMStartupState.Enabled),
            unsupportedClient,
            testAvailable: () => true,
            test: (_, _) =>
            {
                testCount++;
                return Task.FromResult(new FrontendControllerVibrationTestResult(FrontendControllerVibrationTestOutcome.Succeeded, null));
            },
            apply: (_, _) => { applyCount++; return Task.FromResult(true); });

        var snapshot = await control.CaptureControllerVibrationStrengthAsync();
        var blocked = await control.SetControllerVibrationStrengthAsync(10, 90);
        var test = await control.TestControllerVibrationMotorAsync(FrontendControllerVibrationMotor.Left);

        Assert.False(snapshot.Available);
        Assert.False(snapshot.Writable);
        Assert.True(snapshot.TestAvailable);
        Assert.Equal(25, snapshot.LeftPercent);
        Assert.Equal(75, snapshot.RightPercent);
        Assert.Equal(FrontendControllerVibrationStrengthMutationOutcome.Unavailable, blocked.Outcome);
        Assert.Equal(original, store.Load().ControllerVibration);
        Assert.True(test.Succeeded);
        Assert.Equal(1, testCount);
        Assert.Equal(0, applyCount);
        Assert.Equal(0, devices.EnumerationCount);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, 101)]
    public async Task Invalid_values_are_rejected_without_persisting_or_applying(int left, int right)
    {
        var (settings, store) = CreateSettings(ControllerVibrationSettings.Default);
        var applyCount = 0;
        var control = CreateControl(
            settings,
            CenterM(FrontendCenterMStartupState.Disabled),
            CreateClient("msi.claw.cg3em", new CountingEnumerator([]), new NoOpVibrationProfileIo()),
            apply: (_, _) => { applyCount++; return Task.FromResult(true); });

        var result = await control.SetControllerVibrationStrengthAsync(left, right);

        Assert.Equal(FrontendControllerVibrationStrengthMutationOutcome.Failed, result.Outcome);
        Assert.Equal(ControllerVibrationSettings.Default, store.Load().ControllerVibration);
        Assert.Equal(ControllerVibrationSettings.Default, settings.ControllerVibration);
        Assert.Equal(0, applyCount);
    }

    [Fact]
    public async Task Developer_profile_probe_remains_separate_and_requires_disabled_authority()
    {
        var (settings, _) = CreateSettings(ControllerVibrationSettings.Default);
        var devices = new CountingEnumerator([]);
        var io = new NoOpVibrationProfileIo();
        var client = CreateClient("msi.claw.cg3em", devices, io);
        var control = CreateControl(settings, centerM: null, client);

        var result = await control.RunControllerVibrationProfileWriteProbeAsync(
            FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred);

        Assert.Equal(FrontendControllerVibrationProfileWriteProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Equal(0, io.WriteCount);
    }

    private (StartupSettingsCoordinator Settings, SettingsStore Store) CreateSettings(ControllerVibrationSettings vibration)
    {
        var store = new SettingsStore(Path.Combine(_directory, Guid.NewGuid().ToString("N"), "settings.json"));
        var appSettings = new AppSettings { ControllerVibration = vibration };
        store.Save(appSettings);
        return (new StartupSettingsCoordinator(appSettings, store, new NoOpStartupManager()), store);
    }

    private static InProcessAddonFrontendControl CreateControl(
        StartupSettingsCoordinator settings,
        CenterMStartupControl? centerM,
        MsiClawVibrationStrengthClient? client,
        Func<bool>? testAvailable = null,
        Func<FrontendControllerVibrationMotor, CancellationToken, Task<FrontendControllerVibrationTestResult>>? test = null,
        Func<ControllerVibrationSettings, CancellationToken, Task<bool>>? apply = null) =>
        new(settings, new ThrowingSystemStatusProvider(), null,
            centerMStartup: centerM,
            controllerVibrationStrengthClient: client,
            controllerVibrationTestAvailable: testAvailable,
            testControllerVibrationMotor: test,
            applyControllerVibrationSettings: apply);

    private static CenterMStartupControl CenterM(FrontendCenterMStartupState state)
    {
        var (server, updater, mode) = state switch
        {
            FrontendCenterMStartupState.Disabled => (false, false, CenterMFoundationServiceMode.Disabled),
            FrontendCenterMStartupState.Enabled => (true, true, CenterMFoundationServiceMode.Automatic),
            _ => (true, false, CenterMFoundationServiceMode.Other)
        };
        var reader = new CenterMStartupStateReader(
            name => name == CenterMStartupStateReader.ServerTaskName ? server
                : name == CenterMStartupStateReader.UpdaterTaskName ? updater
                : null,
            () => mode);
        return new CenterMStartupControl(true, reader, new NoOpCenterMInvoker());
    }

    private static MsiClawVibrationStrengthClient CreateClient(
        string modelId,
        IControllerDeviceEnumerator devices,
        IMsiClawVibrationProfileIo io) =>
        new(new HandheldDeviceModelId(modelId), devices, new MsiClawControlHidResolver(), io);

    private sealed class CountingEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public int EnumerationCount { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            EnumerationCount++;
            return devices;
        }
    }

    private sealed class NoOpVibrationProfileIo : IMsiClawVibrationProfileIo
    {
        public int WriteCount { get; private set; }
        public Task<bool> WriteAsync(MsiClawControlHidDevice device, ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class NoOpCenterMInvoker : ICenterMStartupHelperInvoker
    {
        public Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
            Task.FromResult(new CenterMStartupHelperResult(CenterMStartupHelperOutcome.Completed, true, true, true, true,
                CenterMFoundationServiceMode.Disabled, null));
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
