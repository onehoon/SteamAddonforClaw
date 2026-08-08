namespace SteamInputAddonforClaw.Controllers.Detection;

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
            var groups = _deviceEnumerator.EnumeratePresentDevices()
                .GroupBy(GetLogicalControllerKey)
                .Select(group => new
                {
                    Devices = group.ToArray(),
                    Classifications = group.Select(_classifier.Classify).ToArray()
                })
                .ToArray();

            if (groups.Any(group => group.Classifications.Contains(ControllerDeviceClassification.Indeterminate)))
            {
                return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, []);
            }

            var externalControllers = groups
                .Where(group => group.Classifications.Contains(ControllerDeviceClassification.ExternalPhysical))
                .Select(group => group.Devices[0])
                .ToArray();

            return externalControllers.Length == 0
                ? new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Clear, 0, [])
                : new ExternalControllerAssessment(ExternalControllerAssessmentStatus.ExternalPresent, externalControllers.Length, externalControllers);
        }
        catch (Exception)
        {
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
