using System.Diagnostics;
using SteamInputAddonforClaw.GameBar;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GameBarStateDiagnosticObserverTests
{
    [Fact]
    public void Observer_is_debug_gated_and_initializes_off_the_hook_thread()
    {
        var source = ObserverSource();
        var start = Method(source, "internal static GameBarStateDiagnosticObserver? Start()");

        var debugGate = start.IndexOf("if (!AppLog.IsEnabled(AppLogLevel.Debug)) return null;", StringComparison.Ordinal);
        var create = start.IndexOf("new GameBarStateDiagnosticObserver()", StringComparison.Ordinal);
        Assert.True(debugGate >= 0 && debugGate < create);
        Assert.Contains("using GameBarApi = global::Windows.Gaming.UI.GameBar;", source);
        Assert.Contains("_initialization = Task.Run(Initialize);", source);
        Assert.DoesNotContain("GetAwaiter().GetResult", source);
        Assert.DoesNotContain("DispatcherQueue", source);
    }

    [Fact]
    public void Observer_subscribes_to_both_events_then_logs_actual_initial_and_changed_state()
    {
        var source = ObserverSource();
        var initialize = Method(source, "private void Initialize()");

        var visibleSubscription = initialize.IndexOf("GameBarApi.VisibilityChanged += OnVisibilityChanged;", StringComparison.Ordinal);
        var redirectedSubscription = initialize.IndexOf("GameBarApi.IsInputRedirectedChanged += OnIsInputRedirectedChanged;", StringComparison.Ordinal);
        var initialSnapshot = initialize.IndexOf("ReadAndLogState(\"ObserverStarted\")", StringComparison.Ordinal);
        Assert.True(visibleSubscription >= 0 && visibleSubscription < redirectedSubscription && redirectedSubscription < initialSnapshot);

        Assert.Contains("private void OnVisibilityChanged(object? sender, object args) => ReadAndLogState(\"VisibilityChanged\")", source);
        Assert.Contains("private void OnIsInputRedirectedChanged(object? sender, object args) => ReadAndLogState(\"IsInputRedirectedChanged\")", source);

        var read = Method(source, "private void ReadAndLogState(string trigger)");
        Assert.Contains("var visible = GameBarApi.Visible;", read);
        Assert.Contains("var isInputRedirected = GameBarApi.IsInputRedirected;", read);
        Assert.Contains("(\"Trigger\", trigger)", read);
        Assert.Contains("(\"Visible\", visible)", read);
        Assert.Contains("(\"IsInputRedirected\", isInputRedirected)", read);
        Assert.Contains("catch (Exception exception)", read);
        Assert.Contains("\"StateReadFailed\"", read);

        Assert.Contains("(\"Operation\", \"Subscribe\")", initialize);
        Assert.Contains("\"ObserverUnavailable\"", initialize);
    }

    [Fact]
    public void Observer_disposes_its_two_subscriptions_and_contains_unsubscribe_failures()
    {
        var source = ObserverSource();
        var remove = Method(source, "private void RemoveSubscriptions()");
        Assert.Contains("GameBarApi.VisibilityChanged -= OnVisibilityChanged;", remove);
        Assert.Contains("GameBarApi.IsInputRedirectedChanged -= OnIsInputRedirectedChanged;", remove);
        Assert.Equal(2, remove.Split("catch (Exception exception)", StringSplitOptions.None).Length - 1);
        var dispose = Method(source, "public async ValueTask DisposeAsync()");
        Assert.Contains("Task.Run(CleanupAfterInitializationAsync)", dispose);
        Assert.Contains("WaitForTaskWithinShutdownBudgetAsync(cleanup)", dispose);
        Assert.DoesNotContain("await _initialization.ConfigureAwait(false)", dispose);
        Assert.Contains("WaitForTaskWithinShutdownBudgetAsync(_initialization)", Method(source, "private async Task CleanupAfterInitializationAsync()"));
        Assert.Contains("(\"Operation\", \"Unsubscribe\")", remove);

        Assert.DoesNotContain("TryRequestSteamPulse", source);
        Assert.DoesNotContain("AttachInitialAsync", source);
        Assert.DoesNotContain("WingActionDispatcher", source);
    }

    [Fact]
    public async Task Observer_shutdown_wait_is_bounded_for_a_stalled_initialization()
    {
        var stalledInitialization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();

        var completed = await GameBarStateDiagnosticObserver.WaitForTaskWithinShutdownBudgetAsync(stalledInitialization.Task);

        Assert.False(completed);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Bounded wait took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Observer_checks_for_shutdown_after_each_event_subscription()
    {
        var initialize = Method(ObserverSource(), "private void Initialize()");
        var visibleSubscription = initialize.IndexOf("GameBarApi.VisibilityChanged += OnVisibilityChanged;", StringComparison.Ordinal);
        var redirectedSubscription = initialize.IndexOf("GameBarApi.IsInputRedirectedChanged += OnIsInputRedirectedChanged;", StringComparison.Ordinal);
        var firstDisposeCheck = initialize.IndexOf("if (Volatile.Read(ref _disposeRequested) != 0)", visibleSubscription, StringComparison.Ordinal);
        var secondDisposeCheck = initialize.IndexOf("if (Volatile.Read(ref _disposeRequested) != 0)", redirectedSubscription, StringComparison.Ordinal);

        Assert.True(visibleSubscription >= 0 && firstDisposeCheck > visibleSubscription && firstDisposeCheck < redirectedSubscription);
        Assert.True(redirectedSubscription >= 0 && secondDisposeCheck > redirectedSubscription);
        var initialStateRead = initialize.IndexOf("ReadAndLogState(\"ObserverStarted\");", StringComparison.Ordinal);
        var postReadDisposeCheck = initialize.IndexOf("if (Volatile.Read(ref _disposeRequested) != 0)", initialStateRead, StringComparison.Ordinal);
        var postReadCleanup = initialize.IndexOf("RemoveSubscriptions();", postReadDisposeCheck, StringComparison.Ordinal);
        Assert.True(initialStateRead >= 0 && postReadDisposeCheck > initialStateRead && postReadCleanup > postReadDisposeCheck);
    }

    [Fact]
    public void Observer_is_scheduled_before_deferred_runtime_startup_and_disposed_before_the_hook()
    {
        var directory = RepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(directory, "src", "SteamInputAddonforClaw", "Hosting", "RuntimeProcessApplication.cs"));
        var messageLoopReady = runtime.IndexOf("_messageLoop.Run(() =>", StringComparison.Ordinal);
        var hookAndObserverStart = runtime.IndexOf("_processHost.StartRuntimeEventWatchers();", messageLoopReady, StringComparison.Ordinal);
        var deferredStartup = runtime.IndexOf("_processHost.StartDeferredRuntimeStartup();", messageLoopReady, StringComparison.Ordinal);
        Assert.True(messageLoopReady >= 0 && hookAndObserverStart > messageLoopReady && hookAndObserverStart < deferredStartup);

        var host = File.ReadAllText(Path.Combine(directory, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"));
        var disposeObserver = host.IndexOf("await _gameBarStateDiagnosticObserver.DisposeAsync()", StringComparison.Ordinal);
        var disposeHook = host.IndexOf("_winGSuppressionGuard.Dispose();", StringComparison.Ordinal);
        Assert.True(disposeObserver >= 0 && disposeObserver < disposeHook);
    }

    private static string ObserverSource()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "GameBar", "GameBarStateDiagnosticObserver.cs"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var nextPrivate = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        var nextPublic = source.IndexOf("\n    public ", start + signature.Length, StringComparison.Ordinal);
        var nextInternal = source.IndexOf("\n    internal ", start + signature.Length, StringComparison.Ordinal);
        var next = new[] { nextPrivate, nextPublic, nextInternal }.Where(index => index >= 0).DefaultIfEmpty(-1).Min();
        return next < 0 ? source[start..] : source[start..next];
    }
}
