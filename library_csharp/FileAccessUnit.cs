using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CheatEngine.Library;

public static class FileAccessUnit
{
    public static void MakePathAccessible(string path)
    {
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var rule = new FileSystemAccessRule(
            everyone,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            security.ModifyAccessRule(AccessControlModification.Add, rule, out _);
            directory.SetAccessControl(security);
            return;
        }

        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            security.ModifyAccessRule(AccessControlModification.Add, rule, out _);
            file.SetAccessControl(security);
            return;
        }

        throw new DirectoryNotFoundException(path);
    }
}