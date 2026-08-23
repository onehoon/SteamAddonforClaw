using System.Text.Json;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Profiles.Display;

internal sealed class GameDisplayResolutionRuntime
{
    private readonly ProfileStore _store; private readonly ProfileMutationGate _gate; private readonly DisplayResolutionService _display; private readonly string _recoveryPath; private DisplayModeSnapshot? _original;
    internal GameDisplayResolutionRuntime(ProfileStore store, ProfileMutationGate gate, string? dataRoot = null, DisplayResolutionService? display = null) { _store = store; _gate = gate; _display = display ?? new(); _recoveryPath = dataRoot is null ? AddonDataPaths.DisplayResolutionRecoveryPath : Path.Combine(dataRoot, "display-resolution-recovery.json"); }
    internal void StartupRecover() { if (!File.Exists(_recoveryPath)) return; try { var r = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(_recoveryPath)); if (r is not null && _display.TryRestore(r.Original)) File.Delete(_recoveryPath); } catch (Exception e) { AppLog.Error("Profiles.Display", "Stale Display recovery failed.", e); } }
    internal void Reconcile(uint appId) { GameProfile? profile; lock (_gate.Sync) { var loaded = _store.Load(); var key = appId.ToString(System.Globalization.CultureInfo.InvariantCulture); profile = loaded.Document.Games.TryGetValue(key, out var p) ? p : null; } var target = appId == 0 ? null : profile?.Display.Resolution; if (target is null) { if (_original is { } original && _display.TryRestore(original)) { _original = null; TryDelete(); } return; } if (_original is null) { if (!_display.TryCapture(out var current)) return; _original = current; Directory.CreateDirectory(Path.GetDirectoryName(_recoveryPath)!); File.WriteAllText(_recoveryPath, JsonSerializer.Serialize(new Recovery(current, new GameDisplayResolution { Width = target.Width, Height = target.Height }))); } if (_display.TryCapture(out var now) && (now.Width != target.Width || now.Height != target.Height)) _display.TryApply(now, target.Width, target.Height); }
    internal void Shutdown() { if (_original is { } original && _display.TryRestore(original)) { _original = null; TryDelete(); } }
    private void TryDelete() { try { if (File.Exists(_recoveryPath)) File.Delete(_recoveryPath); } catch { } }
    private sealed record Recovery(DisplayModeSnapshot Original, GameDisplayResolution Target);
}
