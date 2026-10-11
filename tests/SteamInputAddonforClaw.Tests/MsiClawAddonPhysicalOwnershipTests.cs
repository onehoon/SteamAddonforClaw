using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Prerequisites;
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
    private const string ControlPnp = @"HID\VID_0DB0&PID_1902&MI_00&COL02\7&abcdef&0&0001";
    private const string SecondControlPnp = @"HID\VID_0DB0&PID_1902&MI_00&COL02\7&abcdef&0&0003";
    private const string ConsumerPnp = @"HID\VID_0DB0&PID_1902&MI_01&COL03\7&abcdef&0&0002";
    private const string OtherPrimaryPnp = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&999999&0&0000";
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
        Assert.Equal([PrimaryPnp], result.HiddenTargets);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_cycles_already_pid1902_through_fresh_pid1901_before_acquisition()
    {
        var claimCount = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.7"),
            ClaimBootAttempt = () => { claimCount++; return BootSessionAttemptResult.Claimed; },
            PnpDevices = A2vmOwnedPnpDevices(),
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal(1, claimCount);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.Equal(0, h.ElapsedSettleMilliseconds);
        Assert.True(h.InputSource.IsRunning);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task A2vm_boot_prime_resumes_once_when_strong_pid1901_endpoint_arrives_after_normal_window(string modelId)
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new(modelId),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            FailTransitionOnCall = 1,
            FailureTargetPidPresent = true,
            FailNativeCaptureAfterFirstModeSwitch = true,
            ThrowGlobalPnpSnapshotAfterFirstModeSwitch = true,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        h.Pid1901DeviceSnapshot = () => h.ElapsedSettleMilliseconds >= 3000
            ? [Pid1901ParentDevice("A2VM_XINPUT_123"), Pid1901ControlDevice("A2VM_XINPUT_123")]
            : [Pid1901ParentDevice("A2VM_XINPUT_123")];

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned, result.Reason);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.Equal(2, h.NativeCaptureCalls); // Initial and final Full1902 proofs only.
        Assert.Equal(2, h.SwitchIdentities.Count);
        var freshPid1901Identity = h.SwitchIdentities[1];
        Assert.Equal(MsiClawHardware.XInputProductId, freshPid1901Identity.ProductId);
        Assert.Equal(MsiClawIdentityConfidence.Strong, freshPid1901Identity.Confidence);
        Assert.False(h.SwitchIdentities[0].StronglyMatches(freshPid1901Identity));
        Assert.Equal(3000, h.ElapsedSettleMilliseconds);
        Assert.True(h.InputSource.IsRunning);
        Assert.Equal([PrimaryPnp, ControlPnp], h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_does_not_require_the_redundant_global_snapshot_after_strict_pid1901_success()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            FailNativeCaptureAfterFirstModeSwitch = true,
            ThrowGlobalPnpSnapshotAfterFirstModeSwitch = true,
            PnpDevices = A2vmOwnedPnpDevices(),
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned, result.Reason);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.Equal(2, h.NativeCaptureCalls);
        Assert.Equal(0, h.ElapsedSettleMilliseconds);
    }

    [Fact]
    public async Task A2vm_boot_prime_fails_closed_when_target_scoped_pnp_failure_is_not_classified_as_transient()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.7"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        var targetProbeCount = 0;
        h.Pid1901DeviceSnapshot = () => ++targetProbeCount == 1
            ? throw new InvalidOperationException("Target-scoped PnP property read failed.")
            : [Pid1901ControlDevice("A2VM_XINPUT_123")];

        var result = await h.Build().AcquireAsync(default);

        Assert.False(result.IsOwned);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
        Assert.Equal(1, targetProbeCount);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.Equal(0, h.ElapsedSettleMilliseconds);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_never_issues_second_write_when_pid1901_is_absent_at_bounded_deadline()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            FailTransitionOnCall = 1,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = static () => [],
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.False(result.IsOwned);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.BootRumbleTargetPidNotPresent, result.InitialAcquisitionRetryReason);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.Equal(5000, h.ElapsedSettleMilliseconds);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_stops_settling_if_center_m_authority_changes()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            FailTransitionOnCall = 1,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = static () => [],
        };
        h.AuthoritySnapshot = () => h.ElapsedSettleMilliseconds >= 1000
            ? FrontendCenterMStartupState.Enabled
            : FrontendCenterMStartupState.Disabled;

        var result = await h.Build().AcquireAsync(default);

        Assert.False(result.IsOwned);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.Equal(1000, h.ElapsedSettleMilliseconds);
        Assert.False(h.InputSource.IsRunning);
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("multiple")]
    [InlineData("wrong-usage")]
    public async Task A2vm_boot_prime_fails_closed_for_unusable_fresh_pid1901_endpoint(string endpointCase)
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        h.Pid1901DeviceSnapshot = endpointCase switch
        {
            "weak" => () => [Pid1901ControlDevice("", usagePage: 0xFFA0, usage: 0x0001)],
            "multiple" => () =>
            [
                Pid1901ControlDevice("A2VM_XINPUT_123"),
                Pid1901ControlDevice("OTHER_XINPUT_456"),
            ],
            _ => () => [Pid1901ControlDevice("A2VM_XINPUT_123", usagePage: 0xFFA0, usage: 0x0002)],
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.False(result.IsOwned);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_does_not_accept_pid1901_while_pid1902_is_still_present()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            FailTransitionOnCall = 1,
            PnpDevices = A2vmOwnedPnpDevices(),
            OldPid1902PresentDuringSettle = true,
            Pid1901DeviceSnapshot = static () => [Pid1901ControlDevice("A2VM_XINPUT_123")],
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.False(result.IsOwned);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.Equal(0, h.ElapsedSettleMilliseconds);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Theory]
    [InlineData("AlreadyClaimed")]
    [InlineData("Unavailable")]
    public async Task A2vm_boot_prime_skips_optional_cycle_when_marker_is_not_claimed(string markerName)
    {
        var marker = Enum.Parse<BootSessionAttemptResult>(markerName);
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => marker,
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.False(result.ModeWriteIssued);
        Assert.Empty(h.SwitchTargets);
        Assert.True(h.InputSource.IsRunning);
    }

    [Fact]
    public async Task A2vm_initial_pid1901_consumes_boot_attempt_and_uses_only_normal_transition()
    {
        var claimCount = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => { claimCount++; return BootSessionAttemptResult.Claimed; },
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal(1, claimCount);
        Assert.Equal([MsiClawNativeMode.DirectInput], h.SwitchTargets);
    }

    [Fact]
    public async Task A2vm_pid1901_first_boot_consumes_marker_so_same_boot_pid1902_restart_does_not_cycle()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var marker = Path.Combine(directory, "a2vm-rumble-boot-attempt.txt");
            var windowsStartId = BootSession.TryCreateWindowsStartId(
                100,
                new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
                "0");
            var first = new Harness
            {
                InitialMode = MsiClawNativeMode.XInput,
                HardwareDeviceModel = new("msi.claw.a2vm.7"),
                ClaimBootAttempt = () => BootSession.TryClaimA2vmRumbleAttempt(marker, windowsStartId),
            };
            var firstOwner = first.Build();
            Assert.True((await firstOwner.AcquireAsync(default)).IsOwned);
            Assert.Equal([MsiClawNativeMode.DirectInput], first.SwitchTargets);
            await firstOwner.DisposeAsync();

            var restarted = new Harness
            {
                InitialMode = MsiClawNativeMode.DirectInput,
                HardwareDeviceModel = new("msi.claw.a2vm.7"),
                PnpDevices = A2vmOwnedPnpDevices(),
                ClaimBootAttempt = () => BootSession.TryClaimA2vmRumbleAttempt(marker, windowsStartId),
            };
            var restartedOwner = restarted.Build();
            Assert.True((await restartedOwner.AcquireAsync(default)).IsOwned);
            Assert.Empty(restarted.SwitchTargets);
            await restartedOwner.DisposeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Non_A2vm_models_never_claim_or_run_the_boot_prime_cycle()
    {
        var claimCount = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.cg3em"),
            ClaimBootAttempt = () => { claimCount++; return BootSessionAttemptResult.Claimed; },
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal(0, claimCount);
        Assert.Empty(h.SwitchTargets);
    }

    [Fact]
    public async Task A2vm_boot_prime_transition_failure_fails_closed_before_directinput_acquisition()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.7"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            SwitchFailsForRelease = true,
            PnpDevices = A2vmOwnedPnpDevices(),
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_skips_mode_writes_when_exact_control_hid_is_not_resolved()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.7"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            PnpDevices = [PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00")],
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Empty(h.SwitchTargets);
    }

    [Fact]
    public async Task A2vm_boot_prime_does_not_claim_or_cycle_when_center_m_is_not_disabled()
    {
        var claimCount = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            Authority = FrontendCenterMStartupState.Enabled,
            ClaimBootAttempt = () => { claimCount++; return BootSessionAttemptResult.Claimed; },
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(0, claimCount);
        Assert.Empty(h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
    }

    [Fact]
    public async Task A2vm_boot_prime_fails_closed_when_fresh_pid1901_state_is_not_proven()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = static () => [],
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal([MsiClawNativeMode.XInput], h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_boot_prime_fails_closed_when_second_native_transition_is_not_proven()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.Claimed,
            SwitchSucceeds = false,
            PnpDevices = A2vmOwnedPnpDevices(),
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_first_boot_transition_timeout_is_typed_and_retry_does_not_repeat_optional_prime()
    {
        var markerCalls = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => ++markerCalls == 1 ? BootSessionAttemptResult.Claimed : BootSessionAttemptResult.AlreadyClaimed,
            FailTransitionOnCall = 1,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = static () => [],
        };
        var owner = h.Build();

        var first = await owner.AcquireAsync(default);
        Assert.Equal(Devices.MSI.Claw.MsiClawInitialAcquisitionRetryReason.BootRumbleTargetPidNotPresent, first.InitialAcquisitionRetryReason);
        Assert.False(first.IsOwned);
        Assert.False(h.InputSource.IsRunning);
        Assert.Equal(5000, h.ElapsedSettleMilliseconds);

        var retry = await owner.AcquireAsync(default);

        Assert.True(retry.IsOwned, retry.Reason);
        Assert.Equal(2, markerCalls);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.True(h.InputSource.IsRunning);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task A2vm_boot_second_leg_partial_pid1902_arrival_defers_and_completes_without_repeating_prime(string modelId)
    {
        var markerCalls = 0;
        var pid1901ProbeCalls = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new(modelId),
            ClaimBootAttempt = () => ++markerCalls == 1 ? BootSessionAttemptResult.Claimed : BootSessionAttemptResult.AlreadyClaimed,
            FailTransitionOnCall = 2,
            FailureTargetPidPresent = true,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = () => ++pid1901ProbeCalls == 1
                ? [Pid1901ParentDevice("A2VM_XINPUT_123"), Pid1901ControlDevice("A2VM_XINPUT_123")]
                : [],
        };
        var owner = h.Build();

        var first = await owner.AcquireAsync(default);
        Assert.Equal(Devices.MSI.Claw.MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent, first.InitialAcquisitionRetryReason);
        Assert.False(first.IsOwned);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);

        var retry = await owner.AcquireAsync(default);

        Assert.True(retry.IsOwned, retry.Reason);
        Assert.Equal(2, markerCalls);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.False(retry.ModeWriteIssued);
        Assert.True(h.InputSource.IsRunning);
        Assert.Equal([PrimaryPnp, ControlPnp], h.HidHideApplied);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task A2vm_boot_second_leg_parent_only_recheck_stays_pending_until_exact_control_hid_arrives(string modelId)
    {
        var markerCalls = 0;
        var pid1901ProbeCalls = 0;
        var targetProbeCalls = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new(modelId),
            ClaimBootAttempt = () => ++markerCalls == 1 ? BootSessionAttemptResult.Claimed : BootSessionAttemptResult.AlreadyClaimed,
            FailTransitionOnCall = 2,
            FailureTargetPidPresent = true,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = () => ++pid1901ProbeCalls == 1
                ? [Pid1901ParentDevice("A2VM_XINPUT_123"), Pid1901ControlDevice("A2VM_XINPUT_123")]
                : [],
            Pid1902DeviceSnapshot = () => ++targetProbeCalls == 1
                ? [PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00")]
                : A2vmOwnedPnpDevices(),
        };
        var owner = h.Build();

        var partialTransition = await owner.AcquireAsync(default);
        var parentOnlyRecheck = await owner.AcquireAsync(default);

        Assert.Equal(MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent, partialTransition.InitialAcquisitionRetryReason);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent, parentOnlyRecheck.InitialAcquisitionRetryReason);
        Assert.Equal("Pid1902ControlHidStillEnumerating", parentOnlyRecheck.Reason);
        Assert.False(parentOnlyRecheck.IsOwned);
        Assert.False(parentOnlyRecheck.ModeWriteIssued);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.Empty(h.GamepadMode.QueryIdentities);
        Assert.Empty(h.GamepadMode.SwitchTargets);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);

        var completed = await owner.AcquireAsync(default);

        Assert.True(completed.IsOwned, completed.Reason);
        Assert.Equal(3, markerCalls);
        Assert.Equal(2, targetProbeCalls);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.Single(h.GamepadMode.QueryIdentities);
        Assert.Empty(h.GamepadMode.SwitchTargets);
        Assert.True(h.InputSource.IsRunning);
        Assert.Equal([PrimaryPnp, ControlPnp], h.HidHideApplied);
    }

    [Theory]
    [InlineData("multiple-roots")]
    [InlineData("ambiguous-command-hid")]
    [InlineData("unverified-control")]
    [InlineData("old-pid-present")]
    [InlineData("target-pid-absent")]
    [InlineData("marker-unavailable")]
    [InlineData("read-failure")]
    public async Task A2vm_boot_second_leg_recheck_does_not_defer_when_proof_is_incomplete(string topology)
    {
        var markerCalls = 0;
        var pid1901ProbeCalls = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => ++markerCalls == 1
                ? BootSessionAttemptResult.Claimed
                : topology == "marker-unavailable"
                    ? BootSessionAttemptResult.Unavailable
                    : BootSessionAttemptResult.AlreadyClaimed,
            FailTransitionOnCall = 2,
            FailureTargetPidPresent = true,
            PnpDevices = A2vmOwnedPnpDevices(),
            Pid1901DeviceSnapshot = () => ++pid1901ProbeCalls == 1
                ? [Pid1901ParentDevice("A2VM_XINPUT_123"), Pid1901ControlDevice("A2VM_XINPUT_123")]
                : [],
        };
        h.GamepadMode.QuerySucceeds = false;
        h.GamepadMode.SwitchSucceeds = false;
        h.GamepadMode.SwitchWriteIssued = false;
        h.Pid1902DeviceSnapshot = topology switch
        {
            "multiple-roots" => () =>
            [
                PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
                PnpCollection(OtherPrimaryPnp, OtherPhysKey, 0x0001, 0x0005, "MI_00"),
            ],
            "ambiguous-command-hid" => () =>
            [
                PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
                PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
                PnpCollection(SecondControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
            ],
            "unverified-control" => () =>
            [
                PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
                PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0041, "MI_00"),
            ],
            "old-pid-present" => A2vmOwnedPnpDevices,
            "target-pid-absent" => static () => [],
            "marker-unavailable" => () => [PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00")],
            "read-failure" => () => throw new InvalidOperationException("Simulated target PnP read failure."),
            _ => throw new ArgumentOutOfRangeException(nameof(topology)),
        };
        if (topology == "old-pid-present")
            h.Pid1901DeviceSnapshot = static () => [Pid1901ParentDevice("A2VM_XINPUT_123"), Pid1901ControlDevice("A2VM_XINPUT_123")];

        var owner = h.Build();
        _ = await owner.AcquireAsync(default);
        var result = await owner.AcquireAsync(default);

        Assert.False(result.IsOwned);
        Assert.True(h.GamepadMode.QueryIdentities.Count == 1, $"{topology}: {result.Reason}");
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
        Assert.NotEqual("Pid1902ControlHidStillEnumerating", result.Reason);
        Assert.False(result.ModeWriteIssued);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.Single(h.GamepadMode.QueryIdentities);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.Empty(h.HidHideApplied);
        Assert.False(h.InputSource.StartCalled);
    }

    [Fact]
    public async Task A2vm_initial_pid1901_timeout_retries_only_the_forward_pid1902_transition()
    {
        var markerCalls = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => ++markerCalls == 1 ? BootSessionAttemptResult.Claimed : BootSessionAttemptResult.AlreadyClaimed,
            FailTransitionOnCall = 1,
            ModeAfterFailedTransition = MsiClawNativeMode.XInput,
        };
        var owner = h.Build();

        var first = await owner.AcquireAsync(default);
        Assert.Equal(Devices.MSI.Claw.MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent, first.InitialAcquisitionRetryReason);

        var retry = await owner.AcquireAsync(default);

        Assert.True(retry.IsOwned, retry.Reason);
        Assert.Equal(2, markerCalls);
        Assert.Equal([MsiClawNativeMode.DirectInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
    }

    [Fact]
    public void Initial_transition_timeout_retry_predicate_requires_all_absence_proof()
    {
        static MsiClawModeTransitionResult Result(
            MsiClawModeTransitionStatus status = MsiClawModeTransitionStatus.TargetDeviceDidNotAppear,
            bool write = true, bool oldGone = true, bool targetPidPresent = false,
            bool targetAppeared = false, bool sourceVerified = true, bool targetVerified = false,
            MsiClawNativeMode from = MsiClawNativeMode.XInput) =>
            new(status, from, MsiClawNativeMode.DirectInput, 0x1901, 0x1902,
                write, oldGone, targetAppeared, sourceVerified, targetVerified, 5000,
                "test", targetPidPresent);

        Assert.True(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(targetPidPresent: true), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(status: MsiClawModeTransitionStatus.AmbiguousDevice), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(write: false), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(oldGone: false), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(sourceVerified: false), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(targetAppeared: true), MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput));
        Assert.False(MsiClawAddonPhysicalOwnership.IsTargetNotPresentAfterVerifiedWrite(
            Result(), MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput));
    }

    [Fact]
    public void A2vm_boot_second_leg_partial_pid1902_classifier_requires_the_exact_verified_transition()
    {
        static MsiClawModeTransitionResult Result(
            MsiClawModeTransitionStatus status = MsiClawModeTransitionStatus.TargetDeviceDidNotAppear,
            bool write = true, bool oldGone = true, bool targetPidPresent = true,
            bool targetAppeared = false, bool sourceVerified = true, bool targetVerified = false,
            MsiClawNativeMode from = MsiClawNativeMode.XInput,
            MsiClawNativeMode target = MsiClawNativeMode.DirectInput) =>
            new(status, from, target, 0x1901, 0x1902,
                write, oldGone, targetAppeared, sourceVerified, targetVerified, 5000,
                "test", targetPidPresent);

        Assert.True(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(), bootRumbleCycleEnabled: true));

        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(), bootRumbleCycleEnabled: false));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(status: MsiClawModeTransitionStatus.WriteFailed), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(status: MsiClawModeTransitionStatus.AmbiguousDevice), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(write: false), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(oldGone: false), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(targetPidPresent: false), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(targetAppeared: true), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(sourceVerified: false), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(targetVerified: true), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(from: MsiClawNativeMode.DirectInput), bootRumbleCycleEnabled: true));
        Assert.False(MsiClawAddonPhysicalOwnership.IsA2vmBootRumbleSecondLegPartialPid1902ArrivalAfterVerifiedWrite(
            Result(target: MsiClawNativeMode.XInput), bootRumbleCycleEnabled: true));
    }

    [Fact]
    public async Task Non_a2vm_partial_pid1902_arrival_remains_terminal()
    {
        var claimCount = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            HardwareDeviceModel = new("msi.claw.cg3em"),
            ClaimBootAttempt = () => { claimCount++; return BootSessionAttemptResult.Claimed; },
            FailTransitionOnCall = 1,
            FailureTargetPidPresent = true,
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
        Assert.Equal(0, claimCount);
        Assert.Equal([MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task A2vm_initial_pid1901_partial_pid1902_arrival_remains_strict()
    {
        var markerCalls = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => { markerCalls++; return BootSessionAttemptResult.Claimed; },
            FailTransitionOnCall = 1,
            FailureTargetPidPresent = true,
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
        Assert.Equal(1, markerCalls);
        Assert.Equal([MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7", true, true)]
    [InlineData("msi.claw.a2vm.8", true, true)]
    [InlineData("msi.claw.cg3em", false, true)]
    [InlineData("unknown", false, false)]
    public void Rumble_recovery_model_policy_is_exact(string modelId, bool bootPrime, bool manualRecovery)
    {
        var model = new HandheldDeviceModelId(modelId);
        Assert.Equal(bootPrime, MsiClawAddonPhysicalOwnership.IsA2vmBootRumblePrimeModel(model));
        Assert.Equal(manualRecovery, MsiClawAddonPhysicalOwnership.SupportsManualRumbleRearm(model));
    }

    [Fact]
    public async Task Acquisition_applies_the_exact_current_primary_and_auxiliary_target_set()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            PnpDevices =
            [
                PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
                PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
                PnpCollection(ConsumerPnp, PhysKey, 0x000C, 0x0001, "MI_01"),
            ],
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal([PrimaryPnp, ControlPnp, ConsumerPnp], result.HiddenTargets);
        Assert.Equal([PrimaryPnp, ControlPnp, ConsumerPnp], h.HidHideApplied);
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

    [Fact]
    public async Task Developer_rearm_runs_and_proves_both_real_mode_transitions_before_recovery()
    {
        var h = CreateRearmHarness();
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        var eventStart = h.Events.Count;

        var result = await owner.RunDeveloperRumbleRearmAsync(() => true);

        Assert.Equal(DeveloperRumbleRearmPhysicalOutcome.Completed, result.Outcome);
        Assert.True(result.XInputTransitionVerified);
        Assert.True(result.DirectInputTransitionVerified);
        Assert.True(result.PhysicalOwnershipRestored);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], h.SwitchTargets);
        Assert.True(h.InputSource.IsRunning);
        Assert.Equal(2, h.InputSource.StartCallCount);
        var events = h.Events.Skip(eventStart).ToArray();
        Assert.True(Array.IndexOf(events, "InputStop") < Array.IndexOf(events, "ModeSwitch:XInput"));
        Assert.True(Array.IndexOf(events, "ModeSwitch:XInput") < Array.IndexOf(events, "ModeSwitch:DirectInput"));
        Assert.True(Array.IndexOf(events, "ModeSwitch:DirectInput") < Array.IndexOf(events, "InputStart"));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Developer_rearm_does_not_write_a_mode_command_when_directinput_cleanup_is_unproven()
    {
        var h = CreateRearmHarness();
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.InputSource.CleanupProven = false;

        var result = await owner.RunDeveloperRumbleRearmAsync(() => true);

        Assert.Equal(DeveloperRumbleRearmPhysicalOutcome.Failed, result.Outcome);
        Assert.Equal("DirectInputCleanupUnproven", result.Reason);
        Assert.False(result.ModeWriteIssued);
        Assert.Empty(h.SwitchTargets);
        Assert.False(h.InputSource.IsRunning);

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Developer_rearm_fails_closed_when_either_native_transition_is_not_proven()
    {
        var xInputFailure = CreateRearmHarness();
        var xInputOwner = xInputFailure.Build();
        Assert.True((await xInputOwner.AcquireAsync(default)).IsOwned);
        xInputFailure.SwitchFailsForRelease = true;

        var xInputResult = await xInputOwner.RunDeveloperRumbleRearmAsync(() => true);

        Assert.Equal(DeveloperRumbleRearmPhysicalOutcome.Failed, xInputResult.Outcome);
        Assert.False(xInputResult.XInputTransitionVerified);
        Assert.False(xInputResult.PhysicalOwnershipRestored);
        Assert.Equal([MsiClawNativeMode.XInput], xInputFailure.SwitchTargets);
        Assert.False(xInputFailure.InputSource.IsRunning);
        await xInputOwner.DisposeAsync();

        var directInputFailure = CreateRearmHarness();
        var directInputOwner = directInputFailure.Build();
        Assert.True((await directInputOwner.AcquireAsync(default)).IsOwned);
        directInputFailure.SwitchSucceeds = false;

        var directInputResult = await directInputOwner.RunDeveloperRumbleRearmAsync(() => true);

        Assert.Equal(DeveloperRumbleRearmPhysicalOutcome.Failed, directInputResult.Outcome);
        Assert.True(directInputResult.XInputTransitionVerified);
        Assert.False(directInputResult.DirectInputTransitionVerified);
        Assert.False(directInputResult.PhysicalOwnershipRestored);
        Assert.Equal([MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput], directInputFailure.SwitchTargets);
        Assert.False(directInputFailure.InputSource.IsRunning);
        await directInputOwner.DisposeAsync();
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task Windows_session_end_stops_owned_pid1902_then_writes_one_xinput_command(string modelId)
    {
        var h = new Harness
        {
            HardwareDeviceModel = new(modelId),
            ClaimBootAttempt = () => BootSessionAttemptResult.AlreadyClaimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        var hiddenTargetsBefore = h.HidHideApplied.ToArray();
        h.Events.Clear();

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.CommandWritten, result.Outcome);
        Assert.Equal(1, h.XInputCommandWriteCalls);
        Assert.Empty(h.SwitchTargets); // no verified transition and no PID1901 wait
        Assert.True(h.Events.IndexOf("InputStop") >= 0);
        Assert.True(h.Events.IndexOf("InputStop") < h.Events.IndexOf("XInputCommand"));
        Assert.False(h.InputSource.IsRunning);
        Assert.Null(owner.LiveInputSource);
        Assert.Equal(hiddenTargetsBefore, h.HidHideApplied); // persistent HidHide remains untouched
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_skips_when_center_m_is_not_disabled()
    {
        var h = new Harness
        {
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.AlreadyClaimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.Authority = FrontendCenterMStartupState.Enabled;

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.Skipped, result.Outcome);
        Assert.Equal(0, h.XInputCommandWriteCalls);
        Assert.True(h.InputSource.IsRunning);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_skips_non_a2vm_models_without_a_mode_write()
    {
        var h = new Harness { HardwareDeviceModel = new("msi.claw.cg3em") };
        var owner = h.Build();

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.Skipped, result.Outcome);
        Assert.Equal("UnsupportedModel", result.Reason);
        Assert.Equal(0, h.XInputCommandWriteCalls);
        Assert.Empty(h.SwitchTargets);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_skips_when_no_strong_owned_input_session_exists()
    {
        var h = new Harness { HardwareDeviceModel = new("msi.claw.a2vm.8") };
        var owner = h.Build();

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.Skipped, result.Outcome);
        Assert.Equal("OwnedDirectInputSourceUnavailable", result.Reason);
        Assert.Equal(0, h.XInputCommandWriteCalls);
        Assert.Empty(h.SwitchTargets);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_skips_when_pid1902_command_hid_is_ambiguous()
    {
        var h = new Harness
        {
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.AlreadyClaimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.PnpDevices = A2vmOwnedPnpDevices()
            .Append(PnpCollection("HID\\VID_0DB0&PID_1902&MI_00\\CTRL-AMBIGUOUS", PhysKey, 0xFFF0, 0x0040, "MI_00"))
            .ToArray();

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.Skipped, result.Outcome);
        Assert.Equal("UniquePid1902CommandHidUnavailable", result.Reason);
        Assert.Equal(0, h.XInputCommandWriteCalls);
        Assert.True(h.InputSource.IsRunning);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_skips_command_when_directinput_cleanup_is_unproven()
    {
        var h = new Harness
        {
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.AlreadyClaimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.InputSource.CleanupProven = false;

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.Skipped, result.Outcome);
        Assert.Equal(0, h.XInputCommandWriteCalls);
        Assert.Contains("DirectInputCleanupUnproven", result.Reason);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_reports_one_failed_write_without_retry()
    {
        var h = new Harness
        {
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.AlreadyClaimed,
            PnpDevices = A2vmOwnedPnpDevices(),
            XInputCommandWriteSucceeds = false,
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);

        var result = await owner.PrepareForWindowsSessionEndAsync(CancellationToken.None);

        Assert.Equal(A2vmWindowsSessionEndOutcome.WriteFailed, result.Outcome);
        Assert.Equal(1, h.XInputCommandWriteCalls);
        Assert.Empty(h.SwitchTargets);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Windows_session_end_cancellation_after_slow_input_stop_prevents_a_late_mode_write()
    {
        var h = new Harness
        {
            HardwareDeviceModel = new("msi.claw.a2vm.8"),
            ClaimBootAttempt = () => BootSessionAttemptResult.AlreadyClaimed,
            PnpDevices = A2vmOwnedPnpDevices(),
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        var cleanupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.InputSource.StopCleanupGate = cleanupGate;
        using var cancellation = new CancellationTokenSource();

        var preparation = owner.PrepareForWindowsSessionEndAsync(cancellation.Token);
        cancellation.Cancel();
        cleanupGate.TrySetResult();
        var result = await preparation;

        Assert.Equal(A2vmWindowsSessionEndOutcome.Skipped, result.Outcome);
        Assert.Equal(0, h.XInputCommandWriteCalls);
        Assert.Empty(h.SwitchTargets);
        await owner.DisposeAsync();
    }

    private static Harness CreateRearmHarness() => new()
    {
        RecordModeSwitchEvents = true,
        PnpDevices =
        [
            PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
            PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
            PnpCollection(ConsumerPnp, PhysKey, 0x000C, 0x0001, "MI_01"),
        ],
    };

    private static IReadOnlyList<ControllerDeviceInfo> A2vmOwnedPnpDevices() =>
    [
        PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
        PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
    ];

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
        Assert.Equal(status == "DeviceNotFound", result.NativeDeviceAbsentAtInitialCapture);
        Assert.Equal(MsiClawInitialAcquisitionRetryReason.None, result.InitialAcquisitionRetryReason);
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

    // ---- PR11 section 15: cross-mode root change is allowed; DI identity must still match ----

    [Fact] // a PID1901->PID1902 transition whose fresh final PID1902 root differs from the PID1901
           // root is NOT rejected for that reason -- the Addon-issued transition is the bridge.
    public async Task Cross_mode_root_change_is_accepted_when_the_transition_and_live_directinput_verify()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,                 // initial PID1901 root == PhysKey
            FinalPhysKey = @"USB\VID_0DB0\PID1902ROOT",             // DIFFERENT PID1902 physical root
            DirectInputPnpPhysKey = @"USB\VID_0DB0\PID1902ROOT",    // live DI descriptor matches the FINAL PID1902 identity
        };
        var owner = h.Build();
        var result = await owner.AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal(1, h.SwitchCalls);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied);
        // PR11 section 19: a live PR5 input source MUST exist after a cross-mode ownership success,
        // so PR6/PR7 can attach an X360/SteamDeck presentation instead of "no live PR5 input source".
        Assert.NotNull(owner.LiveInputSource);
        Assert.True(owner.LiveInputSource!.IsRunning);
    }

    [Fact] // ...but a DirectInput descriptor whose PID1902 identity does not match the fresh final
           // PID1902 native identity still fails closed (stale/other-device protection).
    public async Task Final_pid1902_identity_not_matching_the_directinput_descriptor_fails_closed()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            FinalPhysKey = @"USB\VID_0DB0\PID1902ROOT",
            DirectInputPnpPhysKey = OtherPhysKey, // descriptor belongs to a different current PID1902 identity
        };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("DirectInputPhysicalIdentityMismatch", result.Reason);
        Assert.DoesNotContain(h.SwitchTargets, t => t == MsiClawNativeMode.XInput); // no PID1901 rollback
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact] // an already-PID1902 boot keeps the strict SAME-MODE identity check
    public async Task Already_pid1902_boot_still_fails_on_a_same_mode_identity_mismatch()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, SecondCapturePhysKey = OtherPhysKey };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("SameModeIdentityMismatch", result.Reason);
        Assert.Equal(0, h.SwitchCalls);
        Assert.False(h.InputSource.StartCalled);
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
        Assert.Equal([PrimaryPnp], release.HiddenTargets);
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
        Assert.Contains("Pid1901RestoreFailed", release.Reason);
        Assert.Equal([PrimaryPnp], release.HiddenTargets); // still surfaced so the caller does not lose it
        Assert.True(h.InputSource.StopCalled);
    }

    [Fact] // PR11 sections 9/10: the inverse PID1902->PID1901 transition is proven by the controlled
           // transition + a fresh XInput capture. A different PID1901 root is NOT a failure, and the
           // misleading literal "Pid1901RestoreUnverified:Ok" can no longer be emitted.
    public async Task Release_succeeds_across_a_cross_mode_root_change_and_never_reports_restore_unverified_ok()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,        // owned PID1902 root == PhysKey
            FinalPhysKey = @"USB\VID_0DB0\PID1901_ROOT_AFTER",  // fresh PID1901 root after the release switch
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(new[] { MsiClawNativeMode.XInput }, h.SwitchTargets);
        Assert.DoesNotContain("Pid1901RestoreUnverified:Ok", release.Reason);
    }

    [Fact] // PR11 section 10: final mode not XInput is its own reason, not "...:Ok"
    public async Task Release_when_the_final_capture_is_not_xinput_fails_with_a_precise_reason()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            FinalModeAfterSwitch = MsiClawNativeMode.DirectInput, // the switch "succeeded" but PID1902 remains
        };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.False(release.Succeeded);
        Assert.Contains("Pid1901RestoreFinalModeNotXInput", release.Reason);
        Assert.DoesNotContain(":Ok", release.Reason);
    }

    [Fact]
    public async Task Release_remembers_the_verified_target_even_when_the_persistent_apply_fails()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, HidHideOutcome = AddonHidHideBaselineOutcome.VerificationFailed };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal([PrimaryPnp], release.HiddenTargets); // held from the verified descriptor, not the failed apply
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
        Assert.Equal([prior], release.HiddenTargets); // recovered from persistent HidHide
    }

    [Fact]
    public async Task Blocked_boot_release_recovers_the_persisted_target_without_any_acquisition()
    {
        const string prior = @"HID\VID_0DB0&PID_1902&MI_00&COL01\prev";
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, ExistingOwnedTarget = prior };
        var owner = h.Build(); // AcquireAsync is never called on a Blocked boot

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal([prior], release.HiddenTargets);
        Assert.Equal(new[] { MsiClawNativeMode.XInput }, h.SwitchTargets); // PID1902 -> PID1901
    }

    [Fact]
    public async Task Release_fails_closed_on_an_unsupported_native_mode()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.Other, ExistingOwnedTarget = PrimaryPnp };
        var owner = h.Build();

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.False(release.Succeeded);
        Assert.Contains("UnsupportedReleaseMode", release.Reason);
        Assert.Equal(0, h.SwitchCalls); // no PID1901 write against an unknown mode
    }

    [Fact]
    public async Task Release_without_a_prior_acquisition_and_already_stock_pid_is_a_noop_success()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput };
        var owner = h.Build();

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Empty(release.HiddenTargets);
        Assert.Equal(0, h.SwitchCalls);
        Assert.False(h.InputSource.StopCalled);
    }

    [Fact]
    public async Task Disabled_boot_pid1902_mode5_is_reconciled_to_mode2_before_directinput_acquisition()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.GamepadMode.ObservedMode = MsiClawGamepadMode.Bios;

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.Empty(h.SwitchTargets);
        Assert.True(h.InputSource.StartCalled);
    }

    [Fact]
    public async Task Disabled_boot_pid1902_desktop_mode_is_reconciled_to_directinput()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.GamepadMode.ObservedMode = MsiClawGamepadMode.Desktop;

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.True(h.InputSource.StartCalled);
    }

    [Fact]
    public async Task Disabled_boot_mode5_reconcile_failure_stops_before_directinput_acquisition()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.GamepadMode.ObservedMode = MsiClawGamepadMode.Bios;
        h.GamepadMode.SwitchSucceeds = false;

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("GamepadModeDirectInputNotVerified", result.Reason);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.SwitchTargets);
    }

    [Fact]
    public async Task Disabled_boot_query_unavailable_gets_one_bounded_mode2_normalization_attempt()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.GamepadMode.QuerySucceeds = false;

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.True(result.ModeWriteIssued);
        Assert.True(h.InputSource.StartCalled);
    }

    [Fact]
    public async Task Disabled_boot_query_unavailable_and_mode2_verification_failure_fails_closed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.GamepadMode.QuerySucceeds = false;
        h.GamepadMode.SwitchSucceeds = false;

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("GamepadModeDirectInputNotVerified", result.Reason);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Disabled_boot_msi_mode_is_normalized_by_the_same_mode2_invariant()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.GamepadMode.ObservedMode = MsiClawGamepadMode.Msi;

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.True(result.ModeWriteIssued);
        Assert.True(h.InputSource.StartCalled);
    }

    [Fact]
    public async Task Disabled_boot_mode2_normalization_is_blocked_when_authority_changes_before_the_write()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            Authority = FrontendCenterMStartupState.Enabled,
        };
        h.GamepadMode.ObservedMode = MsiClawGamepadMode.Desktop;

        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("AuthorityChangedBeforeGamepadModeNormalization", result.Reason);
        Assert.Empty(h.GamepadMode.SwitchTargets);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Pid1901_startup_verifies_gamepad_mode_using_the_final_pid1902_identity()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            FinalPhysKey = @"USB\VID_0DB0\PID1902ROOT",
            DirectInputPnpPhysKey = @"USB\VID_0DB0\PID1902ROOT",
        };

        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        var queryIdentity = Assert.Single(h.GamepadMode.QueryIdentities);
        Assert.Equal(h.FinalPhysKey, queryIdentity.PhysicalDeviceKey);
        Assert.Equal(MsiClawGamepadMode.DirectInput, h.GamepadMode.ObservedMode);
        Assert.Empty(h.GamepadMode.SwitchTargets);
    }

    // ================= PR8: owned DirectInput session recovery (work order section 21) =================

    private static async Task<(MsiClawAddonPhysicalOwnership Owner, Harness Harness)> AcquiredThenLost(Harness h)
    {
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.InputSource.SimulateSessionLoss();
        h.Recovering = true;
        h.Events.Clear();
        return (owner, h);
    }

    [Fact] // 21.1
    public async Task Recovery_of_the_same_pid1902_reacquires_the_same_source_with_no_mode_write()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", recovery.Reason);
        Assert.Equal([PrimaryPnp], recovery.HiddenTargets);
        Assert.Same(h.InputSource, owner.LiveInputSource);
        Assert.True(h.InputSource.IsRunning);
        Assert.Equal(2, h.InputSource.StartCallCount); // the SAME source, started again
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact]
    public async Task Same_pid1902_recovery_normalizes_desktop_mode_without_pid_reenumeration()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.GamepadMode.RecoveryObservedMode = MsiClawGamepadMode.Desktop;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(0, h.RecoverySwitchCalls);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.True(h.InputSource.IsRunning);
    }

    [Fact]
    public async Task Same_pid1902_recovery_normalizes_msi_mode_without_pid_reenumeration()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.GamepadMode.RecoveryObservedMode = MsiClawGamepadMode.Msi;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(0, h.RecoverySwitchCalls);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
    }

    [Fact]
    public async Task Same_pid1902_recovery_mode2_normalization_failure_stops_before_hidhide_or_restart()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.GamepadMode.RecoveryObservedMode = MsiClawGamepadMode.Desktop;
        h.GamepadMode.RecoverySwitchSucceeds = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("GamepadModeDirectInputNotVerified", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact]
    public async Task Recovery_keeps_primary_identity_and_reconciles_a_refreshed_auxiliary_instance()
    {
        const string refreshedConsumer = @"HID\VID_0DB0&PID_1902&MI_01&COL03\8&REFRESHED&0&0002";
        var hSnapshot = 0;
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.DirectInput,
            PnpDeviceSnapshot = () => hSnapshot++ == 0
                ? [
                    PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
                    PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
                    PnpCollection(ConsumerPnp, PhysKey, 0x000C, 0x0001, "MI_01"),
                ]
                : [
                    PnpCollection(PrimaryPnp, PhysKey, 0x0001, 0x0005, "MI_00"),
                    PnpCollection(ControlPnp, PhysKey, 0xFFF0, 0x0040, "MI_00"),
                    PnpCollection(refreshedConsumer, PhysKey, 0x000C, 0x0001, "MI_01"),
                ],
        };
        var (owner, _) = await AcquiredThenLost(h);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal([PrimaryPnp, ControlPnp, refreshedConsumer], recovery.HiddenTargets);
        Assert.Equal([PrimaryPnp, ControlPnp, ConsumerPnp, PrimaryPnp, ControlPnp, refreshedConsumer], h.HidHideApplied);
    }

    // ---- Full1902 production rumble physical identity/generation (work order section 6 / 19.5) ----

    [Fact]
    public async Task Successful_acquisition_publishes_the_verified_descriptor_identity_and_first_generation()
    {
        var owner = new Harness { InitialMode = MsiClawNativeMode.DirectInput }.Build();

        Assert.True((await owner.AcquireAsync(default)).IsOwned);

        var identity = ((IMsiClawPhysicalInputIdentityProvider)owner).CurrentIdentity;
        Assert.NotNull(identity);
        Assert.Equal(PrimaryPnp, identity!.PnpInstanceId);
        Assert.Equal(1, ((IMsiClawPhysicalInputIdentityProvider)owner).CurrentSessionGeneration);
    }

    [Fact]
    public async Task Real_loss_and_recovery_advances_the_physical_session_generation()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        var provider = (IMsiClawPhysicalInputIdentityProvider)owner;
        Assert.Equal(1, provider.CurrentSessionGeneration);

        Assert.True((await owner.RecoverLostInputAsync(default)).IsOwned);

        Assert.Equal(2, provider.CurrentSessionGeneration);
        Assert.NotNull(provider.CurrentIdentity);
        Assert.Equal(PrimaryPnp, provider.CurrentIdentity!.PnpInstanceId);
    }

    [Fact]
    public async Task A_failed_recovery_leaves_no_usable_rumble_identity()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = AddonHidHideBaselineOutcome.Conflict; // recovery fails at the HidHide step

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        Assert.Null(((IMsiClawPhysicalInputIdentityProvider)owner).CurrentIdentity);
    }

    [Fact]
    public void The_physical_owner_is_the_rumble_identity_provider_and_the_presentation_owner_never_bumps_the_generation()
    {
        Assert.True(typeof(IMsiClawPhysicalInputIdentityProvider).IsAssignableFrom(typeof(MsiClawAddonPhysicalOwnership)));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var presentation = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs"));
        // A virtual X360<->SteamDeck switch must not manufacture a physical-session generation.
        Assert.DoesNotContain("SessionGeneration", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishLivePhysicalSession", presentation, StringComparison.Ordinal);
    }

    [Fact] // 21.2 -- mandatory ordering
    public async Task Recovery_verifies_hidhide_before_restarting_directinput()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        await owner.RecoverLostInputAsync(default);

        Assert.Equal(
            new[] { "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 21.3
    public async Task Recovery_onto_a_different_strong_identity_fails_with_no_hidhide_no_start_no_mode_write()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = OtherPhysKey;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("PhysicalIdentityMismatch", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Equal(0, h.SwitchCalls);
    }

    // ---------- PR9: owned PID1901 drift reclaim (work order section 21) ----------

    /// <summary>Owned, then session lost, then the same device drifted to PID1901.</summary>
    private static async Task<(MsiClawAddonPhysicalOwnership Owner, Harness Harness)> AcquiredThenLostAsPid1901(Harness h)
    {
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.InputSource.SimulateSessionLoss();
        h.Recovering = true;
        h.RecoveryMode = MsiClawNativeMode.XInput; // current observed mode is PID1901
        h.RecoveryModeAfterReclaim = MsiClawNativeMode.DirectInput; // a successful reclaim lands PID1902
        h.Events.Clear();
        return (owner, h);
    }

    [Fact] // 21.1
    public async Task Same_owned_pid1901_reclaims_pid1902_then_continues_the_shared_recovery_tail()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalStateDriftReclaimed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal([PrimaryPnp], recovery.HiddenTargets);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.Equal(MsiClawNativeMode.DirectInput, h.LastSwitchTarget);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets); // never a reverse write
        Assert.Same(h.InputSource, owner.LiveInputSource);
        Assert.True(h.InputSource.IsRunning);
        // identity proven before the write; HidHide proven before the restart
        Assert.Equal(
            new[] { "NativeCapture", "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact]
    public async Task Pid1901_reclaim_normalizes_unexpected_non2_gamepad_mode_before_recovery_tail()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.GamepadMode.RecoveryObservedMode = MsiClawGamepadMode.Desktop;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalStateDriftReclaimed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.Equal([MsiClawGamepadMode.DirectInput], h.GamepadMode.SwitchTargets);
        Assert.True(h.InputSource.IsRunning);
    }

    [Fact] // PR11 section 16: real cross-mode drift -- owned PID1902 root A, drifts to PID1901 root B,
           // reclaims to PID1902 root C. The PID1901 root is NOT compared to the prior PID1902 root.
    public async Task Cross_mode_pid1901_drift_reclaims_even_when_all_three_roots_differ()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = @"USB\VID_0DB0\PID1901_ROOT_B";           // current PID1901 root, != owned PID1902 root
        h.RecoveryPhysKeyAfterReclaim = @"USB\VID_0DB0\PID1902_ROOT_C"; // fresh PID1902 root after the reclaim

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalStateDriftReclaimed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.Equal([PrimaryPnp], recovery.HiddenTargets); // exact owned target unchanged
    }

    [Fact] // review: a reclaim that succeeds then fails LATER in the tail must adopt the fresh PID1902
           // identity so a PR10 deferred Device Arrival retry continues the same-mode PR8 tail.
    public async Task Reclaim_that_fails_after_the_switch_still_lets_a_later_same_mode_retry_recover()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = @"USB\VID_0DB0\PID1901_ROOT_B";
        h.RecoveryPhysKeyAfterReclaim = @"USB\VID_0DB0\PID1902_ROOT_C";
        h.RecoveryPnp = OtherPrimaryPnp; // recovery #1: reclaim succeeds, then the tail fails (target changed)

        var first = await owner.RecoverLostInputAsync(default);
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, first.Outcome);
        Assert.Contains("RecoveredTargetChanged", first.Reason);
        Assert.True(first.ModeWriteIssued);

        // The controller is now PID1902 / root C. A later retry (e.g. PR10 deferred arrival) sees the
        // current same-mode identity, NOT the stale pre-drift one.
        h.RecoveryPnp = null;
        h.Events.Clear();
        var second = await owner.RecoverLostInputAsync(default);

        Assert.True(second.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", second.Reason); // same-mode PR8 tail
        Assert.False(second.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls); // NO second mode write
        Assert.Same(h.InputSource, owner.LiveInputSource);
    }

    [Theory] // 21.3
    [InlineData("Enabled")]
    [InlineData("Partial")]
    [InlineData("Unavailable")]
    public async Task Pid1901_reclaim_is_blocked_when_center_m_is_not_exactly_disabled(string authority)
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryAuthority = Enum.Parse<FrontendCenterMStartupState>(authority);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("AuthorityNotDisabled", recovery.Reason);
        Assert.Equal(0, h.SwitchCalls);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // 21.4
    public async Task Pid1902_reclaim_transition_failure_fails_closed_with_no_reverse_write()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoverySwitchSucceeds = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("OwnedPhysicalStateDriftReclaimFailed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls); // exactly one attempt
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Theory] // PR11 section 8: post-write verification -- the fresh final capture must be PID1902
    [InlineData("XInput")]  // final mode still PID1901
    [InlineData("Other")]   // final mode ambiguous / unsupported
    public async Task Pid1902_reclaim_final_mode_not_pid1902_fails_closed(string finalMode)
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryModeAfterReclaim = Enum.Parse<MsiClawNativeMode>(finalMode);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets); // no rollback
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // PR11 sections 4/8: the fresh final PID1902 native identity must match the resolved
           // PID1902 DirectInput descriptor -- stale/other-device protection after a reclaim.
    public async Task Pid1902_reclaim_where_the_directinput_descriptor_does_not_match_the_final_identity_fails_closed()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKeyAfterReclaim = @"USB\VID_0DB0\FINAL_1902";  // fresh final PID1902 native identity
        h.RecoveryPnpPhysKeyAfterReclaim = OtherPhysKey;             // resolved descriptor belongs elsewhere

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("DirectInputPhysicalIdentityMismatch", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // 21.7
    public async Task Pid1901_reclaim_then_changed_exact_target_fails_closed_without_migration()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPnp = OtherPrimaryPnp;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("RecoveredTargetChanged", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied); // only the original acquisition
    }

    [Theory] // 21.8
    [InlineData("Conflict")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public async Task Pid1901_reclaim_then_hidhide_failure_blocks_restart_without_rollback(string outcome)
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = Enum.Parse<AddonHidHideBaselineOutcome>(outcome);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("HidHideReconcileFailed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.9
    public async Task Pid1901_reclaim_then_directinput_start_failure_fails_closed_without_rollback()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.StartResult = MsiClawInputStartStatus.AcquireFailed;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("DirectInputStartFailed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.10
    public async Task Pid1901_reclaim_then_first_valid_state_failure_stops_the_partial_session()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.FirstValidState = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("FirstValidStateNotObserved", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.True(h.InputSource.StopCalled);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.11 -- the PR8 same-PID1902 path is unchanged
    public async Task Same_pid1902_recovery_still_issues_no_mode_write()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", recovery.Reason);
        Assert.False(recovery.ModeWriteIssued);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Equal(
            new[] { "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 21.12 -- explicit release still works after a post-write reclaim failure
    public async Task Explicit_release_still_works_after_a_failed_pid1901_reclaim()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = AddonHidHideBaselineOutcome.Conflict; // fails after the mode write
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.Recovering = false;
        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal([PrimaryPnp], release.HiddenTargets); // owned target evidence retained through the failure
    }

    // ---------- PR10: physical device loss / PnP return recovery (work order section 20) ----------

    [Fact] // 20.1 -- mandatory: proves _ownsInputSource no longer means "was never committed"
    public async Task Recovery_re_enters_after_an_earlier_device_not_found_failure()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = NativeStateCaptureStatus.DeviceNotFound;

        var first = await owner.RecoverLostInputAsync(default);
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, first.Outcome);
        Assert.Contains("PhysicalDeviceMissing", first.Reason);
        Assert.Null(owner.LiveInputSource);

        h.RecoveryCaptureStatus = null; // the same physical MSI Claw safely returned
        h.Events.Clear();
        var second = await owner.RecoverLostInputAsync(default);

        Assert.True(second.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", second.Reason);
        Assert.False(second.ModeWriteIssued);
        Assert.Same(h.InputSource, owner.LiveInputSource);
        Assert.Equal(
            new[] { "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 20.6 -- return as PID1901 after an earlier absence still reuses the PR9 one-shot reclaim
    public async Task Recovery_re_enters_as_pid1901_after_device_not_found_and_reclaims_once()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = NativeStateCaptureStatus.DeviceNotFound;
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.RecoveryCaptureStatus = null;
        h.RecoveryMode = MsiClawNativeMode.XInput;                 // returned as PID1901
        h.RecoveryModeAfterReclaim = MsiClawNativeMode.DirectInput;
        h.Events.Clear();
        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalStateDriftReclaimed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
    }

    [Fact] // 20.2 -- re-entry still refuses when ownership was never committed
    public async Task Repeated_recovery_still_refuses_when_ownership_was_never_committed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);
        h.Recovering = true;

        Assert.Equal("OwnerNotCommitted", (await owner.RecoverLostInputAsync(default)).Reason);
        Assert.Equal("OwnerNotCommitted", (await owner.RecoverLostInputAsync(default)).Reason); // still, on retry
        Assert.Equal(0, h.SwitchCalls);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact] // 20.8 -- a changed exact target on return stays fail-closed, no migration
    public async Task Recovery_after_device_not_found_with_a_changed_exact_target_fails_closed()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = NativeStateCaptureStatus.DeviceNotFound;
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.RecoveryCaptureStatus = null;
        h.RecoveryPnp = OtherPrimaryPnp; // same strong identity, different exact primary collection
        h.Events.Clear();
        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("RecoveredTargetChanged", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied);
    }

    [Theory] // 21.5
    [InlineData("DeviceNotFound")]
    [InlineData("Indeterminate")]
    public async Task Recovery_with_missing_or_ambiguous_native_state_makes_no_mutation(string status)
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = Enum.Parse<NativeStateCaptureStatus>(status);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("PhysicalDeviceMissing", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact] // 21.6
    public async Task Recovery_when_the_exact_target_changed_does_not_migrate_or_restart()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPnp = OtherPrimaryPnp;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("RecoveredTargetChanged", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied); // only the original acquisition
    }

    [Theory] // 21.7
    [InlineData("Conflict")]
    [InlineData("Unavailable")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public async Task Recovery_blocked_by_hidhide_does_not_restart_directinput(string outcome)
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = Enum.Parse<AddonHidHideBaselineOutcome>(outcome);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("HidHideReconcileFailed", recovery.Reason);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.8
    public async Task Recovery_directinput_start_failure_leaves_output_neutral_without_pid_or_hidhide_teardown()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.StartResult = MsiClawInputStartStatus.AcquireFailed;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("DirectInputStartFailed", recovery.Reason);
        Assert.Null(owner.LiveInputSource);
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact] // 21.9
    public async Task Recovery_first_valid_state_failure_stops_the_partial_session_and_stays_not_live()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.FirstValidState = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("FirstValidStateNotObserved", recovery.Reason);
        Assert.True(h.InputSource.StopCalled);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.10
    public async Task Recovery_is_a_noop_when_the_source_is_still_running()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.Recovering = true;
        h.Events.Clear();

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("RecoveryNotNeeded", recovery.Reason);
        Assert.Empty(h.Events);
        Assert.Same(h.InputSource, owner.LiveInputSource);
    }

    [Fact] // 21.12 -- explicit release still works after a pre-write recovery failure
    public async Task Explicit_release_still_works_after_a_failed_recovery()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = OtherPhysKey; // identity mismatch -> fails before any mode write
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.Recovering = false;
        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal([PrimaryPnp], release.HiddenTargets); // owned target evidence retained through the failure
    }

    [Fact] // section 10.8
    public async Task Recovery_fails_closed_when_center_m_is_no_longer_exactly_disabled()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryAuthority = FrontendCenterMStartupState.Enabled;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("AuthorityNotDisabled", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // section 9
    public async Task Recovery_is_refused_after_release_for_center_m_enable()
    {
        var previousDirectory = AppLog.DirectoryOverride;
        var previousMinimumLevel = AppLog.MinimumLevelOverride;
        var logDirectory = Path.Combine(Path.GetTempPath(), $"PhysicalOwnershipTests-{Guid.NewGuid():N}");
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = logDirectory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;

        try
        {
            var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
            var owner = h.Build();
            await owner.AcquireAsync(default);
            await owner.ReleaseForCenterMEnableAsync(default);
            h.InputSource.SimulateSessionLoss();
            h.Recovering = true;
            h.Events.Clear();
            var modeSwitchesAfterRelease = h.SwitchCalls;
            var gamepadModeWritesAfterRelease = h.GamepadMode.SwitchTargets.Count;
            var inputStartsAfterRelease = h.InputSource.StartCallCount;
            var hidHideTargetsAfterRelease = h.HidHideApplied.Count;

            var recovery = await owner.RecoverLostInputAsync(default);

            Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
            Assert.False(recovery.IsOwned);
            Assert.Equal("ReleasedForCenterMEnable", recovery.Reason);
            Assert.False(recovery.ModeWriteIssued);
            Assert.Equal(modeSwitchesAfterRelease, h.SwitchCalls);
            Assert.Equal(gamepadModeWritesAfterRelease, h.GamepadMode.SwitchTargets.Count);
            Assert.Equal(inputStartsAfterRelease, h.InputSource.StartCallCount);
            Assert.Equal(hidHideTargetsAfterRelease, h.HidHideApplied.Count);
            Assert.Empty(h.Events);

            var log = AppLog.ReadAllTextForTests(AppLog.CurrentLogFilePath);
            var skippedLine = Assert.Single(log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                line => line.Contains("Event=OwnedPhysicalRecoverySkipped", StringComparison.Ordinal));
            Assert.Contains("[INFO]", skippedLine);
            Assert.Contains("Reason=ReleasedForCenterMEnable", skippedLine);
            Assert.Contains("ModeWriteIssued=False", skippedLine);
            Assert.DoesNotContain("OwnedPhysicalRecoveryFailed", log);
        }
        finally
        {
            AppLog.DrainForTests();
            AppLog.DirectoryOverride = previousDirectory;
            AppLog.MinimumLevelOverride = previousMinimumLevel;
            if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact] // section 9 -- recovery before ownership was ever committed
    public async Task Recovery_without_a_committed_acquisition_is_refused()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);
        h.Recovering = true;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Equal("OwnerNotCommitted", recovery.Reason);
    }

    [Fact] // work order sections 7 / 11 / 15 / 22 -- host wiring for the owned-input completion callback
    public void Host_wires_the_owned_input_completion_callback_and_drains_recovery_first_on_shutdown()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // The one completion signal is subscribed at the existing construction seam.
        Assert.Contains("directInputInputSource.TestCompleted += OnOwnedControllerPhysicalInputCompleted;", host, StringComparison.Ordinal);
        // Expected stop and pre-commit startup completions do not schedule recovery.
        Assert.Contains("summary.StopReason == Devices.MSI.Claw.MsiClawInputStopReason.Stopped", host, StringComparison.Ordinal);
        Assert.Contains("!summary.CleanupSucceeded", host, StringComparison.Ordinal);
        // Successful recovery requests the existing PR7 reconcile -- no duplicated Steam/BPM policy.
        Assert.Contains("RequestControllerPresentationReconcile(\"PhysicalInputRecovered\")", host, StringComparison.Ordinal);
        // Shutdown drains the recovery BEFORE the presentation reconcile it may itself request.
        Assert.True(
            host.IndexOf("await _ownedControllerRecovery.ConfigureAwait(false);", StringComparison.Ordinal)
            < host.IndexOf("await _presentationReconcile.ConfigureAwait(false);", StringComparison.Ordinal),
            "owned physical recovery must be drained before the presentation reconcile");
        // No polling / timer / recovery-manager framework.
        foreach (var forbidden in new[] { "ControllerRecoveryManager", "PhysicalRecoveryManager", "RecoveryTimer", "PeriodicTimer" })
            Assert.DoesNotContain(forbidden, host, StringComparison.Ordinal);
    }

    [Fact] // PR10 sections 6-8 / 15 -- host wiring for the Device Arrival PnP-return trigger
    public void Host_wires_the_device_arrival_watcher_and_shares_one_recovery_seam()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // One Runtime-owned watcher, started only once a physical owner has committed.
        Assert.Contains("new Controllers.Detection.WindowsDeviceArrivalWatcher()", host, StringComparison.Ordinal);
        Assert.Contains("watcher.DeviceArrived += OnControllerDeviceArrived;", host, StringComparison.Ordinal);
        // Both triggers funnel through one scheduling seam.
        Assert.Contains("RequestOwnedControllerRecovery(physical, \"UnexpectedDirectInputCompletion\")", host, StringComparison.Ordinal);
        Assert.Contains("RequestOwnedControllerRecovery(physical, \"DeviceArrival\")", host, StringComparison.Ordinal);
        // A live owned source ignores unrelated arrivals with no native/HidHide/DI work.
        Assert.Contains("physical.LiveInputSource is { IsRunning: true }", host, StringComparison.Ordinal);
        // review [P1]: an arrival that lands while an attempt is in flight is retained as a single
        // pending bit and consumed for exactly one follow-up once the attempt finishes.
        Assert.Contains("Interlocked.Exchange(ref _pendingOwnedControllerArrival, 1)", host, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _pendingOwnedControllerArrival, 0) != 0", host, StringComparison.Ordinal);
        Assert.Contains("\"DeferredDeviceArrival\"", host, StringComparison.Ordinal);
        // review [P1]: unproven DirectInput cleanup blocks ALL owned-controller recovery -- including
        // Device Arrival and its deferred follow-up -- for the rest of this Runtime lifetime.
        Assert.Contains("Interlocked.Exchange(ref _ownedControllerRecoveryBlockedByCleanup, 1)", host, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _ownedControllerRecoveryBlockedByCleanup) != 0", host, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _ownedControllerRecoveryBlockedByCleanup) == 0", host, StringComparison.Ordinal);
        // Shutdown stops the watcher before recovery drains.
        Assert.True(
            host.IndexOf("_deviceArrivalWatcher?.Dispose();", StringComparison.Ordinal)
            < host.IndexOf("await _ownedControllerRecovery.ConfigureAwait(false);", StringComparison.Ordinal),
            "the Device Arrival watcher must be disposed before the recovery drain");
        // No PnP polling / manager.
        foreach (var forbidden in new[] { "PnPRecoveryManager", "PnpRecoveryManager", "PeriodicTimer" })
            Assert.DoesNotContain(forbidden, host, StringComparison.Ordinal);
    }

    [Fact] // work order sections 13 / 23 -- no legacy authority / recovery-framework surface
    public void Recovery_code_introduces_no_legacy_takeover_or_recovery_framework_symbols()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs"));

        Assert.Contains("RecoverLostInputAsync", source, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "ExternalNativeTakeover", "ConfirmExternalNativeTakeover", "retryCurrentSessionAfterSafeCleanup",
            "ApplyEnabledModeBaseline", "ControllerRecoveryManager", "PhysicalRecoveryManager",
            "Timer", "epoch", "RecoveryBarrier", "RumblePhysicalIdentityTracker",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);

        // Full1902 production rumble (work order section 6): the ONLY "generation" concept here is the
        // narrow IMsiClawPhysicalInputIdentityProvider contract MsiClawRumbleSink already requires --
        // a single incrementing value on real DirectInput-session commits, not a barrier/epoch
        // framework. It advances on acquisition/recovery commit only, never on a virtual switch.
        Assert.Contains("IMsiClawPhysicalInputIdentityProvider", source, StringComparison.Ordinal);
        Assert.Contains("_currentSessionGeneration++", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoveryGeneration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerationBarrier", source, StringComparison.Ordinal);
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

        // The production gate: acquisition starts only for exact Disabled + admission Ready, but the
        // release seam is constructed on ANY exact Disabled boot (including a Blocked one).
        var host = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        Assert.Contains("startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled", host, StringComparison.Ordinal);
        Assert.Contains("var owner = CreatePhysicalOwnership(startupComposition, startupResult.HardwareDeviceModel);", host, StringComparison.Ordinal);
        Assert.Contains("startupResult.DisabledBootAdmission?.IsReady != true", host, StringComparison.Ordinal);
        Assert.Contains("var allowPrerequisiteRepairWhileRecoveryUnsafe =", host, StringComparison.Ordinal);
        Assert.Contains("startupResult.DisabledBootAdmission?.Outcome == DisabledBootAdmissionOutcome.PrerequisitesNotReady", host, StringComparison.Ordinal);
        Assert.Contains("new SteamInputAddonforClaw.Frontend.FrontendPrerequisiteSetupExecutor(", host, StringComparison.Ordinal);
        Assert.Contains("setupExecutor: setupExecutor", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("_physicalOwnership = owner;", StringComparison.Ordinal)
            < host.IndexOf("startupResult.DisabledBootAdmission?.IsReady != true", StringComparison.Ordinal),
            "the release seam must be assigned before the acquisition admission gate");
    }

    private static ControllerDeviceInfo PnpCollection(string instanceId, string physKey, ushort usagePage, ushort usage, string interfaceId)
    {
        var serial = physKey[(physKey.LastIndexOf('\\') + 1)..];
        return new ControllerDeviceInfo(
            InstanceId: instanceId,
            ContainerId: null,
            ParentInstanceId: $@"USB\VID_0DB0&PID_1902&{interfaceId}\6&xyz&0&0000",
            AncestorInstanceIds: [$@"USB\VID_0DB0&PID_1902\{serial}"],
            EnumeratorName: "HID", HardwareIds: [], CompatibleIds: [], ClassName: "HIDClass", ClassGuid: null, Service: "HidUsb",
            VendorId: 0x0DB0, ProductId: 0x1902, Present: true, FriendlyName: "MSI Claw",
            UsagePage: usagePage, Usage: usage);
    }

    private static ControllerDeviceInfo Pid1901ControlDevice(string serial, ushort usagePage = 0xFFA0, ushort usage = 0x0001)
    {
        IReadOnlyList<string> ancestors = string.IsNullOrEmpty(serial) ? [] : [$@"USB\VID_0DB0&PID_1901\{serial}"];
        return new ControllerDeviceInfo(
            InstanceId: $@"HID\VID_0DB0&PID_1901&MI_00\{(string.IsNullOrEmpty(serial) ? "WEAK" : serial)}_CONTROL",
            ContainerId: null,
            ParentInstanceId: $@"USB\VID_0DB0&PID_1901&MI_00\6&xinput&0&0000",
            AncestorInstanceIds: ancestors,
            EnumeratorName: "HID", HardwareIds: [], CompatibleIds: [], ClassName: "HIDClass", ClassGuid: null, Service: "HidUsb",
            VendorId: 0x0DB0, ProductId: 0x1901, Present: true, FriendlyName: "MSI Claw",
            UsagePage: usagePage, Usage: usage);
    }

    private static ControllerDeviceInfo Pid1901ParentDevice(string serial) => new(
        InstanceId: $@"USB\VID_0DB0&PID_1901\{serial}",
        ContainerId: null,
        ParentInstanceId: null,
        AncestorInstanceIds: [],
        EnumeratorName: "USB", HardwareIds: [], CompatibleIds: [], ClassName: "USB", ClassGuid: null, Service: null,
        VendorId: 0x0DB0, ProductId: 0x1901, Present: true, FriendlyName: "MSI Claw");

    // ---- harness ----

    private sealed class Harness
    {
        public FrontendCenterMStartupState Authority { get; set; } = FrontendCenterMStartupState.Disabled;
        public MsiClawNativeMode InitialMode { get; set; } = MsiClawNativeMode.DirectInput;
        public HandheldDeviceModelId? HardwareDeviceModel { get; set; }
        public Func<BootSessionAttemptResult>? ClaimBootAttempt { get; set; }
        public NativeStateCaptureStatus InitialCaptureStatus { get; set; } = NativeStateCaptureStatus.Success;
        public MsiClawIdentityConfidence InitialConfidence { get; set; } = MsiClawIdentityConfidence.Strong;
        public string FinalPhysKey { get; set; } = PhysKey;
        public string? SecondCapturePhysKey { get; set; }
        public MsiClawNativeMode? FinalModeAfterSwitch { get; set; }
        public MsiClawNativeMode? ModeAfterFailedTransition { get; set; }
        private bool _lastModeSwitchSucceeded;
        private int _nonRecoveringCaptureCount;
        public bool SwitchSucceeds { get; set; } = true;
        public int FailTransitionOnCall { get; set; }
        public bool FailureTargetPidPresent { get; set; }
        public bool RecordModeSwitchEvents { get; set; }
        public int DirectInputMissingAttempts { get; set; }
        public int DirectInputUnverifiedAttempts { get; set; }
        public bool DirectInputAmbiguous { get; set; }
        public string? ExistingOwnedTarget { get; set; }
        public IReadOnlyList<ControllerDeviceInfo>? PnpDevices { get; set; }
        public Func<IReadOnlyList<ControllerDeviceInfo>>? PnpDeviceSnapshot { get; set; }
        public Func<IReadOnlyList<ControllerDeviceInfo>>? Pid1901DeviceSnapshot { get; set; }
        public Func<IReadOnlyList<ControllerDeviceInfo>>? Pid1902DeviceSnapshot { get; set; }
        public Func<FrontendCenterMStartupState>? AuthoritySnapshot { get; set; }
        public bool FailNativeCaptureAfterFirstModeSwitch { get; set; }
        public bool ThrowGlobalPnpSnapshotAfterFirstModeSwitch { get; set; }
        public bool OldPid1902PresentDuringSettle { get; set; }
        public string DirectInputPnp { get; set; } = PrimaryPnp;
        public string DirectInputPnpPhysKey { get; set; } = PhysKey;
        public AddonHidHideBaselineOutcome HidHideOutcome { get; set; } = AddonHidHideBaselineOutcome.Success;
        private long _timestamp;
        public long ElapsedSettleMilliseconds => (long)(_timestamp * 1000d / Stopwatch.Frequency);
        public int DelayCalls { get; private set; }
        public int NativeCaptureCalls { get; private set; }
        public int Pid1901TargetProbeCalls { get; private set; }

        // ---- PR8 recovery knobs (only consulted once Recovering is set) ----
        public bool Recovering { get; set; }
        public MsiClawNativeMode? RecoveryMode { get; set; }
        public NativeStateCaptureStatus? RecoveryCaptureStatus { get; set; }
        public string? RecoveryPhysKey { get; set; }
        public string? RecoveryPnp { get; set; }
        public AddonHidHideBaselineOutcome? RecoveryHidHideOutcome { get; set; }
        public FrontendCenterMStartupState? RecoveryAuthority { get; set; }
        // ---- PR9 PID1901 drift reclaim knobs: the "after the reclaim mode write" observed state ----
        public MsiClawNativeMode? RecoveryModeAfterReclaim { get; set; }
        public string? RecoveryPhysKeyAfterReclaim { get; set; }
        public string? RecoveryPnpPhysKeyAfterReclaim { get; set; }
        public NativeStateCaptureStatus? RecoveryCaptureStatusAfterReclaim { get; set; }
        public bool RecoverySwitchSucceeds { get; set; } = true;
        public int RecoverySwitchCalls { get; private set; }
        public bool XInputCommandWriteSucceeds { get; set; } = true;
        public int XInputCommandWriteCalls { get; private set; }
        public List<string> Events { get; } = [];

        private MsiClawNativeMode RecoveryCurrentMode => RecoverySwitchCalls > 0
            ? RecoveryModeAfterReclaim ?? MsiClawNativeMode.DirectInput
            : RecoveryMode ?? MsiClawNativeMode.DirectInput;
        private string RecoveryCurrentPhysKey => RecoverySwitchCalls > 0
            ? RecoveryPhysKeyAfterReclaim ?? RecoveryPhysKey ?? FinalPhysKey
            : RecoveryPhysKey ?? FinalPhysKey;
        private NativeStateCaptureStatus RecoveryCurrentStatus => RecoverySwitchCalls > 0
            ? RecoveryCaptureStatusAfterReclaim ?? NativeStateCaptureStatus.Success
            : RecoveryCaptureStatus ?? NativeStateCaptureStatus.Success;

        public int SwitchCalls { get; private set; }
        public MsiClawNativeMode LastSwitchTarget { get; private set; }
        public List<MsiClawNativeMode> SwitchTargets { get; } = [];
        public List<MsiClawPhysicalIdentity> SwitchIdentities { get; } = [];
        public int DirectInputEnumerateCalls { get; private set; }
        public List<string> HidHideApplied { get; } = [];
        public List<string> EnabledBaselineCalls { get; } = [];
        public FakeInputSource InputSource { get; } = new();

        public int AuthorityReads { get; private set; }
        public bool SwitchFailsForRelease { get; set; }
        public FakeGamepadModeClient GamepadMode { get; } = new();

        private string EffectivePnp => Recovering && RecoveryPnp is not null ? RecoveryPnp : DirectInputPnp;

        public MsiClawAddonPhysicalOwnership Build()
        {
            InputSource.Events = Events;
            GamepadMode.IsRecovering = () => Recovering;
            return new(
            () =>
            {
                AuthorityReads++;
                if (AuthoritySnapshot is not null) return AuthoritySnapshot();
                return Recovering && RecoveryAuthority is { } authority ? authority : Authority;
            },
            _ =>
            {
                NativeCaptureCalls++;
                if (Recovering)
                {
                    Events.Add("NativeCapture");
                    if (RecoveryCurrentStatus != NativeStateCaptureStatus.Success)
                        return Task.FromResult(new NativeStateCaptureResult(RecoveryCurrentStatus, null, RecoveryCurrentStatus.ToString()));
                    return Task.FromResult(Capture(RecoveryCurrentMode, MsiClawIdentityConfidence.Strong, RecoveryCurrentPhysKey));
                }
                if (FailNativeCaptureAfterFirstModeSwitch && SwitchCalls == 1)
                    return Task.FromResult(new NativeStateCaptureResult(NativeStateCaptureStatus.Failed, null, "Simulated unrelated PnP property read failure."));
                _nonRecoveringCaptureCount++;
                // PR11: an already-PID1902 boot (no switch) still does two captures -- SecondCapturePhysKey
                // lets a test express a same-mode identity mismatch between them.
                var physKey = SwitchCalls == 0
                    ? _nonRecoveringCaptureCount > 1 ? SecondCapturePhysKey ?? PhysKey : PhysKey
                    : FinalPhysKey;
                return Task.FromResult(Capture(
                    SwitchCalls == 0
                        ? InitialMode
                        : !_lastModeSwitchSucceeded
                            ? ModeAfterFailedTransition ?? FinalModeAfterSwitch ?? LastSwitchTarget
                            : FinalModeAfterSwitch ?? LastSwitchTarget,
                    SwitchCalls == 0 ? InitialConfidence : MsiClawIdentityConfidence.Strong,
                    physKey));
            },
            (target, identity, _) =>
            {
                SwitchCalls++;
                if (Recovering) RecoverySwitchCalls++;
                LastSwitchTarget = target;
                SwitchTargets.Add(target);
                SwitchIdentities.Add(identity);
                if (RecordModeSwitchEvents) Events.Add("ModeSwitch:" + target);
                var configuredFailure = FailTransitionOnCall != 0 && SwitchCalls == FailTransitionOnCall;
                var ok = !configuredFailure && (target == MsiClawNativeMode.XInput
                    ? !SwitchFailsForRelease
                    : Recovering ? RecoverySwitchSucceeds : SwitchSucceeds);
                _lastModeSwitchSucceeded = ok;
                var transitionStatus = ok
                    ? MsiClawModeTransitionStatus.Succeeded
                    : configuredFailure
                        ? MsiClawModeTransitionStatus.TargetDeviceDidNotAppear
                        : MsiClawModeTransitionStatus.WriteFailed;
                var sourceMode = target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput;
                return Task.FromResult(new MsiClawModeTransitionResult(
                    transitionStatus,
                    sourceMode, target,
                    sourceMode == MsiClawNativeMode.XInput ? (ushort)0x1901 : (ushort)0x1902,
                    target == MsiClawNativeMode.XInput ? (ushort)0x1901 : (ushort)0x1902,
                    ok || configuredFailure,
                    ok || configuredFailure,
                    ok,
                    ok || configuredFailure,
                    ok,
                    0,
                    ok ? "ok" : configuredFailure ? "Native mode re-enumeration did not complete." : "WriteFailed",
                    ok || configuredFailure && FailureTargetPidPresent,
                    ok || configuredFailure ? configuredFailure ? 5000 : 0 : null));
            },
            () =>
            {
                DirectInputEnumerateCalls++;
                if (Recovering) Events.Add("DescriptorResolve");
                if (DirectInputAmbiguous)
                    return [Descriptor(Guid.NewGuid()), Descriptor(Guid.NewGuid(), physId: "OTHER")];
                if (DirectInputEnumerateCalls <= DirectInputMissingAttempts)
                    return [];
                if (DirectInputEnumerateCalls <= DirectInputMissingAttempts + DirectInputUnverifiedAttempts)
                    return [Descriptor(Guid.NewGuid(), unverified: true)]; // PID1902 present, identity not yet resolved
                return [Descriptor(Guid.NewGuid())];
            },
            instanceId => instanceId == EffectivePnp
                ? PnpDevice(EffectivePnp, Recovering
                    ? RecoverySwitchCalls > 0 && RecoveryPnpPhysKeyAfterReclaim is not null ? RecoveryPnpPhysKeyAfterReclaim : RecoveryCurrentPhysKey
                    : DirectInputPnpPhysKey)
                : null,
            () =>
            {
                if (ThrowGlobalPnpSnapshotAfterFirstModeSwitch && SwitchCalls == 1)
                    throw new InvalidOperationException("Unable to read unrelated PnP property 1.");
                return PnpDeviceSnapshot?.Invoke() ?? PnpDevices ?? [PnpDevice(EffectivePnp, Recovering
                    ? RecoverySwitchCalls > 0 && RecoveryPnpPhysKeyAfterReclaim is not null ? RecoveryPnpPhysKeyAfterReclaim : RecoveryCurrentPhysKey
                    : DirectInputPnpPhysKey)];
            },
            (vendorId, productId) =>
            {
                if (vendorId != MsiClawHardware.VendorId)
                    return [];
                if (productId == MsiClawHardware.XInputProductId)
                {
                    Pid1901TargetProbeCalls++;
                    return Pid1901DeviceSnapshot?.Invoke() ?? [Pid1901ControlDevice("A2VM_XINPUT_123")];
                }
                if (productId == MsiClawHardware.DirectInputProductId)
                    return Pid1902DeviceSnapshot?.Invoke()
                        ?? (PnpDeviceSnapshot?.Invoke() ?? PnpDevices ?? []).Where(device => device.Present
                            && device.VendorId == vendorId && device.ProductId == productId).ToArray();
                return [];
            },
            (_, productId) => productId == MsiClawHardware.DirectInputProductId && OldPid1902PresentDuringSettle,
            InputSource,
            targets =>
            {
                HidHideApplied.AddRange(targets);
                if (Recovering) Events.Add("HidHideApply");
                var outcome = Recovering && RecoveryHidHideOutcome is { } recoveryOutcome ? recoveryOutcome : HidHideOutcome;
                return new AddonHidHideBaselineResult(outcome, outcome.ToString(), AddonHidHideBaselineSnapshot.Unknown);
            },
            () => ExistingOwnedTarget is null ? [] : [ExistingOwnedTarget],
            delay: (duration, _) =>
            {
                DelayCalls++;
                _timestamp += (long)(duration.TotalSeconds * Stopwatch.Frequency);
                return Task.CompletedTask;
            },
            directInputSettleWindow: TimeSpan.FromMilliseconds(200),
            directInputSettleInterval: TimeSpan.FromMilliseconds(1),
            gamepadModeClient: GamepadMode,
            hardwareDeviceModel: HardwareDeviceModel,
            claimA2vmBootAttempt: ClaimBootAttempt,
            getTimestamp: () => _timestamp,
            writeXInputCommand: (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                XInputCommandWriteCalls++;
                Events.Add("XInputCommand");
                return Task.FromResult(new MsiClawModeCommandWriteResult(
                    true,
                    XInputCommandWriteSucceeds,
                    XInputCommandWriteSucceeds ? "CommandWritten" : "ModeCommandWriteNotConfirmed"));
            });
        }

        private NativeStateCaptureResult Capture(MsiClawNativeMode mode, MsiClawIdentityConfidence confidence, string physKey)
        {
            if (!Recovering && InitialCaptureStatus != NativeStateCaptureStatus.Success)
                return new(InitialCaptureStatus, null, InitialCaptureStatus.ToString());
            if (Recovering && RecoveryCaptureStatus is { } status && status != NativeStateCaptureStatus.Success)
                return new(status, null, status.ToString());
            var productId = mode == MsiClawNativeMode.XInput
                ? MsiClawHardware.XInputProductId
                : MsiClawHardware.DirectInputProductId;
            var payload = new MsiClawNativeStatePayload(mode, "inst", "USB\\parent", null, productId, confidence, physKey);
            var snapshot = new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(payload));
            return new(NativeStateCaptureStatus.Success, snapshot, "ok");
        }

        private DirectInputDeviceDescriptor Descriptor(Guid instanceGuid, string? physId = null, bool unverified = false) => new(
            instanceGuid, Guid.NewGuid(), "MSI Claw Controller", 0x0DB0, 0x1902,
            DevicePath: unverified ? null : @"\\?\hid#vid_0db0&pid_1902",
            PnpInstanceId: unverified ? null : EffectivePnp,
            PhysicalIdentity: unverified ? null : physId ?? "msi-claw-phys",
            UsagePage: 0x0001, Usage: 0x0005, ButtonCount: 17, AxisCount: 6);

        private static ControllerDeviceInfo PnpDevice(string instanceId, string physKey)
        {
            var serial = physKey[(physKey.LastIndexOf('\\') + 1)..];
            return new ControllerDeviceInfo(
                InstanceId: instanceId,
                ContainerId: null,
                ParentInstanceId: $@"USB\VID_0DB0&PID_1902&MI_00\6&xyz&0&0000",
                AncestorInstanceIds: [$@"USB\VID_0DB0&PID_1902\{serial}"],
                EnumeratorName: "HID", HardwareIds: [], CompatibleIds: [], ClassName: "HIDClass", ClassGuid: null, Service: "HidUsb",
                VendorId: 0x0DB0, ProductId: 0x1902, Present: true, FriendlyName: "MSI Claw",
                UsagePage: 0x0001, Usage: 0x0005);
        }
    }

    private sealed class FakeGamepadModeClient : IMsiClawGamepadModeClient
    {
        public MsiClawGamepadMode ObservedMode { get; set; } = MsiClawGamepadMode.DirectInput;
        public MsiClawGamepadMode? RecoveryObservedMode { get; set; }
        public bool QuerySucceeds { get; set; } = true;
        public bool? RecoveryQuerySucceeds { get; set; }
        public bool SwitchSucceeds { get; set; } = true;
        public bool SwitchWriteIssued { get; set; } = true;
        public bool? RecoverySwitchSucceeds { get; set; }
        public List<MsiClawGamepadMode> SwitchTargets { get; } = [];
        public List<MsiClawPhysicalIdentity> QueryIdentities { get; } = [];
        public List<MsiClawPhysicalIdentity> SwitchIdentities { get; } = [];
        public Func<bool>? IsRecovering { get; set; }

        public Task<MsiClawGamepadModeQueryResult> QueryAsync(MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            QueryIdentities.Add(expectedIdentity);
            var recovering = IsRecovering?.Invoke() == true;
            var succeeds = recovering ? RecoveryQuerySucceeds ?? QuerySucceeds : QuerySucceeds;
            var mode = recovering ? RecoveryObservedMode ?? ObservedMode : ObservedMode;
            return Task.FromResult(succeeds
                ? new MsiClawGamepadModeQueryResult(true, mode, "ok")
                : MsiClawGamepadModeQueryResult.Unavailable("unavailable"));
        }

        public Task<MsiClawGamepadModeWriteResult> SwitchAndVerifyAsync(MsiClawPhysicalIdentity expectedIdentity, MsiClawGamepadMode targetMode, CancellationToken cancellationToken)
        {
            SwitchIdentities.Add(expectedIdentity);
            SwitchTargets.Add(targetMode);
            var recovering = IsRecovering?.Invoke() == true;
            var succeeds = recovering ? RecoverySwitchSucceeds ?? SwitchSucceeds : SwitchSucceeds;
            return Task.FromResult(new MsiClawGamepadModeWriteResult(succeeds, succeeds ? targetMode : null, SwitchWriteIssued, succeeds, succeeds ? "ok" : "failed"));
        }
    }

    private sealed class FakeInputSource : IMsiClawPreparedInputSource
    {
        public SteamInputAddonforClaw.Input.ControllerState LatestState => default;
        public void ResetLatestStateToNeutral() { }
        public MsiClawInputStartStatus StartResult { get; set; } = MsiClawInputStartStatus.Started;
        public bool FirstValidState { get; set; } = true;
        public bool CleanupProven { get; set; } = true;
        public TaskCompletionSource? StopCleanupGate { get; set; }
        public bool StartCalled { get; private set; }
        public int StartCallCount { get; private set; }
        public bool StopCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public bool IsRunning { get; private set; }
        public List<string>? Events { get; set; }

        public event EventHandler<SteamInputAddonforClaw.Input.ControllerState>? StateChanged { add { } remove { } }

        /// <summary>Simulate an unexpected owned-session termination: the poll loop has already
        /// neutralized state and cleaned up, so the source is simply no longer running.</summary>
        public void SimulateSessionLoss() => IsRunning = false;

        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
        {
            StartCalled = true;
            StartCallCount++;
            Events?.Add("InputStart");
            if (StartResult == MsiClawInputStartStatus.Started) IsRunning = true;
            return new MsiClawInputStartResult(StartResult, StartResult.ToString());
        }

        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken)
        {
            Events?.Add("FirstValidState");
            if (!FirstValidState) IsRunning = true; // still running, just never valid
            return Task.FromResult(FirstValidState);
        }

        public Task StopAsync()
        {
            StopCalled = true;
            Events?.Add("InputStop");
            IsRunning = false;
            return Task.CompletedTask;
        }

        public async Task<bool> StopAndConfirmCleanupAsync()
        {
            await StopAsync();
            if (StopCleanupGate is { } gate)
                await gate.Task;
            return CleanupProven;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
