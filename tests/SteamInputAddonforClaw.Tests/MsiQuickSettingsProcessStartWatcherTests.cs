using SteamInputAddonforClaw.GameBar;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiQuickSettingsProcessStartWatcherTests
{
    [Fact]
    public void Production_adapter_uses_only_the_scoped_Gamebar_Widget_process_start_trace()
    {
        var source = File.ReadAllText(SourcePath("src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsProcessStartWatcher.cs"));

        Assert.Contains("new ManagementScope(@\"\\\\.\\root\\CIMV2\")", source, StringComparison.Ordinal);
        Assert.Contains("SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'Gamebar_Widget.exe'", source, StringComparison.Ordinal);
        Assert.Contains("ProcessID", source, StringComparison.Ordinal);
        Assert.Contains("ParentProcessID", source, StringComparison.Ordinal);
        Assert.Contains("SessionID", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__InstanceCreationEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WITHIN", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Timer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_payload_forwards_all_three_unsigned_WMI_identifiers()
    {
        using var adapter = new FakeAdapter();
        using var watcher = new MsiQuickSettingsProcessStartWatcher(adapter);
        MsiQuickSettingsProcessStart? received = null;
        watcher.ProcessStarted += value => received = value;

        Assert.True(watcher.Start());
        adapter.Raise(new MsiQuickSettingsProcessStartPayload(314u, 27u, 1u));

        Assert.Equal(new MsiQuickSettingsProcessStart(314, 27, 1), received);
    }

    [Fact]
    public void Missing_malformed_or_zero_process_id_is_not_forwarded()
    {
        foreach (var payload in InvalidPayloads())
        {
            using var adapter = new FakeAdapter();
            using var watcher = new MsiQuickSettingsProcessStartWatcher(adapter);
            var callbackCount = 0;
            watcher.ProcessStarted += _ => callbackCount++;

            Assert.True(watcher.Start());
            adapter.Raise(payload);

            Assert.Equal(0, callbackCount);
        }
    }

    private static IEnumerable<MsiQuickSettingsProcessStartPayload> InvalidPayloads()
    {
        yield return new(null, 27u, 1u);
        yield return new(0u, 27u, 1u);
        yield return new(314, 27u, 1u);
        yield return new("314", 27u, 1u);
        yield return new(314u, null, 1u);
        yield return new(314u, 27, 1u);
        yield return new(314u, 27u, null);
        yield return new(314u, 27u, "1");
    }

    [Fact]
    public void Failed_start_is_best_effort_and_dispose_is_idempotent()
    {
        using var adapter = new FakeAdapter { StartSucceeds = false };
        var watcher = new MsiQuickSettingsProcessStartWatcher(adapter);

        Assert.False(watcher.Start());
        watcher.Dispose();
        watcher.Dispose();

        Assert.Equal(1, adapter.StartCount);
        Assert.Equal(1, adapter.DisposeCount);
    }

    [Fact]
    public async Task Dispose_waits_for_an_admitted_callback_to_finish()
    {
        using var adapter = new FakeAdapter();
        using var watcher = new MsiQuickSettingsProcessStartWatcher(adapter);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        watcher.ProcessStarted += _ =>
        {
            entered.Set();
            release.Wait();
        };
        Assert.True(watcher.Start());

        var callback = Task.Run(() => adapter.Raise(new MsiQuickSettingsProcessStartPayload(314u, 27u, 1u)));
        var callbackEntered = entered.Wait(TimeSpan.FromSeconds(5));
        var dispose = Task.Run(watcher.Dispose);
        var concurrentDispose = Task.Run(watcher.Dispose);
        var earlyCompletion = await Task.WhenAny(dispose, concurrentDispose, Task.Delay(TimeSpan.FromMilliseconds(100)));

        release.Set();
        await Task.WhenAll(callback, dispose, concurrentDispose);
        Assert.True(callbackEntered);
        Assert.NotSame(dispose, earlyCompletion);
        Assert.NotSame(concurrentDispose, earlyCompletion);
        Assert.Equal(1, adapter.DisposeCount);
    }

    [Fact]
    public void Host_starts_watcher_before_startup_reconcile_and_stops_it_at_both_authority_boundaries()
    {
        var host = File.ReadAllText(SourcePath("src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        var startup = Method(host, "private async Task TryStartDisabledModeControllerAsync(");
        var watcherStart = startup.IndexOf("StartMsiQuickSettingsProcessStartWatcher()", StringComparison.Ordinal);
        var reconcile = startup.IndexOf("MsiQuickSettingsRuntimeQuiescer.QuiesceExisting()", StringComparison.Ordinal);
        var owner = startup.IndexOf("CreatePhysicalOwnership(startupComposition, startupResult.HardwareDeviceModel)", StringComparison.Ordinal);
        var admission = startup.IndexOf("startupResult.DisabledBootAdmission?.IsReady != true", StringComparison.Ordinal);
        Assert.True(watcherStart >= 0 && watcherStart < reconcile && reconcile < owner && owner < admission);

        var restored = host.IndexOf("onStockAuthorityRestored: () =>", StringComparison.Ordinal);
        var restoredDisarm = host.IndexOf("_winGSuppressionGuard.Disarm();", restored, StringComparison.Ordinal);
        var restoredStop = host.IndexOf("StopMsiQuickSettingsProcessStartWatcher();", restored, StringComparison.Ordinal);
        Assert.True(restored >= 0 && restoredStop > restored && restoredStop < restoredDisarm);

        var shutdown = Method(host, "private bool TryBeginProcessShutdownCore()");
        var shutdownFlag = shutdown.IndexOf("Interlocked.Exchange(ref _processShutdownStarted, 1)", StringComparison.Ordinal);
        var shutdownStop = shutdown.IndexOf("StopMsiQuickSettingsProcessStartWatcher();", StringComparison.Ordinal);
        Assert.True(shutdownFlag >= 0 && shutdownStop > shutdownFlag
            && shutdownStop < shutdown.IndexOf("PrepareRuntimeForShutdown();", StringComparison.Ordinal));

        var handler = Method(host, "private void OnMsiQuickSettingsProcessStarted(");
        var admissionClosed = handler.IndexOf("Volatile.Read(ref _processShutdownStarted)", StringComparison.Ordinal);
        var quiesce = handler.IndexOf("MsiQuickSettingsRuntimeQuiescer.QuiesceStartedProcess(started.ProcessId)", StringComparison.Ordinal);
        Assert.True(admissionClosed >= 0 && admissionClosed < quiesce);
        Assert.DoesNotContain("lock (_msiQuickSettingsWatcherGate)", handler, StringComparison.Ordinal);
        Assert.Contains("ParentProcessId", handler, StringComparison.Ordinal);
        Assert.Contains("SessionId", handler, StringComparison.Ordinal);

        var quiescer = File.ReadAllText(SourcePath("src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsRuntimeQuiescer.cs"));
        var eventPath = Method(quiescer, "internal static MsiQuickSettingsRuntimeQuiesceResult QuiesceStartedProcess(");
        Assert.Contains("QuiesceProcessIds([processId], 1, 0)", eventPath, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProcessesByName", eventPath, StringComparison.Ordinal);
        var sharedPath = Method(quiescer, "private static MsiQuickSettingsRuntimeQuiesceResult QuiesceProcessIds(");
        Assert.Contains("TryGetPackageFullName(processId", sharedPath, StringComparison.Ordinal);
        Assert.Contains("IsExactMsiQuickSettingsPackageFullName(packageFullName)", sharedPath, StringComparison.Ordinal);
        Assert.True(sharedPath.IndexOf("IsExactMsiQuickSettingsPackageFullName(packageFullName)", StringComparison.Ordinal)
            < sharedPath.IndexOf("TerminateExactPackageProcesses(", StringComparison.Ordinal));

        Assert.Equal(1, host.Split("private MsiQuickSettingsProcessStartWatcher? _msiQuickSettingsProcessStartWatcher;", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("_msiQuickSettingsQuiescingAllowed", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_msiQuickSettingsWatcherRetired", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_msiQuickSettingsWatcherStopInProgress", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_msiQuickSettingsWatcherStopCompleted", host, StringComparison.Ordinal);

        var start = Method(host, "private void StartMsiQuickSettingsProcessStartWatcher()");
        Assert.Contains("lock (_msiQuickSettingsWatcherGate)", start, StringComparison.Ordinal);
        Assert.True(start.IndexOf("watcher.Start()", StringComparison.Ordinal)
            < start.IndexOf("_msiQuickSettingsProcessStartWatcher = watcher", StringComparison.Ordinal));

        var stop = Method(host, "private void StopMsiQuickSettingsProcessStartWatcher()");
        Assert.Contains("lock (_msiQuickSettingsWatcherGate)", stop, StringComparison.Ordinal);
        Assert.True(stop.IndexOf("_msiQuickSettingsProcessStartWatcher = null", StringComparison.Ordinal)
            < stop.IndexOf("watcher.Dispose()", StringComparison.Ordinal));

        var watcherSource = File.ReadAllText(SourcePath("src/SteamInputAddonforClaw/GameBar/MsiQuickSettingsProcessStartWatcher.cs"));
        Assert.Contains("_activeCallbacks", watcherSource, StringComparison.Ordinal);
        Assert.Contains("_callbacksDrained.Wait()", watcherSource, StringComparison.Ordinal);
        var watcherDisposeStart = watcherSource.LastIndexOf("public void Dispose()", StringComparison.Ordinal);
        Assert.True(watcherDisposeStart >= 0);
        var watcherDispose = watcherSource[watcherDisposeStart..];
        Assert.True(watcherDispose.IndexOf("_callbackAdmissionOpen = false", StringComparison.Ordinal)
            < watcherDispose.IndexOf("_callbacksDrained.Wait()", StringComparison.Ordinal)
            && watcherDispose.IndexOf("_callbacksDrained.Wait()", StringComparison.Ordinal)
            < watcherDispose.IndexOf("_adapter.Dispose()", StringComparison.Ordinal));
    }

    private static string SourcePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relativePath);
    }

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method not found: {signature}");
        var nextPrivate = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        var nextInternal = source.IndexOf("\n    internal ", start + signature.Length, StringComparison.Ordinal);
        if (nextInternal >= 0 && (nextPrivate < 0 || nextInternal < nextPrivate))
            nextPrivate = nextInternal;
        return nextPrivate < 0 ? source[start..] : source[start..nextPrivate];
    }

    private sealed class FakeAdapter : IMsiQuickSettingsProcessStartWatcherAdapter
    {
        public event Action<MsiQuickSettingsProcessStartPayload>? ProcessStartArrived;
        public bool StartSucceeds { get; init; } = true;
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool TryStart(out Exception? error)
        {
            StartCount++;
            error = StartSucceeds ? null : new InvalidOperationException("test start failure");
            return StartSucceeds;
        }

        public void Raise(MsiQuickSettingsProcessStartPayload payload) => ProcessStartArrived?.Invoke(payload);

        public void Dispose() => DisposeCount++;
    }
}
