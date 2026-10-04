using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerVibrationStrengthFrontendTests
{
    [Fact]
    public async Task Unverified_firmware_mapping_is_unavailable_without_blocking_the_independent_rumble_test()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.VibrationFrontend.{Guid.NewGuid():N}");
        var settings = new StartupSettingsCoordinator(
            new AppSettings(),
            new SettingsStore(Path.Combine(directory, "settings.json")),
            new NoOpStartupManager());
        var client = new MsiClawVibrationStrengthClient(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            new EmptyControllerDeviceEnumerator(),
            new MsiClawControlHidResolver(),
            new NoOpVibrationProfileIo());
        var testInvocations = 0;
        var control = new InProcessAddonFrontendControl(
            settings,
            new ThrowingSystemStatusProvider(),
            null,
            controllerVibrationStrengthClient: client,
            controllerVibrationTestAvailable: () => true,
            testControllerVibrationMotor: (_, _) =>
            {
                testInvocations++;
                return Task.FromResult(new FrontendControllerVibrationTestResult(
                    FrontendControllerVibrationTestOutcome.Succeeded, null));
            });

        var snapshot = await control.CaptureControllerVibrationStrengthAsync();
        var test = await control.TestControllerVibrationMotorAsync(FrontendControllerVibrationMotor.Left);

        Assert.False(snapshot.Available);
        Assert.False(snapshot.Writable);
        Assert.True(snapshot.TestAvailable);
        Assert.Null(snapshot.LeftPercent);
        Assert.Null(snapshot.RightPercent);
        Assert.Equal("Vibration firmware mapping is not verified for this MSI Claw model.", snapshot.Status);
        Assert.True(test.Succeeded);
        Assert.Equal(1, testInvocations);
    }

    [Fact]
    public async Task Developer_profile_probe_does_not_assume_centerM_is_disabled_when_no_startup_authority_is_available()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.VibrationProbeFrontend.{Guid.NewGuid():N}");
        var settings = new StartupSettingsCoordinator(
            new AppSettings(),
            new SettingsStore(Path.Combine(directory, "settings.json")),
            new NoOpStartupManager());
        var devices = new CountingControllerDeviceEnumerator();
        var io = new NoOpVibrationProfileIo();
        var client = new MsiClawVibrationStrengthClient(
            new HandheldDeviceModelId("msi.claw.cg3em"), devices, new MsiClawControlHidResolver(), io);
        var control = new InProcessAddonFrontendControl(
            settings, new ThrowingSystemStatusProvider(), null,
            controllerVibrationStrengthClient: client);

        var result = await control.RunControllerVibrationProfileWriteProbeAsync(
            FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred);

        Assert.Equal(FrontendControllerVibrationProfileWriteProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Equal(0, io.WriteCount);
    }

    private sealed class EmptyControllerDeviceEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => [];
    }

    private sealed class NoOpVibrationProfileIo : IMsiClawVibrationProfileIo
    {
        public int WriteCount { get; private set; }

        public Task<bool> WriteAsync(MsiClawControlHidDevice device, ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
            MsiClawControlHidDevice device,
            ReadOnlyMemory<byte> report,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<byte[]>?>(null);
    }

    private sealed class CountingControllerDeviceEnumerator : IControllerDeviceEnumerator
    {
        public int EnumerationCount { get; private set; }

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            EnumerationCount++;
            return [];
        }
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
