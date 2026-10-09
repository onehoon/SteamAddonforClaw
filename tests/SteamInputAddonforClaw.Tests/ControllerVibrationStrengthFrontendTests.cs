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
    public async Task Unsupported_A2VM7_preserves_saved_values_without_apply()
    {
        var original = new ControllerVibrationSettings(25, 75);
        var (settings, store) = CreateSettings(original);
        var devices = new CountingEnumerator([]);
        var unsupportedClient = CreateClient("msi.claw.a2vm.7", devices, new NoOpVibrationProfileIo());
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
        Assert.Contains("profile-write mapping has not been verified", snapshot.Status, StringComparison.Ordinal);
        Assert.Contains("no profile write was issued", snapshot.Status, StringComparison.Ordinal);
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

    [Fact]
    public async Task A2VM8_snapshot_is_writable_under_Addon_authority_and_stock_authority_keeps_it_read_only()
    {
        var original = new ControllerVibrationSettings(25, 75);
        var (settings, store) = CreateSettings(original);
        var devices = new CountingEnumerator([]);
        var applyCount = 0;
        var client = CreateClient("msi.claw.a2vm.8", devices, new NoOpVibrationProfileIo());
        var addonControl = CreateControl(
            settings,
            CenterM(FrontendCenterMStartupState.Disabled),
            client,
            apply: (pair, _) => { applyCount++; Assert.Equal(pair, store.Load().ControllerVibration); return Task.FromResult(true); });

        var addonSnapshot = await addonControl.CaptureControllerVibrationStrengthAsync();
        Assert.True(addonSnapshot.Available);
        Assert.True(addonSnapshot.Writable);
        Assert.Equal<int?>(original.LeftPercent, addonSnapshot.LeftPercent);
        Assert.Equal<int?>(original.RightPercent, addonSnapshot.RightPercent);
        Assert.True((await addonControl.SetControllerVibrationStrengthAsync(20, 80)).Succeeded);
        Assert.Equal(1, applyCount);
        Assert.Equal(new ControllerVibrationSettings(20, 80), store.Load().ControllerVibration);

        var stockControl = CreateControl(settings, CenterM(FrontendCenterMStartupState.Enabled), client,
            apply: (_, _) => { applyCount++; return Task.FromResult(true); });
        var stockSnapshot = await stockControl.CaptureControllerVibrationStrengthAsync();
        var blocked = await stockControl.SetControllerVibrationStrengthAsync(10, 90);

        Assert.True(stockSnapshot.Available);
        Assert.False(stockSnapshot.Writable);
        Assert.Equal(FrontendControllerVibrationStrengthMutationOutcome.Unavailable, blocked.Outcome);
        Assert.Equal(new ControllerVibrationSettings(20, 80), store.Load().ControllerVibration);
        Assert.Equal(1, applyCount);
        Assert.Equal(0, devices.EnumerationCount);
    }

    [Fact]
    public async Task A2VM8_mutation_keeps_the_saved_pair_when_the_production_apply_fails()
    {
        var (settings, store) = CreateSettings(ControllerVibrationSettings.Default);
        var requested = new ControllerVibrationSettings(37, 63);
        var devices = new CountingEnumerator([]);
        var applyCount = 0;
        var control = CreateControl(
            settings,
            CenterM(FrontendCenterMStartupState.Disabled),
            CreateClient("msi.claw.a2vm.8", devices, new NoOpVibrationProfileIo()),
            apply: (pair, _) =>
            {
                applyCount++;
                Assert.Equal(requested, pair);
                Assert.Equal(requested, store.Load().ControllerVibration);
                return Task.FromResult(false);
            });

        var result = await control.SetControllerVibrationStrengthAsync(requested.LeftPercent, requested.RightPercent);

        Assert.Equal(FrontendControllerVibrationStrengthMutationOutcome.Failed, result.Outcome);
        Assert.Equal(requested, store.Load().ControllerVibration);
        Assert.Equal(requested, settings.ControllerVibration);
        Assert.Equal(1, applyCount);
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

    [Fact]
    public async Task A2vm_probe_status_distinguishes_transport_acceptance_from_verified_effect_and_surfaces_failed_restore()
    {
        var device = CreateControlDevice();
        var identity = MsiClawPhysicalIdentity.From(device);
        var (settings, _) = CreateSettings(ControllerVibrationSettings.Default);
        var applyIo = new NoOpVibrationProfileIo();
        var applyClient = CreateClient("msi.claw.a2vm.8", new CountingEnumerator([device]), applyIo);
        var applyControl = CreateControl(settings, CenterM(FrontendCenterMStartupState.Disabled), applyClient,
            ownedIdentity: () => identity);

        var apply = await applyControl.RunControllerVibrationProfileWriteProbeAsync(
            FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred);

        Assert.Equal(FrontendControllerVibrationProfileWriteProbeOutcome.Succeeded, apply.Outcome);
        Assert.Contains("0% / 100% motor asymmetry was physically observed on A2VM 8", apply.Status, StringComparison.Ordinal);
        Assert.Contains("this probe result confirms HID transport only", apply.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("CG3EM", apply.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, applyIo.WriteCount);

        var restoreIo = new NoOpVibrationProfileIo { WriteResult = false };
        var restoreClient = CreateClient("msi.claw.a2vm.8", new CountingEnumerator([device]), restoreIo);
        var restoreControl = CreateControl(settings, CenterM(FrontendCenterMStartupState.Disabled), restoreClient,
            ownedIdentity: () => identity);

        var restore = await restoreControl.RunControllerVibrationProfileWriteProbeAsync(
            FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty);

        Assert.Equal(FrontendControllerVibrationProfileWriteProbeOutcome.Failed, restore.Outcome);
        Assert.Contains("Restore failed; the motors may remain at test values", restore.Status, StringComparison.Ordinal);
        Assert.Equal(1, restoreIo.WriteCount);
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
        Func<ControllerVibrationSettings, CancellationToken, Task<bool>>? apply = null,
        Func<MsiClawPhysicalIdentity?>? ownedIdentity = null) =>
        new(settings, new ThrowingSystemStatusProvider(), null,
            centerMStartup: centerM,
            controllerVibrationStrengthClient: client,
            controllerVibrationTestAvailable: testAvailable,
            testControllerVibrationMotor: test,
            applyControllerVibrationSettings: apply,
            controllerVibrationProbeIdentitySource: ownedIdentity);

    private static ControllerDeviceInfo CreateControlDevice()
    {
        var root = "USB\\VID_0DB0&PID_1902\\CLAW";
        var container = new Guid("5d6f297b-0f1a-4d58-b737-0048baec0dd1");
        return new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CLAW",
            container,
            root,
            [root],
            "HID",
            ["HID\\VID_0DB0&PID_1902"],
            [],
            "HIDClass",
            null,
            "HidUsb",
            0x0DB0,
            0x1902,
            true,
            "MSI Claw Control",
            0xFFF0,
            0x0040);
    }

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
        public bool WriteResult { get; init; } = true;
        public Task<bool> WriteAsync(MsiClawControlHidDevice device, ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(WriteResult);
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
