using SteamInputAddonforClaw.Devices.Abstractions;

namespace SteamInputAddonforClaw.Devices;

internal enum HardwareCompatibilityStatus { Supported, Unsupported, Indeterminate }

internal sealed record HardwareCompatibilityAssessment(
    HardwareCompatibilityStatus Status,
    HandheldDeviceId? DeviceFamily,
    HandheldDeviceModelId? DeviceModel,
    string Reason)
{
    public bool AllowsMutation => Status == HardwareCompatibilityStatus.Supported;
}

internal interface IHardwareCompatibilityEvaluator
{
    HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture);
}

internal sealed class HardwareCompatibilityEvaluator(HandheldDeviceRegistry registry) : IHardwareCompatibilityEvaluator
{
    public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture)
    {
        if (capture.Status != DeviceProbeCaptureStatus.Success)
            return new(HardwareCompatibilityStatus.Indeterminate, null, null, capture.Reason);

        var family = registry.Resolve(capture.Context);
        if (family.Status is HandheldDeviceResolutionStatus.Indeterminate or HandheldDeviceResolutionStatus.Ambiguous)
            return new(HardwareCompatibilityStatus.Indeterminate, null, null, family.Reason);
        if (family.Status == HandheldDeviceResolutionStatus.Unsupported || family.Adapter is null)
            return new(HardwareCompatibilityStatus.Unsupported, null, null, family.Reason);

        var modelResolver = family.Adapter.ModelResolver;
        if (modelResolver is null)
            return new(HardwareCompatibilityStatus.Indeterminate, family.Adapter.Descriptor.Id, null, "DeviceModelResolverUnavailable");

        HandheldDeviceModelResolution model;
        try { model = modelResolver.Resolve(capture.Context); }
        catch (Exception exception)
        {
            return new(HardwareCompatibilityStatus.Indeterminate, family.Adapter.Descriptor.Id, null, "DeviceModelResolutionFailed:" + exception.GetType().Name);
        }

        return model.Status switch
        {
            HandheldDeviceModelResolutionStatus.Matched when model.Model is not null => new(HardwareCompatibilityStatus.Supported, family.Adapter.Descriptor.Id, model.Model.Id, model.Reason),
            HandheldDeviceModelResolutionStatus.Unsupported => new(HardwareCompatibilityStatus.Unsupported, family.Adapter.Descriptor.Id, null, model.Reason),
            _ => new(HardwareCompatibilityStatus.Indeterminate, family.Adapter.Descriptor.Id, null, model.Reason)
        };
    }
}
