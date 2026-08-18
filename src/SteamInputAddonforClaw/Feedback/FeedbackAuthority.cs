namespace SteamInputAddonforClaw.Feedback;

internal readonly record struct FeedbackAuthorityToken(long Generation, string Source);

internal sealed class FeedbackAuthority
{
    private readonly object _gate = new();
    private long _generation;
    private string? _source;
    private int _activeLeases;

    internal FeedbackAuthorityToken Acquire(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        lock (_gate)
        {
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
        RevokeAndDrain();
    }

    internal void RevokeAndDrain()
    {
        lock (_gate)
        {
            _generation++;
            _source = null;
            while (_activeLeases != 0)
                Monitor.Wait(_gate);
        }
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
