using System.Text.Json;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class QamFrontendBridgeTests
{
    // SF-V2-04: the generic Quick Settings QAM seam must reach the same shared Runtime mutation only
    // through the exact current Device admission rule (Big Picture + no running game), and Profile
    // generic mutation must not become admitted by transport availability alone.

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
    public async Task Generic_profile_mutation_is_not_admitted_by_transport_availability()
    {
        var (bridge, fake, server) = await StartAsync(new(true, 0, FrontendSteamSource.BigPicture));
        await using var _ = server;
        await using var __ = bridge;

        var response = await bridge.HandleRequestAsync(Request("mutateQuickSetting", CpuBoostToggleIntent(QuickSettingsPageId.Profile)), CancellationToken.None);

        Assert.False(response.Ok);
        Assert.Equal(0, fake.MutateCount);
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
    public void Tdp_configuration_round_trips_through_bridge_camel_case_json()
    {
        var expected = new FrontendTdpConfiguration(true, new(21, 31), new(11, 19));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { configuration = expected }, QamFrontendBridge.BridgeJson));

        var actual = QamFrontendBridge.DecodeTdpConfiguration(document.RootElement);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0, WindowsPowerMode.BestPowerEfficiency)]
    [InlineData(1, WindowsPowerMode.Balanced)]
    [InlineData(2, WindowsPowerMode.BestPerformance)]
    public void Power_mode_ordinal_payload_decodes_through_bridge(int ordinal, WindowsPowerMode expected)
    {
        using var document = JsonDocument.Parse($"{{\"mode\":{ordinal}}}");

        Assert.Equal(expected, QamFrontendBridge.DecodePowerMode(document.RootElement));
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
