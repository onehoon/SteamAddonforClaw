using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiRoutingGuardTests
{
    [Fact]
    public async Task Real_main_ui_already_present_prevents_arm_without_starting_anything()
    {
        var snapshots = new FakeSnapshotSource([[new ProcessSnapshotEntry(999, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.RealMainUiPresent, result);
        Assert.False(guard.IsArmed);
        Assert.False(stager.Called);
        Assert.Empty(helperApi.Calls);
        Assert.Equal(0, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Normal_arm_starts_helper_acquires_mutex_and_becomes_armed()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, result);
        Assert.True(guard.IsArmed);
        Assert.Equal(1, mutexFactory.CreateCallCount);
        Assert.Contains("ResumeThread", helperApi.Calls);
    }

    [Fact]
    public async Task Repeated_arm_while_already_armed_is_idempotent()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);
        await guard.ArmAsync();

        var second = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, second);
        Assert.Equal(1, mutexFactory.CreateCallCount);
        Assert.Equal(1, helperApi.Calls.Count(call => call == "CreateSuspended"));
    }

    [Fact]
    public async Task Helper_staging_failure_prevents_mutex_acquisition()
    {
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager(null);
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.HelperFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Empty(helperApi.Calls);
        Assert.Equal(0, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Helper_start_failure_prevents_mutex_acquisition()
    {
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi { CreateSucceeds = false };
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.HelperFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Equal(0, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Mutex_failure_after_helper_start_stops_the_helper_and_never_arms()
    {
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory { NextCreatedNew = false };
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.MutexFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
    }

    [Fact]
    public async Task Invariant_failure_after_helper_and_mutex_releases_both_and_never_arms()
    {
        // The owned helper vanished from the fresh post-mutex snapshot (HelperMissing) -- distinct
        // from a foreign process appearing, which is covered separately below.
        var snapshots = new FakeSnapshotSource([[], []]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.InvariantFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Foreign_main_ui_appearing_during_arm_aborts_without_terminating_it()
    {
        var snapshots = new FakeSnapshotSource(
        [
            [],
            [
                new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null),
                new ProcessSnapshotEntry(555, "MSI Center M", null)
            ]
        ]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.RealMainUiPresent, result);
        Assert.False(guard.IsArmed);
        // Phase 1 policy: never terminate a foreign real MainUI -- only this attempt's OWN
        // resources unwind (the owned helper, terminated via its retained handle -- not the
        // foreign process, which this fake never even models a termination call for).
        Assert.Contains("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Uncertain_enumeration_before_start_fails_closed_without_starting_anything()
    {
        var snapshots = new FakeSnapshotSource(uncertainFirst: true);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Uncertain, result);
        Assert.False(guard.IsArmed);
        Assert.Empty(helperApi.Calls);
    }

    [Fact]
    public async Task Arm_with_already_operational_helper_borrows_it_without_starting()
    {
        // The shared CenterMHelperOwnership is already operationally owned before this arm attempt
        // (as it would be if a future OEM1 lifecycle owner already started it) -- the guard must
        // borrow it rather than call Start a second time.
        var helperApi = new RecordingHelperApi();
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        Assert.Equal(HelperStartResult.Started, helperOwnership.Start("C:\\fake\\MSI Center M.exe"));
        helperApi.Calls.Clear();

        var owned = new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null);
        var snapshots = new FakeSnapshotSource([[owned], [owned]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var mutexFactory = new FakeMutexFactory();
        var guard = new CenterMMainUiRoutingGuard(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: helperOwnership,
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: stager.Stage);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, result);
        Assert.True(guard.IsArmed);
        Assert.False(stager.Called);
        Assert.DoesNotContain("CreateSuspended", helperApi.Calls);
        Assert.Equal(1, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Retained_but_non_operational_ownership_fails_closed_without_starting_or_discarding()
    {
        // ResumeThread fails and the follow-up cleanup wait never confirms exit: IsOwned stays true
        // (a job-less PartialCleanupUnconfirmed residue) but IsOperationallyOwned is false. The
        // guard must fail closed -- never Start a replacement, never discard the retained ownership.
        var helperApi = new RecordingHelperApi { ResumeSucceeds = false, WaitForExitSucceeds = false };
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, helperOwnership.Start("C:\\fake\\MSI Center M.exe"));
        Assert.True(helperOwnership.IsOwned);
        Assert.False(helperOwnership.IsOperationallyOwned);
        helperApi.Calls.Clear();

        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var mutexFactory = new FakeMutexFactory();
        var guard = new CenterMMainUiRoutingGuard(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: helperOwnership,
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: stager.Stage);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.HelperOwnershipUnresolved, result);
        Assert.False(guard.IsArmed);
        Assert.False(stager.Called);
        Assert.DoesNotContain("CreateSuspended", helperApi.Calls);
        Assert.Equal(0, mutexFactory.CreateCallCount);
        Assert.True(helperOwnership.IsOwned);
    }

    [Fact]
    public async Task Disarm_after_borrowed_helper_releases_mutex_but_does_not_stop_the_helper()
    {
        var helperApi = new RecordingHelperApi();
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        Assert.Equal(HelperStartResult.Started, helperOwnership.Start("C:\\fake\\MSI Center M.exe"));
        helperApi.Calls.Clear();

        var owned = new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null);
        var snapshots = new FakeSnapshotSource([[owned], [owned]]);
        var mutexFactory = new FakeMutexFactory();
        var guard = new CenterMMainUiRoutingGuard(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: helperOwnership,
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: new FakeStager("C:\\fake\\MSI Center M.exe").Stage);
        await guard.ArmAsync();

        var disarmed = await guard.DisarmAsync();

        Assert.True(disarmed);
        Assert.False(guard.IsArmed);
        Assert.DoesNotContain("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
        // The borrowed helper survives routing exit -- it remains operationally owned for its
        // external owner rather than being stopped merely because this routing arm ended.
        Assert.True(helperOwnership.IsOperationallyOwned);
    }

    [Fact]
    public async Task Borrowed_helper_plus_mutex_failure_never_stops_the_helper()
    {
        var helperApi = new RecordingHelperApi();
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        Assert.Equal(HelperStartResult.Started, helperOwnership.Start("C:\\fake\\MSI Center M.exe"));
        helperApi.Calls.Clear();

        var owned = new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null);
        var snapshots = new FakeSnapshotSource([[owned]]);
        var mutexFactory = new FakeMutexFactory { NextCreatedNew = false };
        var guard = new CenterMMainUiRoutingGuard(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: helperOwnership,
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: new FakeStager("C:\\fake\\MSI Center M.exe").Stage);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.MutexFailure, result);
        Assert.False(guard.IsArmed);
        Assert.DoesNotContain("Terminate", helperApi.Calls);
        Assert.True(helperOwnership.IsOperationallyOwned);
    }

    [Fact]
    public async Task Borrowed_helper_plus_invariant_failure_releases_mutex_without_stopping_the_helper()
    {
        var helperApi = new RecordingHelperApi();
        var helperOwnership = new CenterMHelperOwnership(helperApi);
        Assert.Equal(HelperStartResult.Started, helperOwnership.Start("C:\\fake\\MSI Center M.exe"));
        helperApi.Calls.Clear();

        var owned = new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null);
        // Post-mutex re-check no longer finds the owned helper -- invariant failure.
        var snapshots = new FakeSnapshotSource([[owned], []]);
        var mutexFactory = new FakeMutexFactory();
        var guard = new CenterMMainUiRoutingGuard(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: helperOwnership,
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: new FakeStager("C:\\fake\\MSI Center M.exe").Stage);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.InvariantFailure, result);
        Assert.False(guard.IsArmed);
        Assert.DoesNotContain("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
        Assert.True(helperOwnership.IsOperationallyOwned);
    }

    [Fact]
    public async Task Guard_created_helper_plus_mutex_failure_still_stops_the_helper()
    {
        // Regression guard: the guard-created (Case A) path must retain its existing cleanup
        // behavior unchanged by the borrow/start decision added above.
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory { NextCreatedNew = false };
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.MutexFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
    }

    [Fact]
    public async Task Composition_passes_the_shared_helper_ownership_instance_to_the_routing_guard()
    {
        // Proves the composition-level requirement: the guard borrows/starts through the SAME
        // CenterMHelperOwnership instance the composition created, not an unrelated one -- exercised
        // directly here (rather than via MsiClawRoutingComposition, which always constructs its own
        // production instance) since that is exactly what constructor injection is for.
        var helperApi = new RecordingHelperApi();
        var sharedOwnership = new CenterMHelperOwnership(helperApi);
        var guard = new CenterMMainUiRoutingGuard(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]),
            helperOwnership: sharedOwnership,
            mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
            stager: new FakeStager("C:\\fake\\MSI Center M.exe").Stage);

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

        Assert.True(sharedOwnership.IsOperationallyOwned);
        Assert.Equal(RecordingHelperApi.FixedProcessId, sharedOwnership.ProcessId);
    }

    [Fact]
    public async Task Disarm_after_arm_stops_the_helper_and_releases_the_mutex()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);
        await guard.ArmAsync();

        var disarmed = await guard.DisarmAsync();

        Assert.True(disarmed);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Disarm_without_ever_arming_is_a_safe_no_op()
    {
        var guard = Create(new FakeSnapshotSource([[]]), new FakeStager(null), new RecordingHelperApi(), new FakeMutexFactory());

        var disarmed = await guard.DisarmAsync();

        Assert.True(disarmed);
        Assert.False(guard.IsArmed);
    }

    [Fact]
    public async Task DisposeAsync_hands_unresolved_exact_helper_ownership_to_orphan_registry()
    {
        CenterMOrphanedHelperRegistry.TestOnly_Clear();
        try
        {
            var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
            var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
            // Every WaitForExit remains unconfirmed, so Stop() can never resolve -- the exact
            // handle must stay retained (never discarded) and end up in the registry.
            var helperApi = new RecordingHelperApi { WaitForExitSucceeds = false };
            var helperOwnership = new CenterMHelperOwnership(helperApi);
            var guard = new CenterMMainUiRoutingGuard(
                publishRootProvider: () => "C:\\fake\\publish",
                processSnapshotSource: snapshots,
                helperOwnership: helperOwnership,
                mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
                stager: stager.Stage,
                helperStopTimeout: TimeSpan.Zero);
            Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

            await guard.DisposeAsync();

            Assert.True(helperOwnership.IsOwned);
            Assert.True(CenterMOrphanedHelperRegistry.Contains(helperOwnership));
        }
        finally
        {
            CenterMOrphanedHelperRegistry.TestOnly_Clear();
        }
    }

    [Fact]
    public async Task PartialCleanupUnconfirmed_start_keeps_this_arm_responsible_for_cleanup()
    {
        // Review fix: Start() returning PartialCleanupUnconfirmed still retained the exact process
        // handle -- this arm must remain responsible for it exactly as it would for a successful
        // Start, not be treated as if it never started anything. Before the fix,
        // _helperStartedByCurrentArm stayed false here, so UnwindAsync never even attempted a Stop
        // and a later DisposeAsync would skip retry/registration entirely.
        CenterMOrphanedHelperRegistry.TestOnly_Clear();
        try
        {
            var snapshots = new FakeSnapshotSource([[]]);
            var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
            var helperApi = new RecordingHelperApi { ResumeSucceeds = false, WaitForExitSucceeds = false };
            var helperOwnership = new CenterMHelperOwnership(helperApi);
            var guard = new CenterMMainUiRoutingGuard(
                publishRootProvider: () => "C:\\fake\\publish",
                processSnapshotSource: snapshots,
                helperOwnership: helperOwnership,
                mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
                stager: stager.Stage,
                helperStopTimeout: TimeSpan.Zero);

            var result = await guard.ArmAsync();

            Assert.Equal(CenterMMainUiRoutingGuardResult.HelperFailure, result);
            // UnwindAsync (run by the failed arm itself) must already have attempted a Stop and,
            // since it could not be confirmed, kept the retained ownership rather than discarding it.
            Assert.True(helperOwnership.IsOwned);

            await guard.DisposeAsync();

            Assert.True(CenterMOrphanedHelperRegistry.Contains(helperOwnership));
        }
        finally
        {
            CenterMOrphanedHelperRegistry.TestOnly_Clear();
        }
    }

    [Fact]
    public async Task Disarm_with_unconfirmed_stop_still_lets_a_later_dispose_finish_cleanup_and_register_orphan()
    {
        // Review fix: an unconfirmed Stop() during DisarmAsync must leave _helperStartedByCurrentArm
        // true so a later terminal DisposeAsync still performs its bounded retries and orphan
        // registration. Before the fix, UnwindAsync unconditionally cleared the flag regardless of
        // whether Stop() actually confirmed termination, so DisposeAsync would see it already false
        // and return early -- silently abandoning the still-retained exact handle.
        CenterMOrphanedHelperRegistry.TestOnly_Clear();
        try
        {
            var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
            var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
            var helperApi = new RecordingHelperApi { WaitForExitSucceeds = false };
            var helperOwnership = new CenterMHelperOwnership(helperApi);
            var guard = new CenterMMainUiRoutingGuard(
                publishRootProvider: () => "C:\\fake\\publish",
                processSnapshotSource: snapshots,
                helperOwnership: helperOwnership,
                mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
                stager: stager.Stage,
                helperStopTimeout: TimeSpan.Zero);
            Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

            var disarmed = await guard.DisarmAsync();

            Assert.False(disarmed);
            Assert.True(helperOwnership.IsOwned);

            await guard.DisposeAsync();

            Assert.True(CenterMOrphanedHelperRegistry.Contains(helperOwnership));
        }
        finally
        {
            CenterMOrphanedHelperRegistry.TestOnly_Clear();
        }
    }

    [Fact]
    public async Task DisposeAsync_does_not_register_a_helper_whose_termination_is_confirmed()
    {
        CenterMOrphanedHelperRegistry.TestOnly_Clear();
        try
        {
            var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
            var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
            var helperApi = new RecordingHelperApi();
            var helperOwnership = new CenterMHelperOwnership(helperApi);
            var guard = new CenterMMainUiRoutingGuard(
                publishRootProvider: () => "C:\\fake\\publish",
                processSnapshotSource: snapshots,
                helperOwnership: helperOwnership,
                mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
                stager: stager.Stage);
            Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

            await guard.DisposeAsync();

            Assert.False(helperOwnership.IsOwned);
            Assert.False(CenterMOrphanedHelperRegistry.Contains(helperOwnership));
            Assert.Equal(0, CenterMOrphanedHelperRegistry.RegisteredCount);
        }
        finally
        {
            CenterMOrphanedHelperRegistry.TestOnly_Clear();
        }
    }

    [Fact]
    public async Task Concurrent_arm_calls_serialize_and_produce_exactly_one_helper_and_mutex()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var blocker = new ManualResetEventSlim(false);
        var helperApi = new RecordingHelperApi { CreateSuspendedBlocker = blocker };
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var armA = Task.Run(() => guard.ArmAsync());
        Assert.True(helperApi.CreateSuspendedEntered.Wait(TimeSpan.FromSeconds(5)));

        var armB = Task.Run(() => guard.ArmAsync());
        // B must be genuinely blocked behind A's still-in-flight transaction, not merely
        // happening to run after it.
        await Task.Delay(50);
        Assert.False(armB.IsCompleted);

        blocker.Set();
        var resultA = await armA;
        var resultB = await armB;

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, resultA);
        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, resultB);
        Assert.True(guard.IsArmed);
        Assert.Equal(1, helperApi.Calls.Count(call => call == "CreateSuspended"));
        Assert.Equal(1, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Overlapping_disarm_never_races_an_in_flight_arm()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var blocker = new ManualResetEventSlim(false);
        var helperApi = new RecordingHelperApi { CreateSuspendedBlocker = blocker };
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var arming = Task.Run(() => guard.ArmAsync());
        Assert.True(helperApi.CreateSuspendedEntered.Wait(TimeSpan.FromSeconds(5)));

        var disarming = Task.Run(() => guard.DisarmAsync());
        await Task.Delay(50);
        Assert.False(disarming.IsCompleted);

        blocker.Set();
        var armResult = await arming;
        var disarmResult = await disarming;

        // Disarm was queued behind the already-in-flight Arm transaction, so it can only ever run
        // to completion after Arm has already published (and then immediately retracted) Armed --
        // never interleaved with it.
        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, armResult);
        Assert.True(disarmResult);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
    }

    private static CenterMMainUiRoutingGuard Create(
        FakeSnapshotSource snapshots, FakeStager stager, RecordingHelperApi helperApi, FakeMutexFactory mutexFactory) =>
        new(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: new CenterMHelperOwnership(helperApi),
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: stager.Stage);

    private sealed class FakeSnapshotSource : IProcessSnapshotSource
    {
        private readonly Queue<IReadOnlyList<ProcessSnapshotEntry>?> _snapshots;
        private readonly bool _uncertainFirst;

        internal FakeSnapshotSource(IEnumerable<IReadOnlyList<ProcessSnapshotEntry>>? snapshots = null, bool uncertainFirst = false)
        {
            _snapshots = new Queue<IReadOnlyList<ProcessSnapshotEntry>?>(snapshots ?? []);
            _uncertainFirst = uncertainFirst;
        }

        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
        {
            if (_uncertainFirst) return null;
            return _snapshots.Count > 0 ? _snapshots.Dequeue() : [];
        }
    }

    private sealed class FakeStager(string? result)
    {
        internal bool Called { get; private set; }
        internal string? Stage(string publishRoot) { Called = true; return result; }
    }

    private sealed class FakeMutexFactory : ICenterMMainUiMutexFactory
    {
        internal bool NextCreatedNew { get; set; } = true;
        internal int CreateCallCount { get; private set; }
        internal FakeHandle? LastHandle { get; private set; }

        public (ICenterMMainUiMutexHandle Handle, bool CreatedNew) Create(string name)
        {
            CreateCallCount++;
            var handle = new FakeHandle();
            LastHandle = handle;
            return (handle, NextCreatedNew);
        }

        internal sealed class FakeHandle : ICenterMMainUiMutexHandle
        {
            internal bool Disposed { get; private set; }
            public void ReleaseMutex() { }
            public void Dispose() => Disposed = true;
        }
    }

    /// <summary>Minimal always-succeeds-unless-configured fake, mirroring CenterMHelperOwnershipTests'
    /// own RecordingApi but scoped to what these guard tests need.</summary>
    private sealed class RecordingHelperApi : IHelperProcessNativeApi
    {
        internal const int FixedProcessId = 4242;
        internal List<string> Calls { get; } = [];
        internal bool CreateSucceeds { get; init; } = true;
        internal bool WaitForExitSucceeds { get; init; } = true;
        internal bool ResumeSucceeds { get; init; } = true;

        /// <summary>When set, the first <see cref="TryCreateSuspended"/> call signals
        /// <see cref="CreateSuspendedEntered"/> and then blocks until this is released -- lets a
        /// test deterministically prove a competing Arm/Disarm call is genuinely serialized behind
        /// the guard's gate, not merely observed to "happen to" run second.</summary>
        internal ManualResetEventSlim? CreateSuspendedBlocker { get; init; }
        internal ManualResetEventSlim CreateSuspendedEntered { get; } = new(false);

        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            Calls.Add("CreateSuspended");
            CreateSuspendedEntered.Set();
            CreateSuspendedBlocker?.Wait(TimeSpan.FromSeconds(10));
            win32Error = 0;
            if (!CreateSucceeds) { processId = 0; processHandle = null; threadHandle = null; return false; }
            processId = FixedProcessId;
            processHandle = new SafeProcessHandle(GetCurrentProcessHandle(), false);
            threadHandle = new SafeFileHandle(GetCurrentProcessHandle(), false);
            return true;
        }

        public bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error)
        {
            Calls.Add("CreateJobObject");
            win32Error = 0;
            jobHandle = new SafeFileHandle(GetCurrentProcessHandle(), false);
            return true;
        }

        public bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error)
        {
            Calls.Add("SetKillOnJobClose");
            win32Error = 0;
            return true;
        }

        public bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error)
        {
            Calls.Add("AssignProcessToJob");
            win32Error = 0;
            return true;
        }

        public bool TryResumeThread(SafeHandle threadHandle, out int win32Error)
        {
            Calls.Add("ResumeThread");
            win32Error = 0;
            return ResumeSucceeds;
        }

        public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error)
        {
            Calls.Add("Terminate");
            win32Error = 0;
            return true;
        }

        public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout)
        {
            Calls.Add("WaitForExit");
            return WaitForExitSucceeds;
        }

        public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle)
        {
            Calls.Add("PollLiveness");
            return LiveProcessProbeStatus.Alive;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        private static IntPtr GetCurrentProcessHandle() => GetCurrentProcess();
    }
}
