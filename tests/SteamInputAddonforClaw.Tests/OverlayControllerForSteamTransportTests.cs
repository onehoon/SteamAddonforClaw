using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayControllerForSteamTransportTests
{
    private static string Pipe() => $"SteamInputAddonforClaw.Overlay.ControllerForSteam.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void Controller_for_steam_mutation_requires_exactly_one_known_setting()
    {
        Assert.True(OverlayProductionControlsWireValidation.IsValidControllerForSteamMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerForSteamMutationRequest,
                FrontendSettingsMutationRequest: new(1, GyroscopeEnabled: false))));
        Assert.True(OverlayProductionControlsWireValidation.IsValidControllerForSteamMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerForSteamMutationRequest,
                FrontendSettingsMutationRequest: new(1, SteamVirtualController: SteamVirtualControllerPreference.DualSenseEdge))));
        Assert.False(OverlayProductionControlsWireValidation.IsValidControllerForSteamMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerForSteamMutationRequest,
                FrontendSettingsMutationRequest: new(1))));
        Assert.False(OverlayProductionControlsWireValidation.IsValidControllerForSteamMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerForSteamMutationRequest,
                FrontendSettingsMutationRequest: new(1, SteamVirtualController: SteamVirtualControllerPreference.SteamDeck, GyroscopeEnabled: false))));
        Assert.False(OverlayProductionControlsWireValidation.IsValidControllerForSteamMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.ControllerForSteamMutationRequest,
                FrontendSettingsMutationRequest: new(1, SteamVirtualController: (SteamVirtualControllerPreference)99))));
    }

    [Fact]
    public async Task Correlated_overlay_mutation_preserves_explicit_gyro_false()
    {
        var requested = new TaskCompletionSource<OverlayFrontendSettingsMutationRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new FrontendSettingsSnapshot(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default)
        {
            GyroscopeEnabled = false,
            ControllerForSteam = new(true, SteamVirtualControllerPreference.SteamDeck, null, true),
        };
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName,
            mutateFrontendSettings: (request, _) =>
            {
                requested.TrySetResult(request);
                return Task.FromResult(new OverlayFrontendSettingsMutationResponse(
                    request.RequestId, true, null, settings, false));
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var response = await client.SendControllerForSteamMutationAsync(gyroscopeEnabled: false)
            .WaitAsync(TimeSpan.FromSeconds(5));

        var receivedRequest = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(receivedRequest.GyroscopeEnabled);
        Assert.Null(receivedRequest.SteamVirtualController);
        Assert.True(response.Succeeded);
        Assert.False(response.Settings.GyroscopeEnabled);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
