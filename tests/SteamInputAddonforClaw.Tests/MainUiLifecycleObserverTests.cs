using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainUiLifecycleObserverTests
{
    [Fact]
    public void CaseA_NormalAppearance_ProcessAliveButNeverVisibleYet_IsNotHiddenAfterVisible()
    {
        var provider = new FakeSnapshotProvider();
        var observer = new MainUiLifecycleObserver(provider);

        provider.Next = new MainUiWindowSnapshot(true, RecognizedMainWindowCount: 0, VisibleMainWindowCount: 0);
        var state = observer.Observe(processId: 100);

        Assert.Equal(MainUiLifecycleState.StartingOrHiddenNeverVisible, state);
        Assert.NotEqual(MainUiLifecycleState.HiddenAfterVisible, state);
    }

    [Fact]
    public void CaseB_VisibleThenHidden_ReportsHiddenAfterVisible()
    {
        var provider = new FakeSnapshotProvider();
        var observer = new MainUiLifecycleObserver(provider);

        provider.Next = new MainUiWindowSnapshot(true, 1, 1);
        Assert.Equal(MainUiLifecycleState.Visible, observer.Observe(200));

        provider.Next = new MainUiWindowSnapshot(true, 1, 0);
        Assert.Equal(MainUiLifecycleState.HiddenAfterVisible, observer.Observe(200));
    }

    [Fact]
    public void CaseC_CauseAgnostic_SameResultRegardlessOfWhatHidTheWindow()
    {
        // The observer is never told whether X, OEM1, or anything else caused the hide -- only
        // that all recognized windows are now hidden after having been visible.
        var provider = new FakeSnapshotProvider();
        var observer = new MainUiLifecycleObserver(provider);

        provider.Next = new MainUiWindowSnapshot(true, 1, 1);
        observer.Observe(300);
        provider.Next = new MainUiWindowSnapshot(true, 1, 0);

        Assert.Equal(MainUiLifecycleState.HiddenAfterVisible, observer.Observe(300));
    }

    [Fact]
    public void CaseD_HiddenBeforeFirstVisible_NeverBecomesHiddenAfterVisible()
    {
        var provider = new FakeSnapshotProvider { Next = new MainUiWindowSnapshot(true, 1, 0) };
        var observer = new MainUiLifecycleObserver(provider);

        var state = observer.Observe(400);

        Assert.Equal(MainUiLifecycleState.StartingOrHiddenNeverVisible, state);
        Assert.False(observer.SeenVisible);
    }

    [Fact]
    public void CaseE_NaturalExit_AfterVisible_ReportsExited()
    {
        var provider = new FakeSnapshotProvider();
        var observer = new MainUiLifecycleObserver(provider);

        provider.Next = new MainUiWindowSnapshot(true, 1, 1);
        observer.Observe(500);
        provider.Next = new MainUiWindowSnapshot(false, 0, 0);

        Assert.Equal(MainUiLifecycleState.Exited, observer.Observe(500));
    }

    [Fact]
    public void CaseF_VisibleAgain_CancelsHiddenState()
    {
        var provider = new FakeSnapshotProvider();
        var observer = new MainUiLifecycleObserver(provider);

        provider.Next = new MainUiWindowSnapshot(true, 1, 1);
        observer.Observe(600);
        provider.Next = new MainUiWindowSnapshot(true, 1, 0);
        Assert.Equal(MainUiLifecycleState.HiddenAfterVisible, observer.Observe(600));

        provider.Next = new MainUiWindowSnapshot(true, 1, 1);
        Assert.Equal(MainUiLifecycleState.Visible, observer.Observe(600));
    }

    [Fact]
    public void CaseG_PidReplacement_DoesNotReuseSeenVisibleFromThePreviousIdentity()
    {
        var provider = new FakeSnapshotProvider();
        var observer = new MainUiLifecycleObserver(provider);

        provider.Next = new MainUiWindowSnapshot(true, 1, 1);
        observer.Observe(700); // PID A becomes SeenVisible

        provider.Next = new MainUiWindowSnapshot(true, 1, 0); // PID B: alive, never yet visible
        var state = observer.Observe(701);

        Assert.Equal(MainUiLifecycleState.StartingOrHiddenNeverVisible, state);
        Assert.False(observer.SeenVisible);
    }

    [Fact]
    public void EnumerationFailure_ReportsUncertain_NeverASpecificState()
    {
        var provider = new FakeSnapshotProvider { Next = null };
        var observer = new MainUiLifecycleObserver(provider);

        Assert.Equal(MainUiLifecycleState.Uncertain, observer.Observe(800));
    }

    [Theory]
    [InlineData("MSI Center M", null, true)]
    [InlineData(null, "HwndWrapper[MSI Center M.exe;;12345]", true)]
    [InlineData("MSI Center M Settings", null, false)]
    [InlineData(null, "WindowsForms10.Window.0.app.0.2167256_r10_ad1", false)]
    public void WindowRecognition_matches_only_the_documented_title_or_class(string? title, string? className, bool expected) =>
        Assert.Equal(expected, MainUiWindowRecognition.IsRecognizedMainUiWindow(title, className));

    private sealed class FakeSnapshotProvider : IMainUiWindowSnapshotProvider
    {
        internal MainUiWindowSnapshot? Next { get; set; }
        public MainUiWindowSnapshot? Capture(int processId) => Next;
    }
}
