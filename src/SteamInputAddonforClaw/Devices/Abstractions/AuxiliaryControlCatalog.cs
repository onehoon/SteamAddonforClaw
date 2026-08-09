namespace SteamInputAddonforClaw.Devices.Abstractions;

public sealed class AuxiliaryControlCatalog
{
    private readonly AuxiliaryControlDescriptor[] _controls;
    private readonly Dictionary<AuxiliaryControlId, int> _indices;

    public AuxiliaryControlCatalog(IEnumerable<AuxiliaryControlDescriptor> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        _controls = controls.ToArray();
        if (_controls.Any(control => control is null)) throw new ArgumentException("Auxiliary controls cannot contain null entries.", nameof(controls));

        _indices = new Dictionary<AuxiliaryControlId, int>(_controls.Length);
        for (var index = 0; index < _controls.Length; index++)
        {
            if (!_indices.TryAdd(_controls[index].Id, index))
                throw new ArgumentException($"Duplicate auxiliary control ID '{_controls[index].Id}'.", nameof(controls));
        }
    }

    public int Count => _controls.Length;
    public AuxiliaryControlDescriptor this[int index] => _controls[index];
    public IReadOnlyList<AuxiliaryControlDescriptor> Controls => Array.AsReadOnly(_controls);

    public bool TryGetIndex(AuxiliaryControlId id, out int index) => _indices.TryGetValue(id, out index);

    public int GetIndex(AuxiliaryControlId id) =>
        _indices.TryGetValue(id, out var index)
            ? index
            : throw new KeyNotFoundException($"Auxiliary control '{id}' is not registered.");
}
