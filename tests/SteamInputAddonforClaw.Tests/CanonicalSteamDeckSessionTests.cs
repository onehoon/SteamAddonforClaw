using System.Runtime.InteropServices;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Lifecycle invariants for <see cref="CanonicalSteamDeckSession"/>: classified remove, fail-closed
/// unsafe/invalid, caller-owned bus preserved across device removal, non-destructive Dispose.
/// </summary>
public sealed class CanonicalSteamDeckSessionTests
{
    [Fact]
    public void Starts_in_canonical_order_with_default_identity()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamDeckSession(native);

        Assert.True(session.Start());

        Assert.Equal(["NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "AttachUSBDevice"], native.Calls);
        Assert.Equal("127.0.0.1:3242", native.Address);
        Assert.False(native.AutoAttach);
        // Zero VID/PID lets VIIPER apply its own canonical Steam Deck default (28DE:1205) rather
        // than the Addon re-encoding the identity constant at this call site.
        Assert.Equal((ushort)0, native.Vendor);
        Assert.Equal((ushort)0, native.Product);
        Assert.Equal(CanonicalSteamDeckSessionState.Active, session.State);
        Assert.Equal((uint)42, session.BusId);
        Assert.Equal((uint)9, session.LogicalDeviceId);
    }

    [Fact]
    public void Typed_state_and_neutral_are_active_only_and_start_does_not_send_neutral()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());
        Assert.Empty(native.States);

        var state = new SteamDeckDeviceState { A = 1 };
        Assert.True(session.SetState(state));
        Assert.True(session.SetNeutral());
        Assert.Equal([state, default], native.States);

