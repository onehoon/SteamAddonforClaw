using System.Runtime.CompilerServices;
using Velopack;
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

        await Task.Delay(50);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadUpdatesAsync(CancellationToken.None));
    }

    private static UpdateInfo UninitializedUpdateInfo() => (UpdateInfo)RuntimeHelpers.GetUninitializedObject(typeof(UpdateInfo));

    private sealed class FakeOperations : IVelopackUpdateOperations
    {
        public bool IsInstalled => true;
        public Task<UpdateInfo?>? CheckTask { private get; init; }
        public UpdateInfo? CheckResult { private get; init; }
        public CancellationToken DownloadToken { get; private set; }
        public Task<UpdateInfo?> CheckForUpdatesAsync() => CheckTask ?? Task.FromResult(CheckResult);
        public Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken) { DownloadToken = cancellationToken; return Task.CompletedTask; }
        public void WaitExitThenApplyUpdates(UpdateInfo update, string[]? restartArguments) { }
    }
}
