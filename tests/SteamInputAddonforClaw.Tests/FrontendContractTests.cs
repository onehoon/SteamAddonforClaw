using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontendContractTests
{
    private static readonly FrontendStatusSnapshot SampleStatus = new(
        new("MSI", "Claw", "BOARD", ["GPU"]),
        new(FrontendHardwareStatus.Supported, "msi.claw", "EX", "Matched"),
        new(FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, ""),
        new(true, 480, FrontendSteamSource.BigPicture),
        FrontendAddonOperationalStatus.Ready, "Eligible", true,
        FrontendSetupStatus.Complete, "Complete", false);

    [Fact]
    public void Status_snapshot_round_trips_through_SystemTextJson()
    {
        var json = JsonSerializer.Serialize(SampleStatus);
        var restored = JsonSerializer.Deserialize<FrontendStatusSnapshot>(json);

        Assert.NotNull(restored);
        // Assert.Equivalent rather than Assert.Equal: record equality on IReadOnlyList<T>
        // properties falls back to reference equality, which a fresh deserialized List<T> can
        // never satisfy against the original array-backed instance -- this is a value-equality
        // check, not a reference check.
        Assert.Equivalent(SampleStatus, restored, strict: true);
    }

    [Fact]
    public void Full1902_removed_the_steam_input_routing_frontend_contract()
    {
        Assert.Null(typeof(FrontendSettingsSnapshot).GetProperty("SteamInputRoutingEnabled"));
        Assert.Null(typeof(IAddonFrontendControl).GetMethod("SetSteamInputRoutingEnabledAsync"));
        Assert.Null(typeof(FrontendSettingsSnapshot).Assembly.GetType(
            "SteamInputAddonforClaw.Contracts.Frontend.FrontendSteamInputRoutingMutationResult"));
    }

    [Fact]
    public void Settings_snapshot_still_round_trips_the_retained_preferences()
    {
        var value = new FrontendSettingsSnapshot(FrontendLogLevel.Debug, SuppressDeveloperMenuWarning: true, Oem1MappingSettings.Default)
        {
            DeveloperMenuEnabled = true,
        };
        var restored = JsonSerializer.Deserialize<FrontendSettingsSnapshot>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
        Assert.True(restored!.SuppressDeveloperMenuWarning);
        Assert.True(restored.DeveloperMenuEnabled);
    }

    [Fact]
    public void Settings_snapshot_no_longer_carries_the_removed_launch_at_startup_contract()
    {
        Assert.Null(typeof(FrontendSettingsSnapshot).GetProperty("LaunchAtWindowsStartup"));
        Assert.Null(typeof(FrontendSettingsSnapshot).GetProperty("LaunchAtWindowsStartupRequired"));
        Assert.Null(typeof(FrontendBootstrapSnapshot).GetProperty("StartupRegistrationMessage"));
        Assert.Null(typeof(IAddonFrontendControl).GetMethod("SetLaunchAtWindowsStartupAsync"));
        Assert.Null(typeof(FrontendSettingsSnapshot).Assembly.GetType(
            "SteamInputAddonforClaw.Contracts.Frontend.FrontendLaunchAtStartupResult"));
        Assert.DoesNotContain("SetLaunchAtWindowsStartup", Enum.GetNames<SteamInputAddonforClaw.FrontendTransport.FrontendRpcMethod>());
    }

    [Fact]
    public void Settings_snapshot_round_trips_through_SystemTextJson()
    {
        var value = new FrontendSettingsSnapshot(FrontendLogLevel.Debug, true, Oem1MappingSettings.Default);
        var restored = JsonSerializer.Deserialize<FrontendSettingsSnapshot>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
    }

    [Fact]
    public void Developer_snapshot_round_trips_through_SystemTextJson()
    {
        var value = new FrontendDeveloperSnapshot(true);
        var restored = JsonSerializer.Deserialize<FrontendDeveloperSnapshot>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
    }

    [Fact]
    public void Bootstrap_snapshot_round_trips_through_SystemTextJson()
    {
        var value = new FrontendBootstrapSnapshot(
            new(FrontendLogLevel.Info, false, Oem1MappingSettings.Default),
            new(false),
            @"C:\Logs",
            Oem1MappingAvailable: true);
        var restored = JsonSerializer.Deserialize<FrontendBootstrapSnapshot>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
    }

    [Fact]
    public void PrerequisiteSetupResult_round_trips_through_SystemTextJson()
    {
        var value = new FrontendPrerequisiteSetupResult(FrontendPrerequisiteSetupResultKind.RebootRequired, SampleStatus);
        var restored = JsonSerializer.Deserialize<FrontendPrerequisiteSetupResult>(JsonSerializer.Serialize(value));
        Assert.Equivalent(value, restored, strict: true);
    }

    [Fact]
    public void PrerequisiteSetupResult_distinguishes_not_installable_from_blocked()
    {
        Assert.NotEqual(FrontendPrerequisiteSetupResultKind.NotInstallable, FrontendPrerequisiteSetupResultKind.Blocked);
        Assert.Contains(
            FrontendPrerequisiteSetupResultKind.NotInstallable,
            Enum.GetValues<FrontendPrerequisiteSetupResultKind>());
    }

    [Fact]
    public void EnvironmentReportResult_round_trips_through_SystemTextJson()
    {
        var value = new FrontendEnvironmentReportResult(false, "boom");
        var restored = JsonSerializer.Deserialize<FrontendEnvironmentReportResult>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
    }

    [Fact]
    public void Mapper_translates_hardware_status_supported() =>
        Assert.Equal(FrontendHardwareStatus.Supported, Snapshot(hardwareStatus: HardwareCompatibilityStatus.Supported).Hardware.Status);

    [Fact]
    public void Mapper_translates_hardware_status_unsupported() =>
        Assert.Equal(FrontendHardwareStatus.Unsupported, Snapshot(hardwareStatus: HardwareCompatibilityStatus.Unsupported).Hardware.Status);

    [Fact]
    public void Mapper_translates_hardware_status_indeterminate() =>
        Assert.Equal(FrontendHardwareStatus.Indeterminate, Snapshot(hardwareStatus: HardwareCompatibilityStatus.Indeterminate).Hardware.Status);

    [Theory]
    [InlineData(false, 0u, SteamSessionSource.Actual, false, 0u, FrontendSteamSource.Actual)]
    [InlineData(true, 480u, SteamSessionSource.Actual, true, 480u, FrontendSteamSource.Actual)]
    [InlineData(true, 480u, SteamSessionSource.BigPicture, true, 480u, FrontendSteamSource.BigPicture)]
    public void Mapper_translates_steam_state(bool isActive, uint appId, SteamSessionSource source, bool expectedActive, uint expectedAppId, FrontendSteamSource expectedSource)
    {
        var mapped = Snapshot(steamActive: isActive, steamAppId: appId, steamSource: source).Steam;
        Assert.Equal(expectedActive, mapped.Active);
        Assert.Equal(expectedAppId, mapped.AppId);
        Assert.Equal(expectedSource, mapped.Source);
    }

    [Fact]
    public void Mapper_preserves_recovery_unsafe()
    {
        var mapped = Snapshot(recoverySafe: false);
        Assert.False(mapped.RecoverySafe);
    }

    [Fact]
    public void Mapper_translates_prerequisite_status_ready() =>
        Assert.Equal(FrontendPrerequisiteStatus.Ready, Snapshot(hidHideStatus: PrerequisiteStatus.Ready).Prerequisites.HidHideStatus);

    [Fact]
    public void Mapper_translates_prerequisite_status_missing() =>
        Assert.Equal(FrontendPrerequisiteStatus.Missing, Snapshot(hidHideStatus: PrerequisiteStatus.Missing).Prerequisites.HidHideStatus);

    [Fact]
    public void Mapper_translates_prerequisite_status_indeterminate() =>
        Assert.Equal(FrontendPrerequisiteStatus.Indeterminate, Snapshot(hidHideStatus: PrerequisiteStatus.Indeterminate).Prerequisites.HidHideStatus);

    [Fact]
    public void Mapper_preserves_fail_closed_safety_values()
    {
        var mapped = Snapshot(
            hardwareStatus: HardwareCompatibilityStatus.Indeterminate,
            recoverySafe: false);

        Assert.False(mapped.RecoverySafe);
        Assert.Equal(FrontendHardwareStatus.Indeterminate, mapped.Hardware.Status);
        Assert.False(mapped.CanInstallRequiredComponents);
    }

    [Fact]
    public void Mapper_translates_addon_status_to_frontend_enum() =>
        Assert.Equal(FrontendAddonOperationalStatus.WaitingForSteam, Snapshot(addonStatus: AddonOperationalStatus.WaitingForSteam).AddonStatus);

    [Fact]
    public void Mapper_unknown_steam_source_fails_closed_to_indeterminate()
    {
        var unknownSteam = (SteamSessionSource)int.MaxValue;
        Assert.Equal(FrontendSteamSource.Indeterminate, Snapshot(steamSource: unknownSteam).Steam.Source);
    }

    [Fact]
    public void Mapper_unknown_addon_status_fails_closed_to_indeterminate()
    {
        var unknownAddonStatus = (AddonOperationalStatus)int.MaxValue;
        Assert.Equal(FrontendAddonOperationalStatus.Indeterminate, Snapshot(addonStatus: unknownAddonStatus).AddonStatus);
    }

    [Fact]
    public void ApplySetup_translates_every_setup_status()
    {
        foreach (var (status, expected) in new (FirstTimeSetupStatus, FrontendSetupStatus)[]
        {
            (FirstTimeSetupStatus.Complete, FrontendSetupStatus.Complete),
            (FirstTimeSetupStatus.Required, FrontendSetupStatus.Required),
            (FirstTimeSetupStatus.RestartRequired, FrontendSetupStatus.RestartRequired),
            (FirstTimeSetupStatus.Blocked, FrontendSetupStatus.Blocked),
            (FirstTimeSetupStatus.NotApplicable, FrontendSetupStatus.NotApplicable),
            (FirstTimeSetupStatus.Indeterminate, FrontendSetupStatus.Indeterminate),
        })
        {
            var setup = new FirstTimeSetupAssessment(status, FirstTimeSetupReason.MissingComponents, false);
            var applied = FrontendSnapshotMapper.ApplySetup(SampleStatus, setup);
            Assert.Equal(expected, applied.SetupStatus);
        }
    }

    [Fact]
    public void ApplySetup_CanInstallRequiredComponents_requires_both_required_status_and_installable_flag()
    {
        // Regression: CanInstallRequiredComponents must mirror
        // PrerequisiteSetupPromptPolicy.IsInstallable exactly (Status == Required AND the
        // installable flag), not just the raw flag on its own -- otherwise the UI could prompt to
        // install prerequisites while setup is Blocked or already Complete.
        var blockedButFlaggedInstallable = new FirstTimeSetupAssessment(FirstTimeSetupStatus.Blocked, FirstTimeSetupReason.RecoveryUnsafe, true);
        Assert.False(FrontendSnapshotMapper.ApplySetup(SampleStatus, blockedButFlaggedInstallable).CanInstallRequiredComponents);

        var requiredAndInstallable = new FirstTimeSetupAssessment(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true);
        Assert.True(FrontendSnapshotMapper.ApplySetup(SampleStatus, requiredAndInstallable).CanInstallRequiredComponents);
    }

    private static FrontendStatusSnapshot Snapshot(
        HardwareCompatibilityStatus hardwareStatus = HardwareCompatibilityStatus.Supported,
        bool steamActive = false,
        uint steamAppId = 0,
        SteamSessionSource steamSource = SteamSessionSource.Actual,
        bool recoverySafe = true,
        PrerequisiteStatus hidHideStatus = PrerequisiteStatus.Ready,
        AddonOperationalStatus addonStatus = AddonOperationalStatus.Ready)
    {
        var runtime = new SystemStatusSnapshot(
            new("MSI", "Claw", "BOARD", []),
            new(hardwareStatus, null, null, "Test"),
            new(new(PrerequisiteKind.HidHide, hidHideStatus, "Test"), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Ready, "Test"), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Ready, "Test")),
            new(steamActive, steamAppId, steamSource),
            new(addonStatus, "Test"),
            recoverySafe);

        return FrontendSnapshotMapper.Map(runtime);
    }
}
