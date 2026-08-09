namespace SteamInputAddonforClaw.Input;

public readonly struct AuxiliaryButtonState : IEquatable<AuxiliaryButtonState>
{
    private readonly bool[]? _pressed;

    public AuxiliaryButtonState(ReadOnlySpan<bool> pressed)
    {
        _pressed = pressed.ToArray();
    }

    public int Count => _pressed?.Length ?? 0;
    public bool this[int index] => GetPressed(index);

    public bool GetPressed(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _pressed![index];
    }

    public bool Equals(AuxiliaryButtonState other) =>
        Count == other.Count && (_pressed ?? []).AsSpan().SequenceEqual(other._pressed ?? []);

    public override bool Equals(object? obj) => obj is AuxiliaryButtonState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        foreach (var pressed in _pressed ?? []) hash.Add(pressed);
        return hash.ToHashCode();
    }

    public static bool operator ==(AuxiliaryButtonState left, AuxiliaryButtonState right) => left.Equals(right);
    public static bool operator !=(AuxiliaryButtonState left, AuxiliaryButtonState right) => !left.Equals(right);
}
