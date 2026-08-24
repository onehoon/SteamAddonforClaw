using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HidHideRoutingAdmissionPolicyTests
{
    private const string Addon = "C:\\Program Files\\SteamAddon\\SteamInputAddonforClaw.exe";
    private const string OfficialClient = "C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideClient.exe";
    private const string RenamedClient = "C:\\Temp\\HidHideClient.exe";

    [Fact]
    public void Empty_configuration_and_addon_only_are_allowed()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.Allowed, Evaluate([]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.Allowed, Evaluate([Addon]));
    }

    [Fact]
    public void Official_client_only_and_addon_plus_official_client_are_allowed()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.Allowed, Evaluate([OfficialClient]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.Allowed, Evaluate([Addon, OfficialClient]));
    }

    [Fact]
    public void Path_comparison_is_case_insensitive()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.Allowed,
            Evaluate([Addon.ToUpperInvariant(), OfficialClient.ToLowerInvariant()]));
    }

    [Fact]
    public void Arbitrary_and_renamed_client_paths_are_foreign()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration, Evaluate(["C:\\other.exe"]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration, Evaluate([RenamedClient]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration, Evaluate([Addon, "C:\\other.exe"]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration, Evaluate([OfficialClient, "C:\\other.exe"]));
    }

    [Fact]
    public void Hidden_entries_inverse_mode_and_unresolved_whitelist_are_foreign()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.Available, new HashSet<string>(), ["HID\\1902"]), Addon, [OfficialClient]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.InverseWhitelist, new HashSet<string>(), IsInverseWhitelist: true), Addon, [OfficialClient]));
        Assert.Equal(HidHideRoutingAdmissionOutcome.ForeignConfiguration,
            HidHideRoutingAdmissionPolicy.Evaluate(new(HidHideInspectionStatus.Available, new HashSet<string>(), HasUnresolvedApplicationWhitelistEntries: true), Addon, [OfficialClient]));
    }

    [Fact]
    public void Unreadable_installed_configuration_is_unavailable()
    {
        Assert.Equal(HidHideRoutingAdmissionOutcome.InspectionUnavailable, Evaluate([], HidHideInspectionStatus.AccessDenied));
        Assert.Equal(HidHideRoutingAdmissionOutcome.InspectionUnavailable, Evaluate([], HidHideInspectionStatus.ConfigurationUnavailable));
    }

    private static HidHideRoutingAdmissionOutcome Evaluate(IReadOnlyList<string> applications, HidHideInspectionStatus status = HidHideInspectionStatus.Available) =>
        HidHideRoutingAdmissionPolicy.Evaluate(new(status, applications.ToHashSet(StringComparer.OrdinalIgnoreCase)), Addon, [OfficialClient]);
}
