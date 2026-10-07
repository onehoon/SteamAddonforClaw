using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayTransportTests
{
    [Fact]
    public void Overlay_endpoint_is_distinct_from_the_main_frontend_endpoint()
    {
        var frontend = FrontendPipeEndpoint.CreateForCurrentUser();
        var overlay = FrontendPipeEndpoint.CreateOverlayForCurrentUser();

        Assert.NotEqual(frontend, overlay);
        Assert.EndsWith(".Overlay", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_profile_show_remains_a_narrow_overlay_command()
    {
        Assert.Equal(18, OverlayTransportProtocol.CurrentVersion);
        Assert.Equal(67, FrontendTransportProtocol.CurrentVersion);

        var command = new OverlayWireMessage(
            OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.Command,
            Command: OverlayCommand.ShowActiveProfile);

        Assert.True(OverlayCommandWireValidation.IsValidCommand(command));
        Assert.False(OverlayCommandWireValidation.IsValidCommand(command with { Navigation = OverlayNavigationAction.NavigateDown }));
        Assert.False(OverlayCommandWireValidation.IsValidCommand(command with { ProtocolVersion = OverlayTransportProtocol.CurrentVersion - 1 }));
        Assert.False(OverlayCommandWireValidation.IsValidCommand(command with { Command = (OverlayCommand)99 }));
    }

    [Fact]
    public async Task Oversized_overlay_frame_is_rejected()
    {
        await using var stream = new MemoryStream();
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, OverlayTransportProtocol.MaxFrameBytes + 1);
        await stream.WriteAsync(prefix);
        stream.Position = 0;

        await Assert.ThrowsAsync<FrontendProtocolException>(() => OverlayWireCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(ProfileTargets))]
    public async Task Active_profile_page_and_mutation_target_round_trip(QuickSettingsProfileTarget target)
    {
        var page = new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, target, true, null, [], []);
        var pageMessage = await RoundTripAsync(new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.QuickSettingsPageState, QuickSettingsPage: page));

        Assert.Equal(target, pageMessage.QuickSettingsPage!.ProfileTarget);
        Assert.True(OverlayQuickSettingsWireValidation.IsStructurallyValid(pageMessage.QuickSettingsPage));

        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, target, QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]);
        var request = new OverlayQuickSettingsMutationRequest(1, intent);
        Assert.True(OverlayQuickSettingsWireValidation.IsStructurallyValid(request));
        var mutationMessage = await RoundTripAsync(new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.QuickSettingsMutationRequest, QuickSettingsMutationRequest: request));

        Assert.Equal(target, mutationMessage.QuickSettingsMutationRequest!.Intent.ProfileTarget);
    }

    [Fact]
    public async Task Xbox_profile_controller_page_and_grouped_mapping_intent_round_trip()
    {
        var target = QuickSettingsProfileTarget.ForXbox("xbox:test-game");
        var options = new[]
        {
            new QuickSettingsDiscreteOption(0, "Disabled"),
            new QuickSettingsDiscreteOption(9, "Left Bumper (LB)"),
            new QuickSettingsDiscreteOption(10, "Right Bumper (RB)"),
        };
        var page = new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, target, true, null,
        [
            new(QuickSettingsSectionId.ProfileGeneral, "Xbox Game",
                [new(QuickSettingsRowId.ProfileEnabled, "Profile", QuickSettingsControlKind.Toggle, true, true, QuickSettingsValue.Boolean(true), null, QuickSettingsCommitPolicy.Immediate)]),
            new(QuickSettingsSectionId.ProfileController, "M1 / M2 Button Mapping",
            [
                new(QuickSettingsRowId.ProfileBackButtonUseGlobal, "M1 / M2 Button Mapping", QuickSettingsControlKind.Toggle,
                    true, true, QuickSettingsValue.Boolean(true), null, QuickSettingsCommitPolicy.Immediate),
                new(QuickSettingsRowId.ProfileBackButtonM1, "M1", QuickSettingsControlKind.Slider, true, true,
                    QuickSettingsValue.Integer(9), new(QuickSettingsSliderKind.Discrete, Options: options),
                    QuickSettingsCommitPolicy.TrailingDebounce300, QuickSettingsCommitGroupId.ProfileBackButtonMapping),
                new(QuickSettingsRowId.ProfileBackButtonM2, "M2", QuickSettingsControlKind.Slider, true, true,
                    QuickSettingsValue.Integer(10), new(QuickSettingsSliderKind.Discrete, Options: options),
                    QuickSettingsCommitPolicy.TrailingDebounce300, QuickSettingsCommitGroupId.ProfileBackButtonMapping),
            ]),
        ], []);

        var pageMessage = await RoundTripAsync(new(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.QuickSettingsPageState, QuickSettingsPage: page));
        Assert.Equivalent(page, pageMessage.QuickSettingsPage, strict: true);
        Assert.True(OverlayQuickSettingsWireValidation.IsStructurallyValid(pageMessage.QuickSettingsPage));

        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, target,
            QuickSettingsRowId.ProfileBackButtonM2,
            [
                new(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(false)),
                new(QuickSettingsRowId.ProfileBackButtonM1, QuickSettingsValue.Integer(9)),
                new(QuickSettingsRowId.ProfileBackButtonM2, QuickSettingsValue.Integer(10)),
            ]);
        var request = new OverlayQuickSettingsMutationRequest(7, intent);
        Assert.True(OverlayQuickSettingsWireValidation.IsStructurallyValid(request));
        var mutationMessage = await RoundTripAsync(new(OverlayTransportProtocol.CurrentVersion,
            OverlayWireMessageKind.QuickSettingsMutationRequest, QuickSettingsMutationRequest: request));
        Assert.Equivalent(request, mutationMessage.QuickSettingsMutationRequest, strict: true);
        Assert.True(OverlayQuickSettingsWireValidation.IsStructurallyValid(mutationMessage.QuickSettingsMutationRequest));
    }

    public static IEnumerable<object[]> ProfileTargets =>
    [
        [QuickSettingsProfileTarget.ForSteam(123)],
        [QuickSettingsProfileTarget.ForXbox("xbox:test-game")],
    ];

    [Fact]
    public void Malformed_profile_targets_and_device_targets_are_rejected()
    {
        Assert.True(QuickSettingsProfileTarget.ForSteam(123).IsStructurallyValid);
        Assert.True(QuickSettingsProfileTarget.ForXbox("xbox:test-game").IsStructurallyValid);
        Assert.False(new QuickSettingsProfileTarget(QuickSettingsProfileTargetKind.Steam, 123, "xbox:key").IsStructurallyValid);
        Assert.False(new QuickSettingsProfileTarget(QuickSettingsProfileTargetKind.Steam, SteamAppId: null).IsStructurallyValid);
        Assert.False(QuickSettingsProfileTarget.ForSteam(0).IsStructurallyValid);
        Assert.False(new QuickSettingsProfileTarget(QuickSettingsProfileTargetKind.Xbox, SteamAppId: 123, XboxGameKey: "xbox:key").IsStructurallyValid);
        Assert.False(QuickSettingsProfileTarget.ForXbox(" ").IsStructurallyValid);
        Assert.True(OverlayQuickSettingsWireValidation.IsStructurallyValid(
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, null, true, null, [], [])));
        Assert.False(OverlayQuickSettingsWireValidation.IsStructurallyValid(
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, null, true, null, [], [])));
        Assert.False(OverlayQuickSettingsWireValidation.IsStructurallyValid(
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, new(QuickSettingsProfileTargetKind.Steam, SteamAppId: 123, XboxGameKey: "x"), true, null, [], [])));
        Assert.False(OverlayQuickSettingsWireValidation.IsStructurallyValid(
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, QuickSettingsProfileTarget.ForSteam(123), true, null, [], [])));
        Assert.False(OverlayQuickSettingsWireValidation.IsStructurallyValid(new OverlayQuickSettingsMutationRequest(1,
            new(QuickSettingsPageId.Profile, new(QuickSettingsProfileTargetKind.Xbox, XboxGameKey: " "), QuickSettingsRowId.ProfileEnabled,
                [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]))));
        Assert.False(OverlayQuickSettingsWireValidation.IsStructurallyValid(new OverlayQuickSettingsMutationRequest(1,
            new(QuickSettingsPageId.Device, QuickSettingsProfileTarget.ForSteam(123), QuickSettingsRowId.DeviceCpuBoostEnabled,
                [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true))]))));
    }

    [Fact]
    public void Retired_profile_catalog_and_selected_page_wire_types_are_absent()
    {
        var messageKinds = Enum.GetNames<OverlayWireMessageKind>();
        Assert.DoesNotContain("ProfileCatalogRequest", messageKinds);
        Assert.DoesNotContain("ProfileCatalogState", messageKinds);
        Assert.DoesNotContain("ProfilePageRequest", messageKinds);
        Assert.DoesNotContain("ProfilePageResult", messageKinds);

        var typeNames = typeof(OverlayWireMessage).Assembly.GetTypes().Select(type => type.Name).ToArray();
        Assert.DoesNotContain("OverlayProfileCatalogState", typeNames);
        Assert.DoesNotContain("OverlayProfilePageRequest", typeNames);
        Assert.DoesNotContain("OverlayProfilePageResponse", typeNames);
    }

    private static async Task<OverlayWireMessage> RoundTripAsync(OverlayWireMessage message)
    {
        await using var stream = new MemoryStream();
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(stream, message, writeGate, CancellationToken.None);
        stream.Position = 0;
        return await OverlayWireCodec.ReadAsync(stream, CancellationToken.None);
    }

    [Fact]
    public async Task V16_peer_is_rejected_by_the_overlay_server()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(16, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
        Assert.Contains("version", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pre_navigation_v2_peer_is_rejected_by_the_overlay_server()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(2, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
    }

    [Fact]
    public async Task Server_delivers_a_semantic_navigation_frame_to_a_visible_overlay()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var actions = new List<OverlayNavigationAction>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, action =>
        {
            lock (actions) actions.Add(action);
            received.TrySetResult();
            return Task.CompletedTask;
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendNavigationAsync(OverlayNavigationAction.NavigateDown));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (actions) Assert.Equal([OverlayNavigationAction.NavigateDown], actions);
        Assert.Equal(OverlayState.Visible, server.State);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Hidden_or_unready_overlay_does_not_accept_navigation_delivery()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();

        // No client connected yet -> unready.
        Assert.False(await server.SendNavigationAsync(OverlayNavigationAction.Accept));

        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        // Ready but still Hidden (no Show yet).
        Assert.False(await server.SendNavigationAsync(OverlayNavigationAction.Accept));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        // Back to Hidden -> rejected again.
        Assert.False(await server.SendNavigationAsync(OverlayNavigationAction.Accept));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Command_and_navigation_frames_stay_intact_through_the_shared_write_gate()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var actions = new List<OverlayNavigationAction>();
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, action =>
        {
            lock (actions)
            {
                actions.Add(action);
                if (actions.Count == Enum.GetValues<OverlayNavigationAction>().Length) all.TrySetResult();
            }
            return Task.CompletedTask;
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var everyAction = Enum.GetValues<OverlayNavigationAction>();
        await Task.WhenAll(everyAction.Select(a => server.SendNavigationAsync(a)));
        await all.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (actions) Assert.Equal(everyAction.OrderBy(x => x), actions.OrderBy(x => x));

        // Connection still usable for a normal command after the navigation burst.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Warm_overlay_protocol_round_trips_show_hide_shutdown()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Overlay_client_sends_ready_without_an_unsolicited_hidden_state()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var writeGate = new SemaphoreSlim(1, 1);

        var hello = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.Handshake, hello.Kind);
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.HandshakeAccepted), writeGate, CancellationToken.None);
        // OQ5-UI-09: the client applies the initial authoritative order before it reports Ready.
        await OverlayWireCodec.WriteAsync(pipe, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.TabOrderState,
            TabOrderState: AddonQuickSettingsTabOrderProduct.Create(AddonQuickSettingsTabOrderContract.DefaultOrder)), writeGate, CancellationToken.None);

        var ready = await OverlayWireCodec.ReadAsync(pipe, CancellationToken.None);
        Assert.Equal(OverlayState.Ready, ready.State);
        using var noExtraState = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OverlayWireCodec.ReadAsync(pipe, noExtraState.Token));

        await client.DisposeAsync();
        try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
    }

    [Fact]
    public async Task Immediate_show_after_ready_is_acknowledged_by_the_real_visible_state()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted
        var initialOrder = await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // OQ5-UI-09 TabOrderState
        Assert.Equal(OverlayWireMessageKind.TabOrderState, initialOrder.Kind);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        var show = server.SendCommandAsync(OverlayCommand.Show);
        var command = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        Assert.Equal(OverlayCommand.Show, command.Command);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Visible), writeGate, CancellationToken.None);

        Assert.True(await show.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(OverlayState.Visible, server.State);
    }

    [Fact]
    public async Task Dismiss_requested_round_trips_without_mutating_overlay_state()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var dismissal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DismissRequested += _ => dismissal.TrySetResult();
        var run = client.RunAsync(_ => Task.CompletedTask);

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        await client.SendDismissRequestedAsync();
        await dismissal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Active_profile_show_reaches_overlay_handler_and_settles_visible()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var commands = new List<OverlayCommand>();
        var run = client.RunAsync(command =>
        {
            lock (commands) commands.Add(command);
            return Task.CompletedTask;
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.ShowActiveProfile));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.Equal(OverlayState.Visible, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        lock (commands)
            Assert.Equal([OverlayCommand.ShowActiveProfile, OverlayCommand.Show, OverlayCommand.Hide, OverlayCommand.Shutdown], commands);
    }

    [Fact]
    public async Task Dismiss_requested_does_not_complete_an_in_flight_show_acknowledgement()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var showReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseShow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dismissal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DismissRequested += _ => dismissal.TrySetResult();
        var run = client.RunAsync(async command =>
        {
            if (command == OverlayCommand.Show)
            {
                showReceived.TrySetResult();
                await releaseShow.Task;
            }
        });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        var show = server.SendCommandAsync(OverlayCommand.Show);
        await showReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.SendDismissRequestedAsync();
        await dismissal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await Task.WhenAny(show, Task.Delay(100)) == show);

        releaseShow.TrySetResult();
        Assert.True(await show.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dismiss_request_raises_the_runtime_signal_and_the_controller_does_not_send_hide_itself()
    {
        // OQ4 section 10: the controller no longer finishes a visible Hide on outside-click -- it
        // validates the dismissal and raises OverlayDismissRequested; AddonProcessHost runs the
        // unified capture-retirement path (which then calls EnsureHiddenAsync).
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var commands = new List<OverlayCommand>();
        var dismissSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // `pause` with a redirected-but-never-written stdin blocks reliably; `timeout` exits early
        // when stdin is not a console, which would race the process-exit path into this test.
        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c pause",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.OverlayDismissRequested += () => dismissSignal.TrySetResult();
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(command =>
            {
                lock (commands) commands.Add(command);
                return Task.CompletedTask;
            });

            Assert.True(await controller.ShowAsync());
            Assert.True(controller.IsVisible);
            await client.SendDismissRequestedAsync();
            await dismissSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);

            // The controller raised the signal but issued no Hide of its own; the overlay is still
            // visible until the Runtime runs the unified retirement path.
            lock (commands) Assert.Equal([OverlayCommand.Show], commands);
            Assert.True(controller.IsVisible);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Ensure_hidden_that_cannot_run_reports_failure_and_keeps_the_surface_visible()
    {
        // OQ4 PR3 review [2]: this is the exact signal AddonProcessHost.RetireOverlayCaptureUnder-
        // TransitionAsync gates on -- EnsureHiddenAsync() == false while IsVisible stays true means
        // "retirement not proven", which blocks the following Main UI launch.
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c pause",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask);

            Assert.True(await controller.ShowAsync());
            Assert.True(controller.IsVisible);

            controller.BeginShutdown(); // a transient state where EnsureHiddenAsync cannot run

            Assert.False(await controller.EnsureHiddenAsync());
            Assert.True(controller.IsVisible); // surface not proven gone -> host blocks Main UI launch

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Explicit_show_and_hide_track_visibility_and_are_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var commands = new List<OverlayCommand>();

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(command =>
            {
                lock (commands) commands.Add(command);
                return Task.CompletedTask;
            });

            Assert.False(controller.IsVisible);
            Assert.True(await controller.EnsureHiddenAsync()); // idempotent while already hidden
            Assert.True(await controller.ShowAsync(preferActiveProfile: true));
            Assert.True(controller.IsVisible);
            Assert.True(await controller.ShowAsync()); // idempotent while already visible
            Assert.True(await controller.EnsureHiddenAsync());
            Assert.False(controller.IsVisible);
            await Task.Delay(100);
            lock (commands) Assert.Equal([OverlayCommand.ShowActiveProfile, OverlayCommand.Hide], commands);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // SF-V2-02/09 section 17.2: a refresh started while visible must not intentionally publish to a
    // session that became hidden before the (possibly slow) capture completed.
    [Fact]
    public async Task RefreshQuickSettingsAsync_does_not_publish_after_the_session_is_hidden_mid_capture()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var captureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // `pause` with a redirected-but-never-written stdin blocks reliably; `timeout` exits early
        // when stdin is not a console, which would race the process-exit path into this test.
        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c pause",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.BindQuickSettingsAuthority(
                captureDevicePage: async _ =>
                {
                    captureEntered.TrySetResult();
                    await releaseCapture.Task;
                    return QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Device);
                },
                captureProfilePage: _ => Task.FromResult(QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile)),
                mutate: (intent, _) => Task.FromResult(OverlayQuickSettingsWireValidation.NotAdmitted(intent, "unused")));

            var deviceFrames = new List<QuickSettingsPageSnapshot>();
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null,
                snapshot => { lock (deviceFrames) deviceFrames.Add(snapshot); return Task.CompletedTask; });

            Assert.True(await controller.ShowAsync());
            Assert.True(controller.IsVisible);

            var refreshTask = controller.RefreshQuickSettingsAsync();
            await captureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(await controller.EnsureHiddenAsync());

            releaseCapture.TrySetResult();
            await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            lock (deviceFrames) Assert.Empty(deviceFrames);

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Process? StartLongRunningTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
        UseShellExecute = false, CreateNoWindow = true
    });

    // SF-V2-09 section 7.7/29.1: page capture/publish remains sequential and follows the requested
    // initial tab priority without changing the default Device-first refresh.
    [Theory]
    [InlineData(false, QuickSettingsPageId.Device, QuickSettingsPageId.Profile)]
    [InlineData(true, QuickSettingsPageId.Profile, QuickSettingsPageId.Device)]
    public async Task RefreshQuickSettingsAsync_publishes_pages_in_the_requested_order(
        bool profileFirst, QuickSettingsPageId first, QuickSettingsPageId second)
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartLongRunningTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.BindQuickSettingsAuthority(
                captureDevicePage: _ => Task.FromResult(QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Device, message: "device-page")),
                captureProfilePage: _ => Task.FromResult(QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, message: "profile-page")),
                mutate: (intent, _) => Task.FromResult(OverlayQuickSettingsWireValidation.NotAdmitted(intent, "unused")));

            var frames = new List<QuickSettingsPageSnapshot>();
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null,
                snapshot => { lock (frames) frames.Add(snapshot); return Task.CompletedTask; });

            Assert.True(await controller.ShowAsync());
            await controller.RefreshQuickSettingsAsync(profileFirst);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline) { lock (frames) { if (frames.Count >= 2) break; } await Task.Delay(20); }

            lock (frames)
            {
                Assert.Equal(2, frames.Count);
                Assert.Equal(first, frames[0].PageId);
                Assert.Equal(first == QuickSettingsPageId.Device ? "device-page" : "profile-page", frames[0].Message);
                Assert.Equal(second, frames[1].PageId);
                Assert.Equal(second == QuickSettingsPageId.Device ? "device-page" : "profile-page", frames[1].Message);
            }

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // SF-V2-09 section 7.6/29.3: one page's capture failure must never suppress the other page's
    // publish -- the failing page is delivered as an explicit Unavailable page instead.
    [Fact]
    public async Task RefreshQuickSettingsAsync_profile_capture_failure_still_publishes_device_and_an_unavailable_profile_page()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartLongRunningTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.BindQuickSettingsAuthority(
                captureDevicePage: _ => Task.FromResult(QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Device, message: "device-ok")),
                captureProfilePage: _ => throw new InvalidOperationException("profile capture boom"),
                mutate: (intent, _) => Task.FromResult(OverlayQuickSettingsWireValidation.NotAdmitted(intent, "unused")));

            var frames = new List<QuickSettingsPageSnapshot>();
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null,
                snapshot => { lock (frames) frames.Add(snapshot); return Task.CompletedTask; });

            Assert.True(await controller.ShowAsync());
            await controller.RefreshQuickSettingsAsync();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline) { lock (frames) { if (frames.Count >= 2) break; } await Task.Delay(20); }

            lock (frames)
            {
                Assert.Equal(2, frames.Count);
                Assert.Equal(QuickSettingsPageId.Device, frames[0].PageId);
                Assert.Equal("device-ok", frames[0].Message);
                Assert.Equal(QuickSettingsPageId.Profile, frames[1].PageId);
                Assert.False(frames[1].Available); // caught capture failure -> explicit Unavailable
            }

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshQuickSettingsAsync_device_capture_failure_still_publishes_profile_and_an_unavailable_device_page()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartLongRunningTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.BindQuickSettingsAuthority(
                captureDevicePage: _ => throw new InvalidOperationException("device capture boom"),
                captureProfilePage: _ => Task.FromResult(QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, message: "profile-ok")),
                mutate: (intent, _) => Task.FromResult(OverlayQuickSettingsWireValidation.NotAdmitted(intent, "unused")));

            var frames = new List<QuickSettingsPageSnapshot>();
            await using var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(_ => Task.CompletedTask, null, null,
                snapshot => { lock (frames) frames.Add(snapshot); return Task.CompletedTask; });

            Assert.True(await controller.ShowAsync());
            await controller.RefreshQuickSettingsAsync();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline) { lock (frames) { if (frames.Count >= 2) break; } await Task.Delay(20); }

            lock (frames)
            {
                Assert.Equal(2, frames.Count);
                Assert.Equal(QuickSettingsPageId.Device, frames[0].PageId);
                Assert.False(frames[0].Available); // caught capture failure -> explicit Unavailable
                Assert.Equal(QuickSettingsPageId.Profile, frames[1].PageId);
                Assert.Equal("profile-ok", frames[1].Message);
            }

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Frontend_settings_capture_failure_does_not_suppress_vibration_publication()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var vibration = new FrontendControllerVibrationStrengthSnapshot(true, true, false, 35, 70, "Ready");

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartLongRunningTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            controller.BindProductionControlsAuthority(
                captureSettings: _ => throw new InvalidOperationException("settings capture boom"),
                mutateSettings: (_, _) => Task.FromResult(new OverlayFrontendSettingsMutationResponse(1, false, "unused",
                    new FrontendSettingsSnapshot(FrontendLogLevel.Off, false, FrontButtonMappingSettings.Default), false)),
                captureVibration: _ => Task.FromResult(vibration),
                mutateVibration: (_, _) => Task.FromResult(new FrontendControllerVibrationStrengthMutationResult(
                    FrontendControllerVibrationStrengthMutationOutcome.Unavailable, vibration, "unused")));

            var settingsReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var vibrationReceived = new TaskCompletionSource<FrontendControllerVibrationStrengthSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var client = new NamedPipeOverlayClient(pipeName);
            client.FrontendSettingsStateReceived += (_, _, available) => settingsReceived.TrySetResult(available);
            client.ControllerVibrationStateReceived += snapshot => vibrationReceived.TrySetResult(snapshot);
            var run = client.RunAsync(_ => Task.CompletedTask);

            Assert.True(await controller.ShowAsync());
            await controller.RefreshFrontendSettingsAsync();
            await controller.RefreshControllerVibrationAsync();

            Assert.False(await settingsReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(vibration, await vibrationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            await controller.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_explicit_hide_retires_the_session_and_reports_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        using var hangCancellation = new CancellationTokenSource();

        Process? StartTestProcess(ProcessStartInfo _) => Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
            UseShellExecute = false, CreateNoWindow = true
        });

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(pipeName));
            var client = new NamedPipeOverlayClient(pipeName);
            var run = client.RunAsync(async command =>
            {
                if (command == OverlayCommand.Hide)
                    await Task.Delay(Timeout.InfiniteTimeSpan, hangCancellation.Token);
            });

            Assert.True(await controller.ShowAsync());
            Assert.False(await controller.EnsureHiddenAsync());
            Assert.False(controller.HasTrackedProcess);
            Assert.False(controller.IsVisible);

            hangCancellation.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            await client.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Disconnect_allows_a_later_overlay_client_to_reconnect()
    {
        var pipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        long? firstGeneration;

        await using (var first = new NamedPipeOverlayClient(pipeName))
        {
            var run = first.RunAsync(_ => Task.CompletedTask);
            Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
            firstGeneration = server.ReadyGeneration;
            Assert.NotNull(firstGeneration);
            Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
            await first.DisposeAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Assert.True(await server.WaitForDisconnectedAsync(firstGeneration.Value, TimeSpan.FromSeconds(5)));
        }

        await using var second = new NamedPipeOverlayClient(pipeName);
        var secondRun = second.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(firstGeneration, server.ReadyGeneration);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await secondRun.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Missing_overlay_publish_payload_is_feature_local_and_shutdown_rejects_later_start()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"));

        Assert.False(await controller.StartAsync());
        controller.BeginShutdown();
        Assert.False(await controller.StartAsync());
    }

    [Fact]
    public async Task Failed_overlay_command_retires_the_session_for_the_next_toggle()
    {
        var root = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw.Overlay.Tests", Guid.NewGuid().ToString("N"));
        var overlayDirectory = Path.Combine(root, "overlay");
        Directory.CreateDirectory(overlayDirectory);
        File.WriteAllText(Path.Combine(overlayDirectory, "SteamInputAddonforClaw.Overlay.exe"), "test payload");
        var firstPipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var secondPipeName = $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";
        var currentPipeName = firstPipeName;
        var starts = 0;
        using var firstClientCancellation = new CancellationTokenSource();

        Process? StartTestProcess(ProcessStartInfo _) {
            Interlocked.Increment(ref starts);
            return Process.Start(new ProcessStartInfo {
                FileName = "cmd.exe", Arguments = "/c timeout /t 30 /nobreak >nul",
                UseShellExecute = false, CreateNoWindow = true
            });
        }

        async Task RunClientAsync(bool acknowledgeShow, CancellationToken cancellationToken = default)
        {
            await using var client = new NamedPipeOverlayClient(currentPipeName);
            try
            {
                await client.RunAsync(async command => {
                    if (command == OverlayCommand.Show && !acknowledgeShow)
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }, cancellationToken);
            }
            catch (Exception) { }
        }

        try
        {
            await using var controller = new OverlayProcessController(root, Path.Combine(root, "logs"),
                StartTestProcess, _ => new NamedPipeOverlayServer(currentPipeName));
            var firstClient = RunClientAsync(acknowledgeShow: false, cancellationToken: firstClientCancellation.Token);
            await controller.ToggleForPocAsync();
            Assert.Equal(1, starts);
            Assert.False(controller.HasTrackedProcess);
            // The intentionally hung handler models a dispatcher that never acknowledges Show.
            // Cancel the client after the controller retires that failed session.
            firstClientCancellation.Cancel();
            await firstClient.WaitAsync(TimeSpan.FromSeconds(5));

            currentPipeName = secondPipeName;
            var secondClient = RunClientAsync(acknowledgeShow: true);
            await Task.Delay(250);
            await controller.ToggleForPocAsync();
            Assert.Equal(2, starts);
            // A second process-start attempt proves the failed first session was retired;
            // cleanup below also covers the intentionally synthetic process.
            await controller.DisposeAsync();
            await secondClient.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
