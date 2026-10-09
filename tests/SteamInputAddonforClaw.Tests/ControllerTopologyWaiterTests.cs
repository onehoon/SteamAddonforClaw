using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class ControllerTopologyWaiterTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"TopologyWaiterTests.{Guid.NewGuid():N}");
    private readonly AppLogLevel _previousLogLevel = AppLog.MinimumLevelOverride;
    private readonly string? _previousLogDirectory = AppLog.DirectoryOverride;

    public ControllerTopologyWaiterTests()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.DirectoryOverride = _logDirectory;
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.MinimumLevelOverride = _previousLogLevel;
        AppLog.DirectoryOverride = _previousLogDirectory;
        if (Directory.Exists(_logDirectory)) Directory.Delete(_logDirectory, recursive: true);
    }

    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalHandheldIsAbsent_ReturnsIndeterminate()
    {
        var waiter = CreateWaiter([], requiredStableSnapshots: 3, timeout: TimeSpan.FromMilliseconds(20));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerTopologyReadiness.Indeterminate, readiness);
    }

    // A realistic Stock Center M topology requires both the gamepad-usage interface AND the
    // mode-switch-critical control HID interface (see MsiClawModeTopology). A single generic
    // MSI VID/PID device is not sufficient for readiness — see the dedicated
    // GamepadOnlyWithoutControlHid regression test below for why.
    [Fact]
    public async Task WaitUntilStableAsync_WhenInternalHandheldTopologyIsStable_ReturnsStable()
    {
        var gamepadInterface = GamepadInterface();
        var directInputControlHid = DirectInputControlHid();
        var waiter = CreateWaiter([gamepadInterface, directInputControlHid], requiredStableSnapshots: 3, timeout: TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerTopologyReadiness.Stable, readiness);
    }

    // Same good path as above, but exercises the XInput control HID topology (PID 1901, UsagePage
    // 0xFFA0, Usage 0x0001) instead of DirectInput, proving readiness recognizes either mode.
    [Fact]
    public async Task WaitUntilStableAsync_GamepadAndXInputControlHidBothStable_ReturnsStable()
    {
        var gamepadInterface = GamepadInterface();
        var xInputControlHid = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1901&MI_02&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1901&MI_02&COL01"],
            ["HID_DEVICE_UP:FFA0_U:0001"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1901,
            true,
            null,
            0xFFA0,
            0x0001);
        var waiter = CreateWaiter([gamepadInterface, xInputControlHid], requiredStableSnapshots: 3, timeout: TimeSpan.FromSeconds(1));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerTopologyReadiness.Stable, readiness);
    }

    // Regression: the MSI gamepad-usage interface enumerating (and staying constant) is not enough on
    // its own. Without the mode-switch-critical control HID (XInput or DirectInput topology) ever
    // appearing, readiness must never settle on Stable, no matter how many stable polls the gamepad
    // interface alone accumulates.
    [Fact]
    public async Task WaitUntilStableAsync_GamepadOnlyWithoutControlHid_DoesNotReportStable()
    {
        var gamepadInterface = GamepadInterface();
        var waiter = CreateWaiter([gamepadInterface], requiredStableSnapshots: 3, timeout: TimeSpan.FromMilliseconds(30));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerTopologyReadiness.Indeterminate, readiness);
    }

    private static ControllerDeviceInfo GamepadInterface() => new(
        "HID\\VID_0DB0&PID_1902&MI_00&COL01",
        Guid.NewGuid(),
        null,
        [],
        "HID",
        ["HID\\VID_0DB0&PID_1902&MI_00&COL01"],
        ["HID_DEVICE_UP:0001_U:0005"],
        "HIDClass",
        null,
        null,
        0x0DB0,
        0x1902,
        true);

    private static ControllerDeviceInfo DirectInputControlHid() => new(
        "HID\\VID_0DB0&PID_1902&MI_02&COL01",
        Guid.NewGuid(),
        null,
        [],
        "HID",
        ["HID\\VID_0DB0&PID_1902&MI_02&COL01"],
        ["HID_DEVICE_UP:FFF0_U:0040"],
        "HIDClass",
        null,
        null,
        0x0DB0,
        0x1902,
        true,
        null,
        0xFFF0,
        0x0040);

    [Fact]
    public async Task WaitUntilStableAsync_ExternalControllerHotplugNoise_DoesNotResetOrBlockStockCenterMStabilization()
    {
        var stableDevices = new[] { GamepadInterface(), DirectInputControlHid() };
        var xboxController = new ControllerDeviceInfo(
            "HID\\VID_045E&PID_0B13",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_045E&PID_0B13"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x045E,
            0x0B13,
            true);
        var steamControllerReceiver = new ControllerDeviceInfo(
            "HID\\VID_28DE&PID_1304&MI_02&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_28DE&PID_1304&MI_02&COL01"],
            ["HID_DEVICE_UP:FF00_U:0001"],
            "HIDClass",
            null,
            null,
            0x28DE,
            0x1304,
            true,
            null,
            0xFF00,
            0x0001);
        var enumerator = new HotplugNoiseEnumerator(stableDevices, [xboxController, steamControllerReceiver]);
        var waiter = new ControllerTopologyWaiter(
            enumerator,
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(2));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerTopologyReadiness.Stable, readiness);
    }

    // Regression for the startup race: the MSI Claw's gamepad-usage HID interface (the one that
    // satisfies a generic "is this a game controller" filter) can enumerate before the vendor/control
    // HID interface (vendor-defined usage page, e.g. XInput PID 1901 UsagePage 0xFFA0/Usage 0x0001, or
    // DirectInput PID 1902 UsagePage 0xFFF0/Usage 0x0040) that the mode-switch logic actually depends
    // on. IsInternalHandheld must track both, not just the gamepad-usage interface, so readiness cannot
    // be declared Stable while the control interface is still settling.
    [Fact]
    public async Task WaitUntilStableAsync_MsiControlInterfaceStillSettling_DoesNotReportStable()
    {
        var gamepadInterface = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1902&MI_00&COL01"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1902,
            true);
        // The control interface never settles: its InstanceId changes on every poll, simulating PnP
        // enumeration still in progress.
        var enumerator = new SettlingControlInterfaceEnumerator(gamepadInterface, settleAfterTick: int.MaxValue);
        var waiter = new ControllerTopologyWaiter(
            enumerator,
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromMilliseconds(30));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        // Prior to the fix, IsInternalHandheld required IsGameControllerCandidate, which the vendor
        // control interface never satisfies, so it was silently excluded from the stability snapshot
        // entirely and the gamepad interface alone would report Stable almost immediately within this
        // same short timeout. With the fix, the still-changing control interface keeps resetting
        // stability, so readiness times out to Indeterminate instead.
        Assert.Equal(ControllerTopologyReadiness.Indeterminate, readiness);
    }

    [Fact]
    public async Task WaitUntilStableAsync_MsiControlInterfaceSettlesAfterFewPolls_ReturnsStableOnceBothSettle()
    {
        var gamepadInterface = new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL01",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_0DB0&PID_1902&MI_00&COL01"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1902,
            true);
        var enumerator = new SettlingControlInterfaceEnumerator(gamepadInterface, settleAfterTick: 2);
        var waiter = new ControllerTopologyWaiter(
            enumerator,
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero,
            timeout: TimeSpan.FromSeconds(2));

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);

        Assert.Equal(ControllerTopologyReadiness.Stable, readiness);
    }

    [Fact]
    public async Task TimeoutDiagnostic_DistinguishesNoPresentMsiCandidatesWithoutLoggingPnpIds()
    {
        var log = await CaptureTimeoutDiagnostic(Array.Empty<ControllerDeviceInfo>(), new MsiClawInternalControllerMatcher());

        Assert.Contains("FailureClass=NoPresentMsiCandidates", log);
        Assert.Contains("PresentMsiCandidateCount=0", log);
        Assert.Contains("ConsecutiveStableSnapshots=0", log);
        Assert.DoesNotContain(@"HID\VID_0DB0", log);
    }

    [Fact]
    public async Task TimeoutDiagnostic_DistinguishesKnownVidPidCandidatesNotMatchedAsInternal()
    {
        var log = await CaptureTimeoutDiagnostic([GamepadInterface()], new NeverMatchInternalControllerMatcher());

        Assert.Contains("FailureClass=MsiCandidatesNotClassifiedAsInternal", log);
        Assert.Contains("PresentMsiCandidateCount=1", log);
        Assert.Contains("RecognizedInternalDeviceCount=0", log);
        Assert.DoesNotContain(@"HID\VID_0DB0", log);
    }

    [Fact]
    public async Task TimeoutDiagnostic_DistinguishesMissingControlHidFromUnstableTopology()
    {
        var missingControlLog = await CaptureTimeoutDiagnostic([GamepadInterface()], new MsiClawInternalControllerMatcher());
        Assert.Contains("FailureClass=RequiredControlHidMissing", missingControlLog);
        Assert.Contains("RecognizedInternalDeviceCount=1", missingControlLog);
        Assert.Contains("Pid1902DirectInputControlHidPresent=False", missingControlLog);

        var gamepad = GamepadInterface();
        var settlingEnumerator = new SettlingControlInterfaceEnumerator(gamepad, settleAfterTick: int.MaxValue);
        var unstableLog = await CaptureTimeoutDiagnostic(settlingEnumerator, new MsiClawInternalControllerMatcher());
        Assert.Contains("FailureClass=RelevantTopologyNotStable", unstableLog);
        Assert.Contains("Pid1902DirectInputControlHidPresent=True", unstableLog);
        Assert.Contains("ConsecutiveStableSnapshots=1", unstableLog);
        Assert.DoesNotContain(@"HID\VID_0DB0", unstableLog);
    }

    [Fact]
    public async Task TimeoutDiagnostic_PreservesEnumerationExceptionAsTheTerminalFailure()
    {
        var waiter = CreateWaiter(new ThrowingEnumerator(), new MsiClawInternalControllerMatcher());

        var readiness = await waiter.WaitUntilStableAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);

        Assert.Equal(ControllerTopologyReadiness.Indeterminate, readiness);
        Assert.Contains("FailureClass=EnumerationFailed", log);
        Assert.Contains("InvalidOperationException", log);
    }

    private async Task<string> CaptureTimeoutDiagnostic(IReadOnlyList<ControllerDeviceInfo> devices, IInternalControllerMatcher matcher) =>
        await CaptureTimeoutDiagnostic(new FakeEnumerator(devices), matcher);

    private async Task<string> CaptureTimeoutDiagnostic(IControllerDeviceEnumerator enumerator, IInternalControllerMatcher matcher)
    {
        var readiness = await CreateWaiter(enumerator, matcher).WaitUntilStableAsync(CancellationToken.None);
        AppLog.DrainForTests();
        Assert.Equal(ControllerTopologyReadiness.Indeterminate, readiness);
        return File.ReadAllText(AppLog.CurrentLogFilePath);
    }

    private static ControllerTopologyWaiter CreateWaiter(IControllerDeviceEnumerator enumerator, IInternalControllerMatcher matcher) =>
        new(enumerator, new ControllerDeviceClassifier(matcher), requiredStableSnapshots: 3,
            sampleInterval: TimeSpan.Zero, timeout: TimeSpan.FromMilliseconds(10));

    private static ControllerTopologyWaiter CreateWaiter(
        IReadOnlyList<ControllerDeviceInfo> devices,
        int requiredStableSnapshots,
        TimeSpan timeout)
    {
        return new ControllerTopologyWaiter(
            new FakeEnumerator(devices),
            new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()),
            requiredStableSnapshots,
            TimeSpan.Zero,
            timeout);
    }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }

    private sealed class NeverMatchInternalControllerMatcher : IInternalControllerMatcher
    {
        public InternalControllerMatchResult Match(InternalControllerMatchContext context) =>
            new(InternalControllerMatchStatus.NoMatch, "TestNoMatch");
    }

    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException("enumeration failed");
    }

    /// <summary>
    /// Simulates external-controller hotplug noise: on every poll tick, a different (or no) external
    /// candidate device is present alongside the always-present stable device. Used to prove that
    /// external-controller connect/disconnect noise cannot reset or block startup stabilization.
    /// </summary>
    private sealed class HotplugNoiseEnumerator(IReadOnlyList<ControllerDeviceInfo> stableDevices, IReadOnlyList<ControllerDeviceInfo> noiseCandidates) : IControllerDeviceEnumerator
    {
        private int _tick;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var index = _tick++ % (noiseCandidates.Count + 1);
            var devices = new List<ControllerDeviceInfo>(stableDevices);
            if (index > 0) devices.Add(noiseCandidates[index - 1]);
            return devices;
        }
    }

    /// <summary>
    /// Simulates the MSI Claw's vendor/control HID interface (same VID/PID family as the gamepad
    /// interface, different collection) still being enumerated by PnP: before <paramref name="settleAfterTick"/>
    /// its InstanceId changes on every poll; from that tick onward it is fixed. The always-present
    /// gamepad interface never changes.
    /// </summary>
    private sealed class SettlingControlInterfaceEnumerator(ControllerDeviceInfo gamepadInterface, int settleAfterTick) : IControllerDeviceEnumerator
    {
        private int _tick;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var tick = _tick++;
            var instanceId = tick < settleAfterTick
                ? $"HID\\VID_0DB0&PID_1902&MI_02&COL01_Settling_{tick}"
                : "HID\\VID_0DB0&PID_1902&MI_02&COL01";
            var controlInterface = new ControllerDeviceInfo(
                instanceId,
                Guid.NewGuid(),
                null,
                [],
                "HID",
                [instanceId],
                ["HID_DEVICE_UP:FFF0_U:0040"],
                "HIDClass",
                null,
                null,
                0x0DB0,
                0x1902,
                true,
                null,
                0xFFF0,
                0x0040);
            return [gamepadInterface, controlInterface];
        }
    }
}
