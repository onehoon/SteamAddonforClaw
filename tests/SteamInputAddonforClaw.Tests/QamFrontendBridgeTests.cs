using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamFrontendBridgeTests
{
    // SF-V2-04/08: the generic Quick Settings QAM seam must reach the same shared Runtime mutation
    // for Device only through the exact current Device admission rule (Big Picture + no running
    // game); Profile generic mutation reaches the Runtime directly -- the SF-V2-08
    // QuickSettingsMutationAdapter is the AppId/current-target/row validation authority, not a
    // second QAM-side check.

    private static FrontendStatusSnapshot StatusWith(FrontendSteamSnapshot steam) => new(
        new("MSI", "Claw", "Board", ["GPU"]),
        new(FrontendHardwareStatus.Supported, "Family", "Model", "Ready"),
        new(FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, ""),
        steam,
        FrontendAddonOperationalStatus.Ready, "Ready", true, FrontendSetupStatus.Complete, "Complete", false);

    private static string Request(string method, object payload) =>
        JsonSerializer.Serialize(new { id = 1, method, payload }, QamFrontendBridge.BridgeJson);

    private static QuickSettingsMutationIntent CpuBoostToggleIntent(QuickSettingsPageId page = QuickSettingsPageId.Device) =>
        new(page, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

    private static async Task<(QamFrontendBridge Bridge, GenericQuickSettingsControl Fake, NamedPipeAddonFrontendServer Server)> StartAsync(FrontendSteamSnapshot steam)
    {
        var pipeName = $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}.Qam";
        var fake = new GenericQuickSettingsControl { Status = StatusWith(steam) };
        var server = new NamedPipeAddonFrontendServer(pipeName, fake);
        await server.StartAsync();
        var bridge = new QamFrontendBridge(pipeName);
        await bridge.ConnectAsync(CancellationToken.None);
        return (bridge, fake, server);
    }

    [Fact]
    public async Task Generic_device_mutation_is_admitted_in_big_picture_with_no_running_game()
    {
        var (bridge, fake, server) = await StartAsync(new(true, 0, FrontendSteamSource.BigPicture));
        await using var _ = server;
        await using var __ = bridge;

        var response = await bridge.HandleRequestAsync(Request("mutateQuickSetting", CpuBoostToggleIntent()), CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(1, fake.MutateCount);
        Assert.Equal(QuickSettingsRowId.DeviceCpuBoostEnabled, fake.LastIntent?.EditedRowId);
    }

    [Theory]
    [InlineData(true, 480u, FrontendSteamSource.BigPicture)]
    [InlineData(true, 0u, FrontendSteamSource.Actual)]
    [InlineData(false, 0u, FrontendSteamSource.BigPicture)]
    public async Task Generic_device_mutation_is_rejected_outside_big_picture_or_with_a_running_game(bool active, uint appId, FrontendSteamSource source)
    {
        var (bridge, fake, server) = await StartAsync(new(active, appId, source));
        await using var _ = server;
        await using var __ = bridge;

        var response = await bridge.HandleRequestAsync(Request("mutateQuickSetting", CpuBoostToggleIntent()), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, fake.MutateCount);
    }

    [Fact]
    public async Task Generic_profile_mutation_reaches_the_runtime_without_device_admission()
    {
        // Section 9.3: Profile has no bridge-level admission check -- it must reach the Runtime even
        // when Device admission (Big Picture + no running game) does not hold, because a running game
        // is exactly the Profile page's own precondition.
        var (bridge, fake, server) = await StartAsync(new(true, 480, FrontendSteamSource.Actual));
        await using var _ = server;
        await using var __ = bridge;
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 480, QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]);

        var response = await bridge.HandleRequestAsync(Request("mutateQuickSetting", intent), CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(1, fake.MutateCount);
        Assert.Equal(QuickSettingsPageId.Profile, fake.LastIntent?.PageId);
    }

    [Fact]
    public async Task Generic_mutation_for_an_unknown_page_is_rejected()
    {
        var (bridge, fake, server) = await StartAsync(new(true, 0, FrontendSteamSource.BigPicture));
        await using var _ = server;
        await using var __ = bridge;

        const string json = """
            { "id": 1, "method": "mutateQuickSetting", "payload": { "pageId": 99, "appId": null, "editedRowId": 0, "values": [ { "rowId": 0, "value": { "kind": 0, "booleanValue": true } } ] } }
            """;

        var response = await bridge.HandleRequestAsync(json, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, fake.MutateCount);
    }

    [Fact]
    public async Task Generic_mutation_missing_required_identity_fields_is_rejected_before_runtime_call()
    {
        var (bridge, fake, server) = await StartAsync(new(true, 0, FrontendSteamSource.BigPicture));
        await using var _ = server;
        await using var __ = bridge;

        // PageId / EditedRowId / nested RowId / Value.Kind all omitted -- must not default to enum
        // member 0 (Device / DeviceTdpEnabled / Boolean) and reach the Runtime.
        const string json = """
            { "id": 1, "method": "mutateQuickSetting", "payload": { "appId": null, "values": [ { "value": { "booleanValue": true } } ] } }
            """;

        var response = await bridge.HandleRequestAsync(json, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, fake.MutateCount);
    }

    [Fact]
    public async Task Generic_capture_missing_page_identity_is_rejected_before_runtime_call()
    {
        var (bridge, fake, server) = await StartAsync(new(true, 0, FrontendSteamSource.BigPicture));
        await using var _ = server;
        await using var __ = bridge;

        const string json = """
            { "id": 1, "method": "captureQuickSettingsPage", "payload": { "appId": null } }
            """;

        var response = await bridge.HandleRequestAsync(json, CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, fake.CaptureCount);
    }

    [Fact]
    public async Task Generic_page_capture_round_trips_through_the_bridge_and_does_not_require_admission()
    {
        var (bridge, fake, server) = await StartAsync(new(true, 480, FrontendSteamSource.Actual));
        await using var _ = server;
        await using var __ = bridge;

        var response = await bridge.HandleRequestAsync(Request("captureQuickSettingsPage", new { pageId = QuickSettingsPageId.Device, appId = (uint?)null }), CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal(1, fake.CaptureCount);
        Assert.Equal(QuickSettingsPageId.Device, fake.LastPageId);
    }

    [Fact]
    public async Task Malformed_bridge_payload_returns_bounded_error()
    {
        await using var bridge = new QamFrontendBridge();

        var response = await bridge.HandleRequestAsync("{", CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, response.Id);
        Assert.NotNull(response.Error);
    }

    private sealed class GenericQuickSettingsControl : IAddonFrontendControl
    {
        public event EventHandler? StateInvalidated { add { } remove { } }

        public FrontendStatusSnapshot Status { get; set; } = null!;
        public int CaptureCount { get; private set; }
        public int MutateCount { get; private set; }
        public QuickSettingsPageId? LastPageId { get; private set; }
        public QuickSettingsMutationIntent? LastIntent { get; private set; }

        private static readonly QuickSettingsPageSnapshot Page = new(
            QuickSettingsPageId.Device, null, true, null,
            [new(QuickSettingsSectionId.DeviceCpuBoost, "CPU Boost",
                [new(QuickSettingsRowId.DeviceCpuBoostEnabled, "CPU Boost", QuickSettingsControlKind.Toggle, true, true, QuickSettingsValue.Boolean(true), null, QuickSettingsCommitPolicy.Immediate)])],
            []);

        public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => Task.FromResult(Status);

        public Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(QuickSettingsPageId pageId, uint? appId = null, CancellationToken t = default)
        { CaptureCount++; LastPageId = pageId; return Task.FromResult(pageId == QuickSettingsPageId.Device && appId is null ? Page : QuickSettingsPageSnapshot.Unavailable(pageId, appId)); }

        public Task<QuickSettingsMutationResult> MutateQuickSettingAsync(QuickSettingsMutationIntent intent, CancellationToken t = default)
        { MutateCount++; LastIntent = intent; return Task.FromResult(new QuickSettingsMutationResult(true, null, Page)); }

        public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(FrontButtonMappingSettings mapping, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => throw new NotSupportedException();
    }
}
