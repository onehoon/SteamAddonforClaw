namespace SteamInputAddonforClaw.Controllers.Detection;

using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

public sealed class ExternalControllerDetector
{
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly ControllerDeviceClassifier _classifier;

    public ExternalControllerDetector(IControllerDeviceEnumerator deviceEnumerator, ControllerDeviceClassifier classifier)
    {
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public ExternalControllerAssessment Detect()
    {
        try
        {
            if (_classifier.HasUncertainIdentityOwnership)
            {
                AppLog.Warn("ExternalController", "Addon-owned virtual-device ownership is uncertain.", null, ("Action", "Passive"));
                return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, []);
            }
            AppLog.Info("ExternalController", "External controller detection started.");
            var stopwatch = Stopwatch.StartNew();
            var devices = _deviceEnumerator.EnumeratePresentDevices();
            var topology = new ControllerTopologySnapshot(devices);
            AppLog.Debug("PnP", "Controller device enumeration completed.", ("DeviceCount", devices.Count));
            var groups = devices
                .GroupBy(GetLogicalControllerKey)
                .Select(group => new
                {
                    Key = group.Key,
                    Devices = group.ToArray(),
                    Classifications = group.Select(device => _classifier.ClassifyDetailed(device, topology)).ToArray()
                })
                .ToArray();

            foreach (var group in groups)
            {
                AppLog.Trace("ExternalController", "Logical controller group.", ("LogicalGroupKey", group.Key), ("InterfaceCount", group.Devices.Length));
                for (var index = 0; index < group.Devices.Length; index++)
                {
                    var device = group.Devices[index];
                    var classification = group.Classifications[index];
                    AppLog.Trace("PnP", "Controller interface classified.", ("LogicalGroupKey", group.Key), ("InstanceId", device.InstanceId), ("ParentInstanceId", device.ParentInstanceId), ("ContainerId", device.ContainerId), ("VID", device.VendorId), ("PID", device.ProductId), ("EnumeratorName", device.EnumeratorName), ("Service", device.Service), ("Classification", classification.Classification), ("Reason", classification.Reason), ("EvidenceAncestorInstanceId", classification.EvidenceDevice?.InstanceId), ("EvidenceHardwareId", classification.EvidenceDevice?.HardwareIds.FirstOrDefault()), ("EvidenceService", classification.EvidenceDevice?.Service));
                }
            }

            var externalControllers = groups
                .Where(group => group.Classifications.Any(result => result.Classification == ControllerDeviceClassification.ExternalPhysical))
                .Select(group => group.Devices[group.Classifications.Select((classification, index) => (classification, index)).First(item => item.classification.Classification == ControllerDeviceClassification.ExternalPhysical).index])
                .ToArray();

            if (externalControllers.Length > 0)
            {
                foreach (var controller in externalControllers)
                {
                    AppLog.Info("ExternalController", "Physical controller candidate confirmed.", ("InstanceId", controller.InstanceId), ("VID", controller.VendorId), ("PID", controller.ProductId), ("VirtualAncestorEvidence", false), ("Classification", ControllerDeviceClassification.ExternalPhysical), ("Action", "Veto"));
                }
                AppLog.Warn("ExternalController", "External physical controller detected.", null, ("Count", externalControllers.Length), ("Action", "Veto"));
                return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.ExternalPresent, externalControllers.Length, externalControllers);
            }

            var assessment = groups.Any(group => group.Classifications.Any(result => result.Classification == ControllerDeviceClassification.Indeterminate))
                ? new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, [])
                : new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Clear, 0, []);
            AppLog.Info("ExternalController", "External controller assessment completed.", ("Status", assessment.Status), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return assessment;
        }
        catch (Exception exception)
        {
            AppLog.Warn("ExternalController", "External controller detection failed.", exception, ("Action", "Passive"), ("Reason", "EnumerationOrClassificationException"));
            return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, []);
        }
    }

    private static string GetLogicalControllerKey(ControllerDeviceInfo device)
    {
        if (device.ContainerId is Guid containerId && IsUsableContainerId(containerId))
        {
            return $"container:{containerId:D}";
        }

        if (device.ContainerId is Guid invalidContainerId)
        {
            AppLog.Trace("PnP", "Container ID ignored for logical grouping.", ("InstanceId", device.InstanceId), ("ContainerId", invalidContainerId), ("Reason", invalidContainerId == Guid.Empty ? "EmptyContainerId" : "SentinelContainerId"), ("Fallback", string.IsNullOrWhiteSpace(device.ParentInstanceId) ? "InstanceId" : "ParentInstanceId"));
        }

        if (!string.IsNullOrWhiteSpace(device.ParentInstanceId))
        {
            return $"parent:{device.ParentInstanceId}";
        }

        return $"instance:{device.InstanceId}";
    }

    private static bool IsUsableContainerId(Guid containerId)
        => containerId != Guid.Empty && containerId != new Guid("00000000-0000-0000-ffff-ffffffffffff");
}
