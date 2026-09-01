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
        public Action<Exception>? Fault { get; set; }
        private bool _running;
        public bool IsRunning => _running;
        public void Start()
        {
            if (StartThrows) throw new InvalidOperationException("start failed");
            Started = true;
            _running = true;
        }
        public Task StopAsync()
        {
            StopCalled = true;
            if (StopThrows) throw new InvalidOperationException("join failed");
            _running = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNative : ICanonicalViiperNativeApi
    {
        internal readonly List<string> Calls = [];
        internal Queue<USBDeviceAttachResult> AttachResults { get; } = [];
        internal Queue<USBDeviceAttachmentState> AttachmentStates { get; } = [];
        internal Queue<bool> StateResults { get; } = [];
        public bool NewUSBServer(ref USBServerConfig config, out nuint handle, ViiperLogCallback? callback = null) { Calls.Add("NewUSBServer"); handle = 10; return true; }
        public bool CloseUSBServer(nuint handle) { Calls.Add("CloseUSBServer"); return true; }
        public bool CreateUSBBus(nuint handle, ref uint bus) { Calls.Add("CreateUSBBus"); bus = 42; return true; }
        public bool RemoveUSBBus(nuint handle, uint bus) { Calls.Add("RemoveUSBBus"); return true; }
        public bool GetUSBDeviceIdentity(nuint handle, out uint bus, out uint id) { Calls.Add("GetUSBDeviceIdentity"); bus = 42; id = handle == 20 ? 9u : 10u; return true; }
        public bool AttachUSBDevice(nuint handle) => throw new NotSupportedException();
        public bool DetachUSBDevice(nuint handle) => throw new NotSupportedException();
        public USBDeviceAttachResult AttachUSBDeviceEx(nuint handle) { Calls.Add("AttachUSBDeviceEx"); return AttachResults.Count > 0 ? AttachResults.Dequeue() : USBDeviceAttachResult.Success; }
        public USBDeviceDetachResult DetachUSBDeviceEx(nuint handle) { Calls.Add("DetachUSBDeviceEx"); return USBDeviceDetachResult.Success; }
        public bool GetUSBDeviceAttachmentState(nuint handle, out USBDeviceAttachmentState state) { Calls.Add("GetUSBDeviceAttachmentState"); state = AttachmentStates.Count > 0 ? AttachmentStates.Dequeue() : USBDeviceAttachmentState.Detached; return true; }
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
