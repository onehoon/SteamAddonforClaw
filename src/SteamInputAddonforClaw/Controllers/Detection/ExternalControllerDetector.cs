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
            AppLog.Info("ExternalController", "External controller detection started.");
            var stopwatch = Stopwatch.StartNew();
            var devices = _deviceEnumerator.EnumeratePresentDevices();
            AppLog.Debug("PnP", "Controller device enumeration completed.", ("DeviceCount", devices.Count));
            var groups = devices
                .GroupBy(GetLogicalControllerKey)
                .Select(group => new
                {
                    Key = group.Key,
                    Devices = group.ToArray(),
                    Classifications = group.Select(_classifier.ClassifyDetailed).ToArray()
                })
                .ToArray();

            foreach (var group in groups)
            {
                AppLog.Trace("ExternalController", "Logical controller group.", ("LogicalGroupKey", group.Key), ("InterfaceCount", group.Devices.Length));
                for (var index = 0; index < group.Devices.Length; index++)
                {
                    var device = group.Devices[index];
                    var classification = group.Classifications[index];
                    AppLog.Trace("PnP", "Controller interface classified.", ("LogicalGroupKey", group.Key), ("InstanceId", device.InstanceId), ("ParentInstanceId", device.ParentInstanceId), ("ContainerId", device.ContainerId), ("VID", device.VendorId), ("PID", device.ProductId), ("EnumeratorName", device.EnumeratorName), ("Service", device.Service), ("Classification", classification.Classification), ("Reason", classification.Reason));
                }
            }

            var externalControllers = groups
                .Where(group => group.Classifications.Any(result => result.Classification == ControllerDeviceClassification.ExternalPhysical))
                .Select(group => group.Devices[0])
                .ToArray();

            if (externalControllers.Length > 0)
            {
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
        if (device.ContainerId is Guid containerId)
        {
            return $"container:{containerId:D}";
        }

        if (!string.IsNullOrWhiteSpace(device.ParentInstanceId))
        {
            return $"parent:{device.ParentInstanceId}";
        }

        return $"instance:{device.InstanceId}";
    }
}
