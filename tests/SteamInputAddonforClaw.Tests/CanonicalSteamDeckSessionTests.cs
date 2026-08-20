using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalSteamDeckSessionTests
{
    [Fact]
    public void Two_routes_reuse_the_exact_same_persistent_handles()
    {
        var native = new FakeNative();
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Ready, runtime.State);
        var expected = (runtime.ServerHandle, runtime.BusId, runtime.DeckDeviceHandle, runtime.DeckLogicalDeviceId);
        using (var first = new CanonicalSteamDeckSession(runtime)) { Assert.True(first.Start()); Assert.True(first.RemoveDevice()); }
        using (var second = new CanonicalSteamDeckSession(runtime)) { Assert.True(second.Start()); Assert.True(second.RemoveDevice()); }
        Assert.Equal(expected, (runtime.ServerHandle, runtime.BusId, runtime.DeckDeviceHandle, runtime.DeckLogicalDeviceId));
        Assert.Equal(2, native.Calls.Count(x => x == "AttachUSBDeviceEx"));
        Assert.Equal(2, native.Calls.Count(x => x == "DetachUSBDeviceEx"));
        Assert.Equal(1, native.Calls.Count(x => x == "CreateSteamDeckDevice"));
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
    }

    [Fact]
    public void Unexpected_pre_attach_state_fails_closed_without_detach_or_remove()
    {
        var native = new FakeNative { AttachmentState = USBDeviceAttachmentState.Attached };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.Null(runtime);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);
        Assert.Contains("RemoveSteamDeckDeviceEx", native.Calls);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void Route_attachment_evidence_poisoning_marks_the_persistent_owner_unsafe(int routeStateValue)
    {
        var native = new FakeNative();
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.NotNull(runtime);
        native.AttachmentStates.Enqueue((USBDeviceAttachmentState)routeStateValue);
        using var session = new CanonicalSteamDeckSession(runtime);
        Assert.False(session.Start());
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
    }

    [Fact]
    public void Route_attachment_query_failure_poisoning_marks_the_persistent_owner_unsafe()
    {
        var native = new FakeNative();
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.NotNull(runtime);
        native.AttachmentQueryResults.Enqueue(false);
        using var session = new CanonicalSteamDeckSession(runtime);
        Assert.False(session.Start());
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);
    }

    [Fact]
    public void Retryable_attach_does_not_create_a_detach_cleanup_obligation()
    {
        var native = new FakeNative();
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.NotNull(runtime);
        native.AttachResults.Enqueue(USBDeviceAttachResult.RetryableFailure);
        using var session = new CanonicalSteamDeckSession(runtime);
        Assert.False(session.Start());
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
        Assert.Equal(CanonicalPendingCleanupPhase.None, session.PendingCleanupPhase);
        Assert.False(session.RetryPendingCleanup());
    }

    private sealed class FakeNative : ICanonicalViiperNativeApi
    {
        internal readonly List<string> Calls = [];
        internal USBDeviceAttachmentState AttachmentState { get; init; } = USBDeviceAttachmentState.Detached;
        internal Queue<USBDeviceAttachmentState> AttachmentStates { get; } = [];
        internal Queue<bool> AttachmentQueryResults { get; } = [];
        internal Queue<USBDeviceAttachResult> AttachResults { get; } = [];
        public bool NewUSBServer(ref USBServerConfig config, out nuint handle, ViiperLogCallback? callback = null) { Calls.Add("NewUSBServer"); handle = 10; return true; }
        public bool CloseUSBServer(nuint handle) { Calls.Add("CloseUSBServer"); return true; }
        public bool CreateUSBBus(nuint handle, ref uint bus) { Calls.Add("CreateUSBBus"); bus = 42; return true; }
        public bool RemoveUSBBus(nuint handle, uint bus) { Calls.Add("RemoveUSBBus"); return true; }
        public bool GetUSBDeviceIdentity(nuint handle, out uint bus, out uint id) { Calls.Add("GetUSBDeviceIdentity"); bus = 42; id = handle == 20 ? 9u : 10u; return true; }
        public bool AttachUSBDevice(nuint handle) => throw new NotSupportedException();
        public bool DetachUSBDevice(nuint handle) => throw new NotSupportedException();
        public USBDeviceAttachResult AttachUSBDeviceEx(nuint handle) { Calls.Add("AttachUSBDeviceEx"); return AttachResults.Count > 0 ? AttachResults.Dequeue() : USBDeviceAttachResult.Success; }
        public USBDeviceDetachResult DetachUSBDeviceEx(nuint handle) { Calls.Add("DetachUSBDeviceEx"); return USBDeviceDetachResult.Success; }
        public bool GetUSBDeviceAttachmentState(nuint handle, out USBDeviceAttachmentState state) { Calls.Add("GetUSBDeviceAttachmentState"); state = AttachmentStates.Count > 0 ? AttachmentStates.Dequeue() : AttachmentState; return AttachmentQueryResults.Count == 0 || AttachmentQueryResults.Dequeue(); }
        public bool CreateSteamDeckDevice(nuint server, out nuint handle, uint bus, bool autoAttach, ushort vid, ushort pid) { Calls.Add("CreateSteamDeckDevice"); handle = 20; return true; }
        public bool SetSteamDeckDeviceState(nuint handle, SteamDeckDeviceState state) { Calls.Add("SetSteamDeckDeviceState"); return true; }
        public bool SetSteamDeckOutputCallback(nuint handle, SteamDeckOutputCallback? callback) { Calls.Add("SetSteamDeckOutputCallback"); return true; }
        public bool RemoveSteamDeckDevice(nuint handle) => throw new NotSupportedException();
        public SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint handle) { Calls.Add("RemoveSteamDeckDeviceEx"); return SteamDeckDeviceRemoveResult.Success; }
        public bool CreateXbox360Device(nuint server, out nuint handle, uint bus, bool autoAttach, ushort vid, ushort pid, byte subtype) { Calls.Add("CreateXbox360Device"); handle = 30; return true; }
        public bool SetXbox360DeviceState(nuint handle, Xbox360DeviceState state) => true;
        public bool RemoveXbox360Device(nuint handle) { Calls.Add("RemoveXbox360Device"); return true; }
        public Xbox360DeviceRemoveResult RemoveXbox360DeviceEx(nuint handle) { Calls.Add("RemoveXbox360DeviceEx"); return Xbox360DeviceRemoveResult.Success; }
    }
}
