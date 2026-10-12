using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Touchpad;

internal sealed class TouchpadContactHandoff
{
    private readonly object _sync = new();
    private SteamDeckRightPadContact _current;
    private SteamDeckRightPadContact _pendingTap;
    private bool _isDown;
    private bool _currentPublished;

    internal void Down(double normalizedX, double normalizedY)
    {
        lock (_sync)
        {
            if (_isDown)
                return;

            _current = SteamDeckRightPadContact.FromNormalizedPosition(true, normalizedX, normalizedY);
            _pendingTap = SteamDeckRightPadContact.Neutral;
            _isDown = _current.IsTouched;
            _currentPublished = false;
        }
    }

    internal void Move(double normalizedX, double normalizedY)
    {
        lock (_sync)
        {
            if (_isDown)
                _current = SteamDeckRightPadContact.FromNormalizedPosition(true, normalizedX, normalizedY);
        }
    }

    internal void Up()
    {
        lock (_sync)
        {
            if (!_isDown)
                return;

            if (!_currentPublished)
                _pendingTap = _current;
            _isDown = false;
            _current = SteamDeckRightPadContact.Neutral;
            _currentPublished = false;
        }
    }

    internal void Cancel() => Clear();

    internal SteamDeckRightPadContact ReadForPublisherTick(bool admitted)
    {
        lock (_sync)
        {
            if (!admitted)
            {
                ClearUnderLock();
                return SteamDeckRightPadContact.Neutral;
            }

            if (_isDown)
            {
                _currentPublished = true;
                return _current;
            }

            if (_pendingTap.IsTouched)
            {
                var pending = _pendingTap;
                _pendingTap = SteamDeckRightPadContact.Neutral;
                return pending;
            }

            return SteamDeckRightPadContact.Neutral;
        }
    }

    internal bool HasContact
    {
        get { lock (_sync) return _isDown || _pendingTap.IsTouched; }
    }

    internal void Clear()
    {
        lock (_sync)
            ClearUnderLock();
    }

    private void ClearUnderLock()
    {
        _current = SteamDeckRightPadContact.Neutral;
        _pendingTap = SteamDeckRightPadContact.Neutral;
        _isDown = false;
        _currentPublished = false;
    }
}

internal readonly record struct TouchpadPixelRect(int X, int Y, int Width, int Height)
{
    internal int Right => checked(X + Width);
    internal int Bottom => checked(Y + Height);
    internal bool Contains(int x, int y) => Width > 0 && Height > 0 && x >= X && y >= Y && x < Right && y < Bottom;

    internal static TouchpadPixelRect BottomRightQuarter(TouchpadPixelRect primary)
    {
        if (primary.Width <= 0 || primary.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(primary));
        var left = primary.Width / 2;
        var top = primary.Height / 2;
        return new TouchpadPixelRect(primary.X + left, primary.Y + top,
            primary.Width - left, primary.Height - top);
    }
}

internal sealed class TouchpadPointerContactTracker(TouchpadContactHandoff handoff)
{
    private uint? _activePointerId;
    internal bool HasContact => _activePointerId is not null;
    internal bool Owns(uint pointerId) => _activePointerId == pointerId;

    internal bool Down(uint pointerId, TouchpadPixelRect region, int rawX, int rawY)
    {
        if (_activePointerId is not null || !region.Contains(rawX, rawY))
            return false;
        _activePointerId = pointerId;
        handoff.Down((rawX - region.X) / (double)region.Width, (rawY - region.Y) / (double)region.Height);
        return true;
    }

    internal bool Move(uint pointerId, TouchpadPixelRect region, int rawX, int rawY)
    {
        if (_activePointerId != pointerId)
            return false;
        handoff.Move((rawX - region.X) / (double)region.Width, (rawY - region.Y) / (double)region.Height);
        return true;
    }

    internal bool End(uint pointerId, bool canceled)
    {
        if (_activePointerId != pointerId)
            return false;
        _activePointerId = null;
        if (canceled)
            handoff.Cancel();
        else
            handoff.Up();
        return true;
    }

    internal void Clear()
    {
        _activePointerId = null;
        handoff.Clear();
    }
}

internal readonly record struct TouchpadAdmissionFacts(
    bool PreferenceEnabled,
    bool AddonOwnsController,
    bool PhysicalInputLive,
    bool SteamGameRunning,
    bool ForegroundIsSteamGame,
    bool SteamDeckPresentationLive,
    bool OverlayPausedOrVisible,
    bool SuspendPaused,
    bool ShuttingDown)
{
    internal bool ShouldArm => PreferenceEnabled
        && AddonOwnsController
        && PhysicalInputLive
        && SteamGameRunning
        && ForegroundIsSteamGame
        && SteamDeckPresentationLive
        && !OverlayPausedOrVisible
        && !SuspendPaused
        && !ShuttingDown;
}
