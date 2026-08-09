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
    public static ProvisioningStorageAssessment EnsureTrustedStorage(string directory)
    {
        var existing = Inspect(directory);
        if (existing.Status != ProvisioningStorageStatus.Missing) return existing;

        try
        {
            Directory.CreateDirectory(directory);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(administrators);
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
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
            if (!Directory.Exists(directory)) return new(ProvisioningStorageStatus.Missing, "ProvisioningStorageMissing");
            var cursor = new DirectoryInfo(directory);
            while (cursor is not null && !string.Equals(cursor.FullName, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase))
            {
                if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageReparsePoint");
                cursor = cursor.Parent;
            }
            var security = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            if (owner is null || (!owner.Equals(administrators) && !owner.Equals(system))) return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageOwnerUnsafe");
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                var authenticated = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                var write = (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.FullControl | FileSystemRights.Delete | FileSystemRights.CreateFiles)) != 0;
                if (rule.AccessControlType == AccessControlType.Allow && write && (sid.Equals(users) || sid.Equals(everyone) || sid.Equals(authenticated))) return new(ProvisioningStorageStatus.Unsafe, "ProvisioningStorageAclUnsafe");
            }
            return new(ProvisioningStorageStatus.Trusted, "ProvisioningStorageTrusted");
        }
        catch { return new(ProvisioningStorageStatus.Indeterminate, "ProvisioningStorageInspectionFailed"); }
    }
}
