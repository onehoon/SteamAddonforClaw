using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HidHideRoutingAdmissionPolicyTests
{
    private const string Addon = "C:\\addon.exe";

    [Fact]
    public void Not_installed_is_allowed_but_foreign_whitelist_is_rejected()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.Allowed,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.NotInstalled, new HashSet<string>()), Addon));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.Available, new HashSet<string> { "C:\\other.exe" }), Addon));
    }

    [Fact]
    public void Hidden_entries_inverse_mode_and_unresolved_whitelist_are_foreign()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.Available, new HashSet<string>(), ["HID\\1902"]), Addon));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.InverseWhitelist, new HashSet<string>(), IsInverseWhitelist: true), Addon));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.Available, new HashSet<string>(), HasUnresolvedApplicationWhitelistEntries: true), Addon));
    }

    [Fact]
    public void Unreadable_installed_configuration_is_unavailable()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.InspectionUnavailable,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.AccessDenied, new HashSet<string>()), Addon));
        Assert.Equal(HidHideRoutingAdmissionOutcome.InspectionUnavailable,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>()), Addon));
    }
}
