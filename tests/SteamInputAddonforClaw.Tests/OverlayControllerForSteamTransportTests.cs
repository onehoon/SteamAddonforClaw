using System.IO.Pipes;
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
    public void Y_button_quick_jump_mutation_requires_only_one_defined_target()
    {
        Assert.True(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.YButtonQuickJumpMutationRequest,
                FrontendSettingsMutationRequest: new(1, YButtonQuickJumpTarget: QuickOverlayYButtonTarget.Off))));
        Assert.False(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.YButtonQuickJumpMutationRequest,
                FrontendSettingsMutationRequest: new(1))));
        Assert.False(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.YButtonQuickJumpMutationRequest,
                FrontendSettingsMutationRequest: new(1, YButtonQuickJumpTarget: (QuickOverlayYButtonTarget)99))));
        Assert.False(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationRequest(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.YButtonQuickJumpMutationRequest,
                FrontendSettingsMutationRequest: new(1, YButtonQuickJumpTarget: QuickOverlayYButtonTarget.Device, CurrentPowerSourceOnly: true))));

        var response = new OverlayFrontendSettingsMutationResponse(1, true, null,
            new FrontendSettingsSnapshot(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default)
            { YButtonQuickJumpTarget = QuickOverlayYButtonTarget.Off }, false);
        Assert.True(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationResult(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.YButtonQuickJumpMutationResult,
                FrontendSettingsMutationResponse: response)));
        Assert.False(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationResult(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.YButtonQuickJumpMutationResult,
                FrontendSettingsMutationResponse: response with { Settings = response.Settings with { YButtonQuickJumpTarget = (QuickOverlayYButtonTarget)99 } })));
        Assert.False(OverlayProductionControlsWireValidation.IsValidYButtonQuickJumpMutationResult(
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.CurrentPowerSourceMutationResult,
                FrontendSettingsMutationResponse: response)));
    }

    [Fact]
    public async Task Visible_overlay_y_mutation_uses_the_existing_correlated_settings_pipe()
    {
        var settings = new FrontendSettingsSnapshot(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default)
        { YButtonQuickJumpTarget = QuickOverlayYButtonTarget.Profile };
        OverlayFrontendSettingsMutationRequest? receivedRequest = null;
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureFrontendSettings: _ => Task.FromResult(new OverlayFrontendSettingsMutationResponse(0, true, null, settings, false)),
            mutateFrontendSettings: (request, _) =>
            {
                receivedRequest = request;
                settings = settings with { YButtonQuickJumpTarget = request.YButtonQuickJumpTarget!.Value };
                return Task.FromResult(new OverlayFrontendSettingsMutationResponse(request.RequestId, true, null, settings, false));
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendYButtonQuickJumpMutationAsync(QuickOverlayYButtonTarget.Controller)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(receivedRequest);
        Assert.True(receivedRequest.RequestId > 0);
        Assert.Equal(QuickOverlayYButtonTarget.Controller, receivedRequest.YButtonQuickJumpTarget);
        Assert.Null(receivedRequest.CurrentPowerSourceOnly);
        Assert.Equal(QuickOverlayYButtonTarget.Controller, result.Settings.YButtonQuickJumpTarget);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Hidden_overlay_y_mutation_returns_readback_without_calling_runtime_mutation()
    {
        var settings = new FrontendSettingsSnapshot(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default)
        { YButtonQuickJumpTarget = QuickOverlayYButtonTarget.Profile };
        var mutationCount = 0;
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName,
            captureFrontendSettings: _ => Task.FromResult(new OverlayFrontendSettingsMutationResponse(0, true, null, settings, false)),
            mutateFrontendSettings: (request, _) =>
            {
                Interlocked.Increment(ref mutationCount);
                return Task.FromResult(new OverlayFrontendSettingsMutationResponse(request.RequestId, true, null, settings, false));
            });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        var result = await client.SendYButtonQuickJumpMutationAsync(QuickOverlayYButtonTarget.Controller)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Contains("not visible", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(QuickOverlayYButtonTarget.Profile, result.Settings.YButtonQuickJumpTarget);
        Assert.Equal(0, Volatile.Read(ref mutationCount));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Y_mutation_rejects_a_same_request_id_power_source_result()
    {
        var pipeName = Pipe();
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);
        Assert.Equal(OverlayWireMessageKind.Handshake, (await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None)).Kind);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState,
                TabOrderState: AddonQuickSettingsTabOrderProduct.Create(AddonQuickSettingsTabOrderContract.DefaultOrder)),
            writeGate, CancellationToken.None);
        Assert.Equal(OverlayState.Ready, (await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None)).State);

        var mutation = client.SendYButtonQuickJumpMutationAsync(QuickOverlayYButtonTarget.Controller);
        var request = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OverlayWireMessageKind.YButtonQuickJumpMutationRequest, request.Kind);
        var response = new OverlayFrontendSettingsMutationResponse(
            request.FrontendSettingsMutationRequest!.RequestId,
            true,
            null,
            new FrontendSettingsSnapshot(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default),
            false);
        await OverlayWireCodec.WriteAsync(pipe,
            new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.CurrentPowerSourceMutationResult,
                FrontendSettingsMutationResponse: response), writeGate, CancellationToken.None);

        await Assert.ThrowsAsync<FrontendProtocolException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutation.WaitAsync(TimeSpan.FromSeconds(5)));
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
