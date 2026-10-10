using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerVibrationStrengthUiTests
{
    [Fact]
    public void Controller_page_exposes_collapsed_icon_vibration_group_with_two_slider_rows_and_recovery_card()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml");
        var document = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var expander = document.Descendants().Single(element =>
            element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute(x + "Name") == "VibrationStrengthExpander");

        Assert.Equal("Vibration Strength", (string?)expander.Attribute("Header"));
        Assert.Equal("False", (string?)expander.Attribute("IsExpanded"));
        Assert.Equal("Set the vibration strength applied while the Addon owns the controller.", (string?)expander.Attribute("Description"));
        Assert.Contains(expander.Elements(), element => element.Name.LocalName == "SettingsExpander.HeaderIcon");
        var ledExpander = document.Descendants().Single(element =>
            element.Name.LocalName == "SettingsExpander"
            && (string?)element.Attribute(x + "Name") == "ControllerLedExpander");
        Assert.NotSame(ledExpander.Parent, expander.Parent);
        Assert.DoesNotContain("Controller Settings", expander.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings.json", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);

        var mappingContent = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "MappingContent");
        Assert.DoesNotContain(mappingContent.Descendants(), element => ReferenceEquals(element, expander));
        Assert.True(document.ToString().IndexOf("x:Name=\"MappingContent\"", StringComparison.Ordinal)
            < document.ToString().IndexOf("x:Name=\"VibrationStrengthExpander\"", StringComparison.Ordinal));

        AssertSliderRow(expander, x, "Left Motor", "LeftVibrationStrengthSlider", "LeftVibrationStrengthPercentText", "LeftVibrationTestButton");
        AssertSliderRow(expander, x, "Right Motor", "RightVibrationStrengthSlider", "RightVibrationStrengthPercentText", "RightVibrationTestButton");

        var cards = expander.Descendants().Where(element => element.Name.LocalName == "SettingsCard").ToArray();
        Assert.Equal(["Left Motor", "Right Motor", "Vibration Recovery"], cards.Select(card => (string?)card.Attribute("Header")));
        var recoveryButton = cards[2].Descendants().Single(element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute(x + "Name") == "RestoreVibrationButton");
        Assert.Equal("Restore Vibration", (string?)recoveryButton.Attribute("Content"));
        Assert.Equal("RestoreVibrationButton_Click", (string?)recoveryButton.Attribute("Click"));
        Assert.Contains("temporarily disconnects the virtual controller", cards[2].ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("save your game and close it", cards[2].ToString(), StringComparison.OrdinalIgnoreCase);
        var pageCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs"));
        Assert.Contains("frontend.RunDeveloperRumbleRearmAsync()", pageCode, StringComparison.Ordinal);
        Assert.Contains("RestoreVibrationButton.IsEnabled = _frontend is not null && CanStartVibrationRearm(", pageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_vibrationSnapshot.Writable", pageCode[pageCode.IndexOf("private async void RestoreVibrationButton_Click", StringComparison.Ordinal)..pageCode.IndexOf("private async Task CommitVibrationStrengthAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("Physical vibration is not verified", pageCode, StringComparison.Ordinal);
        var developerPage = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml"));
        var developerCode = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/VibrationTestPage.xaml.cs"));
        Assert.DoesNotContain("Rearm A2VM 8", developerPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RearmRumble_Click", developerCode, StringComparison.Ordinal);
        Assert.Contains("Xbox360 Terminal STOP Loop", developerPage, StringComparison.Ordinal);
        Assert.Contains("Vibration Profile 0/100 Probe", developerPage, StringComparison.Ordinal);
        Assert.Contains("A2VM LED Profile Read-Only Probe", developerPage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(false, false, false, false, true, false)]
    public void Vibration_recovery_is_blocked_by_pending_edits_tests_and_duplicate_requests(
        bool hasDraft,
        bool pointerGesture,
        bool mutation,
        bool test,
        bool rearm,
        bool expected) =>
        Assert.Equal(expected, ControllerPage.CanStartVibrationRearm(
            hasDraft, pointerGesture, mutation, test, rearm));

    [Fact]
    public async Task Keyboard_settle_waits_300ms_and_only_commits_the_latest_complete_pair()
    {
        var delay = new ManualDelay();
        var committed = new TaskCompletionSource<(int Left, int Right)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var debounce = new ControllerVibrationStrengthDebounce(
            delay.DelayAsync,
            action => { action(); return true; },
            (left, right) => committed.TrySetResult((left, right)));

        debounce.Schedule(60, 50);
        Assert.True(debounce.HasPendingDraft);
        Assert.False(committed.Task.IsCompleted);
        Assert.Equal([TimeSpan.FromMilliseconds(300)], delay.RequestedDelays);

        debounce.Schedule(60, 75);
        Assert.Equal(2, delay.RequestedDelays.Count);
        Assert.All(delay.RequestedDelays, requested => Assert.Equal(TimeSpan.FromMilliseconds(300), requested));
        delay.Elapse();

        Assert.Equal((60, 75), await committed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(debounce.HasPendingDraft);
    }

    [Fact]
    public async Task Cancelling_a_pending_debounce_never_submits_a_vibration_pair()
    {
        var delay = new ManualDelay();
        var commits = 0;
        using var debounce = new ControllerVibrationStrengthDebounce(
            delay.DelayAsync,
            action => { action(); return true; },
            (_, _) => commits++);

        debounce.Schedule(80, 35);
        debounce.CancelPending();
        delay.Elapse();
        await Task.Delay(20);

        Assert.False(debounce.HasPendingDraft);
        Assert.Equal(0, commits);
    }

    [Fact]
    public async Task Flushing_pending_pair_submits_once_and_ignores_late_timer_completion()
    {
        var delay = new ManualDelay();
        var commits = new List<(int Left, int Right)>();
        using var debounce = new ControllerVibrationStrengthDebounce(
            delay.DelayAsync,
            action => { action(); return true; },
            (left, right) => commits.Add((left, right)));

        debounce.Schedule(45, 65);
        Assert.True(debounce.FlushPending());
        Assert.False(debounce.FlushPending());
        delay.Elapse();
        await Task.Delay(20);

        Assert.Equal([(45, 65)], commits);
        Assert.False(debounce.HasPendingDraft);
    }

    [Fact]
    public async Task Held_pointer_cancels_idle_settle_but_keeps_sliders_editable_until_release()
    {
        var delay = new ManualDelay();
        var submissions = 0;
        using var debounce = new ControllerVibrationStrengthDebounce(
            delay.DelayAsync,
            action => { action(); return true; },
            (_, _) => submissions++);

        debounce.Schedule(70, 50);
        debounce.CancelPending(); // Pointer press pauses the idle timer for the whole gesture.
        delay.Elapse();
        await Task.Delay(20);

        Assert.Equal(0, submissions);
        Assert.True(ControllerPage.CanEditVibrationSliders(available: true, writable: true, testInProgress: false));
        Assert.False(ControllerPage.CanSubmitVibrationDraft(
            available: true,
            writable: true,
            hasDraft: true,
            pointerGestureInProgress: true,
            debouncePending: false,
            mutationInProgress: false,
            pageUnloaded: false));
        Assert.True(ControllerPage.CanSubmitVibrationDraft(
            available: true,
            writable: true,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: false,
            mutationInProgress: false,
            pageUnloaded: false));
    }

    [Fact]
    public void Pointer_completion_is_matched_to_the_active_pointer_and_is_idempotent()
    {
        Assert.True(ControllerPage.IsCurrentVibrationPointerGesture(
            gestureInProgress: true, activePointerId: 17, observedPointerId: 17));
        Assert.False(ControllerPage.IsCurrentVibrationPointerGesture(
            gestureInProgress: true, activePointerId: 17, observedPointerId: 18));
        Assert.False(ControllerPage.IsCurrentVibrationPointerGesture(
            gestureInProgress: false, activePointerId: null, observedPointerId: 17));
    }

    [Fact]
    public void In_flight_response_preserves_newer_pair_and_only_the_latest_pair_can_drain_afterward()
    {
        var submitted = (Left: 30, Right: 50);
        var latestDraft = (Left: 80, Right: 25);

        Assert.False(ControllerPage.ShouldClearSubmittedVibrationDraft(latestDraft, submitted));
        Assert.True(ControllerPage.ShouldPreserveVibrationDraft(
            snapshotAvailable: true,
            snapshotWritable: true,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: false));
        Assert.False(ControllerPage.CanSubmitVibrationDraft(
            available: true,
            writable: true,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: false,
            mutationInProgress: true,
            pageUnloaded: false));
        Assert.True(ControllerPage.CanSubmitVibrationDraft(
            available: true,
            writable: true,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: false,
            mutationInProgress: false,
            pageUnloaded: false));
        Assert.True(ControllerPage.ShouldClearSubmittedVibrationDraft(submitted, submitted));
    }

    [Fact]
    public void Unavailable_or_read_only_snapshot_discards_pending_draft_and_never_submits()
    {
        Assert.False(ControllerPage.ShouldPreserveVibrationDraft(
            snapshotAvailable: false,
            snapshotWritable: false,
            hasDraft: true,
            pointerGestureInProgress: true,
            debouncePending: true));
        Assert.False(ControllerPage.ShouldPreserveVibrationDraft(
            snapshotAvailable: true,
            snapshotWritable: false,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: true));
        Assert.False(ControllerPage.CanSubmitVibrationDraft(
            available: false,
            writable: false,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: false,
            mutationInProgress: false,
            pageUnloaded: false));
        Assert.False(ControllerPage.CanSubmitVibrationDraft(
            available: true,
            writable: false,
            hasDraft: true,
            pointerGestureInProgress: false,
            debouncePending: false,
            mutationInProgress: false,
            pageUnloaded: false));
    }

    [Fact]
    public void Test_guard_rejects_pointer_draft_save_and_test_and_allows_only_confirmed_values()
    {
        var available = new FrontendControllerVibrationStrengthSnapshot(
            Available: true,
            Writable: true,
            TestAvailable: true,
            LeftPercent: 50,
            RightPercent: 50,
            Status: "Ready.");

        Assert.False(ControllerPage.CanRunVibrationMotorTest(
            available, hasPendingDraft: false, pointerGestureInProgress: true, mutationInProgress: false, testInProgress: false));
        Assert.False(ControllerPage.CanRunVibrationMotorTest(
            available, hasPendingDraft: true, pointerGestureInProgress: false, mutationInProgress: false, testInProgress: false));
        Assert.False(ControllerPage.CanRunVibrationMotorTest(
            available, hasPendingDraft: false, pointerGestureInProgress: false, mutationInProgress: true, testInProgress: false));
        Assert.False(ControllerPage.CanRunVibrationMotorTest(
            available, hasPendingDraft: false, pointerGestureInProgress: false, mutationInProgress: false, testInProgress: true));
        Assert.True(ControllerPage.CanRunVibrationMotorTest(
            available, hasPendingDraft: false, pointerGestureInProgress: false, mutationInProgress: false, testInProgress: false));
    }

    [Fact]
    public void Page_wires_value_changed_to_draft_only_and_renders_returned_desired_values()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var handlerStart = page.IndexOf("private void VibrationStrengthSlider_ValueChanged", StringComparison.Ordinal);
        var handlerEnd = page.IndexOf("private async void LeftVibrationTestButton_Click", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = page[handlerStart..handlerEnd];

        Assert.Contains("_vibrationDebounce?.Schedule(draft.Left, draft.Right)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("SetControllerVibrationStrengthAsync", handler, StringComparison.Ordinal);
        Assert.Contains("_isRenderingVibration", handler, StringComparison.Ordinal);
        Assert.Contains("_vibrationSnapshot.Available", handler, StringComparison.Ordinal);
        Assert.Contains("_vibrationSnapshot.Writable", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("|| _vibrationMutationInProgress", handler, StringComparison.Ordinal);
        Assert.Contains("_vibrationDebounce?.CancelPending()", handler, StringComparison.Ordinal);
        Assert.Contains("ShouldClearSubmittedVibrationDraft(_vibrationDraft, submittedPair)", page, StringComparison.Ordinal);
        Assert.Contains("ShouldPreserveVibrationDraft(", page, StringComparison.Ordinal);
        Assert.Contains("snapshot.LeftPercent is { } left ? $\"{left}%\" : \"—\"", page, StringComparison.Ordinal);
        Assert.Contains("snapshot.RightPercent is { } right ? $\"{right}%\" : \"—\"", page, StringComparison.Ordinal);
        Assert.Contains("Visibility = snapshot.Available ? Visibility.Visible : Visibility.Collapsed", page, StringComparison.Ordinal);
        Assert.Contains("_isRenderingVibration = true", page, StringComparison.Ordinal);
        Assert.Contains("LeftVibrationStrengthSlider.Value = snapshot.LeftPercent ?? 0", page, StringComparison.Ordinal);
        Assert.Contains("RightVibrationStrengthSlider.Value = snapshot.RightPercent ?? 0", page, StringComparison.Ordinal);
        Assert.Contains("CanEditVibrationSliders(", page, StringComparison.Ordinal);
        Assert.Contains("var testsEnabled = CanRunVibrationMotorTest(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("var testsEnabled = _vibrationSnapshot.Available", page, StringComparison.Ordinal);
        Assert.Contains("LeftVibrationTestButton.IsEnabled = testsEnabled", page, StringComparison.Ordinal);
        Assert.Contains("RightVibrationTestButton.IsEnabled = testsEnabled", page, StringComparison.Ordinal);
        Assert.Contains("&& !testInProgress", page, StringComparison.Ordinal);
        Assert.Contains("private void RegisterVibrationPointerHandlers", page, StringComparison.Ordinal);
        Assert.Contains("UIElement.PointerReleasedEvent", page, StringComparison.Ordinal);
        Assert.Contains("UIElement.PointerCanceledEvent", page, StringComparison.Ordinal);
        Assert.Contains("UIElement.PointerCaptureLostEvent", page, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", page, StringComparison.Ordinal);
        Assert.Contains("CompleteVibrationPointerGesture(args.Pointer.PointerId", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CapturePointer(", page, StringComparison.Ordinal);
        Assert.Contains("private void ControllerPage_Unloaded", page, StringComparison.Ordinal);
        var mainWindow = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/MainWindow.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("else if (wasController) ControllerContent.Deactivate();", mainWindow, StringComparison.Ordinal);

        var testHandlerStart = page.IndexOf("private async Task TestVibrationMotorAsync", StringComparison.Ordinal);
        var testHandlerEnd = page.IndexOf("internal static bool CanRunVibrationMotorTest", testHandlerStart, StringComparison.Ordinal);
        Assert.True(testHandlerStart >= 0 && testHandlerEnd > testHandlerStart);
        var testHandler = page[testHandlerStart..testHandlerEnd];
        Assert.DoesNotContain("_vibrationSnapshot.Available", testHandler, StringComparison.Ordinal);
        Assert.Contains("TryStartVibrationMotorTest(", testHandler, StringComparison.Ordinal);
        Assert.Contains("frontend.TestControllerVibrationMotorAsync(motor)", testHandler, StringComparison.Ordinal);

        Assert.DoesNotContain("Firmware values read successfully", page, StringComparison.Ordinal);
        Assert.DoesNotContain("persistent firmware ceiling", File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw.UI/Views/ControllerPage.xaml")), StringComparison.OrdinalIgnoreCase);

        var unavailable = FrontendControllerVibrationStrengthSnapshot.Unavailable(
            "Production vibration strength is not enabled for this model.") with { TestAvailable = true };
        Assert.False(unavailable.Available);
        Assert.False(unavailable.Writable);
        Assert.True(unavailable.TestAvailable);
        Assert.Null(unavailable.LeftPercent);
        Assert.Null(unavailable.RightPercent);
        Assert.Equal("Production vibration strength is not enabled for this model.", unavailable.Status);
    }

    [Fact]
    public async Task Physical_test_dispatches_once_when_strength_capability_is_unavailable_but_test_is_available()
    {
        var unavailable = FrontendControllerVibrationStrengthSnapshot.Unavailable(
            "Production vibration strength is not enabled for this model.") with { TestAvailable = true };
        Assert.False(unavailable.Available);
        Assert.False(unavailable.Writable);
        Assert.True(unavailable.TestAvailable);
        Assert.True(ControllerPage.CanRunVibrationMotorTest(
            unavailable, hasPendingDraft: false, pointerGestureInProgress: false, mutationInProgress: false, testInProgress: false));

        var dispatchCount = 0;
        var startedCount = 0;
        var expected = new FrontendControllerVibrationTestResult(
            FrontendControllerVibrationTestOutcome.Succeeded,
            "Physical vibration test completed.");
        var request = ControllerPage.TryStartVibrationMotorTest(
            unavailable,
            hasPendingDraft: false,
            pointerGestureInProgress: false,
            mutationInProgress: false,
            testInProgress: false,
            markStarted: () => startedCount++,
            dispatch: () =>
            {
                dispatchCount++;
                return Task.FromResult(expected);
            });

        Assert.NotNull(request);
        Assert.Same(expected, await request!);
        Assert.Equal(1, startedCount);
        Assert.Equal(1, dispatchCount);
    }

    private static void AssertSliderRow(
        XElement expander,
        XNamespace x,
        string header,
        string sliderName,
        string percentName,
        string testButtonName)
    {
        var card = expander.Descendants().Single(element => element.Name.LocalName == "SettingsCard"
            && (string?)element.Attribute("Header") == header);
        Assert.Null((string?)card.Attribute("ContentAlignment"));
        var slider = card.Descendants().Single(element => element.Name.LocalName == "Slider"
            && (string?)element.Attribute(x + "Name") == sliderName);
        Assert.Equal("0", (string?)slider.Attribute("Minimum"));
        Assert.Equal("100", (string?)slider.Attribute("Maximum"));
        Assert.Equal("1", (string?)slider.Attribute("StepFrequency"));
        Assert.Equal("Collapsed", (string?)slider.Attribute("Visibility"));
        Assert.Contains(card.Descendants(), element => element.Name.LocalName == "TextBlock"
            && (string?)element.Attribute(x + "Name") == percentName);
        Assert.Contains(card.Descendants(), element => element.Name.LocalName == "Button"
            && (string?)element.Attribute(x + "Name") == testButtonName
            && (string?)element.Attribute("Content") == "Test");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class ManualDelay
    {
        private readonly object _sync = new();
        private readonly List<TaskCompletionSource> _waiters = [];
        private readonly List<TimeSpan> _requestedDelays = [];

        internal IReadOnlyList<TimeSpan> RequestedDelays
        {
            get { lock (_sync) return _requestedDelays.ToArray(); }
        }

        internal Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _requestedDelays.Add(delay);
                _waiters.Add(waiter);
            }
            cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            return waiter.Task;
        }

        internal void Elapse()
        {
            TaskCompletionSource[] waiters;
            lock (_sync)
            {
                waiters = _waiters.ToArray();
                _waiters.Clear();
            }
            foreach (var waiter in waiters)
                waiter.TrySetResult();
        }
    }
}
