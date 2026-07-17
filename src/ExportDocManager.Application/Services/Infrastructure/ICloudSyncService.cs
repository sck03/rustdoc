using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed record CloudBackupFileInfo(
        string FileName,
        long SizeBytes,
        DateTime LastModified);

    public interface ICloudSyncService
    {
        Task UploadFileAsync(string localFilePath, string remoteFileName);
        Task<IReadOnlyList<CloudBackupFileInfo>> ListBackupFilesAsync();
        Task DownloadFileAsync(string remoteFileName, string localFilePath);
        Task<bool> TestConnectionAsync(WebDavSettings settings);
    }
}
