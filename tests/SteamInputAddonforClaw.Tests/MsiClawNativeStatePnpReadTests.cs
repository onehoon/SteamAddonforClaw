using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class MsiClawNativeStatePnpReadTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"NativeStatePnpReadTests.{Guid.NewGuid():N}");
    private readonly AppLogLevel _previousLogLevel = AppLog.MinimumLevelOverride;
    private readonly string? _previousLogDirectory = AppLog.DirectoryOverride;

    public MsiClawNativeStatePnpReadTests()
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
    public async Task CaptureStableSnapshot_RetriesOnlyTypedPnpReadAndLogsOneRecoverySummary()
    {
        var modeController = new RecordingModeController();
        var enumerator = new PnpFailureOnceEnumerator([MsiDirectInputDevice()]);
        var manager = new MsiClawNativeStateManager(enumerator, modeController, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var result = await manager.CaptureStableCurrentSnapshotAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var log = File.Exists(AppLog.CurrentLogFilePath) ? File.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty;

        Assert.Equal(NativeStateCaptureStatus.Success, result.Status);
        Assert.Null(result.PnpReadFailure);
        Assert.Equal(2, enumerator.Calls);
        Assert.Equal(0, modeController.CallCount);
        Assert.Equal(1, CountOccurrences(log, "Event=NativeStatePnpReadRecovered"));
        Assert.DoesNotContain("[ERROR]", log);
    }

    [Fact]
    public void CaptureSnapshot_DoesNotReadUnrelatedNodePropertiesWhenScopedMsiSnapshotIsHealthy()
    {
        var enumerator = new UnrelatedNodeFailureEnumerator([MsiDirectInputDevice()]);
        var result = new MsiClawNativeStateManager(enumerator).CaptureSnapshot();

        Assert.Equal(NativeStateCaptureStatus.Success, result.Status);
        Assert.Equal(0, enumerator.GlobalCalls);
        Assert.Equal(1, enumerator.ScopedCalls);
        Assert.Equal((ushort)0x1902, result.Snapshot?.Payload.Deserialize<MsiClawNativeStatePayload>()?.ProductId);
    }

    [Fact]
    public async Task CaptureStableSnapshot_PersistentPnpReadFailsClosedWithoutLoggingAnOwnershipLevelError()
    {
        var enumerator = new PersistentPnpFailureEnumerator();
        long timestamp = 0;
        var manager = new MsiClawNativeStateManager(enumerator,
            restoreSettleTimeout: TimeSpan.FromSeconds(2),
            restoreSettlePollInterval: TimeSpan.FromMilliseconds(500),
            settleDelay: (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                timestamp += (long)(duration.TotalSeconds * Stopwatch.Frequency);
                return Task.CompletedTask;
            },
            timestamp: () => timestamp);

        var result = await manager.CaptureStableCurrentSnapshotAsync(CancellationToken.None);
        AppLog.DrainForTests();
        var log = File.Exists(AppLog.CurrentLogFilePath) ? File.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty;

        Assert.Equal(NativeStateCaptureStatus.Failed, result.Status);
        Assert.Equal(5, result.PnpReadFailure?.Win32Error);
        Assert.Equal(4, enumerator.Calls);
        Assert.DoesNotContain("[ERROR]", log);
        Assert.Contains("SPDRP_HARDWAREID=1", result.Reason);
        Assert.Contains("InstanceId=HID", result.Reason);
    }

    [Fact]
    public async Task CaptureStableSnapshot_DoesNotRetryUnrelatedApplicationException()
    {
        var enumerator = new NonPnpFailureEnumerator();
        var manager = new MsiClawNativeStateManager(enumerator, restoreSettleTimeout: TimeSpan.FromSeconds(1));

        var result = await manager.CaptureStableCurrentSnapshotAsync(CancellationToken.None);

        Assert.Equal(NativeStateCaptureStatus.Failed, result.Status);
        Assert.Null(result.PnpReadFailure);
        Assert.Equal(1, enumerator.Calls);
    }

    [Fact]
    public async Task CaptureStableSnapshot_CancellationDuringPnpSettlePropagatesPromptly()
    {
        using var cancellation = new CancellationTokenSource();
        var enumerator = new CancelOnPnpFailureEnumerator(cancellation);
        var manager = new MsiClawNativeStateManager(enumerator, restoreSettleTimeout: TimeSpan.FromSeconds(30),
            restoreSettlePollInterval: TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.CaptureStableCurrentSnapshotAsync(cancellation.Token));
        Assert.Equal(1, enumerator.Calls);
    }

    private static ControllerDeviceInfo MsiDirectInputDevice() => new(
        @"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A", Guid.NewGuid(), @"USB\PARENT",
        [@"USB\VID_0DB0&PID_1902\CLAW_A"], "HID", [@"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A"], [],
        "HIDClass", null, null, 0x0DB0, 0x1902, true, null, 0x0001, 0x0005);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private sealed class PnpFailureOnceEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        internal int Calls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
            throw new Xunit.Sdk.XunitException("Native-state capture must not use global detailed PnP enumeration.");
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
        {
            AssertKnownControllerQuery(vendorId, productIds);
            if (Calls++ == 0) throw Failure();
            return devices;
        }
    }

    private sealed class UnrelatedNodeFailureEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        internal int GlobalCalls { get; private set; }
        internal int ScopedCalls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            GlobalCalls++;
            throw new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", 5, 1,
                "SPDRP_HARDWAREID", 128, @"HID\VID_045E&PID_0B13\OTHER", 91);
        }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
        {
            AssertKnownControllerQuery(vendorId, productIds);
            ScopedCalls++;
            return devices;
        }
    }

    private sealed class PersistentPnpFailureEnumerator : IControllerDeviceEnumerator
    {
        internal int Calls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
            throw new Xunit.Sdk.XunitException("Native-state capture must not use global detailed PnP enumeration.");
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
        {
            AssertKnownControllerQuery(vendorId, productIds);
            Calls++;
            throw Failure();
        }
    }

    private sealed class NonPnpFailureEnumerator : IControllerDeviceEnumerator
    {
        internal int Calls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
            throw new Xunit.Sdk.XunitException("Native-state capture must not use global detailed PnP enumeration.");
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
        {
            AssertKnownControllerQuery(vendorId, productIds);
            Calls++;
            throw new InvalidOperationException("Application failure, not a native PnP error.");
        }
    }

    private sealed class CancelOnPnpFailureEnumerator(CancellationTokenSource cancellation) : IControllerDeviceEnumerator
    {
        internal int Calls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() =>
            throw new Xunit.Sdk.XunitException("Native-state capture must not use global detailed PnP enumeration.");
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
        {
            AssertKnownControllerQuery(vendorId, productIds);
            Calls++;
            cancellation.Cancel();
            throw Failure();
        }
    }

    private sealed class RecordingModeController : IMsiClawModeController
    {
        internal int CallCount { get; private set; }
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target,
            MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new Xunit.Sdk.XunitException("Passive PnP observation must not issue a mode write.");
        }
    }

    private static PnpDeviceReadException Failure() => new(
        "SetupDiGetDeviceRegistryPropertyW", 5, 1, "SPDRP_HARDWAREID", 256,
        @"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A", 42);

    private static void AssertKnownControllerQuery(ushort vendorId, IReadOnlyCollection<ushort> productIds)
    {
        Assert.Equal((ushort)0x0DB0, vendorId);
        Assert.Equal(new ushort[] { 0x1901, 0x1902, 0x1903 }, productIds.Order().ToArray());
    }
}
