namespace SteamInputAddonforClaw.Status;

internal sealed record ControllerEnvironmentAssessmentSnapshot(
    IReadOnlyList<ControllerSoftwareStatus> Software,
    ControllerManagerClassification Manager,
    ControllerEnvironmentCompatibilityAssessment Compatibility);

internal interface IControllerEnvironmentAssessmentProvider
{
    ControllerEnvironmentAssessmentSnapshot Capture();
}

internal sealed class ControllerEnvironmentAssessmentProvider(
    IReadOnlyList<IControllerSoftwareStatusProvider> softwareProviders,
    IControllerEnvironmentCompatibilityPolicy? compatibilityPolicy = null) : IControllerEnvironmentAssessmentProvider
{
    private readonly IControllerEnvironmentCompatibilityPolicy _compatibilityPolicy = compatibilityPolicy ?? new CurrentControllerEnvironmentCompatibilityPolicy();

    public ControllerEnvironmentAssessmentSnapshot Capture()
    {
        var software = ControllerSoftwareStatusSorter.Sort(softwareProviders.Select(provider => provider.Capture()));
        var manager = ControllerSoftwareSnapshot.TryCreate(software, out var snapshot) && snapshot is not null
            ? ControllerManagerClassifier.Classify(snapshot)
            : new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);
        return new(software, manager, _compatibilityPolicy.Evaluate(software));
    }
}
