using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawRawButtonTransitionCollector
{
    internal const int MaximumTrackedButtons = 64;
    internal const int MaximumEventLogLines = 32;

    private readonly object _sync = new();
    private readonly TimeSpan _duration;
    private readonly long _startedAt;
    private readonly TaskCompletionSource<FrontendRawDirectInputButtonTransitionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool[]? _previous;
    private int[]? _risingEdges;
    private int[]? _fallingEdges;
    private int _buttonCount;
    private int _successfulReadCount;
    private bool _button15ChangedWhile16Released;
    private bool _button16ChangedWhile15Released;
    private bool _completed;

    internal MsiClawRawButtonTransitionCollector(TimeSpan duration, long startedAt)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _duration = duration;
        _startedAt = startedAt;
    }

    internal Task<FrontendRawDirectInputButtonTransitionResult> Completion => _completion.Task;

    internal void Observe(IReadOnlyList<bool> buttons, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        lock (_sync)
        {
            if (_completed) return;
            if (buttons.Count < MsiClawHardware.RequiredDirectInputButtonCount
                || (_previous is not null && buttons.Count != _buttonCount))
            {
                CompleteLocked(FrontendRawDirectInputButtonTransitionOutcome.InvalidButtonLayout,
                    "InvalidButtonLayout", timestamp);
                return;
            }

            if (_previous is null)
            {
                _buttonCount = buttons.Count;
                var tracked = Math.Min(buttons.Count, MaximumTrackedButtons);
                _previous = new bool[tracked];
                _risingEdges = new int[tracked];
                _fallingEdges = new int[tracked];
                for (var index = 0; index < tracked; index++) _previous[index] = buttons[index];
                _successfulReadCount = 1;
            }
            else
            {
                _successfulReadCount++;
                for (var index = 0; index < _previous.Length; index++)
                {
                    var wasPressed = _previous[index];
                    var isPressed = buttons[index];
                    if (wasPressed == isPressed) continue;

                    if (isPressed) _risingEdges![index]++;
                    else _fallingEdges![index]++;

                    if (index == MsiClawHardware.M1DirectInputButtonIndex
                        && !wasPressed && !buttons[MsiClawHardware.M2DirectInputButtonIndex])
                        _button15ChangedWhile16Released = true;
                    if (index == MsiClawHardware.M2DirectInputButtonIndex
                        && !wasPressed && !buttons[MsiClawHardware.M1DirectInputButtonIndex])
                        _button16ChangedWhile15Released = true;
                    if (index == MsiClawHardware.M1DirectInputButtonIndex
                        && wasPressed && !isPressed && !buttons[MsiClawHardware.M2DirectInputButtonIndex])
                        _button15ChangedWhile16Released = true;
                    if (index == MsiClawHardware.M2DirectInputButtonIndex
                        && wasPressed && !isPressed && !buttons[MsiClawHardware.M1DirectInputButtonIndex])
                        _button16ChangedWhile15Released = true;
                }

                for (var index = 0; index < _previous.Length; index++) _previous[index] = buttons[index];
            }

            if (Stopwatch.GetElapsedTime(_startedAt, timestamp) >= _duration)
            {
                var hasTransitions = _risingEdges!.Any(count => count > 0) || _fallingEdges!.Any(count => count > 0);
                CompleteLocked(
                    hasTransitions
                        ? FrontendRawDirectInputButtonTransitionOutcome.Completed
                        : FrontendRawDirectInputButtonTransitionOutcome.NoRawButtonTransitionsObserved,
                    hasTransitions ? "Completed" : "NoRawButtonTransitionsObserved",
                    timestamp);
            }
        }
    }

    internal void Cancel(long timestamp) => Complete(
        FrontendRawDirectInputButtonTransitionOutcome.Cancelled, "Cancelled", timestamp);

    internal void EndSession(long timestamp) => Complete(
        FrontendRawDirectInputButtonTransitionOutcome.SessionEnded, "OwnedDirectInputSessionEnded", timestamp);

    internal void FailInvalidLayout(long timestamp) => Complete(
        FrontendRawDirectInputButtonTransitionOutcome.InvalidButtonLayout, "InvalidButtonLayout", timestamp);

    private void Complete(FrontendRawDirectInputButtonTransitionOutcome outcome, string status, long timestamp)
    {
        lock (_sync) CompleteLocked(outcome, status, timestamp);
    }

    private void CompleteLocked(FrontendRawDirectInputButtonTransitionOutcome outcome, string status, long timestamp)
    {
        if (_completed) return;
        _completed = true;
        var transitions = _previous is null
            ? Array.Empty<FrontendRawDirectInputButtonIndexTransition>()
            : Enumerable.Range(0, _previous.Length)
                .Where(index => _risingEdges![index] > 0 || _fallingEdges![index] > 0)
                .Select(index => new FrontendRawDirectInputButtonIndexTransition(
                    index, _risingEdges![index], _fallingEdges![index]))
                .ToArray();
        var requestedMs = (long)_duration.TotalMilliseconds;
        var actualMs = Math.Max(0, (long)Stopwatch.GetElapsedTime(_startedAt, Math.Max(_startedAt, timestamp)).TotalMilliseconds);
        var m1 = MsiClawHardware.M1DirectInputButtonIndex;
        var m2 = MsiClawHardware.M2DirectInputButtonIndex;
        var result = new FrontendRawDirectInputButtonTransitionResult(
            outcome,
            status,
            requestedMs,
            actualMs,
            _previous is null ? null : _buttonCount,
            _previous?.Length ?? 0,
            _successfulReadCount,
            transitions,
            HasPressAndRelease(m1),
            HasPressAndRelease(m2),
            _button15ChangedWhile16Released,
            _button16ChangedWhile15Released);
        _completion.TrySetResult(result);
    }

    private bool HasPressAndRelease(int index) =>
        _risingEdges is not null && _fallingEdges is not null
        && index < _risingEdges.Length
        && _risingEdges[index] > 0 && _fallingEdges[index] > 0;
}
