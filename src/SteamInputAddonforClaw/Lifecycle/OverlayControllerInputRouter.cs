using System.Threading.Channels;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.Lifecycle;

internal enum OverlayConsumedControlsReleaseOutcome
{
    /// <summary>No consumed control (DPad/A/B/LB/RB or either analog stick) was active when the release wait began.</summary>
    AlreadyReleased,
    /// <summary>All consumed controls were observed released after waiting.</summary>
    ReleasedAfterWait,
    /// <summary>The physical source became unavailable (real DirectInput loss) or the wait was
    /// cancelled -- the caller must NOT resume a publisher against it.</summary>
    SourceUnavailable,
}

/// <summary>OQ4 section 6/7: a narrow semantic controller-input router. It is NOT a DirectInput owner
/// and NOT a generic input framework. While Overlay capture is active it listens to the existing PR5
/// <see cref="IMsiClawPreparedInputSource.StateChanged"/>, converts button rising edges into low-rate
/// semantic Overlay actions, stops accepting actions during close, detects release of the consumed
/// controls (DPad/A/B/LB/RB and both analog sticks), and surfaces one source-unavailable signal for
/// real DirectInput loss. Controller authority stays in <c>MsiClawInputSource</c>.</summary>
internal sealed class OverlayControllerInputRouter : IDisposable
{
    // OQ5-UI-03 section 5: implementation-local, not persisted or user-configurable. A stick becomes
    // directionally active at the larger activation threshold and only re-arms once both axes fall
    // back inside the smaller neutral band -- simple hysteresis against analog jitter.
    private const int StickActivationThreshold = 16_000;
    private const int StickNeutralThreshold = 8_000;

    // OQ4 section 6.1 + OQ5-UI-02 section 6: the semantic mapping. Every entry here is also a
    // "consumed control" the section 7 release gate waits for, so adding LB/RB (PreviousTab/NextTab)
    // deliberately makes a held bumper block publisher resume until it is released.
    private static readonly (Func<GamepadButtons, bool> Held, OverlayNavigationAction Action)[] Bindings =
    [
        (b => b.DPadUp, OverlayNavigationAction.NavigateUp),
        (b => b.DPadDown, OverlayNavigationAction.NavigateDown),
        (b => b.DPadLeft, OverlayNavigationAction.NavigateLeft),
        (b => b.DPadRight, OverlayNavigationAction.NavigateRight),
        (b => b.A, OverlayNavigationAction.Accept),
        (b => b.B, OverlayNavigationAction.Back),
        (b => b.LeftBumper, OverlayNavigationAction.PreviousTab),
        (b => b.RightBumper, OverlayNavigationAction.NextTab),
    ];

    private readonly IMsiClawPreparedInputSource _source;
    private readonly Func<OverlayNavigationAction, Task> _deliver;
    private readonly Channel<OverlayNavigationAction> _actions =
        Channel.CreateUnbounded<OverlayNavigationAction>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _sync = new();
    private ControllerState _previousState;
    private bool _leftStickArmed;
    private bool _rightStickArmed;
    private TaskCompletionSource<OverlayConsumedControlsReleaseOutcome>? _releaseWaiter;
    private Task? _deliveryLoop;
    private bool _accepting;
    private bool _started;
    private bool _disposed;
    private bool _sourceUnavailable;

    internal OverlayControllerInputRouter(IMsiClawPreparedInputSource source, Func<OverlayNavigationAction, Task> deliver)
    {
        _source = source;
        _deliver = deliver;
    }

    internal void Start()
    {
        lock (_sync)
        {
            if (_started || _disposed) return;
            _started = true;
            _accepting = true;
            // Section 6.2 / OQ5-UI-03 section 4.3: a control already held -- or a stick already
            // deflected -- when capture begins must not emit an action. A stick that starts
            // outside the neutral band starts disarmed and must return to neutral before it can fire.
            _previousState = _source.LatestState;
            _leftStickArmed = IsStickNeutral(_previousState.LeftStick);
            _rightStickArmed = IsStickNeutral(_previousState.RightStick);
            _deliveryLoop = Task.Run(DeliveryLoopAsync);
        }
        _source.StateChanged += OnStateChanged;
        AppLog.Info("OverlayCapture", "Overlay input router started.", ("Event", "OverlayRouterStarted"));
    }

    internal void StopAcceptingNavigation()
    {
        lock (_sync)
        {
            if (!_accepting) return;
            _accepting = false;
            _actions.Writer.TryComplete();
        }
        AppLog.Info("OverlayCapture", "Overlay input router stopped accepting navigation.", ("Event", "OverlayRouterStopped"));
    }

