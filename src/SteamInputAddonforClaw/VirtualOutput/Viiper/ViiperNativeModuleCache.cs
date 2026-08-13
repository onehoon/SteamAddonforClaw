using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// Loads libVIIPER.dll at most once per normalized absolute path for the lifetime of the
/// process and never unloads it. Freeing a Go c-shared module at runtime is unsafe/racy,
/// so once a path is pinned it stays loaded until the OS tears the process down.
/// </summary>
internal static class ViiperNativeModuleCache
{
    private static readonly ConcurrentDictionary<string, Lazy<nint>> Modules = new(StringComparer.OrdinalIgnoreCase);

    internal static nint GetOrLoad(string absolutePath, Func<string, nint>? loader = null)
    {
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("VIIPER must be loaded from an absolute path.", nameof(absolutePath));
        var key = Normalize(absolutePath);
        var load = loader ?? NativeLibrary.Load;
        var candidate = new Lazy<nint>(() => load(key), LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = Modules.GetOrAdd(key, candidate);
        var inserted = ReferenceEquals(lazy, candidate);

        try
        {
            var handle = lazy.Value;
            AppLog.Debug("Viiper", inserted ? "Native module loaded and pinned for process lifetime." : "Reused already-pinned native module.");
            return handle;
        }
        catch
        {
            Modules.TryRemove(new KeyValuePair<string, Lazy<nint>>(key, lazy));
            throw;
        }
    }

    private static string Normalize(string absolutePath) => Path.GetFullPath(absolutePath);
}
