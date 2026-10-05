using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GameInputSystemButtonProbeTests
{
    [Fact]
    public void Start_is_idempotent_and_stop_unregisters_once()
    {
        var factory = new FakeSessionFactory();
        var probe = new GameInputSystemButtonProbe(() => true, factory);

        Assert.Equal(FrontendGameInputSystemButtonProbeState.Ready, probe.Capture().State);
        Assert.Equal(FrontendGameInputSystemButtonProbeState.Running, probe.Start().State);
        Assert.Equal(FrontendGameInputSystemButtonProbeState.Running, probe.Start().State);
        Assert.Equal(1, factory.RegisterCount);

        Assert.Equal(FrontendGameInputSystemButtonProbeState.Stopped, probe.Stop().State);
        Assert.Equal(FrontendGameInputSystemButtonProbeState.Stopped, probe.Stop().State);
        Assert.Equal(1, factory.Session!.StopCount);
    }

    [Fact]
    public void Start_can_be_repeated_after_a_successful_stop()
    {
        var factory = new FakeSessionFactory();
        var probe = new GameInputSystemButtonProbe(() => true, factory);

        probe.Start();
        probe.Stop();
        var restarted = probe.Start();

        Assert.Equal(FrontendGameInputSystemButtonProbeState.Running, restarted.State);
        Assert.Equal(2, factory.RegisterCount);
    }

    [Fact]
    public void Debug_logging_is_required_before_native_registration()
    {
        var factory = new FakeSessionFactory();
        var probe = new GameInputSystemButtonProbe(() => false, factory);

        var result = probe.Start();

        Assert.Equal(FrontendGameInputSystemButtonProbeState.Unavailable, result.State);
        Assert.Contains("Debug", result.Status, StringComparison.Ordinal);
        Assert.Equal(0, factory.RegisterCount);
    }

    [Theory]
    [InlineData(true, "GameInputCreate:DllNotFoundException", FrontendGameInputSystemButtonProbeState.Unavailable)]
    [InlineData(false, "SetFocusPolicy:COMException", FrontendGameInputSystemButtonProbeState.Failed)]
    [InlineData(false, "RegisterSystemButtonCallback:0x80004005", FrontendGameInputSystemButtonProbeState.Failed)]
    public void Start_failures_are_contained_as_typed_diagnostic_results(
        bool unavailable,
        string reason,
        FrontendGameInputSystemButtonProbeState expectedState)
    {
        var factory = new FakeSessionFactory
        {
            StartFailure = new GameInputSystemButtonProbeStartException(unavailable, reason, "test failure")
        };
        var probe = new GameInputSystemButtonProbe(() => true, factory);

        var result = probe.Start();

        Assert.Equal(expectedState, result.State);
        Assert.Equal(0, result.EventCount);
    }

    [Theory]
    [InlineData(1u, 0u, true, false, false, false)]
    [InlineData(0u, 1u, false, true, false, false)]
    [InlineData(2u, 0u, false, false, true, false)]
    [InlineData(0u, 2u, false, false, false, true)]
    public void Callback_mapping_preserves_raw_masks_and_button_transitions(
        uint current,
        uint previous,
        bool guidePressed,
        bool guideReleased,
        bool sharePressed,
        bool shareReleased)
    {
        var mapped = GameInputSystemButtonProbe.MapEvent(3, new(
            123456, current, previous, true, null, 0x0DB0, 0x1234,
            "WING", @"HID\VID_0DB0&PID_1234", Guid.Empty,
            [1, 2, 3], [4, 5, 6], 0x0004000E, 3));

        Assert.Equal(3, mapped.Sequence);
        Assert.Equal(123456ul, mapped.TimestampMicroseconds);
        Assert.Equal(current, mapped.CurrentButtonsRaw);
        Assert.Equal(previous, mapped.PreviousButtonsRaw);
        Assert.Equal(guidePressed, mapped.GuidePressed);
        Assert.Equal(guideReleased, mapped.GuideReleased);
        Assert.Equal(sharePressed, mapped.SharePressed);
        Assert.Equal(shareReleased, mapped.ShareReleased);
    }

    [Fact]
    public void Callback_identity_formatting_is_stable_and_empty_strings_are_unavailable()
    {
        var mapped = GameInputSystemButtonProbe.MapEvent(1, new(
            7, 1, 0, true, null, 0x0DB0, 0x0419,
            "", "", Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray(),
            [0, 1, 2], 0x0004000E, 3));

        Assert.Equal("0x0DB0", mapped.VendorId);
        Assert.Equal("0x0419", mapped.ProductId);
        Assert.Null(mapped.DisplayName);
        Assert.Null(mapped.PnpPath);
        Assert.Equal("{00000000-0000-0000-0000-000000000001}", mapped.ContainerId);
        Assert.Equal(string.Concat(Enumerable.Range(0, 32).Select(static value => value.ToString("X2"))), mapped.DeviceId);
        Assert.Equal("000102", mapped.DeviceRootId);
        Assert.Equal("Controller|Gamepad", mapped.SupportedInput);
        Assert.Equal("Guide|Share", mapped.SupportedSystemButtons);
    }

    [Fact]
    public void Get_device_info_failure_is_retained_as_an_event_without_stopping_the_probe()
    {
        var factory = new FakeSessionFactory();
        var probe = new GameInputSystemButtonProbe(() => true, factory);
        probe.Start();

        factory.Session!.Publish(new(99, 0, 1, false, "GetDeviceInfo:0x80004005",
            null, null, null, null, null, null, null, null, null));

        var snapshot = probe.Capture();
        Assert.Equal(FrontendGameInputSystemButtonProbeState.Running, snapshot.State);
        Assert.Equal(1, snapshot.EventCount);
        Assert.False(snapshot.LastEvent!.DeviceInfoSucceeded);
        Assert.Equal("GetDeviceInfo:0x80004005", snapshot.LastEvent.DeviceInfoFailure);
        Assert.Equal(0, factory.Session.StopCount);
    }

    [Fact]
    public void Failed_unregister_retains_native_session_and_does_not_retry()
    {
        var factory = new FakeSessionFactory { StopSucceeds = false };
        var probe = new GameInputSystemButtonProbe(() => true, factory);
        probe.Start();

        Assert.Equal(FrontendGameInputSystemButtonProbeState.Failed, probe.Stop().State);
        Assert.Equal(FrontendGameInputSystemButtonProbeState.Failed, probe.Stop().State);
        Assert.Equal(1, factory.Session!.StopCount);
    }

    [Fact]
    public void Native_device_info_layout_matches_the_x64_v3_prefix()
    {
        Assert.Equal(8, IntPtr.Size);
        Assert.Equal(144, Marshal.SizeOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>());
        Assert.Equal(26, Marshal.OffsetOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>("DeviceId").ToInt32());
        Assert.Equal(58, Marshal.OffsetOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>("DeviceRootId").ToInt32());
        Assert.Equal(92, Marshal.OffsetOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>("DeviceFamily").ToInt32());
        Assert.Equal(108, Marshal.OffsetOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>("ContainerId").ToInt32());
        Assert.Equal(128, Marshal.OffsetOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>("DisplayName").ToInt32());
        Assert.Equal(136, Marshal.OffsetOf<NativeGameInputSystemButtonProbeSessionFactory.NativeGameInputDeviceInfo>("PnpPath").ToInt32());
    }

    [Fact]
    public void Focus_policy_uses_background_system_buttons_without_exclusive_policy_or_product_lifecycle_calls()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/Diagnostics/GameInputSystemButtonProbe.cs"));

        Assert.Contains("GameInputEnableBackgroundGuideButton", source, StringComparison.Ordinal);
        Assert.Contains("GameInputEnableBackgroundShareButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GameInputExclusiveForegroundGuideButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GameInputExclusiveForegroundShareButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HidHide", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_presentationOwnership", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class FakeSessionFactory : IGameInputSystemButtonProbeSessionFactory
    {
        public int RegisterCount { get; private set; }
        public bool StopSucceeds { get; init; } = true;
        public GameInputSystemButtonProbeStartException? StartFailure { get; init; }
        public FakeSession? Session { get; private set; }

        public IGameInputSystemButtonProbeSession Register(Action<GameInputSystemButtonProbeNativeEvent> callback)
        {
            RegisterCount++;
            if (StartFailure is not null) throw StartFailure;
            Session = new FakeSession(callback, StopSucceeds);
            return Session;
        }
    }

    private sealed class FakeSession(Action<GameInputSystemButtonProbeNativeEvent> callback, bool stopSucceeds)
        : IGameInputSystemButtonProbeSession
    {
        public int StopCount { get; private set; }
        public void Publish(GameInputSystemButtonProbeNativeEvent value) => callback(value);
        public bool Stop() { StopCount++; return stopSucceeds; }
    }
}
