using SteamInputAddonforClaw.UI.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UiShutdownCoordinatorTests
{
    [Fact]
    public void Exit_dispatch_failure_uses_non_xaml_fallback()
    {
        var xamlExit = 0;
        var fallback = 0;
        new UiExitDispatcher(() => false, () => false, () => xamlExit++, () => fallback++).RequestExit();
        Assert.Equal(0, xamlExit);
        Assert.Equal(1, fallback);
    }

    [Fact]
    public void Final_non_xaml_exit_fallback_is_info_while_shutdown_dispatch_failures_remain_errors()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SteamInputAddonforClaw.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var app = File.ReadAllText(Path.Combine(root!.FullName, "src/SteamInputAddonforClaw.UI/App.xaml.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var methodStart = app.IndexOf("private void RequestExitOnUiThread()", StringComparison.Ordinal);
        var methodEnd = app.IndexOf("\n    }\n}", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var exitMethod = app[methodStart..methodEnd];

        Assert.Contains("AppLog.Info(\"Frontend\", \"UI dispatcher unavailable during final shutdown; terminating without XAML API.\"", exitMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("AppLog.Error(", exitMethod, StringComparison.Ordinal);

        var disconnectedStart = app.IndexOf("private void OnFrontendDisconnected(", StringComparison.Ordinal);
        var closeRequestedStart = app.IndexOf("private void OnFrontendCloseRequested(", disconnectedStart, StringComparison.Ordinal);
        var closeRequestHandlerEnd = app.IndexOf("private void CloseForRuntimeRequestOnUiThread(", closeRequestedStart, StringComparison.Ordinal);
        Assert.True(disconnectedStart >= 0 && closeRequestedStart > disconnectedStart && closeRequestHandlerEnd > closeRequestedStart);
        var disconnectedHandler = app[disconnectedStart..closeRequestedStart];
        var closeRequestedHandler = app[closeRequestedStart..closeRequestHandlerEnd];
        Assert.Contains("AppLog.Error(\"Frontend\", \"Runtime disconnect shutdown dispatch failed", disconnectedHandler, StringComparison.Ordinal);
        Assert.Contains("AppLog.Error(\"Frontend\", \"Runtime close request dispatch failed", closeRequestedHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_thread_exit_does_not_enqueue_or_use_fallback()
    {
        var enqueued = 0;
        var xamlExit = 0;
        var fallback = 0;
        new UiExitDispatcher(() => true, () => { enqueued++; return false; }, () => xamlExit++, () => fallback++).RequestExit();
        Assert.Equal(1, xamlExit);
        Assert.Equal(0, enqueued);
        Assert.Equal(0, fallback);
    }

    [Fact]
    public void Successful_enqueue_does_not_exit_or_fallback_on_current_thread()
    {
        var enqueued = 0;
        var xamlExit = 0;
        var fallback = 0;
        new UiExitDispatcher(
            () => false,
            () => { Interlocked.Increment(ref enqueued); return true; },
            () => Interlocked.Increment(ref xamlExit),
            () => Interlocked.Increment(ref fallback)).RequestExit();

        Assert.Equal(1, enqueued);
        Assert.Equal(0, xamlExit);
        Assert.Equal(0, fallback);
    }

    [Fact]
    public async Task Cleanup_exception_still_requests_exit()
    {
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => Task.FromException(new InvalidOperationException("dispose failed")),
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromSeconds(1));

        await coordinator.ShutdownAsync();

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task Synchronous_cleanup_start_failure_still_requests_exit()
    {
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => throw new InvalidOperationException("cleanup could not start"),
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromSeconds(1));

        await coordinator.ShutdownAsync();

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task Stalled_cleanup_is_bounded_and_still_requests_exit()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => neverCompletes.Task,
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromMilliseconds(25));

        await coordinator.ShutdownAsync();

        Assert.Equal(1, exits);
    }

    [Fact]
    public async Task Competing_shutdown_requests_cleanup_and_exit_only_once()
    {
        var cleanupCalls = 0;
        var exits = 0;
        var coordinator = new UiShutdownCoordinator(
            () => { Interlocked.Increment(ref cleanupCalls); return Task.CompletedTask; },
            () => Interlocked.Increment(ref exits),
            TimeSpan.FromSeconds(1));

        await Task.WhenAll(coordinator.ShutdownAsync(), coordinator.ShutdownAsync(), coordinator.ShutdownAsync());

        Assert.Equal(1, cleanupCalls);
        Assert.Equal(1, exits);
    }
}
