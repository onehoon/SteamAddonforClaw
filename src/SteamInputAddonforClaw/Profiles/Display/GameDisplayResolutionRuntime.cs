using System.Text.Json;
using System.Text.Json.Serialization;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Profiles.Display;

internal enum DisplayScaleCommitKind
{
    AppliedTo100,
    Restored,
}

internal sealed class GameDisplayResolutionRuntime
{
    private readonly ProfileStore _store;
    private readonly ProfileMutationGate _gate;
    private readonly IDisplayResolutionService _display;
    private readonly IDisplayScalingService _scaling;
    private readonly string _recoveryPath;
    private readonly Func<bool> _isBigPictureScalingRequested;
    private Func<ProfileDocument, ResolvedActiveProfile?> _activeProfileResolver = static _ => null;
    private Recovery? _recovery;
    private bool _recoveryPending;

    internal event Action<DisplayScaleCommitKind>? DisplayScaleCommitted;

    internal GameDisplayResolutionRuntime(
        ProfileStore store,
        ProfileMutationGate gate,
        string? dataRoot = null,
        IDisplayResolutionService? display = null,
        IDisplayScalingService? scaling = null,
        Func<bool>? isBigPictureScalingRequested = null)
    {
        _store = store;
        _gate = gate;
        _display = display ?? new DisplayResolutionService();
        _scaling = scaling ?? new DisplayScalingService();
        _isBigPictureScalingRequested = isBigPictureScalingRequested ?? (() => false);
        _recoveryPath = dataRoot is null
            ? AddonDataPaths.DisplayResolutionRecoveryPath
            : Path.Combine(dataRoot, "display-resolution-recovery.json");
    }