    /// <summary>Section 7: complete immediately when the consumed controls are already released, otherwise await
    /// <see cref="IMsiClawPreparedInputSource.StateChanged"/> until every consumed control is
    /// released. No polling timer, no sleep loop.</summary>
    internal Task<OverlayConsumedControlsReleaseOutcome> WaitForConsumedControlsReleaseAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_sourceUnavailable || _disposed)
                return Task.FromResult(OverlayConsumedControlsReleaseOutcome.SourceUnavailable);
            if (!AnyConsumedInputActive(_previousState))
                return Task.FromResult(OverlayConsumedControlsReleaseOutcome.AlreadyReleased);
            _releaseWaiter?.TrySetResult(OverlayConsumedControlsReleaseOutcome.SourceUnavailable);
            var waiter = new TaskCompletionSource<OverlayConsumedControlsReleaseOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseWaiter = waiter;
            cancellationToken.Register(() => waiter.TrySetResult(OverlayConsumedControlsReleaseOutcome.SourceUnavailable));
            return waiter.Task;
        }
    }

    /// <summary>Section 7.1: real physical-source loss. Synchronously stop accepting navigation and
    /// complete any release waiter as <see cref="OverlayConsumedControlsReleaseOutcome.SourceUnavailable"/>
    /// -- <c>MsiClawInputSource</c> does not emit a final StateChanged after its teardown reset.</summary>
    internal void NotifySourceUnavailable()
    {
        lock (_sync)
        {
            _sourceUnavailable = true;
            _accepting = false;
            _actions.Writer.TryComplete();
            _releaseWaiter?.TrySetResult(OverlayConsumedControlsReleaseOutcome.SourceUnavailable);
            _releaseWaiter = null;
        }
        AppLog.Warn("OverlayCapture", "Overlay input router source reported unavailable.", null, ("Event", "OverlayRouterSourceUnavailable"));
    }

    private void OnStateChanged(object? sender, ControllerState state)
    {
        // Section 6.3: raised from the DirectInput poll thread. Compare state, update small in-memory
        // facts, schedule low-rate delivery. Never wait for named-pipe I/O here.
        var buttons = state.Buttons;
        lock (_sync)
        {
            if (!_disposed && _accepting)
            {
                foreach (var (held, action) in Bindings)
                    if (held(buttons) && !held(_previousState.Buttons))
                    {
                        _actions.Writer.TryWrite(action);
                        AppLog.Debug("OverlayCapture", "Navigation action.", ("Event", "OverlayNavigation"), ("Action", action));
                    }
                TryQueueStickNavigation(state.LeftStick, ref _leftStickArmed, "Left");
                TryQueueStickNavigation(state.RightStick, ref _rightStickArmed, "Right");
            }
            _previousState = state;
            if (_releaseWaiter is { } waiter && !AnyConsumedInputActive(state))
            {
                _releaseWaiter = null;
                waiter.TrySetResult(OverlayConsumedControlsReleaseOutcome.ReleasedAfterWait);
            }
        }
    }

    // OQ5-UI-03 section 4: one stick deflection emits at most one dominant-axis action. Armed only
    // while neutral; disarms on fire and re-arms once both axes are back inside the neutral band.
    private void TryQueueStickNavigation(StickState stick, ref bool armed, string which)
    {
        var x = (int)stick.X;
        var y = (int)stick.Y;
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);

        if (armed)
        {
            if (Math.Max(absX, absY) < StickActivationThreshold) return;
            // Frozen dominant-axis rule: abs(Y) >= abs(X) is vertical (tie prefers vertical).
            var action = absY >= absX
                ? (y > 0 ? OverlayNavigationAction.NavigateUp : OverlayNavigationAction.NavigateDown)
                : (x > 0 ? OverlayNavigationAction.NavigateRight : OverlayNavigationAction.NavigateLeft);
            armed = false;
            _actions.Writer.TryWrite(action);
            AppLog.Debug("OverlayCapture", "Navigation action.", ("Event", "OverlayNavigation"), ("Action", action), ("Source", which + "Stick"));
        }
        else if (absX <= StickNeutralThreshold && absY <= StickNeutralThreshold)
        {
            armed = true;
        }
    }

    private async Task DeliveryLoopAsync()
    {
        try
        {
            await foreach (var action in _actions.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try { await _deliver(action).ConfigureAwait(false); }
                catch (Exception exception) { AppLog.Warn("OverlayCapture", "Overlay navigation delivery failed.", exception, ("Action", action)); }
            }
        }
        catch (Exception exception) { AppLog.Warn("OverlayCapture", "Overlay navigation delivery loop ended unexpectedly.", exception); }
    }

    private static bool AnyConsumedHeld(GamepadButtons buttons)
    {
        foreach (var (held, _) in Bindings)
            if (held(buttons)) return true;
        return false;
    }

    private static bool IsStickNeutral(StickState stick) =>
        Math.Abs((int)stick.X) <= StickNeutralThreshold && Math.Abs((int)stick.Y) <= StickNeutralThreshold;

    // OQ5-UI-03 section 7.2: the release gate now waits for buttons AND both sticks. A stick counts
    // as active until it is back inside the neutral band, so a held stick cannot leak into the game
    // the instant Overlay capture is retired.
    private static bool AnyConsumedInputActive(ControllerState state) =>
        AnyConsumedHeld(state.Buttons) || !IsStickNeutral(state.LeftStick) || !IsStickNeutral(state.RightStick);

    public void Dispose()
    {
        Task? deliveryLoop;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _accepting = false;
            _actions.Writer.TryComplete();
            _releaseWaiter?.TrySetResult(OverlayConsumedControlsReleaseOutcome.SourceUnavailable);
            _releaseWaiter = null;
            deliveryLoop = _deliveryLoop;
        }
        _source.StateChanged -= OnStateChanged;
        try { deliveryLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        AppLog.Info("OverlayCapture", "Overlay input router disposed.", ("Event", "OverlayRouterDisposed"));
    }
}
