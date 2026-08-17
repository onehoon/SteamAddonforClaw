namespace SteamInputAddonforClaw.Feedback;

internal readonly record struct FeedbackAuthorityToken(long Generation, string Source);

internal sealed class FeedbackAuthority
{
    private readonly object _gate = new();
    private long _generation;
    private string? _source;

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

    internal void Revoke()
    {
        lock (_gate)
        {
            _generation++;
            _source = null;
        }
    }
}
