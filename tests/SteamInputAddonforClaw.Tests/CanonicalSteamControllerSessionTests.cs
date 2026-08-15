using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalSteamControllerSessionTests
{
    [Fact]
    public void Starts_in_canonical_order_with_explicit_config_and_identity()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamControllerSession(native);

        Assert.True(session.Start());

        Assert.Equal(["NewUSBServer", "CreateUSBBus", "CreateSteamControllerDevice", "GetUSBDeviceIdentity", "AttachUSBDevice"], native.Calls);
        Assert.Equal("127.0.0.1:3241", native.Address);
        Assert.Equal((ulong)30_000, native.Config.ConnectionTimeoutMs);
        Assert.Equal((ulong)5_000, native.Config.DeviceHandlerConnectTimeoutMs);
        Assert.Equal((uint)1, native.Config.WriteBatchFlushIntervalMs);
        Assert.Equal((uint)0, native.BusInput);
        Assert.False(native.AutoAttach);
        Assert.Equal((ushort)0x28DE, native.Vendor);
        Assert.Equal((ushort)0x1102, native.Product);
        Assert.Equal(CanonicalSteamControllerSessionState.Active, session.State);
        Assert.Equal((uint)42, session.BusId);
        Assert.Equal((uint)9, session.LogicalDeviceId);
        // Pins the D-pad diagnostic wiring itself, not just the log callback's own behavior in
        // isolation: a future accidental revert to NewUSBServer(..., logCallback: null) would fail
        // this assertion even though CanonicalViiperDiagnosticLogTests (which call the callback
        // directly) would still all pass.
        Assert.Same(CanonicalViiperDiagnosticLog.Callback, native.LogCallback);
    }

    [Fact]
    public void Typed_state_and_neutral_are_active_only_and_start_does_not_send_neutral()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());
        Assert.Empty(native.States);

        var state = SteamControllerDeviceStateMapper.Map(new ControllerState(new GamepadButtons(true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false), default, default, default, new([false, false])));
        Assert.True(session.SetState(state));
        Assert.True(session.SetNeutral());
        Assert.Equal([state, default], native.States);

        Assert.True(session.RemoveDevice());
        Assert.False(session.SetState(state));
    }

    [Fact]
    public void Close_failure_retries_only_server_phase()
    {
        var native = new FakeNative { CloseResults = new Queue<bool>([false, true]) };
        using var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());
        Assert.True(session.RemoveDevice());

        Assert.False(session.CompleteRuntimeCleanup());
        Assert.Equal(CanonicalPendingCleanupPhase.ServerClose, session.PendingCleanupPhase);
        Assert.Equal(CanonicalSteamControllerSessionState.CleanupPending, session.State);
        Assert.Equal((nuint)10, session.ServerHandle);
        Assert.Null(session.BusId);

        var beforeRetry = native.Calls.Count;
        Assert.True(session.RetryPendingCleanup());
        Assert.Equal(["CloseUSBServer"], native.Calls.Skip(beforeRetry));
        Assert.Equal(CanonicalSteamControllerSessionState.Clean, session.State);
    }

    [Fact]
    public void Bus_failure_retains_bus_and_retries_bus_then_server()
    {
        var native = new FakeNative { RemoveBusResults = new Queue<bool>([false, true]) };
        using var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());
        Assert.True(session.RemoveDevice());

        Assert.False(session.CompleteRuntimeCleanup());
        Assert.Equal(CanonicalPendingCleanupPhase.BusRemoval, session.PendingCleanupPhase);
        Assert.Equal((uint)42, session.BusId);
        Assert.Equal((nuint)10, session.ServerHandle);
        Assert.False(session.Start());

        Assert.True(session.RetryPendingCleanup());
        Assert.Equal(CanonicalSteamControllerSessionState.Clean, session.State);
        Assert.Equal(["RemoveUSBBus", "RemoveUSBBus", "CloseUSBServer"], native.Calls.Where(x => x is "RemoveUSBBus" or "CloseUSBServer"));
    }

    [Fact]
    public void Known_remove_failure_retains_device_and_allows_only_explicit_retry()
    {
        var native = new FakeNative { RemoveDeviceResults = new Queue<SteamControllerDeviceRemoveResult>([
            SteamControllerDeviceRemoveResult.RetryableFailure,
            SteamControllerDeviceRemoveResult.Success]) };
        using var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());

        Assert.False(session.RemoveDevice());
        Assert.Equal(CanonicalPendingCleanupPhase.DeviceRemoval, session.PendingCleanupPhase);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.False(session.Start());
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamControllerDeviceEx"));

        Assert.True(session.RetryPendingCleanup());
        Assert.Equal((nuint)0, session.DeviceHandle);
        Assert.Equal(CanonicalSteamControllerSessionState.DeviceRemoved, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
    }

    [Fact]
    public void Unsafe_or_invalid_remove_fails_closed_without_retry()
    {
        foreach (var result in new[]
        {
            SteamControllerDeviceRemoveResult.UnsafeOutcomeUnknown,
            SteamControllerDeviceRemoveResult.Invalid
        })
        {
            var native = new FakeNative { RemoveDeviceResults = new Queue<SteamControllerDeviceRemoveResult>([result]) };
            using var session = new CanonicalSteamControllerSession(native);
            Assert.True(session.Start());

            Assert.False(session.RemoveDevice());
            Assert.Equal(CanonicalSteamControllerSessionState.Unsafe, session.State);
            Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
            Assert.Equal((nuint)20, session.DeviceHandle);
            Assert.False(session.RetryPendingCleanup());
            Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamControllerDeviceEx"));
        }
    }

    [Fact]
    public void DeviceRemoved_cannot_enter_bus_cleanup_through_retry()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());
        Assert.True(session.RemoveDevice());

        Assert.False(session.RetryPendingCleanup());
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.Equal(CanonicalSteamControllerSessionState.DeviceRemoved, session.State);

        Assert.True(session.CompleteRuntimeCleanup());
        Assert.Contains("RemoveUSBBus", native.Calls);
    }

    [Fact]
    public void Identity_failure_removes_unattached_device_with_classified_result()
    {
        var native = new FakeNative { IdentityResult = false };
        using var session = new CanonicalSteamControllerSession(native);

        Assert.False(session.Start());
        Assert.Contains("RemoveSteamControllerDeviceEx", native.Calls);
        Assert.Contains("RemoveUSBBus", native.Calls);
        Assert.Contains("CloseUSBServer", native.Calls);
        Assert.Equal(CanonicalSteamControllerSessionState.Clean, session.State);
    }

    [Fact]
    public void Identity_bus_mismatch_uses_classified_remove_and_preserves_unsafe_evidence()
    {
        var native = new FakeNative
        {
            IdentityBusId = 99,
            RemoveDeviceResults = new Queue<SteamControllerDeviceRemoveResult>([
                SteamControllerDeviceRemoveResult.UnsafeOutcomeUnknown])
        };
        using var session = new CanonicalSteamControllerSession(native);

        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamControllerSessionState.Unsafe, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Pre_attach_retryable_remove_retains_device_cleanup_pending()
    {
        var native = new FakeNative
        {
            IdentityResult = false,
            RemoveDeviceResults = new Queue<SteamControllerDeviceRemoveResult>([
                SteamControllerDeviceRemoveResult.RetryableFailure])
        };
        using var session = new CanonicalSteamControllerSession(native);

        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamControllerSessionState.CleanupPending, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.DeviceRemoval, session.PendingCleanupPhase);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Pre_attach_creation_failures_clean_only_acquired_resources()
    {
        var newServerFailure = new FakeNative { NewServerResult = false };
        using (var session = new CanonicalSteamControllerSession(newServerFailure))
        {
            Assert.False(session.Start());
            Assert.DoesNotContain("CloseUSBServer", newServerFailure.Calls);
        }

        var busFailure = new FakeNative { CreateBusResult = false };
        using (var session = new CanonicalSteamControllerSession(busFailure))
        {
            Assert.False(session.Start());
            Assert.Equal(["NewUSBServer", "CreateUSBBus", "CloseUSBServer"], busFailure.Calls);
        }

        var deviceFailure = new FakeNative { CreateDeviceResult = false };
        using (var session = new CanonicalSteamControllerSession(deviceFailure))
        {
            Assert.False(session.Start());
            Assert.Equal(["RemoveUSBBus", "CloseUSBServer"], deviceFailure.Calls.Where(x => x is "RemoveUSBBus" or "CloseUSBServer"));
        }
    }

    [Fact]
    public void Create_bus_failure_retains_server_when_initial_close_fails()
    {
        var native = new FakeNative
        {
            CreateBusResult = false,
            CloseResults = new Queue<bool>([false, true])
        };
        using var session = new CanonicalSteamControllerSession(native);

        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamControllerSessionState.CleanupPending, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.ServerClose, session.PendingCleanupPhase);
        Assert.Equal((nuint)10, session.ServerHandle);

        var beforeRetry = native.Calls.Count;
        Assert.True(session.RetryPendingCleanup());
        Assert.Equal(["CloseUSBServer"], native.Calls.Skip(beforeRetry));
        Assert.Equal(CanonicalSteamControllerSessionState.Clean, session.State);
    }

    [Fact]
    public void Create_device_failure_retains_bus_when_initial_bus_removal_fails()
    {
        var native = new FakeNative
        {
            CreateDeviceResult = false,
            RemoveBusResults = new Queue<bool>([false, true])
        };
        using var session = new CanonicalSteamControllerSession(native);

        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamControllerSessionState.CleanupPending, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.BusRemoval, session.PendingCleanupPhase);
        Assert.Equal((uint)42, session.BusId);
        Assert.Equal((nuint)10, session.ServerHandle);

        var beforeRetry = native.Calls.Count;
        Assert.True(session.RetryPendingCleanup());
        Assert.Equal(["RemoveUSBBus", "CloseUSBServer"], native.Calls.Skip(beforeRetry));
        Assert.Equal(CanonicalSteamControllerSessionState.Clean, session.State);
    }

    [Fact]
    public void Attach_failure_is_unsafe_and_does_not_run_destructive_cleanup()
    {
        var native = new FakeNative { AttachResult = false };
        using var session = new CanonicalSteamControllerSession(native);

        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamControllerSessionState.Unsafe, session.State);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.DoesNotContain("DetachUSBDevice", native.Calls);
        Assert.DoesNotContain("RemoveSteamControllerDevice", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
        Assert.False(session.Start());
    }

    [Fact]
    public void Dispose_is_non_destructive_and_preserves_state()
    {
        var native = new FakeNative();
        var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());
        session.Dispose();

        Assert.Equal(CanonicalSteamControllerSessionState.Active, session.State);
        Assert.Equal((nuint)10, session.ServerHandle);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.DoesNotContain("RemoveSteamControllerDevice", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    private sealed class FakeNative : ICanonicalViiperNativeApi
    {
        internal readonly List<string> Calls = [];
        internal readonly List<SteamControllerDeviceState> States = [];
        internal Queue<SteamControllerDeviceRemoveResult> RemoveDeviceResults { get; init; } = new([SteamControllerDeviceRemoveResult.Success]);
        internal Queue<bool> RemoveBusResults { get; init; } = new([true]);
        internal Queue<bool> CloseResults { get; init; } = new([true]);
        internal bool NewServerResult { get; init; } = true;
        internal bool CreateBusResult { get; init; } = true;
        internal bool CreateDeviceResult { get; init; } = true;
        internal bool IdentityResult { get; init; } = true;
        internal uint IdentityBusId { get; init; } = 42;
        internal bool AttachResult { get; init; } = true;
        internal string? Address { get; private set; }
        internal USBServerConfig Config { get; private set; }
        internal uint BusInput { get; private set; }
        internal bool AutoAttach { get; private set; }
        internal ushort Vendor { get; private set; }
        internal ushort Product { get; private set; }
        internal ViiperLogCallback? LogCallback { get; private set; }

        public bool NewUSBServer(ref USBServerConfig config, out nuint serverHandle, ViiperLogCallback? logCallback = null)
        {
            Calls.Add("NewUSBServer");
            Config = config;
            Address = Marshal.PtrToStringUTF8(config.Addr);
            LogCallback = logCallback;
            serverHandle = 10;
            return NewServerResult;
        }

        public bool CloseUSBServer(nuint serverHandle) { Calls.Add("CloseUSBServer"); return CloseResults.Count == 0 || CloseResults.Dequeue(); }
        public bool CreateUSBBus(nuint serverHandle, ref uint busId) { Calls.Add("CreateUSBBus"); BusInput = busId; busId = 42; return CreateBusResult; }
        public bool RemoveUSBBus(nuint serverHandle, uint busId) { Calls.Add("RemoveUSBBus"); return RemoveBusResults.Count == 0 || RemoveBusResults.Dequeue(); }
        public bool GetUSBDeviceIdentity(nuint deviceHandle, out uint busId, out uint deviceId) { Calls.Add("GetUSBDeviceIdentity"); busId = IdentityBusId; deviceId = 9; return IdentityResult; }
        public bool AttachUSBDevice(nuint deviceHandle) { Calls.Add("AttachUSBDevice"); return AttachResult; }
        public bool DetachUSBDevice(nuint deviceHandle) { Calls.Add("DetachUSBDevice"); return true; }
        public bool CreateSteamControllerDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct)
        { Calls.Add("CreateSteamControllerDevice"); deviceHandle = 20; AutoAttach = autoAttachLocalhost; Vendor = idVendor; Product = idProduct; return CreateDeviceResult; }
        public bool SetSteamControllerDeviceState(nuint deviceHandle, SteamControllerDeviceState state) { Calls.Add("SetSteamControllerDeviceState"); States.Add(state); return true; }
        public bool SetSteamControllerOutputCallback(nuint deviceHandle, SteamControllerOutputCallback? callback) { Calls.Add("SetSteamControllerOutputCallback"); return true; }
        public bool RemoveSteamControllerDevice(nuint deviceHandle) { Calls.Add("RemoveSteamControllerDevice"); return true; }
        public SteamControllerDeviceRemoveResult RemoveSteamControllerDeviceEx(nuint deviceHandle)
        { Calls.Add("RemoveSteamControllerDeviceEx"); return RemoveDeviceResults.Count == 0 ? SteamControllerDeviceRemoveResult.Success : RemoveDeviceResults.Dequeue(); }
    }
}
