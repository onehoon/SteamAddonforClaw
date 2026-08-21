using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum TdpPowerNotification
{
    PowerSourceChanged,
    Suspend,
    ResumeAutomatic,
    ResumeSuspend
}

internal sealed class TdpPowerLifecycleWatcher : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(2500);
    private readonly TdpRuntime _runtime;
    private readonly ITdpPowerNotificationSource _source;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Lock _sync = new();
    private CancellationTokenSource? _pending;
    private Task _pendingTask = Task.CompletedTask;
    private bool _pendingForce;
    private bool _pendingInvalidate;
    private readonly List<string> _pendingReasons = [];
    private bool _suspended;
    private bool _resumeSeen;
    private bool _disposed;

    internal TdpPowerLifecycleWatcher(TdpRuntime runtime, ITdpPowerNotificationSource source,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _delay = delay ?? Task.Delay;
    }

    internal bool Start()
    {
        _source.Notification += OnNotification;
        try
        {
            if (_source.TryRegister(out var error)) return true;
            AppLog.Warn("Profiles.Tdp", "TDP power notification registration failed; startup reconcile remains available.",
                null, ("NativeError", error));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Profiles.Tdp", "TDP power notification registration threw; startup reconcile remains available.", exception);
        }
        return false;
    }

    internal void ScheduleStartup() => Schedule(true, false, "Startup");

    internal void ScheduleCenterMReconcile() => Schedule(true, true, "CenterM");

    internal Task DrainPendingAsync()
    {
        lock (_sync) return _pendingTask;
    }

    internal void Observe(TdpPowerNotification notification)
    {
        AppLog.Debug("Profiles.Tdp", "Lifecycle notification", ("Event", notification));
        lock (_sync)
        {
            if (_disposed) return;
            if (notification == TdpPowerNotification.Suspend)
            {
                _suspended = true;
                _resumeSeen = false;
                CancelPendingUnderLock();
                return;
            }

            if (notification is TdpPowerNotification.ResumeAutomatic or TdpPowerNotification.ResumeSuspend)
            {
                if (_resumeSeen)
                {
                    AppLog.Debug("Profiles.Tdp", "Lifecycle notification", ("Event", notification), ("Action", "IgnoredDuplicate"));
                    return;
                }
                _resumeSeen = true;
                _suspended = false;
                ScheduleUnderLock(force: true, invalidate: true, "Resume");
                return;
            }

            if (!_suspended)
                ScheduleUnderLock(force: false, invalidate: false, "PowerSourceChanged");
        }
    }

    private void OnNotification(TdpPowerNotification notification)
    {
        try { Observe(notification); }
        catch (Exception exception) { AppLog.Error("Profiles.Tdp", "TDP power notification processing failed.", exception); }
    }

    private void Schedule(bool force, bool invalidate, string reason)
    {
        lock (_sync)
        {
            if (!_disposed) ScheduleUnderLock(force, invalidate, reason);
        }
    }

    private void ScheduleUnderLock(bool force, bool invalidate, string reason)
    {
        _pendingForce |= force;
        _pendingInvalidate |= invalidate;
        if (!_pendingReasons.Contains(reason, StringComparer.Ordinal))
            _pendingReasons.Add(reason);
        _pending?.Cancel();
        var pending = new CancellationTokenSource();
        _pending = pending;
        _pendingTask = SettleAsync(pending, reason);
    }

    private async Task SettleAsync(CancellationTokenSource pending, string reason)
    {
        try
        {
            await _delay(SettleDelay, pending.Token).ConfigureAwait(false);
            bool force;
            bool invalidate;
            string settledReason;
            lock (_sync)
            {
                if (_disposed || !ReferenceEquals(_pending, pending)) return;
                _pending = null;
                force = _pendingForce;
                invalidate = _pendingInvalidate;
                settledReason = string.Join('+', _pendingReasons);
                _pendingForce = false;
                _pendingInvalidate = false;
                _pendingReasons.Clear();
            }
            AppLog.Debug("Profiles.Tdp", "Lifecycle reconcile settled", ("Reason", settledReason), ("Force", force), ("Invalidate", invalidate));
            _runtime.ReconcileCurrent(force, invalidate, settledReason);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Profiles.Tdp", "TDP lifecycle settle failed.", exception, ("Reason", reason)); }
        finally { pending.Dispose(); }
    }

    private void CancelPendingUnderLock()
    {
        _pending?.Cancel();
        _pending = null;
        _pendingForce = false;
        _pendingInvalidate = false;
        _pendingReasons.Clear();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingUnderLock();
        }
        _source.Notification -= OnNotification;
        _source.Dispose();
    }
}

internal interface ITdpPowerNotificationSource : IDisposable
{
    event Action<TdpPowerNotification>? Notification;
    bool TryRegister(out int nativeError);
}
