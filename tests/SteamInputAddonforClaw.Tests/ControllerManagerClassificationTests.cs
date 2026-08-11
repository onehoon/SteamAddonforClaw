using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerManagerClassificationTests
{
    [Fact]
    public void NoManager_IsNone() => AssertClassification(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager, Snapshot());

    [Theory]
    [InlineData((int)ControllerSoftwareKind.ClawTweaks, (int)ControllerManagerKind.ClawTweaks, (int)ControllerManagerClassificationReason.ClawTweaksDetected)]
    [InlineData((int)ControllerSoftwareKind.HandheldCompanion, (int)ControllerManagerKind.HandheldCompanion, (int)ControllerManagerClassificationReason.HandheldCompanionDetected)]
    [InlineData((int)ControllerSoftwareKind.Winhanced, (int)ControllerManagerKind.Winhanced, (int)ControllerManagerClassificationReason.WinhancedDetected)]
    public void SingleManager_PreservesIdentity(int softwareKindValue, int expectedKindValue, int expectedReasonValue)
    {
        var kind = (ControllerSoftwareKind)softwareKindValue;
        var snapshot = Snapshot(kind);
        AssertClassification((ControllerManagerKind)expectedKindValue, (ControllerManagerClassificationReason)expectedReasonValue, snapshot);
    }

    [Fact]
    public void MultipleManagers_IsMultiple() => AssertClassification(
        ControllerManagerKind.Multiple,
        ControllerManagerClassificationReason.MultipleThirdPartyControllerManagersDetected,
        Snapshot(ControllerSoftwareKind.ClawTweaks, ControllerSoftwareKind.Winhanced));

    [Fact]
    public void UnresolvedManager_IsIndeterminate() => AssertClassification(
        ControllerManagerKind.Indeterminate,
        ControllerManagerClassificationReason.ControllerManagerStateIndeterminate,
        Snapshot(unresolved: ControllerSoftwareKind.Winhanced));

    private static void AssertClassification(ControllerManagerKind kind, ControllerManagerClassificationReason reason, ControllerSoftwareSnapshot snapshot)
    {
        var result = ControllerManagerClassifier.Classify(snapshot);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(reason, result.Reason);
    }

    private static ControllerSoftwareSnapshot Snapshot(params ControllerSoftwareKind[] present) => Snapshot(unresolved: null, present);

    private static ControllerSoftwareSnapshot Snapshot(ControllerSoftwareKind? unresolved, params ControllerSoftwareKind[] present)
    {
        ControllerSoftwareStatus Status(ControllerSoftwareKind kind) => present.Contains(kind)
            ? new(kind, kind.ToString(), SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning, "test")
            : unresolved == kind
                ? new(kind, kind.ToString(), SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "test")
                : new(kind, kind.ToString(), SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning, "test");

        return new(Status(ControllerSoftwareKind.MsiCenterM), Status(ControllerSoftwareKind.ClawTweaks), Status(ControllerSoftwareKind.HandheldCompanion), present.Contains(ControllerSoftwareKind.Winhanced) || unresolved == ControllerSoftwareKind.Winhanced ? Status(ControllerSoftwareKind.Winhanced) : null);
    }
}
