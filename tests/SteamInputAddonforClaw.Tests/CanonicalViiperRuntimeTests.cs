using System.Runtime.InteropServices;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// PR2a: <see cref="CanonicalViiperRuntime"/> is foundation-only in this PR -- fully implemented
/// and tested here, but not yet wired into any production composition path. Production still uses
/// <see cref="CanonicalSteamDeckSession"/>'s own per-route server/bus/device creation unchanged
/// (see <see cref="CanonicalSteamDeckSessionTests"/>). These tests cover the persistent-runtime
/// initialization staged order/failure-unwind (work order sections 2, 26, 28) and final teardown
/// classified ordering (work order sections 19-21, 32).
/// </summary>
public sealed class CanonicalViiperRuntimeTests
{
    private const string LoopbackAddress = "127.0.0.1:3242";

    [Fact]
    public void Initialize_happy_path_creates_deck_and_xbox360_detached_on_the_same_bus()
    {
        var native = new FakeNative();

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(
        [
            "NewUSBServer",
            "CreateUSBBus",
            "CreateSteamDeckDevice",
            "GetUSBDeviceIdentity",
            "GetUSBDeviceAttachmentState",
            "CreateXbox360Device",
            "GetUSBDeviceIdentity",
            "GetUSBDeviceAttachmentState"
        ], native.Calls);

        Assert.Equal(CanonicalViiperRuntimeState.Ready, runtime!.State);
        Assert.Equal((nuint)10, runtime.ServerHandle);
        Assert.Equal((uint)42, runtime.BusId);
        Assert.Equal((nuint)20, runtime.DeckDeviceHandle);
        Assert.Equal((uint)9, runtime.DeckLogicalDeviceId);
        Assert.Equal((nuint)30, runtime.Xbox360DeviceHandle);
        Assert.Equal((uint)11, runtime.Xbox360LogicalDeviceId);

        // Both devices created detached (autoAttachLocalhost = false); no Attach call anywhere.
        Assert.False(native.DeckAutoAttach);
        Assert.False(native.Xbox360AutoAttach);
        Assert.DoesNotContain("AttachUSBDevice", native.Calls);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);

