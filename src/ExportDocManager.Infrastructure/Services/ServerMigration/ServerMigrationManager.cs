using System.Text.Json;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 服务器迁移控制文件存储。只负责 marker/status 路径与持久化，不参与恢复步骤编排。
/// </summary>
public static class ServerMigrationManager
{
    public static bool HasPendingRestore(IAppPathProvider pathProvider) =>
        File.Exists(GetPendingMarkerPath(pathProvider));

    public static string GetControlRoot(IAppPathProvider pathProvider) =>
        Path.Combine(pathProvider.SecurityRoot, ServerMigrationLayout.ControlDirectoryName);

    public static string GetPendingMarkerPath(IAppPathProvider pathProvider) =>
        Path.Combine(GetControlRoot(pathProvider), ServerMigrationLayout.PendingMarkerFileName);

    public static string GetStatusPath(IAppPathProvider pathProvider) =>
        Path.Combine(GetControlRoot(pathProvider), ServerMigrationLayout.StatusFileName);

    internal static FileStream AcquireExclusiveLock(IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string controlRoot = GetControlRoot(pathProvider);
        Directory.CreateDirectory(controlRoot);
        RuntimeFilePermissionHelper.RestrictDirectory(controlRoot);
        string lockPath = Path.Combine(controlRoot, ServerMigrationLayout.LockFileName);
        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.WriteThrough);
            RuntimeFilePermissionHelper.RestrictFile(lockPath);
            return stream;
        }
        catch (IOException ex)
        {
            throw new ResourceConflictException("已有另一个服务器迁移操作正在执行，请稍后重试。", ex);
        }
    }

    public static string GetSafetyBackupRoot(IAppPathProvider pathProvider, string packageId) =>
        Path.Combine(
            pathProvider.BackupRoot,
            ServerMigrationLayout.PackageDirectoryName,
            "Safety",
            packageId);

    internal static void WritePendingMarker(
        IAppPathProvider pathProvider,
        PendingServerMigrationRestore marker)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentNullException.ThrowIfNull(marker);
        ValidateMarker(marker);
        if (!string.Equals(marker.Phase, ServerMigrationRestorePhase.Pending, StringComparison.Ordinal))
        {
            throw new ServiceValidationException("新的服务器迁移任务必须处于等待重启阶段。");
        }

        string markerPath = GetPendingMarkerPath(pathProvider);
        if (File.Exists(markerPath))
        {
            throw new ResourceConflictException("已有服务器迁移任务等待重启执行。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        RuntimeFilePermissionHelper.RestrictDirectory(Path.GetDirectoryName(markerPath)!);
        WriteMarker(markerPath, marker);
        WriteStatus(
            pathProvider,
            marker,
            "服务器迁移恢复已排队，等待服务重启。",
            GetSafetyBackupRoot(pathProvider, marker.PackageId));
    }

    internal static ServerMigrationRestoreStatusSnapshot ReadStatus(IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string path = GetStatusPath(pathProvider);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ServerMigrationRestoreStatusSnapshot>(
                File.ReadAllText(path),
                ServerMigrationService.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ServerMigrationRestoreStatusSnapshot
            {
                Phase = ServerMigrationRestorePhase.Failed,
                UpdatedAtUtc = File.GetLastWriteTimeUtc(path),
                Message = $"服务器迁移状态文件损坏：{ex.Message}"
            };
        }
    }

    public static Task ApplyPendingRestoreAsync(
        IAppPathProvider pathProvider,
        CancellationToken cancellationToken = default) =>
        ServerMigrationRecoveryStateMachine.ApplyAsync(pathProvider, cancellationToken);

    internal static void WriteStatus(
        IAppPathProvider pathProvider,
        PendingServerMigrationRestore marker,
        string message,
        string safetyBackupRoot)
    {
        string path = GetStatusPath(pathProvider);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var status = new ServerMigrationRestoreStatusSnapshot
        {
            PackageId = marker.PackageId,
            PackageFileName = marker.PackageFileName,
            Phase = marker.Phase,
            Attempt = marker.Attempt,
            RequestedBy = marker.RequestedBy,
            UpdatedAtUtc = marker.UpdatedAtUtc == default
                ? DateTimeOffset.UtcNow
                : marker.UpdatedAtUtc,
            Message = message,
            SafetyBackupRoot = safetyBackupRoot
        };
        AtomicFileHelper.WriteAllTextAtomic(
            path,
            JsonSerializer.Serialize(status, ServerMigrationService.JsonOptions));
        RuntimeFilePermissionHelper.RestrictFile(path);
    }

    internal static void WriteMarker(
        string markerPath,
        PendingServerMigrationRestore marker)
    {
        AtomicFileHelper.WriteAllTextAtomic(
            markerPath,
            JsonSerializer.Serialize(marker, ServerMigrationService.JsonOptions));
        RuntimeFilePermissionHelper.RestrictFile(markerPath);
    }

    internal static void TryWriteMarker(
        string markerPath,
        PendingServerMigrationRestore marker)
    {
        try
        {
            WriteMarker(markerPath, marker);
        }
        catch
        {
        }
    }

    internal static void ValidateMarker(PendingServerMigrationRestore marker)
    {
        if (marker == null ||
            marker.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
            !Guid.TryParseExact(marker.PackageId, "N", out _) ||
            !string.Equals(
                marker.StagingDirectoryName,
                $"pending-{marker.PackageId}",
                StringComparison.Ordinal) ||
            !ServerMigrationRestorePhase.IsKnown(marker.Phase) ||
            marker.Manifest == null ||
            marker.Manifest.SchemaVersion != ServerMigrationLayout.SchemaVersion ||
            !string.Equals(
                marker.Manifest.PackageId,
                marker.PackageId,
                StringComparison.Ordinal) ||
            marker.Manifest.Files is null ||
            marker.Manifest.Files.Any(file => file is null))
        {
            throw new InvalidDataException("服务器迁移恢复标记无效。");
        }
    }
}
