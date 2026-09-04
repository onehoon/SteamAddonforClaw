using System.Reflection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR6: the first virtual-presentation owner. Attaches exactly one X360/SteamDeck
/// device only after canonical VIIPER is Ready and the PR5 live input source is running; the attach
/// decision uses one fresh raw Steam/BPM snapshot; no fallback to the other presentation.</summary>
[Collection("AppLog")]
public sealed class MsiClawAddonPresentationTests
{
    private static SteamPresentationSnapshot WantsXbox() => new(0, false);
    private static SteamPresentationSnapshot WantsDeck() => new(1234, false);

    private static MsiClawAddonPresentation Build(FakeNative native, FakePublisher xbox360, FakePublisher deck)
    {
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.NotNull(runtime);
        return new MsiClawAddonPresentation(
            runtime,
            deckSessionFactory: r => new CanonicalSteamDeckSession(r),
            xbox360PublisherFactory: (_, _, fault) => { xbox360.Fault = fault; return xbox360; },
            deckPublisherFactory: (_, _, _, fault) => { deck.Fault = fault; return deck; });
    }

    // ---- 25.2 / 25.4 Xbox360 first attach ----

    [Fact]
    public async Task Xbox360_first_attach_attaches_only_xbox360_neutral_then_publisher()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);

        var result = await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        Assert.True(result.Succeeded);
        Assert.Equal(AddonPresentationKind.Xbox360, result.Presentation);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        Assert.True(xbox360.Started);
        Assert.False(deck.Started);
        Assert.Contains("AttachUSBDeviceEx", native.Calls);
        Assert.Contains("SetXbox360DeviceState", native.Calls); // neutral written
        Assert.Equal(1, native.Calls.Count(c => c == "CreateXbox360Device"));
        Assert.Equal(1, native.Calls.Count(c => c == "CreateSteamDeckDevice")); // deck created once at init, never re-created
        Assert.Equal(1, native.Calls.Count(c => c == "AttachUSBDeviceEx")); // only the selected device
        await owner.DisposeAsync();
    }

    // ---- 25.3 SteamDeck first attach ----

    [Fact]
    public async Task SteamDeck_first_attach_attaches_only_steamdeck_neutral_then_publisher()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);

        var result = await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);

        Assert.True(result.Succeeded);
        Assert.Equal(AddonPresentationKind.SteamDeck, result.Presentation);
        Assert.True(deck.Started);
        Assert.False(xbox360.Started);
        Assert.Contains("SetSteamDeckDeviceState", native.Calls); // neutral
        await owner.DisposeAsync();
    }

    // ---- 25.5 live input required ----

    [Fact]
    public async Task No_attach_when_the_live_input_source_is_not_running()
    {
        var owner = Build(new FakeNative(), new FakePublisher(), new FakePublisher());
        var result = await owner.AttachInitialAsync(new FakeSource { Running = false }, WantsXbox(), default);

        Assert.False(result.Succeeded);
        Assert.Contains("LiveInputSourceNotRunning", result.Reason);
        Assert.Null(owner.ActivePresentation);
        await owner.DisposeAsync();
    }

    // ---- 25.6 VIIPER readiness ----

    [Fact]
    public async Task No_attach_when_viiper_is_not_ready()
    {
        var owner = new MsiClawAddonPresentation(viiper: null);
        var result = await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        Assert.False(result.Succeeded);
        Assert.Contains("ViiperNotReady", result.Reason);
        await owner.DisposeAsync();
    }

    // ---- 25.8 selected attach failure: no fallback ----

    [Fact]
    public async Task Xbox360_attach_failure_never_falls_back_to_steamdeck()
    {
        var native = new FakeNative();
        native.AttachResults.Enqueue(USBDeviceAttachResult.RetryableFailure);
        var xbox360 = new FakePublisher();
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);

        var result = await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        Assert.False(result.Succeeded);
        Assert.Equal(AddonPresentationKind.Xbox360, result.Presentation);
        Assert.False(deck.Started);
        Assert.Null(owner.ActivePresentation);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Xbox360_publisher_start_throw_attempts_detach_and_no_fallback()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher { StartThrows = true };
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);

        var result = await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        Assert.False(result.Succeeded);
        Assert.Contains("DetachUSBDeviceEx", native.Calls);
        Assert.False(deck.Started);
        await owner.DisposeAsync();
    }

    // ---- 25.9 publisher runtime fault ----

    [Fact]
    public async Task Publisher_fault_asynchronously_fails_closed_the_presentation()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        xbox360.Fault!(new InvalidOperationException("write failed"));
        await owner.DisposeAsync(); // awaits the scheduled fault cleanup

        Assert.True(xbox360.StopCalled);
        Assert.Contains("DetachUSBDeviceEx", native.Calls);
        Assert.False(deck.Started); // never attaches the other presentation
        Assert.Null(owner.ActivePresentation);
    }

    // ---- 25.10 release for Center M enable ----

    [Fact]
    public async Task Release_stops_publisher_detaches_then_tears_viiper_down()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        var released = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(released);
        Assert.True(xbox360.StopCalled);
        Assert.True(native.Calls.IndexOf("DetachUSBDeviceEx") < native.Calls.IndexOf("CloseUSBServer"));
        Assert.Contains("CloseUSBServer", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Release_fails_when_the_publisher_cannot_be_joined_and_never_detaches()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher { StopThrows = true };
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();

        var released = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.False(released);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("CloseUSBServer", native.Calls);
    }

    // ---- OQ4 section 5 / 17.1: Overlay-capture pause / resume ----

    [Fact]
    public async Task Xbox360_overlay_pause_stops_publisher_writes_neutral_without_detach_then_resumes_same_publisher()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();

        var pause = await owner.PauseForOverlayAsync(default);

        Assert.Equal(OverlayPauseOutcome.Paused, pause.Outcome);
        Assert.True(owner.IsOverlayPaused);
        Assert.True(xbox360.StopCalled);
        Assert.False(xbox360.IsRunning);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        Assert.Contains("SetXbox360DeviceState", native.Calls); // neutral written
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);

        var resume = await owner.ResumeAfterOverlayAsync(new FakeSource(), default);

        Assert.Equal(OverlayResumeOutcome.Resumed, resume.Outcome);
        Assert.False(owner.IsOverlayPaused);
        Assert.True(xbox360.IsRunning);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task SteamDeck_overlay_pause_stops_publisher_writes_neutral_without_detach_then_resumes_same_publisher()
    {
        var native = new FakeNative();
        var deck = new FakePublisher();
        var owner = Build(native, new FakePublisher(), deck);
        await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);
        native.Calls.Clear();

        var pause = await owner.PauseForOverlayAsync(default);

        Assert.Equal(OverlayPauseOutcome.Paused, pause.Outcome);
        Assert.True(deck.StopCalled);
        Assert.False(deck.IsRunning);
        Assert.Equal(AddonPresentationKind.SteamDeck, owner.ActivePresentation);
        Assert.Contains("SetSteamDeckDeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);

        var resume = await owner.ResumeAfterOverlayAsync(new FakeSource(), default);

        Assert.Equal(OverlayResumeOutcome.Resumed, resume.Outcome);
        Assert.True(deck.IsRunning);
        Assert.Equal(AddonPresentationKind.SteamDeck, owner.ActivePresentation);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_while_overlay_paused_is_blocked_without_attach_or_detach()
    {
        var native = new FakeNative();
        var owner = Build(native, new FakePublisher(), new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);
        native.Calls.Clear();

        var reconcile = await owner.ReconcileDesiredPresentationAsync(new FakeSource(), WantsDeck, default);

        Assert.Equal(PresentationReconcileOutcome.Blocked, reconcile.Outcome);
        Assert.Equal("OverlayCaptureActive", reconcile.Reason);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_pause_fails_when_publisher_cannot_be_joined_and_never_neutralizes_or_detaches()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher { StopThrows = true };
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();

        var pause = await owner.PauseForOverlayAsync(default);

        Assert.Equal(OverlayPauseOutcome.PublisherNotStopped, pause.Outcome);
        Assert.False(owner.IsOverlayPaused);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        Assert.DoesNotContain("SetXbox360DeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_pause_with_rejected_neutral_write_fails_closed_through_the_owner_without_fallback()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();
        native.StateResults.Enqueue(false); // the pause neutral write is rejected

        var pause = await owner.PauseForOverlayAsync(default);

        Assert.Equal(OverlayPauseOutcome.NeutralRejectedPresentationRetired, pause.Outcome);
        Assert.False(owner.IsOverlayPaused);
        Assert.Null(owner.ActivePresentation);
        Assert.Contains("DetachUSBDeviceEx", native.Calls); // current presentation retired
        Assert.False(deck.Started); // no alternate presentation fallback
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_pause_neutral_rejected_and_failed_retirement_does_not_claim_retirement()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();
        native.StateResults.Enqueue(false); // pause neutral write rejected
        native.DetachResults.Enqueue(USBDeviceDetachResult.RetryableFailure); // and the retire detach fails

        var pause = await owner.PauseForOverlayAsync(default);

        Assert.Equal(OverlayPauseOutcome.NeutralRejectedRetireFailed, pause.Outcome);
        Assert.False(pause.Succeeded);
        Assert.False(owner.IsOverlayPaused);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation); // ownership evidence retained
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Xbox360_overlay_resume_with_a_detached_device_leaves_neutral_and_does_not_restart_the_publisher()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);
        native.AttachmentStates.Enqueue(USBDeviceAttachmentState.Detached); // device dropped while Overlay was open

        var resume = await owner.ResumeAfterOverlayAsync(new FakeSource(), default);

        Assert.Equal(OverlayResumeOutcome.LeftNeutral, resume.Outcome);
        Assert.Contains("AttachmentNotAttached", resume.Reason);
        Assert.False(owner.IsOverlayPaused);
        Assert.False(xbox360.IsRunning);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task SteamDeck_overlay_resume_with_a_detached_device_leaves_neutral_and_does_not_restart_the_publisher()
    {
        var native = new FakeNative();
        var deck = new FakePublisher();
        var owner = Build(native, new FakePublisher(), deck);
        await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);
        native.AttachmentStates.Enqueue(USBDeviceAttachmentState.Detached);

        var resume = await owner.ResumeAfterOverlayAsync(new FakeSource(), default);

        Assert.Equal(OverlayResumeOutcome.LeftNeutral, resume.Outcome);
        Assert.Contains("AttachmentNotAttached", resume.Reason);
        Assert.False(owner.IsOverlayPaused);
        Assert.False(deck.IsRunning);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_resume_with_a_null_source_still_clears_the_pause_and_unblocks_a_later_reconcile()
    {
        // OQ4 PR3 review [1]: real PID1902 loss routes through the unified retirement path with no
        // live source. The pause fact must still be cleared so recovery / PR7 reconcile is not
        // permanently Blocked:OverlayCaptureActive.
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);
        Assert.True(owner.IsOverlayPaused);

        var resume = await owner.ResumeAfterOverlayAsync(null, default);

        Assert.Equal(OverlayResumeOutcome.LeftNeutral, resume.Outcome);
        Assert.False(owner.IsOverlayPaused);
        Assert.False(xbox360.IsRunning);

        var reconcile = await owner.ReconcileDesiredPresentationAsync(new FakeSource(), WantsXbox, default);
        Assert.NotEqual(PresentationReconcileOutcome.Blocked, reconcile.Outcome);
        Assert.NotEqual("OverlayCaptureActive", reconcile.Reason);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_resume_with_unavailable_source_leaves_output_neutral_and_allows_a_later_reconcile()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);

        var resume = await owner.ResumeAfterOverlayAsync(new FakeSource { Running = false }, default);

        Assert.Equal(OverlayResumeOutcome.LeftNeutral, resume.Outcome);
        Assert.False(owner.IsOverlayPaused);
        Assert.False(xbox360.IsRunning);

        // A later normal reconcile is no longer blocked by the (now cleared) Overlay pause.
        var reconcile = await owner.ReconcileDesiredPresentationAsync(new FakeSource(), WantsXbox, default);
        Assert.NotEqual(PresentationReconcileOutcome.Blocked, reconcile.Outcome);
        Assert.NotEqual("OverlayCaptureActive", reconcile.Reason);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Center_M_release_while_overlay_paused_still_stops_detaches_and_tears_viiper_down()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var owner = Build(native, xbox360, new FakePublisher());
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);
        native.Calls.Clear();

        var released = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(released);
        Assert.Null(owner.ActivePresentation);
        Assert.False(owner.IsOverlayPaused);
        Assert.True(native.Calls.IndexOf("DetachUSBDeviceEx") < native.Calls.IndexOf("CloseUSBServer"));
        Assert.Contains("CloseUSBServer", native.Calls);
        await owner.DisposeAsync();
    }

    // ---- 25.12 / 25.13 architecture guards ----

    [Fact]
    public void Presentation_owner_has_no_old_routing_or_switching_dependency()
    {
        var ctorParams = typeof(MsiClawAddonPresentation).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single().GetParameters().Select(p => p.ParameterType.FullName ?? "");
        Assert.All(ctorParams, name =>
        {
            Assert.DoesNotContain("AddonRoutingRuntime", name);
            Assert.DoesNotContain("RoutingPipeline", name);
            Assert.DoesNotContain("MsiClawNativeModeSessionCoordinator", name);
            Assert.DoesNotContain("MsiClawPhysicalIsolationStage", name);
            Assert.DoesNotContain("RecoveryManager", name);
        });

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs"));
        foreach (var forbidden in new[]
        {
            "CanonicalSteamDeckOutputStage", "RoutingPipelineRuntimeCoordinator", "AddonRoutingRuntime",
            "ActualRunningAppIdChanged", "BigPictureStateChanged", "HandleGameBarForegroundChanged", "SwitchModeAsync",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    // ---- 25.10 / 25.11 / 16 composition guards (AddonProcessHost) ----

    [Fact]
    public void Host_composes_virtual_release_before_physical_and_disposes_presentation_first()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // Enable-and-Restart release: presentation retire first, its failure short-circuits everything.
        Assert.Contains("_presentationOwnership is { } presentation && !await presentation.ReleaseForCenterMEnableAsync", host);
        Assert.Contains("\"VirtualPresentationReleaseFailed\"", host);

        // Teardown order: presentation before physical DirectInput.
        Assert.True(
            host.IndexOf("_presentationOwnership.DisposeAsync", StringComparison.Ordinal)
            < host.IndexOf("_physicalOwnership.DisposeAsync", StringComparison.Ordinal));

        // VIIPER Ready is required before PR5 AcquireAsync; the controller sequence runs before the
        // frontend transport is marked ready (section 16).
        Assert.True(
            host.IndexOf("presentation.ViiperState != VirtualOutput.Viiper.CanonicalViiperRuntimeState.Ready", StringComparison.Ordinal)
            < host.IndexOf("owner.AcquireAsync(_startupCancellationTokenSource.Token)", StringComparison.Ordinal));
        Assert.True(
            host.IndexOf("TryStartDisabledModeControllerAsync(startupComposition, startupResult)", StringComparison.Ordinal)
            < host.IndexOf("_frontendServer.StartAsync()", StringComparison.Ordinal));

        // No PR7 runtime switching subscription in the new owner.
        var presentationSource = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs"));
        Assert.DoesNotContain("ActualRunningAppIdChanged", presentationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BigPictureStateChanged", presentationSource, StringComparison.Ordinal);
    }

    // ================= PR7: runtime Xbox360 <-> SteamDeck presentation switching =================

    [Fact]
    public async Task Reconcile_xbox360_to_steamdeck_stops_joins_publisher_then_detaches_then_attaches_deck()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Native.Calls.Clear();
        h.Snapshot = WantsDeck();

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.Switched, result.Outcome);
        Assert.Equal(AddonPresentationKind.SteamDeck, h.Owner.ActivePresentation);
        Assert.True(h.Xbox360Publishers[0].StopCalled);
        // X360 publisher stop precedes X360 native detach; deck attach follows the detach.
        Assert.True(h.Native.Calls.IndexOf("DetachUSBDeviceEx") >= 0);
        Assert.True(h.Native.Calls.IndexOf("DetachUSBDeviceEx") < h.Native.Calls.IndexOf("AttachUSBDeviceEx"));
        Assert.Single(h.DeckPublishers);
        Assert.True(h.DeckPublishers[0].Started);
        // no VIIPER server/bus/device recreation
        Assert.DoesNotContain("NewUSBServer", h.Native.Calls);
        Assert.DoesNotContain("CreateUSBBus", h.Native.Calls);
        Assert.DoesNotContain("CreateSteamDeckDevice", h.Native.Calls);
        Assert.DoesNotContain("CreateXbox360Device", h.Native.Calls);
        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_steamdeck_to_xbox360_inverse_order_and_final_state()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        h.Snapshot = WantsXbox();

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.Switched, result.Outcome);
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation);
        Assert.True(h.DeckPublishers[0].StopCalled);
        Assert.Single(h.Xbox360Publishers);
        Assert.True(h.Xbox360Publishers[0].Started);
        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_is_a_noop_when_the_active_presentation_already_matches_policy()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Native.Calls.Clear();
        h.Snapshot = WantsXbox();

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.NoChange, result.Outcome);
        Assert.Empty(h.Native.Calls); // no attach/detach/state
        Assert.Single(h.Xbox360Publishers); // publisher not restarted
        await h.Owner.DisposeAsync();
    }

    [Theory]
    [InlineData(1234u, true)]   // game + BPM -> Deck
    [InlineData(1234u, false)]  // game only -> Deck
    [InlineData(0u, true)]      // BPM only -> Deck
    public async Task Reconcile_or_semantics_keeps_deck_and_only_returns_to_xbox360_when_both_inactive(uint appId, bool bpm)
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        h.Native.Calls.Clear();

        h.Snapshot = new SteamPresentationSnapshot(appId, bpm);
        Assert.Equal(PresentationReconcileOutcome.NoChange, (await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default)).Outcome);
        Assert.Equal(AddonPresentationKind.SteamDeck, h.Owner.ActivePresentation);

        h.Snapshot = new SteamPresentationSnapshot(0, false);
        Assert.Equal(PresentationReconcileOutcome.Switched, (await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default)).Outcome);
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation);
        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_queued_behind_another_captures_the_fresh_desired_state_inside_the_gate()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default); // active = Xbox360

        // Reconcile A: wants Deck -> retires X360; its publisher StopAsync blocks while A holds the gate.
        h.Snapshot = WantsDeck();
        h.Xbox360Publishers[0].StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        // While A is stuck retiring X360, the actual Steam state flips back (game closed).
        await Task.Delay(30);
        h.Snapshot = WantsXbox();
        var b = h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default); // queues on the gate

        h.Xbox360Publishers[0].StopGate!.SetResult();
        Assert.Equal(PresentationReconcileOutcome.Switched, (await a).Outcome);   // A committed Deck
        var rb = await b;

        // B entered the gate AFTER A, captured the current (Xbox) fact, and converged back.
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation);
        Assert.Contains(rb.Outcome, new[] { PresentationReconcileOutcome.Switched, PresentationReconcileOutcome.NoChange });
        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_current_publisher_join_failure_never_detaches_or_attaches()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Xbox360Publishers[0].StopThrows = true;
        h.Native.Calls.Clear();
        h.Snapshot = WantsDeck();

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("DetachUSBDeviceEx", h.Native.Calls);
        Assert.DoesNotContain("AttachUSBDeviceEx", h.Native.Calls);
        Assert.Empty(h.DeckPublishers);
    }

    [Fact]
    public async Task Reconcile_target_attach_failure_leaves_both_detached_with_no_fallback()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Native.Calls.Clear();
        h.Snapshot = WantsDeck();
        h.Native.AttachResults.Enqueue(USBDeviceAttachResult.RetryableFailure); // deck attach fails

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.Failed, result.Outcome);
        Assert.Null(h.Owner.ActivePresentation);                 // both detached
        Assert.True(h.Xbox360Publishers[0].StopCalled);          // old presentation was safely retired
        Assert.False(h.DeckPublishers.Count > 0 && h.DeckPublishers[^1].Started);

        // A later genuine event can attach the then-desired presentation (no timer / retry loop).
        h.Snapshot = WantsXbox();
        var recover = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);
        Assert.Equal(PresentationReconcileOutcome.Attached, recover.Outcome);
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation);
        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_blocks_when_live_input_source_stopped()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Native.Calls.Clear();
        h.Source.Running = false;
        h.Snapshot = WantsDeck();

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.Blocked, result.Outcome);
        Assert.Empty(h.Native.Calls);
        Assert.Empty(h.DeckPublishers);
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation); // current not retired
        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Reconcile_blocks_after_viiper_teardown()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        await h.Owner.ReleaseForCenterMEnableAsync(default); // VIIPER now Closed
        h.Native.Calls.Clear();
        h.Snapshot = WantsDeck();

        var result = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal(PresentationReconcileOutcome.Blocked, result.Outcome);
        Assert.Empty(h.Native.Calls);
    }

    [Fact]
    public void Reconcile_source_takes_no_physical_dependency_and_the_host_wires_events()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var owner = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPresentation.cs"));
        var host = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // PR7 section 27.20: the switch code must not touch physical mode / persistent HidHide primitives.
        foreach (var forbidden in new[] { "SwitchModeAsync", "ApplyDisabledModeBaseline", "AddHiddenDevice", "MsiClawNativeStateManager", "HidHideDriverClient" })
            Assert.DoesNotContain(forbidden, owner, StringComparison.Ordinal);

        // Event-driven only: raw RunningAppID + BPM callbacks request the reconcile; no timer/poll.
        Assert.Contains("RequestControllerPresentationReconcile(\"RunningAppIdChanged\")", host, StringComparison.Ordinal);
        Assert.Contains("bigPictureStateChanged: OnBigPictureStateChanged", host, StringComparison.Ordinal);
        Assert.Contains("_qamHostController.OnBigPictureStateChanged(active)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new Timer(", host, StringComparison.Ordinal);

        // The reconcile is drained before the presentation owner is torn down (section 19.1).
        Assert.True(
            host.IndexOf("await _presentationReconcile.ConfigureAwait(false)", StringComparison.Ordinal)
            < host.IndexOf("_presentationOwnership.DisposeAsync", StringComparison.Ordinal));
    }

    // ================= Full1902 A2 section 15.4: synthetic Steam/QuickAccess system-button pulses =================

    [Fact]
    public async Task SteamDeck_live_steam_pulse_is_requested_and_reaches_the_shared_overlay()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        Assert.Equal(AddonPresentationKind.SteamDeck, h.Owner.ActivePresentation);
        var overlay = Assert.Single(h.DeckOverlays);

        Assert.True(((IMsiClawAddonPresentation)h.Owner).TryRequestSteamPulse());
        Assert.Equal((byte)1, overlay.Apply(new SteamDeckDeviceState()).Steam);

        Assert.True(((IMsiClawAddonPresentation)h.Owner).TryRequestQuickAccessPulse());
        Assert.Equal((byte)1, overlay.Apply(new SteamDeckDeviceState()).QuickAccess);

        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Xbox360_presentation_reports_both_pulse_methods_unavailable()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);

        Assert.False(((IMsiClawAddonPresentation)h.Owner).TryRequestSteamPulse());
        Assert.False(((IMsiClawAddonPresentation)h.Owner).TryRequestQuickAccessPulse());

        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task Absent_presentation_reports_pulses_unavailable()
    {
        var owner = new MsiClawAddonPresentation(viiper: null);
        Assert.False(((IMsiClawAddonPresentation)owner).TryRequestSteamPulse());
        Assert.False(((IMsiClawAddonPresentation)owner).TryRequestQuickAccessPulse());
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_capture_pause_reports_steam_pulse_unavailable()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await h.Owner.PauseForOverlayAsync(default)).Outcome);

        Assert.False(((IMsiClawAddonPresentation)h.Owner).TryRequestSteamPulse());

        await h.Owner.DisposeAsync();
    }

    [Fact]
    public async Task SteamDeck_retirement_clears_a_pending_pulse_so_it_cannot_survive_into_a_new_publisher()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        var firstOverlay = Assert.Single(h.DeckOverlays);
        Assert.True(((IMsiClawAddonPresentation)h.Owner).TryRequestSteamPulse());

        // Switch away and back: the overlay instance is shared, but retirement clears it.
        h.Snapshot = WantsXbox();
        await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);

        Assert.Equal((byte)0, firstOverlay.Apply(new SteamDeckDeviceState()).Steam);

        h.Snapshot = WantsDeck();
        await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);
        // A fresh Deck publication starts with no leftover synthetic button asserted.
        Assert.Equal((byte)0, h.DeckOverlays[^1].Apply(new SteamDeckDeviceState()).Steam);

        await h.Owner.DisposeAsync();
    }

    private sealed class SwitchHarness
    {
        public FakeNative Native { get; } = new();
        public FakeSource Source { get; } = new();
        public SteamPresentationSnapshot Snapshot { get; set; } = new(0, false);
        public List<FakePublisher> Xbox360Publishers { get; } = [];
        public List<FakePublisher> DeckPublishers { get; } = [];
        public List<SteamInputAddonforClaw.VirtualOutput.Viiper.SteamDeckSystemButtonOverlay> DeckOverlays { get; } = [];
        public MsiClawAddonPresentation Owner { get; }

        public Func<SteamPresentationSnapshot> Capture => () => Snapshot;

        public SwitchHarness()
        {
            var runtime = CanonicalViiperRuntime.TryInitialize(Native, "127.0.0.1:3242");
            Assert.NotNull(runtime);
            Owner = new MsiClawAddonPresentation(
                runtime,
                deckSessionFactory: r => new CanonicalSteamDeckSession(r),
                xbox360PublisherFactory: (_, _, fault) => { var p = new FakePublisher { Fault = fault }; Xbox360Publishers.Add(p); return p; },
                deckPublisherFactory: (_, _, overlay, fault) => { var p = new FakePublisher { Fault = fault }; DeckOverlays.Add(overlay); DeckPublishers.Add(p); return p; });
        }
    }

    // ---- Full1902 production rumble feedback lifetime (work order sections 8-13 / 19.4 / 19.6) ----

    private static MsiClawAddonPresentation BuildWithSink(FakeNative native, FakePublisher xbox360, FakePublisher deck, FakeRumbleSink sink)
    {
        var runtime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        Assert.NotNull(runtime);
        return new MsiClawAddonPresentation(
            runtime,
            rumbleSink: sink,
            deckSessionFactory: r => new CanonicalSteamDeckSession(r),
            xbox360PublisherFactory: (_, _, fault) => { xbox360.Fault = fault; return xbox360; },
            deckPublisherFactory: (_, _, _, fault) => { deck.Fault = fault; return deck; });
    }

    [Fact]
    public async Task Xbox360_attach_arms_only_the_xbox360_rumble_callback()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);

        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        Assert.Contains("SetXbox360RumbleCallback", native.Calls);
        Assert.DoesNotContain("SetSteamDeckOutputCallback", native.Calls);
        Assert.NotNull(native.ArmedXbox360RumbleCallback);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task SteamDeck_attach_arms_only_the_deck_output_callback()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);

        await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);

        Assert.Contains("SetSteamDeckOutputCallback", native.Calls);
        Assert.DoesNotContain("SetXbox360RumbleCallback", native.Calls);
        Assert.NotNull(native.ArmedSteamDeckOutputCallback);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Driving_the_xbox360_rumble_callback_writes_the_expanded_two_motor_rumble_to_the_shared_sink()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        native.ArmedXbox360RumbleCallback!(0, 255, 0);

        Assert.Contains(new SteamInputAddonforClaw.Feedback.TwoMotorRumble(65535, 0), sink.Writes);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Driving_the_deck_output_callback_decodes_recognized_packets_and_ignores_non_rumble()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);

        var rumble = new byte[] { 0xEB, 9, 1, 0x10, 0x00, 0xC8, 0x00, 0x40, 0x00, 0, 0 };
        DriveDeckCallback(native, rumble);
        Assert.Contains(new SteamInputAddonforClaw.Feedback.TwoMotorRumble(0x00C8, 0x0040), sink.Writes);

        var writesBefore = sink.Writes.Count;
        DriveDeckCallback(native, new byte[] { 0xB6, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }); // unsupported opcode
        Assert.Equal(writesBefore, sink.Writes.Count);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Callback_exceptions_and_write_failures_are_contained_and_the_presentation_stays_active()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink { Throw = true };
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        var exception = Record.Exception(() => native.ArmedXbox360RumbleCallback!(0, 10, 20));

        Assert.Null(exception);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Switch_xbox360_to_deck_clears_the_old_callback_requests_stop_then_arms_deck()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);

        Assert.Equal(PresentationReconcileOutcome.Attached, (await owner.ReconcileDesiredPresentationAsync(new FakeSource(), () => WantsXbox(), default)).Outcome);
        native.Calls.Clear();
        sink.Writes.Clear();

        Assert.Equal(PresentationReconcileOutcome.Switched, (await owner.ReconcileDesiredPresentationAsync(new FakeSource(), () => WantsDeck(), default)).Outcome);

        Assert.Contains("ClearXbox360RumbleCallback", native.Calls);
        Assert.True(native.Calls.IndexOf("ClearXbox360RumbleCallback") < native.Calls.IndexOf("DetachUSBDeviceEx"));
        Assert.Contains(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped, sink.Writes);
        Assert.Contains("SetSteamDeckOutputCallback", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Retirement_clears_the_callback_and_requests_a_physical_stop()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();
        sink.Writes.Clear();

        Assert.True(await owner.ReleaseForCenterMEnableAsync(default));

        Assert.Contains("ClearXbox360RumbleCallback", native.Calls);
        Assert.Contains(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped, sink.Writes);
    }

    [Fact]
    public async Task Xbox360_rumble_callback_registration_failure_keeps_the_controller_presentation_healthy()
    {
        var native = new FakeNative();
        native.RumbleCallbackResults.Enqueue(false); // arm fails
        var xbox360 = new FakePublisher();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, xbox360, new FakePublisher(), sink);

        var result = await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        Assert.True(result.Succeeded);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation);
        Assert.True(xbox360.Started);
        Assert.Null(native.ArmedXbox360RumbleCallback);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_pause_disarms_and_stops_and_resume_rearms_the_same_presentation()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();
        sink.Writes.Clear();

        Assert.Equal(OverlayPauseOutcome.Paused, (await owner.PauseForOverlayAsync(default)).Outcome);
        Assert.Contains("ClearXbox360RumbleCallback", native.Calls);
        Assert.Contains(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped, sink.Writes);

        native.Calls.Clear();
        Assert.Equal(OverlayResumeOutcome.Resumed, (await owner.ResumeAfterOverlayAsync(new FakeSource(), default)).Outcome);
        Assert.Contains("SetXbox360RumbleCallback", native.Calls);
        await owner.DisposeAsync();
    }

    private static void DriveDeckCallback(FakeNative native, byte[] report)
    {
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(report, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { native.ArmedSteamDeckOutputCallback!(0, handle.AddrOfPinnedObject(), (uint)report.Length); }
        finally { handle.Free(); }
    }

    private sealed class FakeRumbleSink : SteamInputAddonforClaw.Feedback.IPhysicalRumbleSink
    {
        private readonly object _sync = new();
        internal List<SteamInputAddonforClaw.Feedback.TwoMotorRumble> Writes { get; } = [];
        internal bool Throw { get; set; }
        // When set, the FIRST non-zero write blocks on this gate until the test releases it, modeling
        // WindowsMsiClawRumbleTransport's up-to-250 ms pending physical write.
        internal ManualResetEventSlim? BlockFirstNonZeroWrite { get; set; }
        internal ManualResetEventSlim FirstNonZeroWriteEntered { get; } = new(false);
        private bool _blocked;

        public SteamInputAddonforClaw.Feedback.PhysicalRumbleWriteResult SetRumble(SteamInputAddonforClaw.Feedback.TwoMotorRumble rumble)
        {
            if (Throw) throw new InvalidOperationException("sink failure");
            if (BlockFirstNonZeroWrite is { } gate && !_blocked && !rumble.Equals(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped))
            {
                _blocked = true;
                FirstNonZeroWriteEntered.Set();
                gate.Wait();
            }
            lock (_sync) Writes.Add(rumble);
            return new(SteamInputAddonforClaw.Feedback.PhysicalRumbleWriteStatus.Succeeded, "OK");
        }
    }

    [Fact] // PR #488 review finding 2: a callback already inside a pending physical write is drained
           // by the bridge Dispose, so the lifecycle STOP is always the final physical write.
    public async Task A_callback_pending_in_the_sink_cannot_land_a_non_zero_write_after_the_lifecycle_stop()
    {
        var native = new FakeNative();
        var gate = new ManualResetEventSlim(false);
        var sink = new FakeRumbleSink { BlockFirstNonZeroWrite = gate };
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        // A native rumble callback enters the sink and blocks mid-write.
        var callback = Task.Run(() => native.ArmedXbox360RumbleCallback!(0, 200, 100));
        Assert.True(sink.FirstNonZeroWriteEntered.Wait(TimeSpan.FromSeconds(2)));

        // Retire on another thread: DisarmFeedbackAndStopLocked -> bridge.Dispose() must block draining
        // the in-progress write before the STOP is issued.
        var retire = Task.Run(() => owner.ReleaseForCenterMEnableAsync(default));
        await Task.Delay(100);
        Assert.False(retire.IsCompleted); // still draining the blocked callback

        gate.Set();
        Assert.True(await retire);
        await callback;

        Assert.Equal(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped, sink.Writes[^1]);
        Assert.Single(sink.Writes, w => w.Equals(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped));
        await owner.DisposeAsync();
    }

    // ================= Full1902 Suspend/Resume: power-suspend neutral presentation =================

    [Fact] // section 16.2
    public async Task Xbox360_suspend_pause_stops_publisher_disarms_feedback_writes_neutral_and_keeps_the_device_attached()
    {
        var native = new FakeNative();
        var sink = new FakeRumbleSink();
        var xbox360 = new FakePublisher();
        var owner = BuildWithSink(native, xbox360, new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();
        sink.Writes.Clear();

        var pause = await owner.PauseForSuspendAsync(default);

        Assert.Equal(SuspendPauseOutcome.Paused, pause.Outcome);
        Assert.True(pause.Safe);
        Assert.True(((IMsiClawAddonPresentation)owner).IsSuspendPaused);
        Assert.True(xbox360.StopCalled);
        Assert.False(xbox360.IsRunning);
        Assert.Equal(AddonPresentationKind.Xbox360, owner.ActivePresentation); // device stays attached
        Assert.Contains("ClearXbox360RumbleCallback", native.Calls);
        Assert.Contains(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped, sink.Writes);
        Assert.Contains("SetXbox360DeviceState", native.Calls); // neutral written
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        Assert.DoesNotContain("AttachUSBDeviceEx", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact] // section 16.1
    public async Task SteamDeck_suspend_pause_stops_publisher_disarms_feedback_writes_neutral_and_keeps_the_device_attached()
    {
        var native = new FakeNative();
        var deck = new FakePublisher();
        var owner = Build(native, new FakePublisher(), deck);
        await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);
        native.Calls.Clear();

        var pause = await owner.PauseForSuspendAsync(default);

        Assert.Equal(SuspendPauseOutcome.Paused, pause.Outcome);
        Assert.True(deck.StopCalled);
        Assert.False(deck.IsRunning);
        Assert.Equal(AddonPresentationKind.SteamDeck, owner.ActivePresentation);
        Assert.Contains("SetSteamDeckDeviceState", native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", native.Calls);
        await owner.DisposeAsync();
    }

    [Fact] // section 16.3
    public async Task No_live_presentation_mutation_while_suspend_paused()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        Assert.Equal(SuspendPauseOutcome.Paused, (await h.Owner.PauseForSuspendAsync(default)).Outcome);
        h.Native.Calls.Clear();

        h.Snapshot = WantsXbox();
        var reconcile = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);
        Assert.Equal(PresentationReconcileOutcome.Blocked, reconcile.Outcome);
        Assert.Equal("SuspendPaused", reconcile.Reason);

        Assert.False(((IMsiClawAddonPresentation)h.Owner).TryRequestSteamPulse());
        Assert.False(((IMsiClawAddonPresentation)h.Owner).TryRequestQuickAccessPulse());

        Assert.DoesNotContain("AttachUSBDeviceEx", h.Native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", h.Native.Calls);
        Assert.Equal(AddonPresentationKind.SteamDeck, h.Owner.ActivePresentation);
        await h.Owner.DisposeAsync();
    }

    [Theory] // section 16.4
    [InlineData(false)] // Xbox360
    [InlineData(true)]  // SteamDeck
    public async Task Same_presentation_resume_restarts_the_same_publisher_with_zero_detach_or_attach(bool deck)
    {
        var h = new SwitchHarness();
        var wanted = deck ? WantsDeck() : WantsXbox();
        h.Snapshot = wanted;
        await h.Owner.AttachInitialAsync(h.Source, wanted, default);
        var publisher = deck ? h.DeckPublishers[0] : h.Xbox360Publishers[0];
        Assert.Equal(SuspendPauseOutcome.Paused, (await h.Owner.PauseForSuspendAsync(default)).Outcome);
        h.Native.Calls.Clear();

        var resume = await h.Owner.ResumeAfterSuspendAsync(h.Source, h.Capture, default);

        Assert.Equal(SuspendResumeOutcome.SamePublisherResumed, resume.Outcome);
        Assert.False(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused);
        Assert.True(publisher.IsRunning);
        Assert.Single(deck ? h.DeckPublishers : h.Xbox360Publishers); // SAME object, not recreated
        Assert.DoesNotContain("DetachUSBDeviceEx", h.Native.Calls);
        Assert.DoesNotContain("AttachUSBDeviceEx", h.Native.Calls);
        Assert.DoesNotContain("NewUSBServer", h.Native.Calls);
        await h.Owner.DisposeAsync();
    }

    [Fact] // section 16.5
    public async Task Desired_kind_changed_during_sleep_leaves_the_old_publisher_stopped_and_requires_a_pr7_reconcile()
    {
        var h = new SwitchHarness();
        h.Snapshot = WantsDeck();
        await h.Owner.AttachInitialAsync(h.Source, WantsDeck(), default);
        Assert.Equal(SuspendPauseOutcome.Paused, (await h.Owner.PauseForSuspendAsync(default)).Outcome);
        h.Native.Calls.Clear();

        h.Snapshot = WantsXbox(); // BPM exited while sleeping
        var resume = await h.Owner.ResumeAfterSuspendAsync(h.Source, h.Capture, default);

        Assert.Equal(SuspendResumeOutcome.ReconcileRequired, resume.Outcome);
        Assert.False(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused);
        Assert.False(h.DeckPublishers[0].IsRunning); // old publisher NOT briefly restarted
        Assert.Single(h.DeckPublishers);
        Assert.Empty(h.Xbox360Publishers);
        Assert.DoesNotContain("AttachUSBDeviceEx", h.Native.Calls);

        // The existing PR7 reconcile performs the actual Deck -> X360 switch.
        var switched = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);
        Assert.Equal(PresentationReconcileOutcome.Switched, switched.Outcome);
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation);
        await h.Owner.DisposeAsync();
    }

    [Fact] // section 16.6
    public async Task Suspend_after_overlay_pause_then_resume_leaves_overlay_owning_the_neutral_pause()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        Assert.Equal(OverlayPauseOutcome.Paused, (await h.Owner.PauseForOverlayAsync(default)).Outcome);
        Assert.Equal(SuspendPauseOutcome.Paused, (await h.Owner.PauseForSuspendAsync(default)).Outcome);

        var resume = await h.Owner.ResumeAfterSuspendAsync(h.Source, h.Capture, default);

        Assert.Equal(SuspendResumeOutcome.LeftNeutral, resume.Outcome);
        Assert.False(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused);
        Assert.True(h.Owner.IsOverlayPaused);
        Assert.False(h.Xbox360Publishers[0].IsRunning); // still neutral under the Overlay pause

        // Ending the Overlay pause then resumes the same publisher normally.
        Assert.Equal(OverlayResumeOutcome.Resumed, (await h.Owner.ResumeAfterOverlayAsync(h.Source, default)).Outcome);
        Assert.True(h.Xbox360Publishers[0].IsRunning);
        await h.Owner.DisposeAsync();
    }

    [Fact] // section 16.6
    public async Task Overlay_pause_request_is_blocked_while_suspend_paused_and_never_restarts_publication()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        Assert.Equal(SuspendPauseOutcome.Paused, (await h.Owner.PauseForSuspendAsync(default)).Outcome);

        var overlay = await h.Owner.PauseForOverlayAsync(default);

        Assert.Equal(OverlayPauseOutcome.Blocked, overlay.Outcome);
        Assert.Equal("SuspendPaused", overlay.Reason);
        Assert.True(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused);
        Assert.False(h.Owner.IsOverlayPaused);
        Assert.False(h.Xbox360Publishers[0].IsRunning);
        await h.Owner.DisposeAsync();
    }

    [Fact] // section 13.1
    public async Task Suspend_pause_publisher_join_failure_keeps_the_pause_and_never_writes_neutral_or_detaches()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Xbox360Publishers[0].StopThrows = true;
        h.Native.Calls.Clear();

        var pause = await h.Owner.PauseForSuspendAsync(default);

        Assert.Equal(SuspendPauseOutcome.PublisherNotStopped, pause.Outcome);
        Assert.False(pause.Safe);
        Assert.True(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused); // stays paused
        Assert.DoesNotContain("SetXbox360DeviceState", h.Native.Calls);
        Assert.DoesNotContain("DetachUSBDeviceEx", h.Native.Calls);
        await h.Owner.DisposeAsync();
    }

    [Fact] // section 13.2
    public async Task Suspend_pause_with_a_rejected_neutral_write_retires_the_presentation_through_the_owner()
    {
        var native = new FakeNative();
        var xbox360 = new FakePublisher();
        var deck = new FakePublisher();
        var owner = Build(native, xbox360, deck);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        native.Calls.Clear();
        native.StateResults.Enqueue(false); // suspend neutral write rejected

        var pause = await owner.PauseForSuspendAsync(default);

        Assert.Equal(SuspendPauseOutcome.NeutralRejectedPresentationRetired, pause.Outcome);
        Assert.True(pause.Safe);
        Assert.Null(owner.ActivePresentation);
        Assert.Contains("DetachUSBDeviceEx", native.Calls);
        Assert.False(deck.Started); // no alternate presentation fallback
        await owner.DisposeAsync();
    }

    [Fact] // review #490: a retained-partial retire state (publisher stopped/cleared, active kind
           // retained because the canonical neutral+detach was not proven) must NOT be certified
           // PausedNoPresentation/Safe=true -- Suspend must retry the SAME-device neutral write.
    public async Task Suspend_pause_after_a_failed_retire_retries_neutral_instead_of_certifying_no_presentation_safe()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        h.Snapshot = WantsDeck();
        h.Native.DetachResults.Enqueue(USBDeviceDetachResult.RetryableFailure); // switch-away retire's detach fails

        var switchResult = await h.Owner.ReconcileDesiredPresentationAsync(h.Source, h.Capture, default);
        Assert.Equal(PresentationReconcileOutcome.Failed, switchResult.Outcome);
        Assert.Equal(AddonPresentationKind.Xbox360, h.Owner.ActivePresentation); // ownership retained
        Assert.True(h.Xbox360Publishers[0].StopCalled); // publisher already proven stopped/cleared
        h.Native.Calls.Clear();

        var pause = await h.Owner.PauseForSuspendAsync(default);

        Assert.NotEqual(SuspendPauseOutcome.PausedNoPresentation, pause.Outcome);
        Assert.Equal(SuspendPauseOutcome.Paused, pause.Outcome);
        Assert.True(pause.Safe);
        Assert.Contains("SetXbox360DeviceState", h.Native.Calls); // neutral actually re-attempted and proven
        await h.Owner.DisposeAsync();
    }

    [Fact] // review #490 (2nd pass): a rejected INITIAL Xbox360 neutral write followed by a failed
           // cleanup detach never commits _activeKind/_publisher, but the device is still attached and
           // non-neutral-proven -- PausedNoPresentation must not certify that safe.
    public async Task Suspend_pause_after_a_rejected_initial_xbox360_neutral_and_failed_cleanup_detach_stays_blocked()
    {
        var native = new FakeNative();
        var owner = Build(native, new FakePublisher(), new FakePublisher());
        native.StateResults.Enqueue(false); // initial Xbox360 neutral rejected
        native.DetachResults.Enqueue(USBDeviceDetachResult.RetryableFailure); // cleanup detach fails

        var attach = await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);
        Assert.False(attach.Succeeded);
        Assert.Null(owner.ActivePresentation); // _activeKind/_publisher were never committed

        var pause = await owner.PauseForSuspendAsync(default);

        Assert.Equal(SuspendPauseOutcome.Blocked, pause.Outcome);
        Assert.False(pause.Safe);
        Assert.Equal("ResidualXbox360Attachment:Attached", pause.Reason);
        await owner.DisposeAsync();
    }

    [Fact] // review #490 (2nd pass): same class of gap for the SteamDeck initial-attach cleanup path.
    public async Task Suspend_pause_after_a_rejected_initial_steamdeck_neutral_and_failed_cleanup_detach_stays_blocked()
    {
        var native = new FakeNative();
        var owner = Build(native, new FakePublisher(), new FakePublisher());
        native.StateResults.Enqueue(false); // initial SteamDeck neutral rejected
        native.DetachResults.Enqueue(USBDeviceDetachResult.RetryableFailure); // cleanup detach fails

        var attach = await owner.AttachInitialAsync(new FakeSource(), WantsDeck(), default);
        Assert.False(attach.Succeeded);
        Assert.Null(owner.ActivePresentation); // _activeKind/_publisher were never committed

        var pause = await owner.PauseForSuspendAsync(default);

        Assert.Equal(SuspendPauseOutcome.Blocked, pause.Outcome);
        Assert.False(pause.Safe);
        Assert.Equal("ResidualSteamDeckSession", pause.Reason); // the retained _deckSession is the evidence
        await owner.DisposeAsync();
    }

    [Fact] // section 16.9 / 10.1
    public async Task Resume_with_an_unavailable_physical_source_keeps_the_suspend_pause_active()
    {
        var h = new SwitchHarness();
        await h.Owner.AttachInitialAsync(h.Source, WantsXbox(), default);
        Assert.Equal(SuspendPauseOutcome.Paused, (await h.Owner.PauseForSuspendAsync(default)).Outcome);

        var resume = await h.Owner.ResumeAfterSuspendAsync(new FakeSource { Running = false }, h.Capture, default);

        Assert.Equal(SuspendResumeOutcome.DeferredSourceUnavailable, resume.Outcome);
        Assert.True(resume.StillBlocked);
        Assert.True(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused);
        Assert.False(h.Xbox360Publishers[0].IsRunning);

        // Once a live source is provided (existing PR8/PR10 recovery), the same release path finishes.
        var recovered = await h.Owner.ResumeAfterSuspendAsync(h.Source, h.Capture, default);
        Assert.Equal(SuspendResumeOutcome.SamePublisherResumed, recovered.Outcome);
        Assert.False(((IMsiClawAddonPresentation)h.Owner).IsSuspendPaused);
        await h.Owner.DisposeAsync();
    }

    [Fact] // section 16.10: reuse the #488 blocked-callback technique
    public async Task A_rumble_callback_pending_in_the_sink_cannot_land_a_non_zero_write_after_the_suspend_stop()
    {
        var native = new FakeNative();
        var gate = new ManualResetEventSlim(false);
        var sink = new FakeRumbleSink { BlockFirstNonZeroWrite = gate };
        var owner = BuildWithSink(native, new FakePublisher(), new FakePublisher(), sink);
        await owner.AttachInitialAsync(new FakeSource(), WantsXbox(), default);

        var callback = Task.Run(() => native.ArmedXbox360RumbleCallback!(0, 200, 100));
        Assert.True(sink.FirstNonZeroWriteEntered.Wait(TimeSpan.FromSeconds(2)));

        var pause = Task.Run(() => owner.PauseForSuspendAsync(default));
        await Task.Delay(100);
        Assert.False(pause.IsCompleted); // draining the blocked callback before the STOP

        gate.Set();
        Assert.True((await pause).Safe);
        await callback;

        Assert.Equal(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped, sink.Writes[^1]);
        Assert.Single(sink.Writes, w => w.Equals(SteamInputAddonforClaw.Feedback.TwoMotorRumble.Stopped));
        await owner.DisposeAsync();
    }

    // ---- fakes ----

    private sealed class FakeSource : IMsiClawPreparedInputSource
    {
        public bool Running { get; set; } = true;
        public bool IsRunning => Running;
        public ControllerState LatestState { get; set; }
        public int ResetLatestStateToNeutralCalls { get; private set; }
        public void ResetLatestStateToNeutral() { ResetLatestStateToNeutralCalls++; LatestState = default; }
        public event EventHandler<ControllerState>? StateChanged { add { } remove { } }
        public MsiClawInputStartResult StartPrepared(Input.DirectInput.DirectInputDeviceDescriptor descriptor) => new(MsiClawInputStartStatus.Started, "");
        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePublisher : IAddonPresentationPublisher
    {
        public bool Started { get; private set; }
        public bool StopCalled { get; private set; }
        public bool StartThrows { get; set; }
        public bool StopThrows { get; set; }
        public TaskCompletionSource? StopGate { get; set; }
        public Action<Exception>? Fault { get; set; }
        private bool _running;
        public bool IsRunning => _running;
        public void Start()
        {
            if (StartThrows) throw new InvalidOperationException("start failed");
            Started = true;
            _running = true;
        }
        public async Task StopAsync()
        {
            StopCalled = true;
            if (StopGate is not null) await StopGate.Task.ConfigureAwait(false);
            if (StopThrows) throw new InvalidOperationException("join failed");
            _running = false;
        }
    }

    private sealed class FakeNative : ICanonicalViiperNativeApi
    {
        internal readonly List<string> Calls = [];
        internal Queue<USBDeviceAttachResult> AttachResults { get; } = [];
        internal Queue<USBDeviceDetachResult> DetachResults { get; } = [];
        internal Queue<USBDeviceAttachmentState> AttachmentStates { get; } = [];
        internal Queue<bool> StateResults { get; } = [];
        private readonly Dictionary<nuint, USBDeviceAttachmentState> _attachmentByHandle = [];
        public bool NewUSBServer(ref USBServerConfig config, out nuint handle, ViiperLogCallback? callback = null) { Calls.Add("NewUSBServer"); handle = 10; return true; }
        public bool CloseUSBServer(nuint handle) { Calls.Add("CloseUSBServer"); return true; }
        public bool CreateUSBBus(nuint handle, ref uint bus) { Calls.Add("CreateUSBBus"); bus = 42; return true; }
        public bool RemoveUSBBus(nuint handle, uint bus) { Calls.Add("RemoveUSBBus"); return true; }
        public bool GetUSBDeviceIdentity(nuint handle, out uint bus, out uint id) { Calls.Add("GetUSBDeviceIdentity"); bus = 42; id = handle == 20 ? 9u : 10u; return true; }
        public bool AttachUSBDevice(nuint handle) => throw new NotSupportedException();
        public bool DetachUSBDevice(nuint handle) => throw new NotSupportedException();
        public USBDeviceAttachResult AttachUSBDeviceEx(nuint handle) { Calls.Add("AttachUSBDeviceEx"); var r = AttachResults.Count > 0 ? AttachResults.Dequeue() : USBDeviceAttachResult.Success; if (r == USBDeviceAttachResult.Success) _attachmentByHandle[handle] = USBDeviceAttachmentState.Attached; return r; }
        public USBDeviceDetachResult DetachUSBDeviceEx(nuint handle) { Calls.Add("DetachUSBDeviceEx"); var r = DetachResults.Count > 0 ? DetachResults.Dequeue() : USBDeviceDetachResult.Success; if (r == USBDeviceDetachResult.Success) _attachmentByHandle[handle] = USBDeviceAttachmentState.Detached; return r; }
        public bool GetUSBDeviceAttachmentState(nuint handle, out USBDeviceAttachmentState state) { Calls.Add("GetUSBDeviceAttachmentState"); state = AttachmentStates.Count > 0 ? AttachmentStates.Dequeue() : (_attachmentByHandle.TryGetValue(handle, out var s) ? s : USBDeviceAttachmentState.Detached); return true; }
        public bool CreateSteamDeckDevice(nuint server, out nuint handle, uint bus, bool autoAttach, ushort vid, ushort pid) { Calls.Add("CreateSteamDeckDevice"); handle = 20; return true; }
        public bool SetSteamDeckDeviceState(nuint handle, SteamDeckDeviceState state) { Calls.Add("SetSteamDeckDeviceState"); return StateResults.Count == 0 || StateResults.Dequeue(); }
        public bool SetSteamDeckOutputCallback(nuint handle, SteamDeckOutputCallback? callback) { Calls.Add(callback is null ? "ClearSteamDeckOutputCallback" : "SetSteamDeckOutputCallback"); var ok = RumbleCallbackResults.Count == 0 || RumbleCallbackResults.Dequeue(); if (ok) ArmedSteamDeckOutputCallback = callback; return ok; }
        public bool RemoveSteamDeckDevice(nuint handle) => throw new NotSupportedException();
        public SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint handle) { Calls.Add("RemoveSteamDeckDeviceEx"); return SteamDeckDeviceRemoveResult.Success; }
        internal Queue<bool> RumbleCallbackResults { get; } = [];
        internal Xbox360RumbleCallback? ArmedXbox360RumbleCallback { get; private set; }
        internal SteamDeckOutputCallback? ArmedSteamDeckOutputCallback { get; private set; }
        public bool CreateXbox360Device(nuint server, out nuint handle, uint bus, bool autoAttach, ushort vid, ushort pid, byte subtype) { Calls.Add("CreateXbox360Device"); handle = 30; return true; }
        public bool SetXbox360DeviceState(nuint handle, Xbox360DeviceState state) { Calls.Add("SetXbox360DeviceState"); return StateResults.Count == 0 || StateResults.Dequeue(); }
        public bool SetXbox360RumbleCallback(nuint handle, Xbox360RumbleCallback? callback)
        {
            Calls.Add(callback is null ? "ClearXbox360RumbleCallback" : "SetXbox360RumbleCallback");
            var ok = RumbleCallbackResults.Count == 0 || RumbleCallbackResults.Dequeue();
            if (ok) ArmedXbox360RumbleCallback = callback;
            return ok;
        }
        public bool RemoveXbox360Device(nuint handle) { Calls.Add("RemoveXbox360Device"); return true; }
        public Xbox360DeviceRemoveResult RemoveXbox360DeviceEx(nuint handle) { Calls.Add("RemoveXbox360DeviceEx"); return Xbox360DeviceRemoveResult.Success; }
    }
}
