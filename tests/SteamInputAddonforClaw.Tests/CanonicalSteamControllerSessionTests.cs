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
        var native = new FakeNative { RemoveDeviceResults = new Queue<bool>([false, true]) };
        using var session = new CanonicalSteamControllerSession(native);
        Assert.True(session.Start());

        Assert.False(session.RemoveDevice());
        Assert.Equal(CanonicalPendingCleanupPhase.DeviceRemoval, session.PendingCleanupPhase);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.False(session.Start());
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamControllerDevice"));

        Assert.True(session.RetryPendingCleanup());
        Assert.Equal((nuint)0, session.DeviceHandle);
        Assert.Equal(CanonicalSteamControllerSessionState.DeviceRemoved, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
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
        internal Queue<bool> RemoveDeviceResults { get; init; } = new([true]);
        internal Queue<bool> RemoveBusResults { get; init; } = new([true]);
        internal Queue<bool> CloseResults { get; init; } = new([true]);
        internal bool AttachResult { get; init; } = true;
        internal string? Address { get; private set; }
        internal USBServerConfig Config { get; private set; }
        internal uint BusInput { get; private set; }
        internal bool AutoAttach { get; private set; }
        internal ushort Vendor { get; private set; }
        internal ushort Product { get; private set; }

        public bool NewUSBServer(ref USBServerConfig config, out nuint serverHandle, ViiperLogCallback? logCallback = null)
        {
            Calls.Add("NewUSBServer");
            Config = config;
            Address = Marshal.PtrToStringUTF8(config.Addr);
            serverHandle = 10;
            return true;
        }

        public bool CloseUSBServer(nuint serverHandle) { Calls.Add("CloseUSBServer"); return CloseResults.Count == 0 || CloseResults.Dequeue(); }
        public bool CreateUSBBus(nuint serverHandle, ref uint busId) { Calls.Add("CreateUSBBus"); BusInput = busId; busId = 42; return true; }
        public bool RemoveUSBBus(nuint serverHandle, uint busId) { Calls.Add("RemoveUSBBus"); return RemoveBusResults.Count == 0 || RemoveBusResults.Dequeue(); }
        public bool GetUSBDeviceIdentity(nuint deviceHandle, out uint busId, out uint deviceId) { Calls.Add("GetUSBDeviceIdentity"); busId = 42; deviceId = 9; return true; }
        public bool AttachUSBDevice(nuint deviceHandle) { Calls.Add("AttachUSBDevice"); return AttachResult; }
        public bool DetachUSBDevice(nuint deviceHandle) { Calls.Add("DetachUSBDevice"); return true; }
        public bool CreateSteamControllerDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct)
        { Calls.Add("CreateSteamControllerDevice"); deviceHandle = 20; AutoAttach = autoAttachLocalhost; Vendor = idVendor; Product = idProduct; return true; }
        public bool SetSteamControllerDeviceState(nuint deviceHandle, SteamControllerDeviceState state) { Calls.Add("SetSteamControllerDeviceState"); States.Add(state); return true; }
        public bool SetSteamControllerOutputCallback(nuint deviceHandle, SteamControllerOutputCallback? callback) { Calls.Add("SetSteamControllerOutputCallback"); return true; }
        public bool RemoveSteamControllerDevice(nuint deviceHandle) { Calls.Add("RemoveSteamControllerDevice"); return RemoveDeviceResults.Count == 0 || RemoveDeviceResults.Dequeue(); }
    }
}
