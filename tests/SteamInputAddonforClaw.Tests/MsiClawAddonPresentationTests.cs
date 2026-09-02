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
            deckPublisherFactory: (_, _, fault) => { deck.Fault = fault; return deck; });
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

    private sealed class SwitchHarness
    {
        public FakeNative Native { get; } = new();
        public FakeSource Source { get; } = new();
        public SteamPresentationSnapshot Snapshot { get; set; } = new(0, false);
        public List<FakePublisher> Xbox360Publishers { get; } = [];
        public List<FakePublisher> DeckPublishers { get; } = [];
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
                deckPublisherFactory: (_, _, fault) => { var p = new FakePublisher { Fault = fault }; DeckPublishers.Add(p); return p; });
        }
    }

    // ---- fakes ----

    private sealed class FakeSource : IMsiClawPreparedInputSource
    {
        public bool Running { get; set; } = true;
        public bool IsRunning => Running;
        public ControllerState LatestState => default;
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
        public bool SetSteamDeckOutputCallback(nuint handle, SteamDeckOutputCallback? callback) { Calls.Add("SetSteamDeckOutputCallback"); return true; }
        public bool RemoveSteamDeckDevice(nuint handle) => throw new NotSupportedException();
        public SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint handle) { Calls.Add("RemoveSteamDeckDeviceEx"); return SteamDeckDeviceRemoveResult.Success; }
        public bool CreateXbox360Device(nuint server, out nuint handle, uint bus, bool autoAttach, ushort vid, ushort pid, byte subtype) { Calls.Add("CreateXbox360Device"); handle = 30; return true; }
        public bool SetXbox360DeviceState(nuint handle, Xbox360DeviceState state) { Calls.Add("SetXbox360DeviceState"); return StateResults.Count == 0 || StateResults.Dequeue(); }
        public bool RemoveXbox360Device(nuint handle) { Calls.Add("RemoveXbox360Device"); return true; }
        public Xbox360DeviceRemoveResult RemoveXbox360DeviceEx(nuint handle) { Calls.Add("RemoveXbox360DeviceEx"); return Xbox360DeviceRemoveResult.Success; }
    }
}
