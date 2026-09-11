using System.IO.Pipes;
using System.Text;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// SF-V2-06: .Overlay v7 replaces the v6 Device-specific state/mutation wire with the shared
// QuickSettingsPageSnapshot / QuickSettingsMutationIntent / QuickSettingsMutationResult contract
// already consumed by .Frontend/.Qam (SF-V2-04/05), inside narrow transport correlation wrappers.
// OQ4/lifecycle regression coverage lives in OverlayTransportTests/OverlayTabOrderTransportTests and
// is unaffected by this migration (verified green alongside this file).
public sealed class OverlayDeviceQuickSettingsTransportTests
{
    private static string Pipe() => $"SteamInputAddonforClaw.Overlay.Tests.{Guid.NewGuid():N}";

    // A representative page exercising every shared shape: Toggle, Numeric slider (with suffix),
    // Discrete slider (ordered options), Immediate/TrailingDebounce policy, the TDP commit group, and
    // linked slider constraints -- built through the real shared projection, not a hand-rolled copy.
    private static readonly QuickSettingsPageSnapshot SamplePage = QuickSettingsPresentation.BuildDevice(new FrontendDeviceQuickSettingsSnapshot(
        new FrontendCpuBoostSnapshot(new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Aggressive, CpuBoostMode.Aggressive), new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Disabled), true, true, null),
        new FrontendTdpSnapshot(true, true, new(true, new(20, 25), new(20, 25)), new(8, 30, 8, 37)),
        new FrontendPowerModeSnapshot(new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced), new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.BestPowerEfficiency, WindowsPowerMode.BestPowerEfficiency), true, true, null)));

    private static readonly QuickSettingsPageSnapshot PartialPage = QuickSettingsPresentation.BuildDevice(new FrontendDeviceQuickSettingsSnapshot(
        new FrontendCpuBoostSnapshot(new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Aggressive, CpuBoostMode.Aggressive), new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Disabled), true, true, null),
        FrontendTdpSnapshot.Unavailable,
        new FrontendPowerModeSnapshot(new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced), new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.BestPowerEfficiency, WindowsPowerMode.BestPowerEfficiency), true, true, null)));

    // ---- Protocol / handshake -----------------------------------------------------------------

    [Fact]
    public void Protocol_is_v7_and_frontend_transport_is_unaffected()
    {
        Assert.Equal(7, OverlayTransportProtocol.CurrentVersion);
        // SF-V2-06 owns only .Overlay v6 -> v7. The desktop/QAM frontend protocol is independent of it.
        Assert.Equal(28, FrontendTransportProtocol.CurrentVersion);
    }

    [Fact]
    public async Task A_v6_peer_is_rejected_by_the_v7_server()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);

        await OverlayWireCodec.WriteAsync(client, new(6, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        var response = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);

        Assert.Equal(OverlayWireMessageKind.ProtocolError, response.Kind);
    }

    [Fact]
    public async Task Quick_settings_page_state_is_not_a_mandatory_pre_ready_frame()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted

        // The mandatory pre-Ready frame is still exactly TabOrderState -- no Quick Settings page frame
        // is interleaved before it or required before the client may report Ready.
        var next = await OverlayWireCodec.ReadAsync(client, CancellationToken.None);
        Assert.Equal(OverlayWireMessageKind.TabOrderState, next.Kind);

        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
    }

    // ---- Shared Device page round trip ---------------------------------------------------------

    [Fact]
    public async Task Visible_overlay_receives_the_complete_shared_page_intact()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var received = new TaskCompletionSource<QuickSettingsPageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, page => { received.TrySetResult(page); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendQuickSettingsPageStateAsync(SamplePage));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equivalent(SamplePage, got, strict: true);
        // Prove the round trip actually exercised every shared shape, not just a page header.
        Assert.Contains(got.Sections, s => s.Rows.Any(r => r.ControlKind == QuickSettingsControlKind.Slider && r.SliderSpec!.Kind == QuickSettingsSliderKind.Numeric));
        Assert.Contains(got.Sections, s => s.Rows.Any(r => r.ControlKind == QuickSettingsControlKind.Slider && r.SliderSpec!.Kind == QuickSettingsSliderKind.Discrete));
        Assert.Contains(got.Sections, s => s.Rows.Any(r => r.CommitPolicy == QuickSettingsCommitPolicy.Immediate));
        Assert.Contains(got.Sections, s => s.Rows.Any(r => r.CommitGroupId == QuickSettingsCommitGroupId.DeviceTdpConfiguration));
        Assert.NotEmpty(got.LinkedSliderConstraints);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Partial_child_availability_survives_the_wire()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var received = new TaskCompletionSource<QuickSettingsPageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, page => { received.TrySetResult(page); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendQuickSettingsPageStateAsync(PartialPage));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equivalent(PartialPage, got, strict: true);
        Assert.False(got.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceTdp).Rows.Single().Available);
        Assert.True(got.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceCpuBoost).Rows.Single(r => r.RowId == QuickSettingsRowId.DeviceCpuBoostEnabled).Available);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Whole_page_unavailable_survives_the_wire()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var received = new TaskCompletionSource<QuickSettingsPageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask, null, null, page => { received.TrySetResult(page); return Task.CompletedTask; });
        var unavailable = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Device);

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendQuickSettingsPageStateAsync(unavailable));

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equivalent(unavailable, got, strict: true);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Hidden_or_unready_overlay_receives_no_page_frame()
    {
        var pipeName = Pipe();
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();

        // No client connected yet -> unready.
        Assert.False(await server.SendQuickSettingsPageStateAsync(SamplePage));

        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));

        // Ready but still Hidden (no Show yet).
        Assert.False(await server.SendQuickSettingsPageStateAsync(SamplePage));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.False(await server.SendQuickSettingsPageStateAsync(SamplePage));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Generic mutation round trip ------------------------------------------------------------

    private static QuickSettingsMutationIntent ToggleIntent() => new(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
        [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

    private static QuickSettingsMutationIntent IndependentSliderIntent() => new(QuickSettingsPageId.Device, null, QuickSettingsRowId.DevicePowerModeAc,
        [new(QuickSettingsRowId.DevicePowerModeAc, QuickSettingsValue.Integer((int)WindowsPowerMode.BestPerformance))]);

    private static QuickSettingsMutationIntent GroupedTdpIntent() => new(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceTdpAcPl1,
    [
        new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true)),
        new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(20)),
        new(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(25)),
        new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(15)),
        new(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(20)),
    ]);

    public static TheoryData<QuickSettingsMutationIntent> RepresentativeIntents => new()
    {
        ToggleIntent(), IndependentSliderIntent(), GroupedTdpIntent(),
    };

    [Theory]
    [MemberData(nameof(RepresentativeIntents))]
    public async Task Generic_mutation_round_trips_the_exact_intent_and_result(QuickSettingsMutationIntent intent)
    {
        var pipeName = Pipe();
        var control = new FakeQuickSettingsOverlayControl();
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendQuickSettingsMutationAsync(intent);

        Assert.Equivalent(new[] { intent }, control.Calls, strict: true);
        Assert.Equivalent(control.MutationResult, result, strict: true);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Central adapter ownership regression ----------------------------------------------------

    [Fact]
    public void The_v6_device_specific_dispatch_authority_no_longer_exists()
    {
        var types = typeof(NamedPipeOverlayServer).Assembly.GetTypes().Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain("OverlayDeviceMutationDispatch", types);
        Assert.DoesNotContain("OverlayDeviceMutationKind", types);
        Assert.DoesNotContain("OverlayDeviceMutationRequest", types);
        Assert.DoesNotContain("OverlayDeviceMutationResponse", types);
    }

    // ---- Strict malformed generic request rejection -----------------------------------------------

    public static TheoryData<string> MalformedMutationFrames()
    {
        var data = new TheoryData<string>();
        // RequestId 0 / negative -- structurally decodes, fails the transport's own RequestId check.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":0,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":-1,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        // Missing required Intent.PageId -- RespectRequiredConstructorParameters throws at decode.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        // Missing required Intent.EditedRowId.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"Device","AppId":null,"Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        // Missing nested RowId.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        // Missing nested Value.Kind.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"BooleanValue":true}}]}}"""));
        // Values = null.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":null}}"""));
        // Null nested row-value entry.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[null]}}"""));
        // Null nested Value.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"Device","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":null}]}}"""));
        // Unknown enum string.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":"NotAPage","AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        // Numeric enum token where a string enum name is required.
        data.Add(Envelope("QuickSettingsMutationRequest", """{"RequestId":1,"Intent":{"PageId":0,"AppId":null,"EditedRowId":"DeviceCpuBoostEnabled","Values":[{"RowId":"DeviceCpuBoostEnabled","Value":{"Kind":"Boolean","BooleanValue":true}}]}}"""));
        return data;
    }

    private static string Envelope(string kindField, string payloadField) =>
        $$"""{"ProtocolVersion":{{OverlayTransportProtocol.CurrentVersion}},"Kind":"{{kindField}}","{{kindField}}":{{payloadField}}}""";

    [Theory]
    [MemberData(nameof(MalformedMutationFrames))]
    public async Task Malformed_generic_mutation_frame_invokes_zero_mutations_and_tears_the_connection(string json)
    {
        var pipeName = Pipe();
        var control = new FakeQuickSettingsOverlayControl();
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writeGate = new SemaphoreSlim(1, 1);
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.Handshake), writeGate, CancellationToken.None);
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // HandshakeAccepted
        await OverlayWireCodec.ReadAsync(client, CancellationToken.None); // TabOrderState
        await OverlayWireCodec.WriteAsync(client, new(OverlayTransportProtocol.CurrentVersion, OverlayWireMessageKind.State, State: OverlayState.Ready), writeGate, CancellationToken.None);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        // Structural validation happens unconditionally before any Ready/Visible admission check or
        // Runtime invocation -- no Show/acknowledgement is needed for this assertion.

        await WriteRawFrameAsync(client, json);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => OverlayWireCodec.ReadAsync(client, timeout.Token));
        Assert.Empty(control.Calls);
    }

    private static bool PagesEqual(QuickSettingsPageSnapshot a, QuickSettingsPageSnapshot b)
    {
        try { Assert.Equivalent(a, b, strict: true); return true; }
        catch (global::Xunit.Sdk.XunitException) { return false; }
    }

    private static async Task WriteRawFrameAsync(Stream stream, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length));
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    // ---- Hidden / not-captured / non-Device-page admission ----------------------------------------

    [Fact]
    public async Task Hidden_overlay_cannot_mutate_quick_settings()
    {
        var pipeName = Pipe();
        var control = new FakeQuickSettingsOverlayControl();
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        // Ready but still Hidden -- never Shown.

        var result = await client.SendQuickSettingsMutationAsync(ToggleIntent());

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
        Assert.False(result.Page.Available);

        // Connection remains usable afterward.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Not_bound_mutate_delegate_is_not_admitted_and_invokes_nothing()
    {
        var pipeName = Pipe();
        // No mutateQuickSettings bound at all -- the frozen "tests / no-authority contexts" default.
        await using var server = new NamedPipeOverlayServer(pipeName);
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendQuickSettingsMutationAsync(ToggleIntent());

        Assert.False(result.Succeeded);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Typed failures are not transport failures -------------------------------------------------

    [Fact]
    public async Task Typed_feature_failure_returns_over_the_connection_without_closing_it()
    {
        var pipeName = Pipe();
        var control = new FakeQuickSettingsOverlayControl
        {
            MutationResult = new QuickSettingsMutationResult(false, "TDP apply failed.", SamplePage),
        };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var result = await client.SendQuickSettingsMutationAsync(GroupedTdpIntent());

        Assert.False(result.Succeeded);
        Assert.Equal("TDP apply failed.", result.FailureMessage);
        Assert.Equivalent(SamplePage, result.Page, strict: true);

        // The connection is still usable for an ordinary operation afterward.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Thrown mutation failure remains feature-local -----------------------------------------------

    [Fact]
    public async Task Thrown_mutation_exception_returns_a_narrow_error_and_the_connection_survives()
    {
        var pipeName = Pipe();
        var control = new FakeQuickSettingsOverlayControl { ThrowOnMutate = true };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        await Assert.ThrowsAsync<FrontendProtocolException>(() => client.SendQuickSettingsMutationAsync(ToggleIntent()));

        // Overlay command/state transport remains usable after the feature-local failure.
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Critical regression: long TDP mutation must not block Hide ------------------------------

    [Fact]
    public async Task Long_tdp_mutation_does_not_block_hide_or_subsequent_state_processing()
    {
        var pipeName = Pipe();
        var mutationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new FakeQuickSettingsOverlayControl();
        control.MutationFactory = async () =>
        {
            mutationEntered.TrySetResult();
            await releaseMutation.Task;
            return control.MutationResult;
        };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var mutationResultReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        var mutationTask = client.SendQuickSettingsMutationAsync(GroupedTdpIntent())
            .ContinueWith(_ => mutationResultReceived.TrySetResult(), TaskScheduler.Default);
        await mutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Hide must complete BEFORE the blocked TDP mutation is released -- proving the sole
        // ServeAsync read loop was never blocked by the in-flight mutation.
        var hideTask = server.SendCommandAsync(OverlayCommand.Hide);
        Assert.True(await hideTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(OverlayState.Hidden, server.State);
        Assert.False(mutationResultReceived.Task.IsCompleted);

        releaseMutation.TrySetResult();
        await mutationTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(mutationResultReceived.Task.IsCompletedSuccessfully);

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Request correlation -------------------------------------------------------------------------

    [Fact]
    public async Task A_late_result_for_a_retired_request_never_completes_a_newer_request()
    {
        var pipeName = Pipe();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = new FakeQuickSettingsOverlayControl();
        var callCount = 0;
        control.MutationFactory = async () =>
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1) { firstEntered.TrySetResult(); await releaseFirst.Task; }
            return control.MutationResult with { FailureMessage = $"call-{call}" };
        };
        await using var server = new NamedPipeOverlayServer(pipeName, mutateQuickSettings: (i, t) => control.MutateQuickSettingAsync(i, t));
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);
        var run = client.RunAsync(_ => Task.CompletedTask);
        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        // Request A is sent and its wait is abandoned (cancelled) before the Runtime settles it.
        using var cancelA = new CancellationTokenSource();
        var requestA = client.SendQuickSettingsMutationAsync(ToggleIntent(), cancelA.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancelA.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestA);

        // Request B becomes current and completes normally.
        var requestB = await client.SendQuickSettingsMutationAsync(ToggleIntent());
        Assert.Equal("call-2", requestB.FailureMessage);

        // A's late result now arrives; it must not have completed B (already proven above) and must
        // not corrupt the connection.
        releaseFirst.TrySetResult();
        await Task.Delay(200);
        Assert.True(await server.SendCommandAsync(OverlayCommand.Hide));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // ---- Shared write-gate integrity -----------------------------------------------------------------

    [Fact]
    public async Task Page_state_and_navigation_and_tab_order_share_one_write_gate_and_never_interleave()
    {
        var pipeName = Pipe();
        IReadOnlyList<OverlayTabId> current = OverlayTabOrderContract.DefaultOrder;
        await using var server = new NamedPipeOverlayServer(pipeName, () => current, requested => { current = requested; return true; });
        await server.StartAsync();
        await using var client = new NamedPipeOverlayClient(pipeName);

        var pageFrames = new List<QuickSettingsPageSnapshot>();
        var navActions = new List<OverlayNavigationAction>();
        var orders = new List<IReadOnlyList<OverlayTabId>>();
        var run = client.RunAsync(
            _ => Task.CompletedTask,
            action => { lock (navActions) navActions.Add(action); return Task.CompletedTask; },
            order => { lock (orders) orders.Add(order); return Task.CompletedTask; },
            page => { lock (pageFrames) pageFrames.Add(page); return Task.CompletedTask; });

        Assert.True(await server.WaitForReadyAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await server.SendCommandAsync(OverlayCommand.Show));

        for (var i = 0; i < 30; i++)
        {
            var pageTask = server.SendQuickSettingsPageStateAsync(i % 2 == 0 ? SamplePage : PartialPage);
            var navTask = server.SendNavigationAsync(OverlayNavigationAction.NavigateDown);
            var orderTask = client.SendSetTabOrderAsync(current);
            Assert.True(await pageTask);
            await navTask;
            Assert.True(await orderTask);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (pageFrames) lock (navActions)
                if (pageFrames.Count >= 30 && navActions.Count >= 30) break;
            Assert.False(run.IsFaulted, run.Exception?.ToString());
            await Task.Delay(20);
        }

        Assert.False(run.IsFaulted, run.Exception?.ToString());
        lock (pageFrames) Assert.All(pageFrames, p => Assert.True(PagesEqual(p, SamplePage) || PagesEqual(p, PartialPage)));
        lock (navActions) Assert.All(navActions, a => Assert.Equal(OverlayNavigationAction.NavigateDown, a));

        Assert.True(await server.SendCommandAsync(OverlayCommand.Shutdown));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeQuickSettingsOverlayControl : IAddonFrontendControl
    {
        public event EventHandler? StateInvalidated { add { } remove { } }
        public List<QuickSettingsMutationIntent> Calls { get; } = new();

        // Not exercised by these Overlay Quick Settings transport tests -- IAddonFrontendControl
        // declares these without a default body, unlike the two generic methods below.
        public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(SteamInputAddonforClaw.Contracts.FrontButtons.FrontButtonMappingSettings mapping, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => throw new NotSupportedException();

        public QuickSettingsPageSnapshot Page { get; set; } = SamplePage;
        public QuickSettingsMutationResult MutationResult { get; set; } = new(true, null, SamplePage);
        public Func<Task<QuickSettingsMutationResult>>? MutationFactory { get; set; }
        public bool ThrowOnMutate { get; set; }
        public int CaptureCount { get; private set; }

        public Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(QuickSettingsPageId pageId, uint? appId = null, CancellationToken t = default)
        { CaptureCount++; return Task.FromResult(Page); }

        public Task<QuickSettingsMutationResult> MutateQuickSettingAsync(QuickSettingsMutationIntent intent, CancellationToken t = default)
        {
            Calls.Add(intent);
            if (ThrowOnMutate) throw new InvalidOperationException("Quick Settings boom.");
            return MutationFactory?.Invoke() ?? Task.FromResult(MutationResult);
        }
    }
}
