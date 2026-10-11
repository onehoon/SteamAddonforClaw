using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostInitialControllerAcquisitionTests
{
    [Fact]
    public void Eligible_initial_timeout_registers_the_existing_watcher_before_one_fresh_acquire()
    {
        var host = ReadHost();
        var startup = DeclarationContaining(host, "acquired.InitialAcquisitionRetryReason !=");
        var typedGate = startup.IndexOf("acquired.InitialAcquisitionRetryReason != Devices.MSI.Claw.MsiClawInitialAcquisitionRetryReason.None", StringComparison.Ordinal);
        var context = startup.IndexOf("_initialControllerAcquisition = context", typedGate, StringComparison.Ordinal);
        var watcher = startup.IndexOf("StartControllerDeviceArrivalWatcher()", context, StringComparison.Ordinal);
        var recheck = startup.IndexOf("RequestOwnedControllerRecovery(owner, \"ImmediateRecheck\")", watcher, StringComparison.Ordinal);

        Assert.True(typedGate >= 0 && context > typedGate && watcher > context && recheck > watcher);
        Assert.Contains("presentation.ViiperState == VirtualOutput.Viiper.CanonicalViiperRuntimeState.Ready", startup, StringComparison.Ordinal);
        Assert.Contains("DeviceArrivalWatcherStarted", startup, StringComparison.Ordinal);
        Assert.Contains("RetryRequested", startup, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _controllerOwnershipReleaseStarted) != 0", startup, StringComparison.Ordinal);
        Assert.Equal(1, Count(host, "new Controllers.Detection.WindowsDeviceArrivalWatcher()"));
    }

    [Fact]
    public void Deferred_initial_retries_use_AcquireAsync_and_only_typed_absence_can_remain_pending()
    {
        var host = ReadHost();
        var retry = DeclarationContaining(host, "context.Owner.AcquireAsync");
        var eligibility = DeclarationContaining(host, "result.InitialAcquisitionRetryReason !=");
        var scheduler = DeclarationContaining(host, "if (_initialControllerAcquisition is { } initial");

        Assert.Contains("context.Owner.AcquireAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoverLostInputAsync", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachInitialAsync", retry, StringComparison.Ordinal);
        Assert.Contains("result.InitialAcquisitionRetryReason", eligibility, StringComparison.Ordinal);
        Assert.Contains("context.OriginRetryReason", eligibility, StringComparison.Ordinal);
        Assert.Contains("result.NativeDeviceAbsentAtInitialCapture", eligibility, StringComparison.Ordinal);
        Assert.Contains("_centerMStartupControl?.Capture().State == FrontendCenterMStartupState.Disabled", eligibility, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _controllerOwnershipReleaseStarted) == 0", eligibility, StringComparison.Ordinal);
        Assert.Contains("context.IsViiperReady()", eligibility, StringComparison.Ordinal);
        Assert.Contains("RunInitialControllerAcquisitionAsync(initial, trigger)", scheduler, StringComparison.Ordinal);
        Assert.Contains("_centerMStartupControl?.Capture().State != FrontendCenterMStartupState.Disabled", scheduler, StringComparison.Ordinal);
        Assert.Contains("!initial.IsViiperReady()", scheduler, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _controllerOwnershipReleaseStarted) != 0", scheduler, StringComparison.Ordinal);
        Assert.Contains("RecoverOwnedControllerPhysicalInputAsync(physical, trigger", scheduler, StringComparison.Ordinal);
    }

    [Fact]
    public void Deferred_partial_pid1902_recheck_returns_before_context_clear_or_viiper_release()
    {
        var host = ReadHost();
        var retry = DeclarationContaining(host, "if (CanKeepInitialControllerAcquisitionDeferred(context, result))");
        var pending = retry.IndexOf("if (CanKeepInitialControllerAcquisitionDeferred(context, result))", StringComparison.Ordinal);
        var pendingReturn = retry.IndexOf("return;", pending, StringComparison.Ordinal);
        var clear = retry.IndexOf("ClearInitialControllerAcquisition(context);", pending, StringComparison.Ordinal);
        var release = retry.IndexOf("context.Presentation.ReleaseForCenterMEnableAsync", pending, StringComparison.Ordinal);
        var arrivalReschedule = retry.IndexOf("SchedulePendingInitialControllerArrival(context);", pending, StringComparison.Ordinal);

        Assert.True(pending >= 0 && pendingReturn > pending && clear > pendingReturn && release > clear);
        Assert.True(arrivalReschedule > pendingReturn);
    }

    [Fact]
    public void Initial_commit_preserves_hardware_and_presentation_order_for_both_entry_paths()
    {
        var host = ReadHost();
        var continuation = DeclarationContaining(host, "ApplyOwnedControllerLedSettingsAsync(context.StartupSettings.ControllerLed");
        var source = continuation.IndexOf("if (source is null || !source.IsRunning)", StringComparison.Ordinal);
        var led = continuation.IndexOf("ApplyOwnedControllerLedSettingsAsync(context.StartupSettings.ControllerLed", StringComparison.Ordinal);
        var vibration = continuation.IndexOf("ApplyOwnedControllerVibrationSettingsAsync(", led, StringComparison.Ordinal);
        var suppression = continuation.IndexOf("EnsureAddonAuthorityWinGSuppression()", vibration, StringComparison.Ordinal);
        var watcher = continuation.IndexOf("StartControllerDeviceArrivalWatcher()", suppression, StringComparison.Ordinal);
        var attach = continuation.IndexOf("context.Presentation.AttachInitialAsync(source, snapshot", watcher, StringComparison.Ordinal);
        var frontButton = continuation.IndexOf("MsiClawFrontButtonRuntime.Create", attach, StringComparison.Ordinal);

        Assert.True(source >= 0 && led > source && vibration > led && suppression > vibration && watcher > suppression && attach > watcher && frontButton > attach);
        Assert.Contains("CapturePresentationSnapshot()", continuation, StringComparison.Ordinal);
        Assert.Contains("ReconcileMotionReadersAsync(trigger", continuation, StringComparison.Ordinal);
        Assert.Contains("_frontButtonRuntime ??=", continuation, StringComparison.Ordinal);
        Assert.Equal(1, Count(host, "context.Presentation.AttachInitialAsync("));
        Assert.Contains("DeferredInitialControllerAcquisitionCompleted", host, StringComparison.Ordinal);
        Assert.Contains("OwnershipVerified", host, StringComparison.Ordinal);
        Assert.Contains("PublisherStarted", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Enable_and_shutdown_close_initial_retry_admission_and_drain_before_teardown()
    {
        var host = ReadHost();
        var releaseStart = host.IndexOf("async Task<SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult> ReleasePhysicalOwnershipAsync(", StringComparison.Ordinal);
        var releaseEnd = host.IndexOf("static bool IsFileProvenAbsent", releaseStart, StringComparison.Ordinal);
        Assert.True(releaseStart >= 0 && releaseEnd > releaseStart);
        var release = host[releaseStart..releaseEnd];
        var closeAdmission = release.IndexOf("_initialControllerAcquisition = null", StringComparison.Ordinal);
        var drain = release.IndexOf("await inFlightControllerWork.ConfigureAwait(false)", StringComparison.Ordinal);
        var presentationRelease = release.IndexOf("presentation.ReleaseForCenterMEnableAsync", StringComparison.Ordinal);
        var physicalRelease = release.IndexOf("owner.ReleaseForCenterMEnableAsync", StringComparison.Ordinal);
        Assert.True(closeAdmission >= 0 && drain > closeAdmission && presentationRelease > drain && physicalRelease > presentationRelease);

        var shutdown = Method(host, "private bool TryBeginProcessShutdownCore()");
        Assert.Contains("_initialControllerAcquisition = null", shutdown, StringComparison.Ordinal);
        Assert.Contains("_deviceArrivalWatcher?.Dispose()", shutdown, StringComparison.Ordinal);
        var dispose = Method(host, "public async ValueTask DisposeAsync()");
        Assert.True(dispose.IndexOf("await _ownedControllerRecovery.ConfigureAwait(false)", StringComparison.Ordinal)
            < dispose.IndexOf("await _presentationReconcile.ConfigureAwait(false)", StringComparison.Ordinal));
    }

    private static int Count(string source, string token) => source.Split(token, StringSplitOptions.None).Length - 1;

    private static string DeclarationContaining(string source, string token)
    {
        var marker = source.IndexOf(token, StringComparison.Ordinal);
        Assert.True(marker >= 0, $"Could not find source marker: {token}");
        var start = source.LastIndexOf("\n    private ", marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find containing declaration for: {token}");
        var end = source.IndexOf("\n    private ", marker + token.Length, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        return source[start..end];
    }

    private static string ReadHost()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Method(string source, string startToken, string? endToken = null)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method start: {startToken}");
        if (endToken is null) return source[start..];
        var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find method end: {endToken}");
        return source[start..end];
    }
}
