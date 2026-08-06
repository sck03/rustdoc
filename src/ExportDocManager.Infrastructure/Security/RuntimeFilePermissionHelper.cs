using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace ExportDocManager.Services.Security
{
    public static class RuntimeFilePermissionHelper
    {
        private const UnixFileMode OwnerFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private const UnixFileMode OwnerDirectoryMode = OwnerFileMode | UnixFileMode.UserExecute;

        public static void RestrictFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (OperatingSystem.IsWindows())
            {
                RestrictWindowsFile(Path.GetFullPath(path));
                return;
            }

            File.SetUnixFileMode(path, OwnerFileMode);
        }

        public static void RestrictDirectory(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (OperatingSystem.IsWindows())
            {
                RestrictWindowsDirectory(Path.GetFullPath(path));
                return;
            }

            File.SetUnixFileMode(path, OwnerDirectoryMode);
        }

        [SupportedOSPlatform("windows")]
        private static void RestrictWindowsFile(string path)
        {
            var security = new FileSecurity();
            ConfigureWindowsAcl(security, FileSystemRights.FullControl, InheritanceFlags.None);
            FileSystemAclExtensions.SetAccessControl(new FileInfo(path), security);
        }

        [SupportedOSPlatform("windows")]
        private static void RestrictWindowsDirectory(string path)
        {
            var security = new DirectorySecurity();
            ConfigureWindowsAcl(
                security,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path), security);
        }

        [SupportedOSPlatform("windows")]
        private static void ConfigureWindowsAcl(
            FileSystemSecurity security,
            FileSystemRights rights,
            InheritanceFlags inheritanceFlags)
        {
            var currentUser = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("无法解析当前 Windows 用户 SID。");
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AddFullControl(security, currentUser, rights, inheritanceFlags);
            AddFullControl(
                security,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                rights,
                inheritanceFlags);
            AddFullControl(
                security,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                rights,
                inheritanceFlags);
        }

        [SupportedOSPlatform("windows")]
        private static void AddFullControl(
            FileSystemSecurity security,
            IdentityReference identity,
            FileSystemRights rights,
            InheritanceFlags inheritanceFlags)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                rights,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
    }
}
