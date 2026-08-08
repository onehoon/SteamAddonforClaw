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
                    Devices = group.ToArray(),
                    Classifications = group.Select(_classifier.Classify).ToArray()
                })
                .ToArray();

            var externalControllers = groups
                .Where(group => group.Classifications.Contains(ControllerDeviceClassification.ExternalPhysical))
                .Select(group => group.Devices[0])
                .ToArray();

            if (externalControllers.Length > 0)
            {
                AppLog.Warn("ExternalController", "External physical controller detected.", null, ("Count", externalControllers.Length), ("Action", "Veto"));
                return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.ExternalPresent, externalControllers.Length, externalControllers);
            }

            var assessment = groups.Any(group => group.Classifications.Contains(ControllerDeviceClassification.Indeterminate))
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
