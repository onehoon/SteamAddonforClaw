using SteamInputAddonforClaw.Controllers.Detection;
using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Startup;

internal interface IControllerEnvironmentWaiter
{
    Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode mode, CancellationToken cancellationToken);
}

internal enum ControllerEnvironmentReadiness { NotApplicable, Stable, Indeterminate }

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

    public async Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(ControllerEnvironmentMode mode, CancellationToken cancellationToken)
    {
        if (mode == ControllerEnvironmentMode.Unsupported)
        {
            AppLog.Info("Environment", "Environment readiness wait skipped.", ("Mode", mode), ("Result", ControllerEnvironmentReadiness.NotApplicable), ("Reason", "UnsupportedHardware"));
            return ControllerEnvironmentReadiness.NotApplicable;
        }
        string? previousSnapshot = null;
        var stableSnapshotCount = 0;
        var deadline = DateTimeOffset.UtcNow + _timeout;
        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;
        AppLog.Info("Environment", "Environment readiness wait started.", ("Mode", mode), ("TimeoutMs", _timeout.TotalMilliseconds), ("PollIntervalMs", _sampleInterval.TotalMilliseconds));

        try
        {
            while (DateTimeOffset.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;
                var (snapshot, ready) = CreateRelevantTopologySnapshot(mode);
                AppLog.Trace("Environment", "Readiness poll.", ("Attempt", attempt), ("Ready", ready), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
                if (!ready)
                {
                    stableSnapshotCount = 0;
                    previousSnapshot = null;
                    await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                stableSnapshotCount = snapshot == previousSnapshot ? stableSnapshotCount + 1 : 1;
                if (stableSnapshotCount >= _requiredStableSnapshots)
                {
                    AppLog.Info("Environment", "Environment readiness stable.", ("Mode", mode), ("Attempts", attempt), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
                    return ControllerEnvironmentReadiness.Stable;
                }

                previousSnapshot = snapshot;
                await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Debug("Environment", "Environment readiness wait cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Environment", "Environment readiness wait failed.", exception, ("Action", "Passive"));
        }

        AppLog.Warn("Environment", "Environment readiness timeout.", null, ("Mode", mode), ("Action", "Passive"), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
        return ControllerEnvironmentReadiness.Indeterminate;
    }

    private (string Snapshot, bool Ready) CreateRelevantTopologySnapshot(ControllerEnvironmentMode mode)
    {
        var devices = _deviceEnumerator.EnumeratePresentDevices();
        var topology = new ControllerTopologySnapshot(devices);
        var relevantDevices = mode == ControllerEnvironmentMode.ClawTweaks
            ? devices.Where(device => _classifier.IsClawTweaksVirtualControllerCandidate(device, topology)).ToArray()
            : devices.Where(_classifier.IsRelevantTopologyDevice).ToArray();
        var snapshot = string.Join('\n', relevantDevices
            .Select(device => string.Join('|',
                device.InstanceId,
                device.ParentInstanceId ?? string.Empty,
                string.Join(',', device.AncestorInstanceIds)))
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase));
        var ready = mode switch
        {
            ControllerEnvironmentMode.StockCenterM => devices.Any(device => _classifier.Classify(device) == ControllerDeviceClassification.InternalHandheld),
            ControllerEnvironmentMode.ClawTweaks => relevantDevices.Length > 0,
            _ => false
        };
        return (snapshot, ready);
    }
}
