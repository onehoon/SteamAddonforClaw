using System.Security.AccessControl;
using System.Security.Principal;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ProvisioningStorageSecurityTests
{
    [Fact]
    public void ExpectedMachineAcl_IsTrusted()
    {
        var assessment = ProvisioningStorageSecurity.AssessAcl(Administrators, ExpectedRules());
        Assert.True(assessment.Status == ProvisioningStorageStatus.Trusted, assessment.Reason);
    }

    [Fact]
    public void UserWritableAcl_IsRejected()
    {
        var rules = ExpectedRules().Append(new FileSystemAccessRule(Users, FileSystemRights.Modify, AccessControlType.Allow));
        Assert.Equal(ProvisioningStorageStatus.Unsafe, ProvisioningStorageSecurity.AssessAcl(Administrators, rules).Status);
    }

    [Fact]
    public void UnexpectedOwner_IsRejected() => Assert.Equal(ProvisioningStorageStatus.Unsafe, ProvisioningStorageSecurity.AssessAcl(Users, ExpectedRules()).Status);

    [Fact]
    public void MissingSystemWritePermission_IsRejected()
    {
        var rules = ExpectedRules().Where(rule => !rule.IdentityReference.Translate(typeof(SecurityIdentifier)).Equals(System));
        Assert.Equal(ProvisioningStorageStatus.Unsafe, ProvisioningStorageSecurity.AssessAcl(Administrators, rules).Status);
    }

    private static IEnumerable<FileSystemAccessRule> ExpectedRules()
    {
        yield return new FileSystemAccessRule(System, FileSystemRights.FullControl, AccessControlType.Allow);
        yield return new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow);
        yield return new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, AccessControlType.Allow);
    }

    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
}