    internal void SetActiveProfileResolver(Func<ProfileDocument, ResolvedActiveProfile?> resolver)
        => _activeProfileResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    internal void StartupRecover()
    {
        if (!File.Exists(_recoveryPath)) return;
        try
        {
            _recovery = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(_recoveryPath));
            if (_recovery is null)
            {
                _recoveryPending = true;
                AppLog.Error("Profiles.Display", "Stale display recovery data was empty; new display overrides are blocked.", null);
                return;
            }

            _recoveryPending = true;
            if (TryRestoreOwnedComponents(_recovery, finalResolution: null, out var updated))
            {
                _recovery = updated;
                TryClearRecovery();
            }
            else
            {
                _recovery = updated;
                TrySaveRecovery(updated);
                AppLog.Error("Profiles.Display", "Stale display recovery failed; new display overrides are blocked.", null);
            }
        }
        catch (Exception exception)
        {
            _recoveryPending = true;
            AppLog.Error("Profiles.Display", "Stale display recovery failed; new display overrides are blocked.", exception);
        }
    }

    internal bool Reconcile()
    {
        if (_recoveryPending && !RetryPendingRecovery()) return false;

        ResolvedActiveProfile? active;
        lock (_gate.Sync)
        {
            var loaded = _store.Load();
            active = loaded.CanSafelyReplace ? _activeProfileResolver(loaded.Document) : null;
        }

        var target = active?.Display.Resolution;
        var profileScaling = active?.Display.ScalingTo100Percent == true;
        var scalingEnabled = profileScaling || _isBigPictureScalingRequested();
        if (target is null && !scalingEnabled)
            return _recovery is null || RestoreAndRetireRecovery();

        if (target is null && scalingEnabled
            && _recovery is { OwnsResolution: true } previous
            && (!previous.OwnsScaling
                || (previous.ScaleCaptureMode is { } scaleMode
                    && previous.Original is { } originalMode
                    && !SameMode(scaleMode, originalMode)))
            && !RestoreAndRetireRecovery())
            return false;

        if (!TryAcquireNewComponents(target, scalingEnabled, active?.TargetLabel, out var recovery))
            return false;
        _recovery = recovery;
        recovery = recovery with
        {
            Target = target is null ? null : new GameDisplayResolution { Width = target.Width, Height = target.Height },
        };
        if (!TrySaveRecovery(recovery)) return false;
        _recovery = recovery;

        if (!scalingEnabled && recovery.OwnsScaling)
        {
            DisplayModeSnapshot? finalMode = target is null ? null : new DisplayModeSnapshot(target.Width, target.Height, 0, 0);
            var scaleRestored = TryRestoreScale(recovery, finalMode, out var restoredRecovery);
            recovery = restoredRecovery;
            if (recovery.ScalingOwned == false && !TrySaveRecovery(recovery)) return false;
            _recovery = recovery;
            if (!scaleRestored)
            {
                SetRecoveryPending(recovery, "Captured display scaling could not be restored safely.");
                return false;
            }
        }

        if (target is null && recovery.OwnsResolution)
        {
            if (!TryRestoreResolution(recovery, out recovery))
            {
                SetRecoveryPending(recovery, "Captured display resolution could not be restored.");
                return false;
            }
            if (!TrySaveRecovery(recovery)) return false;
            _recovery = recovery;
        }
        else if (target is not null && recovery.OwnsResolution
            && !TryApplyResolution(target.Width, target.Height, active?.TargetLabel))
        {
            AppLog.Warn("Profiles.Display", "Display resolution apply failed; restoring owned display changes.", null,
                ("ProfileTarget", active?.TargetLabel));
            return FailClosed();
        }

        if (scalingEnabled && recovery.OwnsScaling && recovery.OriginalScale is { } originalScale)
        {
            if (!_scaling.TrySetTo100(originalScale))
            {
                AppLog.Warn("Profiles.Display", "Display scaling apply failed; restoring owned display changes.", null,
                    ("ProfileTarget", active?.TargetLabel));
                return FailClosed();
            }

            NotifyDisplayScaleCommitted(DisplayScaleCommitKind.AppliedTo100);
        }

        if (!recovery.OwnsResolution && !recovery.OwnsScaling)
            return TryClearRecovery();
        return true;
    }

    internal void Shutdown()
    {
        if (_recovery is not null)
            RestoreAndRetireRecovery();
    }

    private bool TryAcquireNewComponents(
        GameDisplayResolution? target,
        bool scalingEnabled,
        string? profileTarget,
        out Recovery recovery)
    {
        recovery = _recovery ?? new Recovery();
        var needsResolution = target is not null && !recovery.OwnsResolution;
        var needsScaling = scalingEnabled && !recovery.OwnsScaling;
        if (!needsResolution && !needsScaling) return true;

        DisplayModeSnapshot? originalResolution = null;
        DisplayScaleSnapshot? originalScale = null;
        DisplayModeSnapshot? scaleCaptureMode = null;
        var currentMode = default(DisplayModeSnapshot);
        if ((needsResolution || needsScaling) && !_display.TryCapture(out currentMode))
        {
            AppLog.Warn("Profiles.Display", "Current display mode could not be captured before acquiring a profile override.", null,
                ("ProfileTarget", profileTarget));
            return false;
        }
        else if (needsResolution || needsScaling)
        {
            if (needsResolution) originalResolution = currentMode;
            if (needsScaling)
            {
                scaleCaptureMode = currentMode;
                if (!_scaling.TryCapture(out var capturedScale))
                {
                    AppLog.Warn("Profiles.Display", "Current primary display scaling could not be captured before applying a profile override.", null,
                        ("ProfileTarget", profileTarget));
                    return false;
                }
                originalScale = capturedScale;
            }
        }

        recovery = recovery with
        {
            Original = originalResolution ?? recovery.Original,
            ResolutionOwned = needsResolution ? true : recovery.ResolutionOwned,
            OriginalScale = originalScale ?? recovery.OriginalScale,
            ScaleCaptureMode = scaleCaptureMode ?? recovery.ScaleCaptureMode,
            ScalingOwned = needsScaling ? true : recovery.ScalingOwned,
        };
        if (!TrySaveRecovery(recovery)) return false;
        _recovery = recovery;
        return true;
    }

    private bool TryApplyResolution(int width, int height, string? profileTarget)
    {
        if (!_display.TryCapture(out var current))
        {
            AppLog.Warn("Profiles.Display", "Display mode capture failed after recovery persistence.", null,
                ("ProfileTarget", profileTarget));
            return false;
        }
        return current.Width == width && current.Height == height || _display.TryApply(current, width, height);
    }

    private bool TryRestoreResolution(Recovery recovery, out Recovery updated)
    {
        updated = recovery;
        if (!recovery.OwnsResolution || recovery.Original is not { } original) return true;
        if (!_display.TryRestore(original)) return false;
        updated = recovery with { ResolutionOwned = false };
        return true;
    }

    private bool TryRestoreScale(Recovery recovery, DisplayModeSnapshot? finalResolution, out Recovery updated)
    {
        updated = recovery;
        if (!recovery.OwnsScaling || recovery.OriginalScale is not { } originalScale) return true;
        if (!_display.TryCapture(out var currentMode)) return false;

        var modeMatches = recovery.ScaleCaptureMode is not { } captureMode
            || SameMode(currentMode, captureMode);
        if (!modeMatches && !recovery.OwnsResolution)
        {
            if (!_scaling.TryCapture(out var currentScale)
                || !SameDisplay(currentScale, originalScale)
                || currentScale.OriginalRelativeScale != originalScale.OriginalRelativeScale)
                return false;
            updated = recovery with { ScalingOwned = false };
            NotifyDisplayScaleCommitted(DisplayScaleCommitKind.Restored);
            return true;
        }

        if (!modeMatches && recovery.ScaleCaptureMode is { } requiredMode)
        {
            if (!_display.TryApply(currentMode, requiredMode.Width, requiredMode.Height)) return false;
        }

        var scaleRestored = _scaling.TryRestore(originalScale);
        if (scaleRestored)
        {
            updated = recovery with { ScalingOwned = false };
            NotifyDisplayScaleCommitted(DisplayScaleCommitKind.Restored);
        }

        if (finalResolution is { } returnMode
            && (!TryCaptureMode(out var afterScale)
                || !SameDimensions(afterScale, returnMode)
                    && !_display.TryApply(afterScale, returnMode.Width, returnMode.Height)))
            return false;

        return scaleRestored;
    }

    private bool TryRestoreOwnedComponents(Recovery recovery, DisplayModeSnapshot? finalResolution, out Recovery updated)
    {
        updated = recovery;
        var ownsResolution = recovery.OwnsResolution && recovery.Original is not null;
        var ownsScaling = recovery.OwnsScaling && recovery.OriginalScale is not null;

        if (ownsResolution && ownsScaling && recovery.ScaleCaptureMode is { } scaleMode
            && recovery.Original is { } original && !SameMode(scaleMode, original))
        {
            if (!TryRestoreScale(updated, finalResolution: null, out var afterScale)) return false;
            updated = afterScale;
            if (!TryRestoreResolution(updated, out var afterResolution)) return false;
            updated = afterResolution;
            return true;
        }

        if (ownsResolution)
        {
            if (!TryRestoreResolution(updated, out var afterResolution)) return false;
            updated = afterResolution;
        }
        if (ownsScaling)
        {
            if (!TryRestoreScale(updated, finalResolution, out var afterScale)) return false;
            updated = afterScale;
        }
        return !updated.OwnsResolution && !updated.OwnsScaling;
    }

    private bool RestoreAndRetireRecovery()
    {
        if (_recovery is not { } recovery) return TryClearRecovery();
        if (!TryRestoreOwnedComponents(recovery, finalResolution: null, out var updated))
        {
            _recovery = updated;
            TrySaveRecovery(updated);
            _recoveryPending = true;
            return false;
        }
        _recovery = updated;
        return TryClearRecovery();
    }

    private bool RetryPendingRecovery()
    {
        if (_recovery is null && File.Exists(_recoveryPath))
        {
            try { _recovery = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(_recoveryPath)); }
            catch (Exception exception)
            {
                AppLog.Error("Profiles.Display", "Pending display recovery could not be read; profile overrides remain blocked.", exception);
                return false;
            }
        }
        if (_recovery is null) return false;
        if (!TryRestoreOwnedComponents(_recovery, finalResolution: null, out var updated))
        {
            _recovery = updated;
            TrySaveRecovery(updated);
            return false;
        }
        _recovery = updated;
        if (!TryClearRecovery()) return false;
        _recoveryPending = false;
        return true;
    }

    private bool FailClosed()
    {
        _recoveryPending = true;
        RestoreAndRetireRecovery();
        return false;
    }

    private void SetRecoveryPending(Recovery recovery, string message)
    {
        _recovery = recovery;
        _recoveryPending = true;
        TrySaveRecovery(recovery);
        AppLog.Warn("Profiles.Display", message, null);
    }

    private bool TrySaveRecovery(Recovery recovery)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recoveryPath)!);
            File.WriteAllText(_recoveryPath, JsonSerializer.Serialize(recovery));
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Profiles.Display", "Display recovery state could not be persisted; no new display mutation was authorized.", exception);
            return false;
        }
    }

    private bool TryClearRecovery()
    {
        try
        {
            if (File.Exists(_recoveryPath)) File.Delete(_recoveryPath);
            _recovery = null;
            _recoveryPending = false;
            return true;
        }
        catch (Exception exception)
        {
            _recoveryPending = true;
            AppLog.Error("Profiles.Display", "Restored display recovery state could not be retired; new display overrides remain blocked.", exception);
            return false;
        }
    }

    private void NotifyDisplayScaleCommitted(DisplayScaleCommitKind change)
    {
        try { DisplayScaleCommitted?.Invoke(change); }
        catch (Exception exception)
        {
            AppLog.Warn("Profiles.Display", "Display scale notification callback failed; the display operation remains committed.", exception,
                ("Change", change));
        }
    }

    private bool TryCaptureMode(out DisplayModeSnapshot current)
        => _display.TryCapture(out current);

    private static bool SameDisplay(DisplayScaleSnapshot left, DisplayScaleSnapshot right)
        => string.Equals(left.PrimaryGdiDeviceName, right.PrimaryGdiDeviceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.MonitorDevicePath, right.MonitorDevicePath, StringComparison.OrdinalIgnoreCase);

    private static bool SameMode(DisplayModeSnapshot left, DisplayModeSnapshot right)
        => left == right;

    private static bool SameDimensions(DisplayModeSnapshot left, DisplayModeSnapshot right)
        => left.Width == right.Width && left.Height == right.Height;

    private sealed record Recovery
    {
        public DisplayModeSnapshot? Original { get; init; }
        public GameDisplayResolution? Target { get; init; }
        public DisplayScaleSnapshot? OriginalScale { get; init; }
        public DisplayModeSnapshot? ScaleCaptureMode { get; init; }
        public bool? ResolutionOwned { get; init; }
        public bool? ScalingOwned { get; init; }
        [JsonIgnore] public bool OwnsResolution => ResolutionOwned ?? Original is not null;
        [JsonIgnore] public bool OwnsScaling => ScalingOwned ?? OriginalScale is not null;
    }
}