        Assert.True(session.RemoveDevice());
        Assert.False(session.SetState(state));
    }

    [Fact]
    public void Output_callback_registration_and_clear_use_the_authoritative_device_handle()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());
        SteamDeckOutputCallback callback = (_, _, _) => { };

        Assert.True(session.SetOutputCallback(callback));
        Assert.True(session.ClearOutputCallback());
        Assert.Equal([((nuint)20, true), ((nuint)20, false)], native.Callbacks);
    }

    [Fact]
    public void Output_callback_wrapper_rejects_invalid_states_and_propagates_native_failures()
    {
        var beforeStartNative = new FakeNative();
        using var beforeStart = new CanonicalSteamDeckSession(beforeStartNative);
        SteamDeckOutputCallback callback = (_, _, _) => { };
        Assert.False(beforeStart.SetOutputCallback(callback));
        Assert.False(beforeStart.ClearOutputCallback());

        var native = new FakeNative { RegistrationResult = false, ClearResult = false };
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());
        Assert.False(session.SetOutputCallback(callback));
        Assert.False(session.ClearOutputCallback());
        Assert.Equal(CanonicalSteamDeckSessionState.Active, session.State);
        Assert.Equal((uint)9, session.LogicalDeviceId);

        native.RegistrationResult = true;
        native.ClearResult = true;
        Assert.True(session.SetOutputCallback(callback));
        Assert.True(session.ClearOutputCallback());
        Assert.True(session.RemoveDevice());
        Assert.False(session.SetOutputCallback(callback));
        Assert.False(session.ClearOutputCallback());
        Assert.Equal((nuint)0, session.DeviceHandle);
    }

    [Fact]
    public void Known_remove_failure_retains_device_and_allows_only_explicit_retry()
    {
        var native = new FakeNative
        {
            RemoveDeviceResults = new Queue<SteamDeckDeviceRemoveResult>(
            [
                SteamDeckDeviceRemoveResult.RetryableFailure,
                SteamDeckDeviceRemoveResult.Success
            ])
        };
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());

        Assert.False(session.RemoveDevice());
        Assert.Equal(CanonicalPendingCleanupPhase.DeviceRemoval, session.PendingCleanupPhase);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamDeckDeviceEx"));

        Assert.True(session.RetryPendingCleanup());
        Assert.Equal((nuint)0, session.DeviceHandle);
        Assert.Equal(CanonicalSteamDeckSessionState.DeviceRemoved, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
    }

    [Fact]
    public void Unsafe_or_invalid_remove_fails_closed_without_retry()
    {
        foreach (var result in new[]
        {
            SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown,
            SteamDeckDeviceRemoveResult.Invalid
        })
        {
            var native = new FakeNative { RemoveDeviceResults = new Queue<SteamDeckDeviceRemoveResult>([result]) };
            using var session = new CanonicalSteamDeckSession(native);
            Assert.True(session.Start());

            Assert.False(session.RemoveDevice());
            Assert.Equal(CanonicalSteamDeckSessionState.Unsafe, session.State);
            Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
            Assert.Equal((nuint)20, session.DeviceHandle);
            Assert.False(session.RetryPendingCleanup());
            Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamDeckDeviceEx"));
        }
    }

    [Fact]
    public void Successful_remove_leaves_the_caller_owned_bus_alive()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());

        Assert.True(session.RemoveDevice());
        Assert.Equal(CanonicalSteamDeckSessionState.DeviceRemoved, session.State);
        Assert.Equal((uint)42, session.BusId);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.True(session.CompleteRuntimeCleanup());
        Assert.Contains("RemoveUSBBus", native.Calls);
        Assert.Contains("CloseUSBServer", native.Calls);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
    }

    [Fact]
    public void Identity_failure_removes_unattached_device_with_classified_result()
    {
        var native = new FakeNative { IdentityResult = false };
        using var session = new CanonicalSteamDeckSession(native);

        Assert.False(session.Start());
        Assert.Contains("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.Contains("RemoveUSBBus", native.Calls);
        Assert.Contains("CloseUSBServer", native.Calls);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
    }

    [Fact]
    public void Attach_failure_is_unsafe_and_does_not_run_destructive_cleanup()
    {
        var native = new FakeNative { AttachResult = false };
        using var session = new CanonicalSteamDeckSession(native);

        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamDeckSessionState.Unsafe, session.State);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.DoesNotContain("DetachUSBDevice", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDevice", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
        Assert.False(session.Start());
    }

    [Fact]
    public void Stale_zero_handle_fails_safely_without_calling_native_setstate()
    {
        var native = new FakeNative();
        using var session = new CanonicalSteamDeckSession(native);
        // Never Start()ed: device handle stays zero/clean, SetState must fail closed rather than
        // reach the native layer with a stale/zero handle.
        Assert.False(session.SetState(default));
        Assert.DoesNotContain("SetSteamDeckDeviceState", native.Calls);
    }

    [Fact]
    public void Dispose_is_non_destructive_and_preserves_state()
    {
        var native = new FakeNative();
        var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());
        session.Dispose();

        Assert.Equal(CanonicalSteamDeckSessionState.Active, session.State);
        Assert.Equal((nuint)10, session.ServerHandle);
        Assert.Equal((nuint)20, session.DeviceHandle);
        Assert.DoesNotContain("RemoveSteamDeckDevice", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void NewUSBServer_failure_fails_closed_without_further_calls()
    {
        var native = new FakeNative { NewServerResult = false };
        using var session = new CanonicalSteamDeckSession(native);

        Assert.False(session.Start());
        Assert.Equal(["NewUSBServer"], native.Calls);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
    }

    [Fact]
    public void CreateUSBBus_failure_closes_the_server_it_already_opened()
    {
        var native = new FakeNative { CreateBusResult = false };
        using var session = new CanonicalSteamDeckSession(native);

        Assert.False(session.Start());
        Assert.Equal(["NewUSBServer", "CreateUSBBus", "CloseUSBServer"], native.Calls);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
    }

    [Fact]
    public void CreateSteamDeckDevice_failure_tears_down_the_bus_and_server_it_already_opened()
    {
        var native = new FakeNative { CreateDeviceResult = false };
        using var session = new CanonicalSteamDeckSession(native);

        Assert.False(session.Start());
        Assert.Equal(["NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "RemoveUSBBus", "CloseUSBServer"], native.Calls);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
    }

    [Fact]
    public void Identity_BusId_mismatch_is_treated_as_an_identity_failure_and_removes_the_unattached_device()
    {
        var native = new FakeNative { IdentityBusId = 999 }; // does not match the bus this session created (42)
        using var session = new CanonicalSteamDeckSession(native);

        Assert.False(session.Start());
        Assert.Contains("GetUSBDeviceIdentity", native.Calls);
        Assert.Contains("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.Contains("RemoveUSBBus", native.Calls);
        Assert.Contains("CloseUSBServer", native.Calls);
        Assert.DoesNotContain("AttachUSBDevice", native.Calls);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
    }

    [Fact]
    public void Bus_removal_first_attempt_failure_then_retry_succeeds()
    {
        var native = new FakeNative { RemoveBusResults = new Queue<bool>([false, true]) };
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());
        Assert.True(session.RemoveDevice());

        Assert.False(session.CompleteRuntimeCleanup());
        Assert.Equal(CanonicalSteamDeckSessionState.CleanupPending, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.BusRemoval, session.PendingCleanupPhase);
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveUSBBus"));

        Assert.True(session.RetryPendingCleanup());
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
        Assert.Equal(2, native.Calls.Count(x => x == "RemoveUSBBus"));
        // Retry must not re-invoke device removal a second time -- the device was already removed
        // before the bus-removal phase began.
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamDeckDeviceEx"));
    }

    [Fact]
    public void Server_close_first_attempt_failure_then_retry_succeeds()
    {
        var native = new FakeNative { CloseResults = new Queue<bool>([false, true]) };
        using var session = new CanonicalSteamDeckSession(native);
        Assert.True(session.Start());
        Assert.True(session.RemoveDevice());

        Assert.False(session.CompleteRuntimeCleanup());
        Assert.Equal(CanonicalSteamDeckSessionState.CleanupPending, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.ServerClose, session.PendingCleanupPhase);
        Assert.Equal(1, native.Calls.Count(x => x == "CloseUSBServer"));
        // Bus removal already succeeded and must not be repeated on retry.
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveUSBBus"));

        Assert.True(session.RetryPendingCleanup());
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
        Assert.Equal(2, native.Calls.Count(x => x == "CloseUSBServer"));
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveUSBBus"));
        Assert.Equal(1, native.Calls.Count(x => x == "RemoveSteamDeckDeviceEx"));
    }

    private sealed class FakeNative : ICanonicalViiperNativeApi
    {
        internal readonly List<string> Calls = [];
        internal readonly List<SteamDeckDeviceState> States = [];
        internal Queue<SteamDeckDeviceRemoveResult> RemoveDeviceResults { get; init; } = new([SteamDeckDeviceRemoveResult.Success]);
        internal Queue<bool> RemoveBusResults { get; init; } = new([true]);
        internal Queue<bool> CloseResults { get; init; } = new([true]);
        internal bool NewServerResult { get; init; } = true;
        internal bool CreateBusResult { get; init; } = true;
        internal bool CreateDeviceResult { get; init; } = true;
        internal bool IdentityResult { get; init; } = true;
        internal uint IdentityBusId { get; init; } = 42;
        internal bool AttachResult { get; init; } = true;
        internal bool RegistrationResult { get; set; } = true;
        internal bool ClearResult { get; set; } = true;
        internal List<(nuint Handle, bool Registered)> Callbacks { get; } = [];
        internal string? Address { get; private set; }
        internal bool AutoAttach { get; private set; }
        internal ushort Vendor { get; private set; }
        internal ushort Product { get; private set; }

        public bool NewUSBServer(ref USBServerConfig config, out nuint serverHandle, ViiperLogCallback? logCallback = null)
        {
            Calls.Add("NewUSBServer");
            Address = Marshal.PtrToStringUTF8(config.Addr);
            serverHandle = 10;
            return NewServerResult;
        }

        public bool CloseUSBServer(nuint serverHandle) { Calls.Add("CloseUSBServer"); return CloseResults.Count == 0 || CloseResults.Dequeue(); }
        public bool CreateUSBBus(nuint serverHandle, ref uint busId) { Calls.Add("CreateUSBBus"); busId = 42; return CreateBusResult; }
        public bool RemoveUSBBus(nuint serverHandle, uint busId) { Calls.Add("RemoveUSBBus"); return RemoveBusResults.Count == 0 || RemoveBusResults.Dequeue(); }
        public bool GetUSBDeviceIdentity(nuint deviceHandle, out uint busId, out uint deviceId) { Calls.Add("GetUSBDeviceIdentity"); busId = IdentityBusId; deviceId = 9; return IdentityResult; }
        public bool AttachUSBDevice(nuint deviceHandle) { Calls.Add("AttachUSBDevice"); return AttachResult; }
        public bool DetachUSBDevice(nuint deviceHandle) { Calls.Add("DetachUSBDevice"); return true; }

        public bool CreateSteamDeckDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct)
        { Calls.Add("CreateSteamDeckDevice"); deviceHandle = 20; AutoAttach = autoAttachLocalhost; Vendor = idVendor; Product = idProduct; return CreateDeviceResult; }
        public bool SetSteamDeckDeviceState(nuint deviceHandle, SteamDeckDeviceState state) { Calls.Add("SetSteamDeckDeviceState"); States.Add(state); return true; }
        public bool SetSteamDeckOutputCallback(nuint deviceHandle, SteamDeckOutputCallback? callback) { Calls.Add("SetSteamDeckOutputCallback"); Callbacks.Add((deviceHandle, callback is not null)); return callback is null ? ClearResult : RegistrationResult; }
        public bool RemoveSteamDeckDevice(nuint deviceHandle) { Calls.Add("RemoveSteamDeckDevice"); return true; }
        public SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint deviceHandle)
        { Calls.Add("RemoveSteamDeckDeviceEx"); return RemoveDeviceResults.Count == 0 ? SteamDeckDeviceRemoveResult.Success : RemoveDeviceResults.Dequeue(); }
    }
}
