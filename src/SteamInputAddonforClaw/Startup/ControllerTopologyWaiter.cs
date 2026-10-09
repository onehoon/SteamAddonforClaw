using SteamInputAddonforClaw.Controllers.Detection;
using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices.MSI.Claw;

namespace SteamInputAddonforClaw.Startup;

internal interface IControllerTopologyWaiter
{
    Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken);
}

internal enum ControllerTopologyReadiness { Stable, Indeterminate }

internal sealed class ControllerTopologyWaiter : IControllerTopologyWaiter
{
    private sealed record TopologyObservation(
        string Snapshot,
        bool Ready,
        int PresentMsiCandidateCount,
        int PresentPid1901CandidateCount,
        int PresentPid1902CandidateCount,
        int PresentPid1903CandidateCount,
        int RecognizedInternalDeviceCount,
        bool Pid1901XInputControlHidPresent,
        bool Pid1902DirectInputControlHidPresent,
        int A2vm230ControlEndpointCandidateCount);

    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly ControllerDeviceClassifier _classifier;
    private readonly int _requiredStableSnapshots;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _timeout;

    public ControllerTopologyWaiter(
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

    /// <summary>Answers only: is the supported MSI Claw's relevant physical controller topology
    /// stable and usable for the caller's next step? It does not decide controller authority --
    /// startup-root state does. Unsupported/indeterminate hardware is already handled before this
    /// runs.</summary>
    public async Task<ControllerTopologyReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
    {
        string? previousSnapshot = null;
        var stableSnapshotCount = 0;
        var deadline = DateTimeOffset.UtcNow + _timeout;
        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;
        var lastObservation = new TopologyObservation(string.Empty, false, 0, 0, 0, 0, 0, false, false, 0);
        Exception? enumerationFailure = null;
        AppLog.Info("ControllerTopology", "Topology readiness wait started.", ("TimeoutMs", _timeout.TotalMilliseconds), ("PollIntervalMs", _sampleInterval.TotalMilliseconds));

        try
        {
            while (DateTimeOffset.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;
                lastObservation = CreateRelevantTopologySnapshot();
                AppLog.Debug("ControllerTopology", "Topology readiness poll.", ("Attempt", attempt), ("Ready", lastObservation.Ready), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
                if (!lastObservation.Ready)
                {
                    stableSnapshotCount = 0;
                    previousSnapshot = null;
                    await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                stableSnapshotCount = lastObservation.Snapshot == previousSnapshot ? stableSnapshotCount + 1 : 1;
                if (stableSnapshotCount >= _requiredStableSnapshots)
                {
                    AppLog.Info("ControllerTopology", "Topology readiness stable.", ("Attempts", attempt), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
                    return ControllerTopologyReadiness.Stable;
                }

                previousSnapshot = lastObservation.Snapshot;
                await Task.Delay(_sampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Debug("ControllerTopology", "Topology readiness wait cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            enumerationFailure = exception;
        }

        var failureClass = enumerationFailure is not null
            ? "EnumerationFailed"
            : GetFailureClass(lastObservation);
        AppLog.Warn("ControllerTopology", "Topology readiness ended without a stable controller topology.", enumerationFailure,
            ("FailureClass", failureClass),
            ("PresentMsiCandidateCount", lastObservation.PresentMsiCandidateCount),
            ("PresentPid1901CandidateCount", lastObservation.PresentPid1901CandidateCount),
            ("PresentPid1902CandidateCount", lastObservation.PresentPid1902CandidateCount),
            ("PresentPid1903CandidateCount", lastObservation.PresentPid1903CandidateCount),
            ("RecognizedInternalDeviceCount", lastObservation.RecognizedInternalDeviceCount),
            ("Pid1901XInputControlHidPresent", lastObservation.Pid1901XInputControlHidPresent),
            ("Pid1902DirectInputControlHidPresent", lastObservation.Pid1902DirectInputControlHidPresent),
            ("A2vm230ControlEndpointCandidateCount", lastObservation.A2vm230ControlEndpointCandidateCount),
            ("ConsecutiveStableSnapshots", stableSnapshotCount),
            ("RequiredStableSnapshots", _requiredStableSnapshots),
            ("Attempts", attempt), ("ElapsedMs", stopwatch.ElapsedMilliseconds), ("Action", "Passive"));
        return ControllerTopologyReadiness.Indeterminate;
    }

    private TopologyObservation CreateRelevantTopologySnapshot()
    {
        var devices = _deviceEnumerator.EnumeratePresentDevices();
        var topology = new ControllerTopologySnapshot(devices);
        var msiCandidates = devices.Where(device => device.Present
            && device.VendorId == MsiClawHardware.VendorId
            && device.ProductId is 0x1901 or 0x1902 or 0x1903).ToArray();
        // Stability tracking must be scoped to the MSI Claw's own internal-controller topology only.
        // Any device that merely looks like a generic game controller (an Xbox controller, DualSense,
        // a real Steam Controller, etc.) must never be part of this snapshot: connecting/disconnecting
        // one during startup must not reset the stable-poll counter or push readiness into
        // Indeterminate. Uses the narrow IsInternalHandheld predicate ("is this the MSI Claw?") rather
        // than the general classifier, so non-Claw devices are never classified at all here.
        var relevantDevices = devices.Where(device => _classifier.IsInternalHandheld(device, topology)).ToArray();
        var snapshot = string.Join('\n', relevantDevices
            .Select(device => string.Join('|',
                device.InstanceId,
                device.ParentInstanceId ?? string.Empty,
                string.Join(',', device.AncestorInstanceIds)))
            .OrderBy(identity => identity, StringComparer.OrdinalIgnoreCase));
        // An MSI VID/PID device existing at all is not sufficient: the mode-switch step immediately
        // after startup readiness resolves a specific control HID collection (see
        // MsiClawModeTopology/MsiClawControlHidResolver), not just "some MSI device". If that control
        // HID hasn't enumerated yet, readiness must not settle on the gamepad-usage interface alone.
        // Either a PID1901 XInput or a PID1902 DirectInput control HID satisfies readiness -- PnP /
        // mode-transition timing may legitimately expose either while the caller is stabilizing.
        var pid1901ControlHidPresent = MatchesModeTopology(relevantDevices, MsiClawNativeMode.XInput);
        var pid1902ControlHidPresent = MatchesModeTopology(relevantDevices, MsiClawNativeMode.DirectInput);
        var ready = relevantDevices.Length > 0 && (pid1901ControlHidPresent || pid1902ControlHidPresent);
        return new TopologyObservation(
            snapshot,
            ready,
            msiCandidates.Length,
            msiCandidates.Count(device => device.ProductId == 0x1901),
            msiCandidates.Count(device => device.ProductId == 0x1902),
            msiCandidates.Count(device => device.ProductId == 0x1903),
            relevantDevices.Length,
            pid1901ControlHidPresent,
            pid1902ControlHidPresent,
            msiCandidates.Count(MsiClawHardware.IsA2vm230ObservedControlEndpointCandidate));
    }

    private static string GetFailureClass(TopologyObservation observation)
    {
        if (observation.PresentMsiCandidateCount == 0) return "NoPresentMsiCandidates";
        if (observation.RecognizedInternalDeviceCount == 0) return "MsiCandidatesNotClassifiedAsInternal";
        if (!observation.Pid1901XInputControlHidPresent && !observation.Pid1902DirectInputControlHidPresent)
            return observation.A2vm230ControlEndpointCandidateCount > 0
                ? "A2vm230ControlEndpointUnverified"
                : "RequiredControlHidMissing";
        return "RelevantTopologyNotStable";
    }

    private static bool MatchesModeTopology(IReadOnlyList<ControllerDeviceInfo> devices, MsiClawNativeMode mode)
    {
        if (!MsiClawModeTopology.TryGet(mode, out var topology)) return false;
        return devices.Any(device => device.Present
            && device.VendorId == MsiClawHardware.VendorId
            && device.ProductId == topology.ProductId
            && device.UsagePage == topology.UsagePage
            && device.Usage == topology.Usage);
    }
}
