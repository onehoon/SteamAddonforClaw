namespace SteamInputAddonforClaw.Controllers.Detection;

using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.SteamController1304;

public sealed class ExternalControllerDetector
{
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly ControllerDeviceClassifier _classifier;
    private readonly ISteamController1304ConnectionProbe _steamController1304Probe;

    public ExternalControllerDetector(IControllerDeviceEnumerator deviceEnumerator, ControllerDeviceClassifier classifier)
        : this(deviceEnumerator, classifier, null) { }

    internal ExternalControllerDetector(IControllerDeviceEnumerator deviceEnumerator, ControllerDeviceClassifier classifier, ISteamController1304ConnectionProbe? steamController1304Probe)
    {
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _steamController1304Probe = steamController1304Probe ?? new WindowsSteamController1304ConnectionProbe(new WindowsSteamController1304ReadOnlyTransport());
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
            AppLog.Debug("ExternalController", "External controller detection started.");
            var stopwatch = Stopwatch.StartNew();
            var devices = _deviceEnumerator.EnumeratePresentDevices();
            var topology = new ControllerTopologySnapshot(devices);
            AppLog.Debug("PnP", "Controller device enumeration completed.", ("DeviceCount", devices.Count));
            var steamReceiverAssessments = devices
                .Where(device => device.Present && device.VendorId == 0x28DE && device.ProductId == 0x1304 && string.Equals(device.EnumeratorName, "USB", StringComparison.OrdinalIgnoreCase) && string.Equals(device.Service, "usbccgp", StringComparison.OrdinalIgnoreCase) && device.InstanceId.StartsWith("USB\\VID_28DE&PID_1304\\", StringComparison.OrdinalIgnoreCase))
                .Select(receiver => (receiver.InstanceId, Assessment: _steamController1304Probe.Probe(receiver)))
                .ToArray();
            var groups = devices
                .GroupBy(GetLogicalControllerKey)
                .Select(group => new
                {
                    Key = group.Key,
                    Devices = group.ToArray(),
                    Classifications = ApplySteamController1304ConnectionState(group.ToArray(), group.Select(device => _classifier.ClassifyDetailed(device, topology)).ToArray(), steamReceiverAssessments)
                })
                .ToArray();

            foreach (var group in groups)
            {
                AppLog.Debug("ExternalController", "Logical controller group.", ("LogicalGroupKey", group.Key), ("InterfaceCount", group.Devices.Length));
                for (var index = 0; index < group.Devices.Length; index++)
                {
                    var device = group.Devices[index];
                    var classification = group.Classifications[index];
                    AppLog.Debug("PnP", "Controller interface classified.", ("LogicalGroupKey", group.Key), ("InstanceId", device.InstanceId), ("ParentInstanceId", device.ParentInstanceId), ("ContainerId", device.ContainerId), ("VID", device.VendorId), ("PID", device.ProductId), ("EnumeratorName", device.EnumeratorName), ("Service", device.Service), ("Classification", classification.Classification), ("Reason", classification.Reason), ("EvidenceAncestorInstanceId", classification.EvidenceDevice?.InstanceId), ("EvidenceHardwareId", classification.EvidenceDevice?.HardwareIds.FirstOrDefault()), ("EvidenceService", classification.EvidenceDevice?.Service));
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
                    AppLog.Debug("ExternalController", "Physical controller candidate confirmed.", ("InstanceId", controller.InstanceId), ("VID", controller.VendorId), ("PID", controller.ProductId), ("VirtualAncestorEvidence", false), ("Classification", ControllerDeviceClassification.ExternalPhysical), ("Action", "Veto"));
                }
                AppLog.Warn("ExternalController", "External physical controller detected.", null, ("Count", externalControllers.Length), ("Action", "Veto"));
                return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.ExternalPresent, externalControllers.Length, externalControllers);
            }

            var assessment = groups.Any(group => group.Classifications.Any(result => result.Classification == ControllerDeviceClassification.Indeterminate))
                ? new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, [])
                : new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Clear, 0, []);
            AppLog.Debug("ExternalController", "External controller assessment completed.", ("Status", assessment.Status), ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return assessment;
        }
        catch (Exception exception)
        {
            AppLog.Warn("ExternalController", "External controller detection failed.", exception, ("Action", "Passive"), ("Reason", "EnumerationOrClassificationException"));
            return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, []);
        }
    }

    private static ControllerClassificationResult[] ApplySteamController1304ConnectionState(ControllerDeviceInfo[] devices, ControllerClassificationResult[] classifications, IReadOnlyList<(string InstanceId, SteamController1304ConnectionAssessment Assessment)> assessments)
    {
        var receiverAssessment = assessments.FirstOrDefault(item => devices.Any(device => string.Equals(device.InstanceId, item.InstanceId, StringComparison.OrdinalIgnoreCase) || device.AncestorInstanceIds.Contains(item.InstanceId, StringComparer.OrdinalIgnoreCase)));
        if (receiverAssessment == default) return classifications;
        if (!classifications.Any(result => result.Classification == ControllerDeviceClassification.ExternalPhysical || result.Reason == "SteamController1304PhysicalRootUnverified"))
            return classifications;
        var assessment = receiverAssessment.Assessment;
        AppLog.Debug("ExternalController", "Steam Controller receiver connection assessed.",
            ("InstanceId", assessment.ReceiverInstanceId), ("ConnectionStatus", assessment.Status), ("ConnectionReason", assessment.Reason), ("Evidence", assessment.Evidence));
        return assessment.Status switch
        {
            SteamController1304ConnectionStatus.ReceiverOnly => classifications.Select((_, index) => new ControllerClassificationResult(ControllerDeviceClassification.NotController, "SteamController1304ReceiverOnly")).ToArray(),
            SteamController1304ConnectionStatus.Indeterminate => classifications.Select((_, index) => new ControllerClassificationResult(ControllerDeviceClassification.Indeterminate, $"SteamController1304ConnectionIndeterminate:{assessment.Reason}")).ToArray(),
            _ => classifications
        };
    }

    private static string GetLogicalControllerKey(ControllerDeviceInfo device)
    {
        if (ControllerLogicalIdentity.IsUsableContainerId(device.ContainerId))
            return ControllerLogicalIdentity.GetLogicalKey(device);

        if (device.ContainerId is Guid invalidContainerId)
        {
            AppLog.Debug("PnP", "Container ID ignored for logical grouping.", ("InstanceId", device.InstanceId), ("ContainerId", invalidContainerId), ("Reason", invalidContainerId == Guid.Empty ? "EmptyContainerId" : "SentinelContainerId"), ("Fallback", string.IsNullOrWhiteSpace(device.ParentInstanceId) ? "InstanceId" : "ParentInstanceId"));
        }

        return ControllerLogicalIdentity.GetLogicalKey(device);
    }
}
