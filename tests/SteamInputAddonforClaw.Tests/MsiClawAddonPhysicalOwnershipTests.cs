using System.Reflection;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR5: the first real physical ownership operation. Reconciles the same physical
/// MSI Claw to PID1902, acquires verified DirectInput, and persistently hides the exact primary
/// gamepad collection -- attaching no virtual controller and never rolling PID1902 back to PID1901.</summary>
[Collection("AppLog")]
public sealed class MsiClawAddonPhysicalOwnershipTests
{
    private const string PhysKey = @"USB\VID_0DB0\SERIAL123";
    private const string OtherPhysKey = @"USB\VID_0DB0\SERIAL999";
    private const string PrimaryPnp = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&abcdef&0&0000";
    private const string NonPrimaryPnp = @"HID\VID_0DB0&PID_1902&MI_03\7&abcdef&0&0003";

    // ---- 25.1 already PID1902 ----

    [Fact]
    public async Task Already_pid1902_acquires_without_a_mode_write()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.False(result.ModeWriteIssued);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Equal(PrimaryPnp, result.HiddenTarget);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied);
    }

    // ---- 25.2 PID1901 switches exactly once ----

    [Fact]
    public async Task Pid1901_switches_to_pid1902_exactly_once()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal(1, h.SwitchCalls);
        Assert.Equal(MsiClawNativeMode.DirectInput, h.LastSwitchTarget);
    }

    // ---- 25.3 fail closed before mutation ----

    [Fact]
    public async Task Authority_no_longer_disabled_fails_before_any_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, Authority = FrontendCenterMStartupState.Enabled };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Empty(h.HidHideApplied);
        Assert.False(h.InputSource.StartCalled);
    }

    [Theory]
    [InlineData("DeviceNotFound")]
    [InlineData("Indeterminate")]
    public async Task Missing_or_ambiguous_native_state_fails_before_mutation(string status)
    {
        var h = new Harness { InitialCaptureStatus = Enum.Parse<NativeStateCaptureStatus>(status) };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Weak_native_identity_fails_before_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, InitialConfidence = MsiClawIdentityConfidence.Indeterminate };
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await h.Build().AcquireAsync(default)).Outcome);
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact]
    public async Task Unsupported_native_mode_fails_before_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.Other };
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await h.Build().AcquireAsync(default)).Outcome);
        Assert.Equal(0, h.SwitchCalls);
    }

    // ---- 25.4 mode transition failure ----

    [Fact]
    public async Task Mode_transition_failure_blocks_directinput_and_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, SwitchSucceeds = false };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(result.ModeWriteIssued);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 25.5 final cross-mode identity mismatch (MANDATORY) ----

    [Fact]
    public async Task Final_cross_mode_identity_mismatch_fails_with_no_directinput_no_hide_no_rollback()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            FinalPhysKey = OtherPhysKey, // mode write "succeeded" but a different physical MSI Claw
        };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("CrossModeIdentityMismatch", result.Reason);
        Assert.Equal(1, h.SwitchCalls);
        Assert.DoesNotContain(h.SwitchTargets, t => t == MsiClawNativeMode.XInput); // no PID1901 rollback
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 26.1 DirectInput appears after a short delay ----

    [Fact]
    public async Task Directinput_appearing_after_a_bounded_delay_still_acquires()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = 3 };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(h.DirectInputEnumerateCalls > 1);
    }

    // ---- 26.2 DirectInput never appears ----

    [Fact]
    public async Task Directinput_never_appearing_fails_with_no_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Empty(h.HidHideApplied);
        Assert.False(h.InputSource.StartCalled);
    }

    // ---- 26.3 ambiguous DirectInput fails immediately; transient unresolved identity is retried ----

    [Fact]
    public async Task Ambiguous_directinput_fails_immediately_without_retrying()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputAmbiguous = true };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(1, h.DirectInputEnumerateCalls); // MultiplePhysicalIdentities is proven-invalid
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Transiently_unverified_directinput_identity_is_retried_then_acquired()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputUnverifiedAttempts = 3 };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(h.DirectInputEnumerateCalls > 3);
    }

    // ---- 26.4 descriptor is a different physical MSI Claw ----

    [Fact]
    public async Task Descriptor_of_a_different_physical_claw_is_not_acquired_or_hidden()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputPnpPhysKey = OtherPhysKey };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("DirectInputPhysicalIdentityMismatch", result.Reason);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Non_primary_directinput_collection_is_rejected()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputPnp = NonPrimaryPnp };
        var result = await h.Build().AcquireAsync(default);
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 26.5 / 26.6 acquire + first valid state ----

    [Fact]
    public async Task Directinput_start_failure_blocks_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.InputSource.StartResult = MsiClawInputStartStatus.AcquireFailed;
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task First_valid_state_never_arriving_stops_the_source_and_blocks_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.InputSource.FirstValidState = false;
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(h.InputSource.StopCalled);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 27.2 / 27.3 HidHide reconcile ----

    [Theory]
    [InlineData("Conflict")]
    [InlineData("Unavailable")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public async Task HidHide_reconcile_failure_releases_directinput_without_pid_rollback(string outcome)
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, HidHideOutcome = Enum.Parse<AddonHidHideBaselineOutcome>(outcome) };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(h.InputSource.StopCalled);
        Assert.Equal(1, h.SwitchCalls); // one PID1901 -> PID1902, never a reverse
        Assert.DoesNotContain(h.SwitchTargets, t => t == MsiClawNativeMode.XInput);
    }

    // ---- 27.7 / owned source stays alive ----

    [Fact]
    public async Task Successful_ownership_keeps_the_input_source_alive_and_exposed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        var result = await owner.AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(h.InputSource.IsRunning);
        Assert.Same(h.InputSource, owner.LiveInputSource);
    }

    // ---- 29 teardown ----

    [Fact]
    public async Task Controlled_teardown_releases_directinput_without_authority_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        await owner.AcquireAsync(default);

        await owner.DisposeAsync();
        await owner.DisposeAsync(); // idempotent

        Assert.True(h.InputSource.StopCalled);
        Assert.True(h.InputSource.DisposeCalled);
        Assert.Equal(0, h.SwitchCalls); // was already 1902 -> no writes at all, none on teardown
        Assert.Empty(h.EnabledBaselineCalls);
        Assert.Null(owner.LiveInputSource);
    }

    // ---- fresh authority read at the actual mutation boundary ----

    [Fact]
    public async Task Authority_flips_before_the_mode_write_fails_closed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, Authority = FrontendCenterMStartupState.Enabled };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("AuthorityChangedBeforeModeWrite", result.Reason);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Equal(1, h.AuthorityReads); // exactly one fresh read, at the boundary
    }

    [Fact]
    public async Task Authority_flips_before_directinput_acquire_on_an_already_1902_boot_fails_closed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, Authority = FrontendCenterMStartupState.Partial };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("AuthorityChangedBeforeDirectInputAcquire", result.Reason);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- PR5 section 16: Enable-and-Restart release seam ----

    [Fact]
    public async Task Release_for_center_m_enable_stops_directinput_then_restores_pid1901_and_returns_the_target()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        var acquired = await owner.AcquireAsync(default);
        Assert.True(acquired.IsOwned);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(PrimaryPnp, release.HiddenTarget);
        Assert.True(h.InputSource.StopCalled);
        Assert.Equal(new[] { MsiClawNativeMode.XInput }, h.SwitchTargets); // PID1902 -> PID1901, once
        Assert.Null(owner.LiveInputSource);

        // A subsequent acquisition is refused -- ownership was released for the official enable path.
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);
    }

    [Fact]
    public async Task Release_when_pid1901_restore_cannot_be_verified_reports_failure()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, SwitchFailsForRelease = true };
        var owner = h.Build();
        await owner.AcquireAsync(default);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.False(release.Succeeded);
        Assert.Contains("Pid1901Restore", release.Reason);
        Assert.Equal(PrimaryPnp, release.HiddenTarget); // still surfaced so the caller does not lose it
        Assert.True(h.InputSource.StopCalled);
    }

    [Fact]
    public async Task Release_remembers_the_verified_target_even_when_the_persistent_apply_fails()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, HidHideOutcome = AddonHidHideBaselineOutcome.VerificationFailed };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(PrimaryPnp, release.HiddenTarget); // held from the verified descriptor, not the failed apply
    }

    [Fact]
    public async Task Release_recovers_a_previous_boot_target_when_this_acquisition_never_owned()
    {
        const string prior = @"HID\VID_0DB0&PID_1902&MI_00&COL01\prev";
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue, ExistingOwnedTarget = prior };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(prior, release.HiddenTarget); // recovered from persistent HidHide
    }

    [Fact]
    public async Task Release_without_a_prior_acquisition_and_already_stock_pid_is_a_noop_success()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput };
        var owner = h.Build();

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Null(release.HiddenTarget);
        Assert.Equal(0, h.SwitchCalls);
        Assert.False(h.InputSource.StopCalled);
    }

    // ---- 30 architecture guard ----

    [Fact]
    public void Physical_owner_takes_no_route_scoped_or_virtual_dependency()
    {
        var parameterTypes = typeof(MsiClawAddonPhysicalOwnership)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single().GetParameters().Select(p => p.ParameterType.FullName ?? "");
        Assert.All(parameterTypes, name =>
        {
            Assert.DoesNotContain("MsiClawNativeModeSessionCoordinator", name);
            Assert.DoesNotContain("MsiClawPhysicalIsolationStage", name);
            Assert.DoesNotContain("RecoveryManager", name);
            Assert.DoesNotContain("Viiper", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Xbox360", name);
        });

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs"));
        foreach (var forbidden in new[]
        {
            "MsiClawNativeModeSessionCoordinator", "MsiClawPhysicalIsolationStage", "RoutingPipelineSessionCoordinator",
            "BeginDeviceNativeStateMutation", "RecordHidHideDeviceAddition", "AttachXbox360", "AttachSteamDeck", "EnterXbox360PresentationAsync",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);

        // The production gate: PR5 ownership starts only for exact Center M Disabled + admission Ready.
        var host = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        Assert.Contains("startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled", host, StringComparison.Ordinal);
        Assert.Contains("startupResult.DisabledBootAdmission?.IsReady != true", host, StringComparison.Ordinal);
    }

    // ---- harness ----

    private sealed class Harness
    {
        public FrontendCenterMStartupState Authority { get; set; } = FrontendCenterMStartupState.Disabled;
        public MsiClawNativeMode InitialMode { get; set; } = MsiClawNativeMode.DirectInput;
        public NativeStateCaptureStatus InitialCaptureStatus { get; set; } = NativeStateCaptureStatus.Success;
        public MsiClawIdentityConfidence InitialConfidence { get; set; } = MsiClawIdentityConfidence.Strong;
        public string FinalPhysKey { get; set; } = PhysKey;
        public bool SwitchSucceeds { get; set; } = true;
        public int DirectInputMissingAttempts { get; set; }
        public int DirectInputUnverifiedAttempts { get; set; }
        public bool DirectInputAmbiguous { get; set; }
        public string? ExistingOwnedTarget { get; set; }
        public string DirectInputPnp { get; set; } = PrimaryPnp;
        public string DirectInputPnpPhysKey { get; set; } = PhysKey;
        public AddonHidHideBaselineOutcome HidHideOutcome { get; set; } = AddonHidHideBaselineOutcome.Success;

        public int SwitchCalls { get; private set; }
        public MsiClawNativeMode LastSwitchTarget { get; private set; }
        public List<MsiClawNativeMode> SwitchTargets { get; } = [];
        public int DirectInputEnumerateCalls { get; private set; }
        public List<string> HidHideApplied { get; } = [];
        public List<string> EnabledBaselineCalls { get; } = [];
        public FakeInputSource InputSource { get; } = new();

        public int AuthorityReads { get; private set; }
        public bool SwitchFailsForRelease { get; set; }

        public MsiClawAddonPhysicalOwnership Build() => new(
            () => { AuthorityReads++; return Authority; },
            _ => Task.FromResult(Capture(
                SwitchCalls == 0 ? InitialMode : LastSwitchTarget,
                SwitchCalls == 0 ? InitialConfidence : MsiClawIdentityConfidence.Strong,
                SwitchCalls == 0 ? PhysKey : FinalPhysKey)),
            (target, _, _) =>
            {
                SwitchCalls++;
                LastSwitchTarget = target;
                SwitchTargets.Add(target);
                var ok = target == MsiClawNativeMode.XInput ? !SwitchFailsForRelease : SwitchSucceeds;
                return Task.FromResult(new MsiClawModeTransitionResult(
                    ok ? MsiClawModeTransitionStatus.Succeeded : MsiClawModeTransitionStatus.WriteFailed,
                    MsiClawNativeMode.XInput, target, 0x1901, 0x1902, ok, ok, ok, ok, ok, 0,
                    ok ? "ok" : "WriteFailed"));
            },
            () =>
            {
                DirectInputEnumerateCalls++;
                if (DirectInputAmbiguous)
                    return [Descriptor(Guid.NewGuid()), Descriptor(Guid.NewGuid(), physId: "OTHER")];
                if (DirectInputEnumerateCalls <= DirectInputMissingAttempts)
                    return [];
                if (DirectInputEnumerateCalls <= DirectInputMissingAttempts + DirectInputUnverifiedAttempts)
                    return [Descriptor(Guid.NewGuid(), unverified: true)]; // PID1902 present, identity not yet resolved
                return [Descriptor(Guid.NewGuid())];
            },
            instanceId => instanceId == DirectInputPnp ? PnpDevice(DirectInputPnpPhysKey) : null,
            InputSource,
            target =>
            {
                HidHideApplied.Add(target);
                return new AddonHidHideBaselineResult(HidHideOutcome, HidHideOutcome.ToString(), AddonHidHideBaselineSnapshot.Unknown);
            },
            () => ExistingOwnedTarget,
            delay: (_, _) => Task.CompletedTask,
            directInputSettleWindow: TimeSpan.FromMilliseconds(200),
            directInputSettleInterval: TimeSpan.FromMilliseconds(1));

        private NativeStateCaptureResult Capture(MsiClawNativeMode mode, MsiClawIdentityConfidence confidence, string physKey)
        {
            if (InitialCaptureStatus != NativeStateCaptureStatus.Success)
                return new(InitialCaptureStatus, null, InitialCaptureStatus.ToString());
            var payload = new MsiClawNativeStatePayload(mode, "inst", "USB\\parent", null, 0x1902, confidence, physKey);
            var snapshot = new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(payload));
            return new(NativeStateCaptureStatus.Success, snapshot, "ok");
        }

        private DirectInputDeviceDescriptor Descriptor(Guid instanceGuid, string? physId = null, bool unverified = false) => new(
            instanceGuid, Guid.NewGuid(), "MSI Claw Controller", 0x0DB0, 0x1902,
            DevicePath: unverified ? null : @"\\?\hid#vid_0db0&pid_1902",
            PnpInstanceId: unverified ? null : DirectInputPnp,
            PhysicalIdentity: unverified ? null : physId ?? "msi-claw-phys",
            UsagePage: 0x0001, Usage: 0x0005, ButtonCount: 17, AxisCount: 6);

        private static ControllerDeviceInfo PnpDevice(string physKey)
        {
            var serial = physKey[(physKey.LastIndexOf('\\') + 1)..];
            return new ControllerDeviceInfo(
                InstanceId: PrimaryPnp,
                ContainerId: null,
                ParentInstanceId: $@"USB\VID_0DB0&PID_1902&MI_00\6&xyz&0&0000",
                AncestorInstanceIds: [$@"USB\VID_0DB0&PID_1902\{serial}"],
                EnumeratorName: "HID", HardwareIds: [], CompatibleIds: [], ClassName: "HIDClass", ClassGuid: null, Service: "HidUsb",
                VendorId: 0x0DB0, ProductId: 0x1902, Present: true, FriendlyName: "MSI Claw",
                UsagePage: 0x0001, Usage: 0x0005);
        }
    }

    private sealed class FakeInputSource : IMsiClawPreparedInputSource
    {
        public MsiClawInputStartStatus StartResult { get; set; } = MsiClawInputStartStatus.Started;
        public bool FirstValidState { get; set; } = true;
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public bool IsRunning { get; private set; }

        public event EventHandler<SteamInputAddonforClaw.Input.ControllerState>? StateChanged { add { } remove { } }

        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
        {
            StartCalled = true;
            if (StartResult == MsiClawInputStartStatus.Started) IsRunning = true;
            return new MsiClawInputStartResult(StartResult, StartResult.ToString());
        }

        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken)
        {
            if (!FirstValidState) IsRunning = true; // still running, just never valid
            return Task.FromResult(FirstValidState);
        }

        public Task StopAsync()
        {
            StopCalled = true;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
