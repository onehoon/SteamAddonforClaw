using System.Security.AccessControl;
using System.Security.Principal;

namespace SteamInputAddonforClaw.MsiLiveUpdateHelper;

internal static class MsiLiveUpdatePathSecurity
{
    private const FileSystemRights MutatingRights =
        FileSystemRights.WriteData
        | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership;

    internal static HashSet<string> GetUnelevatedUserSids()
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null).Value
        };

        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is not null) sids.Add(identity.User.Value);
        if (identity.Groups is not null)
        {
            foreach (IdentityReference group in identity.Groups)
            {
                var sid = (SecurityIdentifier)group.Translate(typeof(SecurityIdentifier));
                if (sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)) continue;
                sids.Add(sid.Value);
            }
        }

        return sids;
    }

    internal static bool IsFileSystemObjectProtected(FileSystemSecurity security, HashSet<string> unprivilegedSids)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || unprivilegedSids.Contains(owner.Value)) return false;

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & MutatingRights) == 0)
                continue;
            if (unprivilegedSids.Contains(((SecurityIdentifier)rule.IdentityReference).Value)) return false;
        }

        return true;
    }
}
