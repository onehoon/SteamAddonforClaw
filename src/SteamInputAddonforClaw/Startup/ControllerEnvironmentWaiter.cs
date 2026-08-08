using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Startup;

internal interface IControllerEnvironmentWaiter
{
    Task WaitUntilStableAsync(CancellationToken cancellationToken);
}

internal sealed class ControllerEnvironmentWaiter : IControllerEnvironmentWaiter
{
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly ControllerDeviceClassifier _classifier;
    private readonly int _requiredStableSnapshots;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _timeout;

    public ControllerEnvironmentWaiter(
        IControllerDeviceEnumerator deviceEnumerator,
        ControllerDeviceClassifier classifier,
        int requiredStableSnapshots = 3,
        TimeSpan? sampleInterval = null,
        TimeSpan? timeout = null)
    {
        _deviceEnumerator = deviceEnumerator;
        _classifier = classifier;
        _requiredStableSnapshots = requiredStableSnapshots;
        _sampleInterval = sampleInterval ?? TimeSpan.FromMilliseconds(350);
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task WaitUntilStableAsync(CancellationToken cancellationToken)
    {
        string? previousSnapshot = null;
        var stableSnapshotCount = 0;
        var deadline = DateTimeOffset.UtcNow + _timeout;

        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = CreateRelevantTopologySnapshot();
            stableSnapshotCount = snapshot == previousSnapshot ? stableSnapshotCount + 1 : 1;
            if (stableSnapshotCount >= _requiredStableSnapshots)
            {
                return;
            }

            previousSnapshot = snapshot;
            await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private string CreateRelevantTopologySnapshot()
    {
        return string.Join('\n', _deviceEnumerator.EnumeratePresentDevices()
            .Where(_classifier.IsRelevantTopologyDevice)
            .Select(device => string.Join('|',
                device.InstanceId,
                device.ParentInstanceId ?? string.Empty,
                string.Join(',', device.AncestorInstanceIds)))
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase));
    }
}
