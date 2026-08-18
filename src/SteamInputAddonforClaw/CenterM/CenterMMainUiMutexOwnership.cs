using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal enum CenterMMainUiMutexAcquireResult
{
    Acquired,
    AlreadyOwnedByThis,
    Unavailable
}

/// <summary>Owned handle to a named OS mutex. Isolated behind this interface (rather than exposing
/// <see cref="Mutex"/> directly) so tests can exercise <see cref="CenterMMainUiMutexOwnership"/>
/// without creating a real, globally named OS mutex that could collide across parallel CI runs or
/// with a real MSI Center M MainUI on the test machine.</summary>
internal interface ICenterMMainUiMutexHandle : IDisposable
{
    void ReleaseMutex();
}

/// <summary>Creates (never opens/steals) a named mutex, reporting whether this call was the one
/// that actually created the underlying kernel object.</summary>
internal interface ICenterMMainUiMutexFactory
{
    (ICenterMMainUiMutexHandle Handle, bool CreatedNew) Create(string name);
}

internal sealed class Win32CenterMMainUiMutexFactory : ICenterMMainUiMutexFactory
{
    public (ICenterMMainUiMutexHandle Handle, bool CreatedNew) Create(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        return (new RealHandle(mutex), createdNew);
    }

    private sealed class RealHandle(Mutex mutex) : ICenterMMainUiMutexHandle
    {
        public void ReleaseMutex()
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException)
            {
                // Not held by this thread (e.g. a prior release already ran) -- nothing further to
                // release. Never treated as a failure: the goal (this thread no longer holds it) is
                // already satisfied.
            }
        }

        public void Dispose() => mutex.Dispose();
    }
}

/// <summary>
/// Transient ownership of the real MSI Center M MainUI's own duplicate-instance guard resource,
/// exactly <c>Local\MSI Center M.exe</c> (research: MSI_Center_M.App.IsAlreadyRunning constructs
/// this from "Local\\" + the running executable's own filename). While this Addon holds it, a real
/// MainUI's own <c>new Mutex(true, "Local\\MSI Center M.exe", out createdNew)</c> observes
/// <c>createdNew == false</c> and exits before <c>MainWindow</c>/controller-mode initialization --
/// this class never opens or waits on a foreign holder's mutex, and never reinterprets <c>Local\</c>
/// as a filesystem path. Idempotent acquire/release; deterministic disposal; a process crash lets the
/// OS close the handle and release the mutex naturally (fail-open), matching the same crash-safety
/// philosophy as <see cref="CenterMHelperOwnership"/>.
/// </summary>
internal sealed class CenterMMainUiMutexOwnership(ICenterMMainUiMutexFactory? factory = null) : IDisposable
{
    internal const string MutexName = @"Local\MSI Center M.exe";

    private readonly ICenterMMainUiMutexFactory _factory = factory ?? new Win32CenterMMainUiMutexFactory();
    private readonly object _sync = new();
    private ICenterMMainUiMutexHandle? _handle;

    internal bool IsOwned { get { lock (_sync) return _handle is not null; } }

    /// <summary>Idempotent: a second call while already owned by this instance returns
    /// <see cref="CenterMMainUiMutexAcquireResult.AlreadyOwnedByThis"/> without any further native
    /// call. Never steals or abandons a mutex an unrelated holder (the real MainUI, or a stale/
    /// foreign Addon instance) already owns -- that case is reported as
    /// <see cref="CenterMMainUiMutexAcquireResult.Unavailable"/>, and this instance's own handle
    /// (from the failed create attempt) is disposed rather than retained.</summary>
    internal CenterMMainUiMutexAcquireResult Acquire()
    {
        lock (_sync)
        {
            if (_handle is not null) return CenterMMainUiMutexAcquireResult.AlreadyOwnedByThis;

            (ICenterMMainUiMutexHandle Handle, bool CreatedNew) created;
            try
            {
                created = _factory.Create(MutexName);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                AppLog.Warn("CenterM.RoutingGuard", "MainUI mutex creation failed.", ex);
                return CenterMMainUiMutexAcquireResult.Unavailable;
            }

            if (!created.CreatedNew)
            {
                created.Handle.Dispose();
                AppLog.Warn("CenterM.RoutingGuard", "MainUI mutex already exists; not stealing an unrelated holder's ownership.", null);
                return CenterMMainUiMutexAcquireResult.Unavailable;
            }

            _handle = created.Handle;
            return CenterMMainUiMutexAcquireResult.Acquired;
        }
    }

    /// <summary>Idempotent: a no-op returning true when nothing is owned.</summary>
    internal bool Release()
    {
        lock (_sync)
        {
            if (_handle is null) return true;
            _handle.ReleaseMutex();
            _handle.Dispose();
            _handle = null;
            return true;
        }
    }

    public void Dispose() => Release();
}
