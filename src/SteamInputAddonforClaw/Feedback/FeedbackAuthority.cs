namespace SteamInputAddonforClaw.Feedback;

internal readonly record struct FeedbackAuthorityToken(long Generation, string Source);

internal sealed class FeedbackAuthority
{
    private readonly object _gate = new();
    private long _generation;
    private string? _source;
    private int _activeLeases;
    private bool _revocationInProgress;

    internal bool IsRevocationInProgress
    {
        get { lock (_gate) return _revocationInProgress; }
    }

    internal FeedbackAuthorityToken Acquire(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        lock (_gate)
        {
            while (_revocationInProgress)
                Monitor.Wait(_gate);
            _generation++;
            _source = source;
            return new FeedbackAuthorityToken(_generation, source);
        }
    }

    internal bool IsCurrent(FeedbackAuthorityToken token)
    {
        lock (_gate)
            return token.Generation == _generation && token.Generation != 0 && token.Source == _source;
    }

    internal bool TryAcquireLease(FeedbackAuthorityToken token, out FeedbackAuthorityLease? lease)
    {
        lock (_gate)
        {
            if (token.Generation != _generation || token.Generation == 0 || token.Source != _source)
            {
                lease = null;
                return false;
            }

            _activeLeases++;
            lease = new FeedbackAuthorityLease(this, token);
            return true;
        }
    }

    internal void Revoke()
    {
        lock (_gate)
            BeginRevocationLocked();
    }

    internal void RevokeAndDrain()
    {
        lock (_gate)
        {
            BeginRevocationLocked();
            while (_activeLeases != 0)
                Monitor.Wait(_gate);
            _revocationInProgress = false;
            Monitor.PulseAll(_gate);
        }
    }

    private void BeginRevocationLocked()
    {
        if (_revocationInProgress) return;
        _revocationInProgress = true;
        _generation++;
        _source = null;
    }

    private void ReleaseLease()
    {
        lock (_gate)
        {
            _activeLeases--;
            if (_activeLeases == 0) Monitor.PulseAll(_gate);
        }
    }

    internal sealed class FeedbackAuthorityLease : IDisposable
    {
        private readonly FeedbackAuthority _owner;
        private int _released;

        internal FeedbackAuthorityLease(FeedbackAuthority owner, FeedbackAuthorityToken token)
        {
            _owner = owner;
            Token = token;
        }

        internal FeedbackAuthorityToken Token { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _owner.ReleaseLease();
        }
    }
}
