using System.Runtime.CompilerServices;
using Velopack;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Updates;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class VelopackUpdateClientTests
{
    [Fact]
    public async Task Download_forwards_the_caller_cancellation_token_to_Velopack()
    {
        var operations = new FakeOperations { CheckResult = UninitializedUpdateInfo() };
        var client = new VelopackUpdateClient(operations);
        using var cancellation = new CancellationTokenSource();
        await client.CheckForUpdatesAsync(cancellation.Token);

        await client.DownloadUpdatesAsync(cancellation.Token);

        Assert.Equal(cancellation.Token, operations.DownloadToken);
    }

    [Fact]
    public async Task Silent_update_download_cancellation_does_not_schedule_apply()
    {
        var operations = new FakeOperations
        {
            CheckResult = UninitializedUpdateInfo(),
            DownloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var client = new VelopackUpdateClient(operations);
        using var cancellation = new CancellationTokenSource();
        var update = new SilentUpdateService(client).CheckAndDownloadAsync(cancellation.Token);
        await operations.DownloadStarted.Task;

        cancellation.Cancel();
        operations.DownloadCompletion!.SetCanceled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        Assert.Equal(1, operations.DownloadCount);
        Assert.Equal(0, operations.ApplyCount);
        Assert.Equal(cancellation.Token, operations.DownloadToken);
    }

    [Fact]
    public void Pending_update_is_applied_only_when_the_primary_startup_boundary_calls_it()
    {
        var pending = UninitializedAsset();
        var operations = new FakeOperations { PendingUpdate = pending };
        var client = new VelopackUpdateClient(operations);

        var scheduled = client.TrySchedulePendingUpdateApply(["--background", "--restart"]);

        Assert.True(scheduled);
        Assert.Same(pending, operations.AppliedUpdate);
        Assert.NotNull(operations.RestartArguments);
        Assert.Equal(["--background", "--restart"], operations.RestartArguments!);
    }

    [Fact]
    public void No_pending_update_does_not_schedule_apply()
    {
        var operations = new FakeOperations();
        var client = new VelopackUpdateClient(operations);

        Assert.False(client.TrySchedulePendingUpdateApply(["--background"]));
        Assert.Null(operations.AppliedUpdate);
    }

    [Fact]
    public void Pending_apply_failure_is_fail_open_for_runtime_startup()
    {
        var operations = new FakeOperations
        {
            PendingUpdate = UninitializedAsset(),
            ApplyFailure = new InvalidOperationException("simulated local update lock")
        };
        var client = new VelopackUpdateClient(operations);

        Assert.False(client.TrySchedulePendingUpdateApply(["--background"]));
    }

    [Fact]
    public async Task Main_ui_install_requests_a_safe_restart_after_the_response_without_applying_updates_in_runtime()
    {
        var operations = new FakeOperations
        {
            PendingUpdate = UninitializedAsset(),
            CheckResult = null
        };
        var client = new VelopackUpdateClient(operations);
        var restartRequests = 0;
        var coordinator = new FrontendUpdateCoordinator(client, () =>
        {
            restartRequests++;
            return true;
        });

        var checkedSnapshot = await coordinator.CheckAndDownloadAsync(CancellationToken.None);
        Assert.Equal(FrontendUpdateState.ReadyToInstall, checkedSnapshot.State);

        var result = await coordinator.InstallAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(0, restartRequests);
        Assert.Equal(0, operations.ApplyCount);

        await coordinator.CompleteInstallAfterResponseAsync();

        Assert.Equal(1, restartRequests);
        Assert.Equal(0, operations.ApplyCount);
    }

    [Fact]
    public async Task Main_ui_install_surfaces_restart_failure_without_applying_the_pending_update()
    {
        var operations = new FakeOperations { PendingUpdate = UninitializedAsset() };
        var coordinator = new FrontendUpdateCoordinator(new VelopackUpdateClient(operations), () => false);

        var result = await coordinator.InstallAsync(CancellationToken.None);
        Assert.True(result.Succeeded);

        await coordinator.CompleteInstallAfterResponseAsync();

        Assert.Equal(FrontendUpdateState.ReadyToInstall, coordinator.Capture().State);
        Assert.Contains("could not be restarted", coordinator.Capture().Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, operations.ApplyCount);
    }

    [Fact]
    public async Task Non_cancellable_check_stops_waiting_when_the_caller_cancels()
    {
        var completion = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FakeOperations { CheckTask = completion.Task };
        var client = new VelopackUpdateClient(operations);
        using var cancellation = new CancellationTokenSource();

        var check = client.CheckForUpdatesAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.False(check.IsCompletedSuccessfully);
        completion.SetResult(UninitializedUpdateInfo());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadUpdatesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Late_check_fault_is_observed_and_does_not_make_an_update_available()
    {
        var completion = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FakeOperations { CheckTask = completion.Task };
        var client = new VelopackUpdateClient(operations);
        using var cancellation = new CancellationTokenSource();

        var check = client.CheckForUpdatesAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        completion.SetException(new InvalidOperationException("late check failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadUpdatesAsync(CancellationToken.None));
    }

    private static UpdateInfo UninitializedUpdateInfo() => (UpdateInfo)RuntimeHelpers.GetUninitializedObject(typeof(UpdateInfo));
    private static VelopackAsset UninitializedAsset() => (VelopackAsset)RuntimeHelpers.GetUninitializedObject(typeof(VelopackAsset));

    private sealed class FakeOperations : IVelopackUpdateOperations
    {
        public bool IsInstalled => true;
        public VelopackAsset? PendingUpdate { get; init; }
        public VelopackAsset? AppliedUpdate { get; private set; }
        public string[]? RestartArguments { get; private set; }
        public Exception? ApplyFailure { get; init; }
        public Task<UpdateInfo?>? CheckTask { private get; init; }
        public UpdateInfo? CheckResult { private get; init; }
        public CancellationToken DownloadToken { get; private set; }
        public TaskCompletionSource? DownloadCompletion { get; init; }
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }
        public Task<UpdateInfo?> CheckForUpdatesAsync() => CheckTask ?? Task.FromResult(CheckResult);
        public Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken)
        {
            DownloadCount++;
            DownloadToken = cancellationToken;
            DownloadStarted.TrySetResult();
            return DownloadCompletion?.Task ?? Task.CompletedTask;
        }
        public void WaitExitThenApplyUpdates(VelopackAsset update, string[]? restartArguments)
        {
            if (ApplyFailure is not null) throw ApplyFailure;
            AppliedUpdate = update;
            RestartArguments = restartArguments;
        }
        public VelopackAsset? UpdatePendingRestart => PendingUpdate;
    }
}
