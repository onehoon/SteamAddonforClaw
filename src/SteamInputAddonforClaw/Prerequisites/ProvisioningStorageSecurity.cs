namespace SteamInputAddonforClaw.Prerequisites;

using System.Security.AccessControl;
using System.Security.Principal;

internal enum ProvisioningStorageStatus { Trusted, Missing, Unsafe, Indeterminate }
internal sealed record ProvisioningStorageAssessment(ProvisioningStorageStatus Status, string Reason)
{
    public bool AllowsRead => Status is ProvisioningStorageStatus.Trusted or ProvisioningStorageStatus.Missing;
}

internal static class ProvisioningStorageSecurity
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsers = new(WellKnownSidType.AuthenticatedUserSid, null);

    public static ProvisioningStorageAssessment EnsureTrustedStorage(string directory)
    {
        var assessment = Inspect(directory);
        if (assessment.Status != ProvisioningStorageStatus.Missing) return assessment;

        try
        {
            foreach (var segment in GetOwnedPathSegments(directory))
            {
                if (Directory.Exists(segment))
                {
                    var existing = AssessExistingDirectory(segment);
                    if (existing.Status != ProvisioningStorageStatus.Trusted) return existing;
                    continue;
                }

                Directory.CreateDirectory(segment);
                new DirectoryInfo(segment).SetAccessControl(CreateExpectedSecurity());
                var created = AssessExistingDirectory(segment);
                if (created.Status != ProvisioningStorageStatus.Trusted) return created;
            }

            return Inspect(directory);
        }
        catch
        {
            return new(ProvisioningStorageStatus.Indeterminate, "ProvisioningStorageCreationFailed");
        }
    }

    public static ProvisioningStorageAssessment Inspect(string directory)
    {
        try
        {
            foreach (var segment in GetOwnedPathSegments(directory))
            {
                if (!Directory.Exists(segment)) return new(ProvisioningStorageStatus.Missing, "ProvisioningStorageMissing");
                var assessment = AssessExistingDirectory(segment);
                if (assessment.Status != ProvisioningStorageStatus.Trusted) return assessment;
            }

            return new(ProvisioningStorageStatus.Trusted, "ProvisioningStorageTrusted");
        }
        catch (ArgumentException)
        {
            return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStoragePathOutsideProgramData");
        }
        catch
        {
            return new(ProvisioningStorageStatus.Indeterminate, "ProvisioningStorageInspectionFailed");
        }
    }

    private static IEnumerable<string> GetOwnedPathSegments(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
        var target = Path.GetFullPath(directory);
        var prefix = root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Provisioning storage must be under ProgramData.", nameof(directory));
        var current = root;
        foreach (var part in target[prefix.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            yield return current;
        }
    }

    private static ProvisioningStorageAssessment AssessExistingDirectory(string directory)
    {
        var info = new DirectoryInfo(directory);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageReparsePoint");
        var security = info.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        return AssessAcl(owner, rules);
    }

    internal static ProvisioningStorageAssessment AssessAcl(SecurityIdentifier? owner, IEnumerable<FileSystemAccessRule> rules)
    {
        if (owner is null || (!owner.Equals(Administrators) && !owner.Equals(System))) return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageOwnerUnsafe");
        var materialized = rules.ToArray();
        if (!Allows(materialized, System, FileSystemRights.FullControl) || !Allows(materialized, Administrators, FileSystemRights.FullControl) || !Allows(materialized, Users, FileSystemRights.ReadAndExecute))
            return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageAclIncomplete");
        var prohibited = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (materialized.Any(rule => rule.AccessControlType == AccessControlType.Allow && IsBroadPrincipal((SecurityIdentifier)rule.IdentityReference) && (rule.FileSystemRights & prohibited) != 0))
            return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageAclUnsafe");
        return new(ProvisioningStorageStatus.Trusted, "ProvisioningStorageTrusted");
    }

    private static DirectorySecurity CreateExpectedSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static bool Allows(IEnumerable<FileSystemAccessRule> rules, SecurityIdentifier principal, FileSystemRights required) =>
        rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && ((SecurityIdentifier)rule.IdentityReference).Equals(principal) && (rule.FileSystemRights & required) == required);

    private static bool IsBroadPrincipal(SecurityIdentifier principal) => principal.Equals(Users) || principal.Equals(Everyone) || principal.Equals(AuthenticatedUsers);
}