        // Minor fix: the Addon does not invent an Xbox360 identity -- it forwards zero
        // VID/PID/subtype so VIIPER supplies its own canonical default (045E:028E, subtype 1),
        // exactly like the existing Deck path already does.
        Assert.Equal((ushort)0, native.Xbox360CreateVendorId);
        Assert.Equal((ushort)0, native.Xbox360CreateProductId);
        Assert.Equal((byte)0, native.Xbox360CreateXInputSubType);
    }

    [Fact]
    public void NewUSBServer_failure_leaves_nothing_to_clean_up()
    {
        var native = new FakeNative { NewServerResult = false };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(["NewUSBServer"], native.Calls);
    }

    [Fact]
    public void CreateUSBBus_failure_closes_the_server_it_already_opened()
    {
        var native = new FakeNative { CreateBusResult = false };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(["NewUSBServer", "CreateUSBBus", "CloseUSBServer"], native.Calls);
    }

    [Fact]
    public void Deck_create_failure_tears_down_the_bus_and_server()
    {
        var native = new FakeNative { CreateDeckResult = false };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(["NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "RemoveUSBBus", "CloseUSBServer"], native.Calls);
    }

    [Fact]
    public void Deck_identity_failure_removes_the_unattached_deck_then_bus_and_server()
    {
        var native = new FakeNative { DeckIdentityBusId = 999 }; // mismatches the bus this init created (42)

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity",
            "RemoveSteamDeckDeviceEx", "RemoveUSBBus", "CloseUSBServer"
        ], native.Calls);
    }

    [Fact]
    public void Deck_initial_attachment_state_attached_neutral_writes_detaches_then_removes_deck_then_bus_and_server()
    {
        var native = new FakeNative { DeckInitialAttachmentState = USBDeviceAttachmentState.Attached };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState",
            "SetSteamDeckDeviceState", "DetachUSBDeviceEx", "RemoveSteamDeckDeviceEx", "RemoveUSBBus", "CloseUSBServer"
        ], native.Calls);
    }

    [Fact]
    public void Deck_initial_attachment_state_outcome_unknown_stops_with_no_destructive_calls_and_retains_owner()
    {
        var native = new FakeNative { DeckInitialAttachmentState = USBDeviceAttachmentState.OutcomeUnknown };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState"
        ], native.Calls);
        Assert.DoesNotContain("SetSteamDeckDeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Deck_attachment_state_query_failure_stops_with_no_destructive_calls_and_retains_owner()
    {
        var native = new FakeNative { DeckAttachmentStateQuerySucceeds = false };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.DoesNotContain("SetSteamDeckDeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Xbox360_create_failure_removes_only_the_deck_then_bus_and_server()
    {
        var native = new FakeNative { CreateXbox360Result = false };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState",
            "CreateXbox360Device", "RemoveSteamDeckDeviceEx", "RemoveUSBBus", "CloseUSBServer"
        ], native.Calls);
    }

    [Fact]
    public void Xbox360_identity_failure_removes_xbox360_then_deck_then_bus_and_server()
    {
        var native = new FakeNative { Xbox360IdentityBusId = 999 };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState",
            "CreateXbox360Device", "GetUSBDeviceIdentity", "RemoveXbox360DeviceEx", "RemoveSteamDeckDeviceEx", "RemoveUSBBus", "CloseUSBServer"
        ], native.Calls);
    }

    [Fact]
    public async Task Xbox360_identity_failure_with_retryable_xbox_remove_retries_then_removes_deck_before_bus()
    {
        var native = new FakeNative
        {
            Xbox360IdentityBusId = 999,
            RemoveXbox360Results = new Queue<Xbox360DeviceRemoveResult>(
                [Xbox360DeviceRemoveResult.RetryableFailure,
                 Xbox360DeviceRemoveResult.Success])
        };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime!.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.Xbox360Remove, runtime.TeardownPhase);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);

        Assert.True(await runtime.TeardownAsync());

        var xboxRetry = native.Calls.LastIndexOf("RemoveXbox360DeviceEx");
        var deckRemove = native.Calls.LastIndexOf("RemoveSteamDeckDeviceEx");
        var busRemove = native.Calls.LastIndexOf("RemoveUSBBus");
        Assert.True(xboxRetry < deckRemove);
        Assert.True(deckRemove < busRemove);
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
    }

    [Fact]
    public void Xbox360_initial_attachment_state_outcome_unknown_stops_with_no_destructive_calls_and_retains_owner()
    {
        var native = new FakeNative { Xbox360InitialAttachmentState = USBDeviceAttachmentState.OutcomeUnknown };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState",
            "CreateXbox360Device", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState"
        ], native.Calls);
        Assert.DoesNotContain("SetXbox360DeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Xbox360_initial_attachment_state_attached_neutral_writes_detaches_then_removes_xbox360_then_deck_then_bus_and_server()
    {
        var native = new FakeNative { Xbox360InitialAttachmentState = USBDeviceAttachmentState.Attached };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.Null(runtime);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState",
            "CreateXbox360Device", "GetUSBDeviceIdentity", "GetUSBDeviceAttachmentState",
            "SetXbox360DeviceState", "DetachUSBDeviceEx",
            "RemoveXbox360DeviceEx", "RemoveSteamDeckDeviceEx", "RemoveUSBBus", "CloseUSBServer"
        ], native.Calls);
    }

    [Fact]
    public void Xbox360_attachment_state_query_failure_stops_with_no_destructive_calls_and_retains_owner()
    {
        var native = new FakeNative { Xbox360AttachmentStateQuerySucceeds = false };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Deck_identity_failure_with_unsafe_deck_removal_stops_without_touching_bus_or_server()
    {
        var native = new FakeNative
        {
            DeckIdentityBusId = 999,
            RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>([SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown])
        };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        // Native ownership is retained, not discarded, when unwind cannot be proven clean.
        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.Equal(
        [
            "NewUSBServer", "CreateUSBBus", "CreateSteamDeckDevice", "GetUSBDeviceIdentity", "RemoveSteamDeckDeviceEx"
        ], native.Calls);
        // No destructive cleanup continues past an unsafe/unknown removal result.
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public async Task Deck_identity_failure_with_invalid_deck_removal_marks_unsafe_and_never_retries()
    {
        var native = new FakeNative
        {
            DeckIdentityBusId = 999,
            RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>([SteamDeckDeviceRemoveResult.Invalid])
        };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        // A poisoned owner refuses to act again -- no further RemoveSteamDeckDeviceEx call.
        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
    }

    [Fact]
    public async Task Deck_identity_failure_with_retryable_deck_removal_retains_owner_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            DeckIdentityBusId = 999,
            RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>(
                [SteamDeckDeviceRemoveResult.RetryableFailure, SteamDeckDeviceRemoveResult.Success])
        };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime!.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.DeckRemove, runtime.TeardownPhase);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        var closed = await runtime.TeardownAsync();

        Assert.True(closed);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveUSBBus"));
        Assert.Equal(1, native.Calls.Count(c => c == "CloseUSBServer"));
    }

    [Fact]
    public async Task CreateSteamDeckDevice_failure_with_retryable_bus_removal_retains_owner_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            CreateDeckResult = false,
            RemoveBusResults = new Queue<bool>([false, true])
        };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime!.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.BusRemoval, runtime.TeardownPhase);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        var closed = await runtime.TeardownAsync();

        Assert.True(closed);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "RemoveUSBBus"));
        Assert.Equal(1, native.Calls.Count(c => c == "CloseUSBServer"));
    }

    [Fact]
    public async Task CreateUSBBus_failure_with_retryable_server_close_retains_owner_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            CreateBusResult = false,
            CloseServerResults = new Queue<bool>([false, true])
        };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime!.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.ServerClose, runtime.TeardownPhase);

        var closed = await runtime.TeardownAsync();

        Assert.True(closed);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "CloseUSBServer"));
    }

    // ---- Final teardown (work order sections 19-21, test section 32) ----

    [Fact]
    public async Task TeardownAsync_happy_path_both_already_detached()
    {
        var native = new FakeNative();
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.True(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(
        [
            "GetUSBDeviceAttachmentState", "GetUSBDeviceAttachmentState",
            "RemoveSteamDeckDeviceEx", "RemoveXbox360DeviceEx", "RemoveUSBBus", "CloseUSBServer"
        ], native.Calls);

        // A fully Closed runtime must expose no non-zero native capability -- retired handles
        // are never left looking like they are still authoritative.
        Assert.Equal((nuint)0, runtime.ServerHandle);
        Assert.Equal(0u, runtime.BusId);
        Assert.Equal((nuint)0, runtime.DeckDeviceHandle);
        Assert.Equal(0u, runtime.DeckLogicalDeviceId);
        Assert.Equal((nuint)0, runtime.Xbox360DeviceHandle);
        Assert.Equal(0u, runtime.Xbox360LogicalDeviceId);
    }

    [Fact]
    public async Task TeardownAsync_deck_remove_retryable_failure_retains_the_exact_handle_then_zeroes_it_on_retry_success()
    {
        var native = new FakeNative { RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>([SteamDeckDeviceRemoveResult.RetryableFailure, SteamDeckDeviceRemoveResult.Success]) };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        var originalDeckHandle = runtime.DeckDeviceHandle;
        Assert.NotEqual((nuint)0, originalDeckHandle);

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime.State);
        // A retryable failure must retain the exact handle/identity as evidence, not zero it.
        Assert.Equal(originalDeckHandle, runtime.DeckDeviceHandle);

        Assert.True(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal((nuint)0, runtime.DeckDeviceHandle);
        Assert.Equal(0u, runtime.DeckLogicalDeviceId);
    }

    [Fact]
    public async Task TeardownAsync_deck_attachment_state_out_of_range_fails_closed_with_no_destructive_calls()
    {
        var native = new FakeNative { DeckAttachmentStateDuringTeardown = (USBDeviceAttachmentState)99 };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("SetSteamDeckDeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public async Task TeardownAsync_xbox360_attachment_state_out_of_range_fails_closed_with_no_destructive_calls()
    {
        var native = new FakeNative { Xbox360AttachmentStateDuringTeardown = (USBDeviceAttachmentState)99 };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("SetXbox360DeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Deck_initial_attachment_state_out_of_range_fails_closed_with_no_destructive_calls()
    {
        var native = new FakeNative { DeckInitialAttachmentState = (USBDeviceAttachmentState)99 };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.DoesNotContain("SetSteamDeckDeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("CreateXbox360Device", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public void Xbox360_initial_attachment_state_out_of_range_fails_closed_with_no_destructive_calls()
    {
        var native = new FakeNative { Xbox360InitialAttachmentState = (USBDeviceAttachmentState)99 };

        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress);

        Assert.NotNull(runtime);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime!.State);
        Assert.DoesNotContain("SetXbox360DeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public async Task TeardownAsync_detaches_deck_first_when_unexpectedly_attached()
    {
        var native = new FakeNative { DeckAttachmentStateDuringTeardown = USBDeviceAttachmentState.Attached };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.True(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Contains("SetSteamDeckDeviceState", native.Calls);
        Assert.Contains("DetachUSBDeviceEx", native.Calls);
        Assert.True(native.Calls.IndexOf("DetachUSBDeviceEx") < native.Calls.IndexOf("RemoveSteamDeckDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_detaches_xbox360_first_when_unexpectedly_attached()
    {
        var native = new FakeNative { Xbox360AttachmentStateDuringTeardown = USBDeviceAttachmentState.Attached };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.True(completed);
        Assert.Contains("SetXbox360DeviceState", native.Calls);
        // Only the Xbox360 handle was attached (the Deck handle was already Detached), so exactly
        // one DetachUSBDeviceEx call is issued.
        Assert.Equal(1, native.Calls.Count(c => c == "DetachUSBDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_stops_when_attachment_state_outcome_unknown()
    {
        var native = new FakeNative { DeckAttachmentStateDuringTeardown = USBDeviceAttachmentState.OutcomeUnknown };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);

        // No blind retry of an unsafe/unknown outcome.
        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "GetUSBDeviceAttachmentState"));
    }

    [Fact]
    public async Task TeardownAsync_deck_remove_retryable_failure_retains_xbox360_bus_and_server_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>(
                [SteamDeckDeviceRemoveResult.RetryableFailure, SteamDeckDeviceRemoveResult.Success])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var first = await runtime.TeardownAsync();
        Assert.False(first);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.DeckRemove, runtime.TeardownPhase);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        var second = await runtime.TeardownAsync();
        Assert.True(second);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveXbox360DeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_deck_remove_invalid_marks_unsafe_and_never_retries()
    {
        var native = new FakeNative
        {
            RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>([SteamDeckDeviceRemoveResult.Invalid])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_xbox360_remove_invalid_marks_unsafe_and_never_retries()
    {
        var native = new FakeNative
        {
            RemoveXbox360Results = new Queue<Xbox360DeviceRemoveResult>([Xbox360DeviceRemoveResult.Invalid])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveXbox360DeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_deck_detach_invalid_marks_unsafe_and_never_retries()
    {
        var native = new FakeNative
        {
            DeckAttachmentStateDuringTeardown = USBDeviceAttachmentState.Attached,
            DeckDetachResults = new Queue<USBDeviceDetachResult>([USBDeviceDetachResult.Invalid])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "DetachUSBDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_xbox360_detach_invalid_marks_unsafe_and_never_retries()
    {
        var native = new FakeNative
        {
            Xbox360AttachmentStateDuringTeardown = USBDeviceAttachmentState.Attached,
            Xbox360DetachResults = new Queue<USBDeviceDetachResult>([USBDeviceDetachResult.Invalid])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "DetachUSBDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_deck_neutral_write_failure_stops_before_detach_or_removal_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            DeckAttachmentStateDuringTeardown = USBDeviceAttachmentState.Attached,
            SetDeckStateResults = new Queue<bool>([false, true])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.DeckDetach, runtime.TeardownPhase);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        var retried = await runtime.TeardownAsync();

        Assert.True(retried);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "SetSteamDeckDeviceState"));
        Assert.Equal(1, native.Calls.Count(c => c == "DetachUSBDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_xbox360_neutral_write_failure_stops_before_detach_or_removal_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            Xbox360AttachmentStateDuringTeardown = USBDeviceAttachmentState.Attached,
            SetXbox360StateResults = new Queue<bool>([false, true])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.CleanupPending, runtime.State);
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.Xbox360Detach, runtime.TeardownPhase);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveSteamDeckDeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        var retried = await runtime.TeardownAsync();

        Assert.True(retried);
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "SetXbox360DeviceState"));
        Assert.Equal(1, native.Calls.Count(c => c == "DetachUSBDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_deck_remove_unsafe_stops_before_xbox360_bus_or_server()
    {
        var native = new FakeNative
        {
            RemoveDeckResults = new Queue<SteamDeckDeviceRemoveResult>([SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        var completed = await runtime.TeardownAsync();

        Assert.False(completed);
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("RemoveXbox360DeviceEx", native.Calls);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_xbox360_remove_retryable_failure_retains_bus_and_server_then_succeeds_on_retry()
    {
        var native = new FakeNative
        {
            RemoveXbox360Results = new Queue<Xbox360DeviceRemoveResult>(
                [Xbox360DeviceRemoveResult.RetryableFailure, Xbox360DeviceRemoveResult.Success])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.Xbox360Remove, runtime.TeardownPhase);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);

        Assert.True(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
        Assert.Equal(2, native.Calls.Count(c => c == "RemoveXbox360DeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_xbox360_remove_unsafe_stops_before_bus_or_server()
    {
        var native = new FakeNative
        {
            RemoveXbox360Results = new Queue<Xbox360DeviceRemoveResult>([Xbox360DeviceRemoveResult.UnsafeOutcomeUnknown])
        };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeState.Unsafe, runtime.State);
        Assert.DoesNotContain("RemoveUSBBus", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    [Fact]
    public async Task TeardownAsync_bus_removal_failure_retains_server_then_succeeds_on_retry()
    {
        var native = new FakeNative { RemoveBusResults = new Queue<bool>([false, true]) };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.BusRemoval, runtime.TeardownPhase);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveUSBBus"));

        Assert.True(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "RemoveUSBBus"));
        // Device removal must not be repeated once already successful.
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveSteamDeckDeviceEx"));
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveXbox360DeviceEx"));
    }

    [Fact]
    public async Task TeardownAsync_server_close_failure_retains_ownership_then_succeeds_on_retry()
    {
        var native = new FakeNative { CloseServerResults = new Queue<bool>([false, true]) };
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;
        native.Calls.Clear();

        Assert.False(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeTeardownPhase.ServerClose, runtime.TeardownPhase);
        Assert.Equal(1, native.Calls.Count(c => c == "CloseUSBServer"));
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveUSBBus"));

        Assert.True(await runtime.TeardownAsync());
        Assert.Equal(CanonicalViiperRuntimeState.Closed, runtime.State);
        Assert.Equal(2, native.Calls.Count(c => c == "CloseUSBServer"));
        Assert.Equal(1, native.Calls.Count(c => c == "RemoveUSBBus"));
    }

    [Fact]
    public async Task TeardownAsync_is_idempotent_once_closed()
    {
        var native = new FakeNative();
        var runtime = CanonicalViiperRuntime.TryInitialize(native, LoopbackAddress)!;

        Assert.True(await runtime.TeardownAsync());
        var callsAfterFirst = native.Calls.Count;

        Assert.True(await runtime.TeardownAsync());
        Assert.Equal(callsAfterFirst, native.Calls.Count);
    }

    private sealed class FakeNative : ICanonicalViiperNativeApi
    {
        internal readonly List<string> Calls = [];
        internal bool NewServerResult { get; init; } = true;
        internal bool CreateBusResult { get; init; } = true;
        internal bool CreateDeckResult { get; init; } = true;
        internal bool CreateXbox360Result { get; init; } = true;
        internal uint DeckIdentityBusId { get; init; } = 42;
        internal uint Xbox360IdentityBusId { get; init; } = 42;
        internal USBDeviceAttachmentState DeckInitialAttachmentState { get; init; } = USBDeviceAttachmentState.Detached;
        internal USBDeviceAttachmentState Xbox360InitialAttachmentState { get; init; } = USBDeviceAttachmentState.Detached;
        internal USBDeviceAttachmentState DeckAttachmentStateDuringTeardown { get; init; } = USBDeviceAttachmentState.Detached;
        internal USBDeviceAttachmentState Xbox360AttachmentStateDuringTeardown { get; init; } = USBDeviceAttachmentState.Detached;
        internal bool DeckAttachmentStateQuerySucceeds { get; init; } = true;
        internal bool Xbox360AttachmentStateQuerySucceeds { get; init; } = true;
        internal Queue<SteamDeckDeviceRemoveResult> RemoveDeckResults { get; init; } = new([SteamDeckDeviceRemoveResult.Success]);
        internal Queue<Xbox360DeviceRemoveResult> RemoveXbox360Results { get; init; } = new([Xbox360DeviceRemoveResult.Success]);
        internal Queue<USBDeviceDetachResult> DeckDetachResults { get; init; } = new([USBDeviceDetachResult.Success]);
        internal Queue<USBDeviceDetachResult> Xbox360DetachResults { get; init; } = new([USBDeviceDetachResult.Success]);
        internal Queue<bool> SetDeckStateResults { get; init; } = new([true]);
        internal Queue<bool> SetXbox360StateResults { get; init; } = new([true]);
        internal Queue<bool> RemoveBusResults { get; init; } = new([true]);
        internal Queue<bool> CloseServerResults { get; init; } = new([true]);
        internal bool DeckAutoAttach { get; private set; }
        internal bool Xbox360AutoAttach { get; private set; }
        internal ushort? Xbox360CreateVendorId { get; private set; }
        internal ushort? Xbox360CreateProductId { get; private set; }
        internal byte? Xbox360CreateXInputSubType { get; private set; }

        private int _attachmentStateQueries;

        public bool NewUSBServer(ref USBServerConfig config, out nuint serverHandle, ViiperLogCallback? logCallback = null)
        {
            Calls.Add("NewUSBServer");
            serverHandle = 10;
            return NewServerResult;
        }

        public bool CloseUSBServer(nuint serverHandle)
        {
            Calls.Add("CloseUSBServer");
            return CloseServerResults.Count == 0 || CloseServerResults.Dequeue();
        }

        public bool CreateUSBBus(nuint serverHandle, ref uint busId) { Calls.Add("CreateUSBBus"); busId = 42; return CreateBusResult; }
        public bool RemoveUSBBus(nuint serverHandle, uint busId) { Calls.Add("RemoveUSBBus"); return RemoveBusResults.Count == 0 || RemoveBusResults.Dequeue(); }

        public bool GetUSBDeviceIdentity(nuint deviceHandle, out uint busId, out uint deviceId)
        {
            Calls.Add("GetUSBDeviceIdentity");
            if (deviceHandle == 20) { busId = DeckIdentityBusId; deviceId = 9; return true; }
            busId = Xbox360IdentityBusId; deviceId = 11; return true;
        }

        public bool AttachUSBDevice(nuint deviceHandle) { Calls.Add("AttachUSBDevice"); return true; }
        public bool DetachUSBDevice(nuint deviceHandle) { Calls.Add("DetachUSBDevice"); return true; }

        // Not exercised by PR2a (route entry/AttachDeck is deferred to PR2b); present only to
        // satisfy ICanonicalViiperNativeApi.
        public USBDeviceAttachResult AttachUSBDeviceEx(nuint deviceHandle) { Calls.Add("AttachUSBDeviceEx"); return USBDeviceAttachResult.Success; }

        public USBDeviceDetachResult DetachUSBDeviceEx(nuint deviceHandle)
        {
            Calls.Add("DetachUSBDeviceEx");
            var queue = deviceHandle == 20 ? DeckDetachResults : Xbox360DetachResults;
            return queue.Count == 0 ? USBDeviceDetachResult.Success : queue.Dequeue();
        }

        public bool GetUSBDeviceAttachmentState(nuint deviceHandle, out USBDeviceAttachmentState state)
        {
            Calls.Add("GetUSBDeviceAttachmentState");
            _attachmentStateQueries++;
            // Initialization queries the Deck then the Xbox360 handle exactly once each (calls 1
            // and 2); every subsequent query is a final-teardown query.
            var duringInit = _attachmentStateQueries <= 2;
            if (duringInit)
            {
                state = deviceHandle == 20 ? DeckInitialAttachmentState : Xbox360InitialAttachmentState;
                return deviceHandle == 20 ? DeckAttachmentStateQuerySucceeds : Xbox360AttachmentStateQuerySucceeds;
            }
            state = deviceHandle == 20 ? DeckAttachmentStateDuringTeardown : Xbox360AttachmentStateDuringTeardown;
            return true;
        }

        public bool CreateSteamDeckDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct)
        {
            Calls.Add("CreateSteamDeckDevice");
            deviceHandle = 20;
            DeckAutoAttach = autoAttachLocalhost;
            return CreateDeckResult;
        }

        public bool SetSteamDeckDeviceState(nuint deviceHandle, SteamDeckDeviceState state)
        {
            Calls.Add("SetSteamDeckDeviceState");
            return SetDeckStateResults.Count == 0 || SetDeckStateResults.Dequeue();
        }
        public bool SetSteamDeckOutputCallback(nuint deviceHandle, SteamDeckOutputCallback? callback) { Calls.Add("SetSteamDeckOutputCallback"); return true; }
        public bool RemoveSteamDeckDevice(nuint deviceHandle) { Calls.Add("RemoveSteamDeckDevice"); return true; }

        public SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint deviceHandle)
        {
            Calls.Add("RemoveSteamDeckDeviceEx");
            return RemoveDeckResults.Count == 0 ? SteamDeckDeviceRemoveResult.Success : RemoveDeckResults.Dequeue();
        }

        public bool CreateXbox360Device(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct, byte xinputSubType)
        {
            Calls.Add("CreateXbox360Device");
            deviceHandle = 30;
            Xbox360AutoAttach = autoAttachLocalhost;
            Xbox360CreateVendorId = idVendor;
            Xbox360CreateProductId = idProduct;
            Xbox360CreateXInputSubType = xinputSubType;
            return CreateXbox360Result;
        }

        public bool SetXbox360DeviceState(nuint deviceHandle, Xbox360DeviceState state)
        {
            Calls.Add("SetXbox360DeviceState");
            return SetXbox360StateResults.Count == 0 || SetXbox360StateResults.Dequeue();
        }
        public bool RemoveXbox360Device(nuint deviceHandle) { Calls.Add("RemoveXbox360Device"); return true; }

        public Xbox360DeviceRemoveResult RemoveXbox360DeviceEx(nuint deviceHandle)
        {
            Calls.Add("RemoveXbox360DeviceEx");
            return RemoveXbox360Results.Count == 0 ? Xbox360DeviceRemoveResult.Success : RemoveXbox360Results.Dequeue();
        }
    }
}
