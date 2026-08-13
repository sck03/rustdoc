using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed record CloudBackupFileInfo(
        string FileName,
        long SizeBytes,
        DateTimeOffset LastModified);

    public interface ICloudSyncService
    {
        Task UploadFileAsync(
            string localFilePath,
            string remoteFileName,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<CloudBackupFileInfo>> ListBackupFilesAsync(
            CancellationToken cancellationToken = default);
        Task DownloadFileAsync(
            string remoteFileName,
            string localFilePath,
            CancellationToken cancellationToken = default);
        Task<bool> TestConnectionAsync(
            WebDavSettings settings,
            CancellationToken cancellationToken = default);
    }
}
