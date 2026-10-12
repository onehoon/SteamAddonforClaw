namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>One sampled contact for the Steam Deck's independent right trackpad.</summary>
internal readonly record struct SteamDeckRightPadContact(bool IsTouched, short X, short Y)
{
    private const double MaximumAxis = 32767.0;

    internal static SteamDeckRightPadContact Neutral => default;

    /// <summary>
    /// Maps a local top-left-to-bottom-right normalized point to the signed Deck pad range.
    /// Positive Y at the top is the initial convention and still requires Steam Input field validation.
    /// </summary>
    internal static SteamDeckRightPadContact FromNormalizedPosition(
        bool isTouched,
        double normalizedX,
        double normalizedY)
    {
        if (!isTouched || !double.IsFinite(normalizedX) || !double.IsFinite(normalizedY))
            return Neutral;

        return new SteamDeckRightPadContact(
            IsTouched: true,
            X: EncodePadX(normalizedX),
            Y: EncodePadY(normalizedY));
    }

    private static short EncodePadX(double normalizedX)
    {
        var clamped = Math.Clamp(normalizedX, 0.0, 1.0);
        return (short)Math.Round((clamped * 2.0 - 1.0) * MaximumAxis, MidpointRounding.AwayFromZero);
    }

    private static short EncodePadY(double normalizedY)
    {
        var clamped = Math.Clamp(normalizedY, 0.0, 1.0);
        return (short)Math.Round((1.0 - clamped * 2.0) * MaximumAxis, MidpointRounding.AwayFromZero);
    }
}
