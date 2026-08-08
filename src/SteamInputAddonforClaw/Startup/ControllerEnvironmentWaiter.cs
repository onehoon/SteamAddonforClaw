using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Startup;

internal interface IControllerEnvironmentWaiter
{
    Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(CancellationToken cancellationToken);
}

internal enum ControllerEnvironmentReadiness { Stable, Indeterminate }

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

    public async Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
    {
        string? previousSnapshot = null;
        var stableSnapshotCount = 0;
        var deadline = DateTimeOffset.UtcNow + _timeout;

        try
        {
            while (DateTimeOffset.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (snapshot, hasInternalClaw) = CreateRelevantTopologySnapshot();
                if (!hasInternalClaw)
                {
                    stableSnapshotCount = 0;
                    previousSnapshot = null;
                    await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                stableSnapshotCount = snapshot == previousSnapshot ? stableSnapshotCount + 1 : 1;
                if (stableSnapshotCount >= _requiredStableSnapshots)
                {
                    return ControllerEnvironmentReadiness.Stable;
                }

                previousSnapshot = snapshot;
                await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }

        return ControllerEnvironmentReadiness.Indeterminate;
    }

    private (string Snapshot, bool HasInternalClaw) CreateRelevantTopologySnapshot()
    {
        var devices = _deviceEnumerator.EnumeratePresentDevices();
        var snapshot = string.Join('\n', devices
            .Where(_classifier.IsRelevantTopologyDevice)
            .Select(device => string.Join('|',
                device.InstanceId,
                device.ParentInstanceId ?? string.Empty,
                string.Join(',', device.AncestorInstanceIds)))
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase));
        return (snapshot, devices.Any(device => _classifier.Classify(device) == ControllerDeviceClassification.InternalClaw));
    }
}
