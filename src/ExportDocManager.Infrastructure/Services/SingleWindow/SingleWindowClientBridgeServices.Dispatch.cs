using System.Data;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge
    {
        public async Task<SingleWindowClientDispatchResult> DispatchBatchToImportRootAsync(
            int batchId,
            CancellationToken cancellationToken = default)
        {
            EnsureSqliteStation();
            var profile = await _clientProfileService.GetActiveAsync(cancellationToken);
            string stationKey = await _stationIdentity
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);
            var reservation = await ReserveClientDispatchAsync(
                batchId,
                profile,
                stationKey,
                cancellationToken);
            SingleWindowClientFolderLayout? layout = null;
            string stagingDirectory = string.Empty;
            IReadOnlyList<string> publishedFiles = [];
            try
            {
                string importRootPath = ResolveConfiguredRoot(profile, reservation.BusinessType);
                SingleWindowClientFolderLayout currentLayout = ResolveBusinessLayout(
                    importRootPath,
                    createDirectories: true);
                layout = currentLayout;
                await SetClientDispatchPathAsync(
                    batchId,
                    reservation.OperationId,
                    currentLayout.OutBox,
                    cancellationToken);
                stagingDirectory = Path.Combine(
                    _pathProvider.SingleWindowRoot,
                    "DispatchStaging",
                    $"{reservation.BatchReference}-{Guid.NewGuid():N}");
                PathBoundaryHelper.EnsureNoLinkLikeComponents(
                    stagingDirectory,
                    "单一窗口客户端派发暂存目录无效。");
                PathBoundaryHelper.EnsureWithinRoot(
                    stagingDirectory,
                    _pathProvider.SingleWindowRoot,
                    "单一窗口客户端派发暂存目录越界。");
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var batch = await _businessDataAccessScope
                    .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                    .FirstAsync(item => item.Id == batchId, cancellationToken);
                var workingPackage = await EnsureWorkingPackageAsync(context, batch, cancellationToken);
                var stagedFiles = await CopyPayloadFilesToOutBoxAsync(
                    workingPackage.Directory,
                    workingPackage.Manifest,
                    stagingDirectory,
                    batch.BatchReference,
                    cancellationToken);
                publishedFiles = await PublishPayloadFilesAsync(
                    stagedFiles,
                    currentLayout.OutBox,
                    batch.BatchReference,
                    cancellationToken);
                await CompleteClientDispatchAsync(
                    batchId,
                    profile,
                    stationKey,
                    reservation.OperationId,
                    currentLayout.OutBox,
                    publishedFiles.Count,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                if (ex is ClientDispatchPublicationException publicationException)
                {
                    publishedFiles = publicationException.PublishedFiles;
                }
                // A publish failure is reconciled conservatively.  Files that are
                // already in the official OutBox are never removed automatically;
                // the persisted failure state carries the directory for manual
                // reconciliation before another attempt is allowed.
                _logger.LogError(
                    ex,
                    "Single Window client dispatch {BatchId} operation {OperationId} failed after publishing {PublishedFileCount} file(s)",
                    batchId,
                    reservation.OperationId,
                    publishedFiles.Count);
                await MarkClientDispatchFailedAsync(
                    batchId,
                    reservation.OperationId,
                    BuildDispatchFailureMessage(ex, publishedFiles.Count > 0),
                    publishedFiles.Count > 0 ? layout?.OutBox : null,
                    CancellationToken.None);
                throw;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(stagingDirectory))
                {
                    AtomicFileHelper.TryDeleteDirectory(stagingDirectory);
                }
            }

            return new SingleWindowClientDispatchResult
            {
                BatchId = reservation.BatchId,
                BatchReference = reservation.BatchReference,
                TargetDirectory = (layout ?? throw new InvalidOperationException(
                    "客户端派发完成但未生成目标目录。")).OutBox,
                ProfileName = profile.ProfileName,
                PayloadFileCount = publishedFiles.Count,
                AttachmentFileCount = reservation.AttachmentFileCount
            };
        }

        private async Task<ClientDispatchReservation> ReserveClientDispatchAsync(
            int batchId,
            SwClientProfile profile,
            string stationKey,
            CancellationToken cancellationToken)
        {
            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var batch = await _businessDataAccessScope
                        .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                        .FirstOrDefaultAsync(item => item.Id == batchId, token)
                        ?? throw new ResourceNotFoundException("未找到要写入交接 OutBox 的单一窗口批次。");
                    if (!Enum.TryParse<SingleWindowBusinessType>(batch.BusinessType, true, out var businessType))
                    {
                        throw new ServiceValidationException("单一窗口批次业务类型无效。");
                    }

                    EnsureBatchBelongsToCurrentStation(batch, profile, stationKey, businessType);
                    if (batch.Status is not SingleWindowBatchStatusCatalog.SubmitPackageImported and
                        not SingleWindowBatchStatusCatalog.ClientDispatchFailed)
                    {
                        throw new ResourceConflictException(
                            "只有本机已导入且尚未派发，或上次派发已完整回滚的提交包可以写入官方客户端。");
                    }
                    if (batch.Status == SingleWindowBatchStatusCatalog.ClientDispatchFailed &&
                        !string.IsNullOrWhiteSpace(batch.ClientDispatchPath))
                    {
                        throw new ResourceConflictException(
                            "上次派发已登记官方客户端目录，结果可能已被客户端读取；请人工核对 OutBox 后再决定是否重试。");
                    }

                    DateTimeOffset nowUtc = _clock.UtcNow;
                    string operationId = $"SWD-{Guid.NewGuid():N}";
                    batch.Status = SingleWindowBatchStatusCatalog.ClientDispatching;
                    batch.ClientProfileName = profile.ProfileName;
                    batch.AssignedStationKey = stationKey;
                    batch.AssignedProfileKey = profile.ProfileKey;
                    batch.AssignedCardIdentifier = profile.CardIdentifier;
                    batch.LastError = string.Empty;
                    batch.ClientDispatchOperationId = operationId;
                    batch.ClientDispatchLeaseUntil = nowUtc.AddMinutes(10);
                    batch.ClientDispatchAttemptCount = checked(batch.ClientDispatchAttemptCount + 1);
                    batch.ClientDispatchPayloadDigest = batch.SubmitPackageDigest;
                    batch.ClientDispatchPath = string.Empty;
                    batch.UpdatedAt = nowUtc;
                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new ServiceConcurrencyException(
                            "该单一窗口批次正在被其他派发操作处理，请刷新后重试。",
                            exception);
                    }
                    return new ClientDispatchReservation(
                        batch.Id,
                        batch.BatchReference,
                        businessType,
                        batch.AttachmentFileCount,
                        operationId,
                        batch.PayloadFileCount,
                        batch.SubmitPackageDigest);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private async Task SetClientDispatchPathAsync(
            int batchId,
            string operationId,
            string outBoxPath,
            CancellationToken cancellationToken)
        {
            await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var batch = await _businessDataAccessScope
                        .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                        .FirstOrDefaultAsync(item => item.Id == batchId, token)
                        ?? throw new ResourceNotFoundException("客户端派发批次在登记目标目录阶段不存在。");
                    if (batch.Status != SingleWindowBatchStatusCatalog.ClientDispatching ||
                        !string.Equals(batch.ClientDispatchOperationId, operationId, StringComparison.Ordinal))
                    {
                        throw new ServiceConcurrencyException("客户端派发操作已被其他请求接管，不能登记目标目录。");
                    }

                    batch.ClientDispatchPath = outBoxPath;
                    batch.UpdatedAt = _clock.UtcNow;
                    await context.SaveChangesAsync(token);
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private async Task CompleteClientDispatchAsync(
            int batchId,
            SwClientProfile profile,
            string stationKey,
            string operationId,
            string outBoxPath,
            int payloadFileCount,
            CancellationToken cancellationToken)
        {
            await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var batch = await _businessDataAccessScope
                        .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                        .FirstOrDefaultAsync(item => item.Id == batchId, token)
                        ?? throw new ResourceNotFoundException("客户端派发批次在确认阶段不存在。");
                    EnsureBatchBelongsToCurrentStation(
                        batch,
                        profile,
                        stationKey,
                        Enum.Parse<SingleWindowBusinessType>(batch.BusinessType, true));
                    if (batch.Status != SingleWindowBatchStatusCatalog.ClientDispatching)
                    {
                        throw new ServiceConcurrencyException("客户端派发状态已被其他操作修改，不能确认完成。");
                    }
                    if (!string.Equals(batch.ClientDispatchOperationId, operationId, StringComparison.Ordinal))
                    {
                        throw new ServiceConcurrencyException("客户端派发操作已被其他请求接管，不能确认完成。");
                    }

                    DateTimeOffset nowUtc = _clock.UtcNow;
                    batch.Status = SingleWindowBatchStatusCatalog.QueuedToClient;
                    batch.ClientDispatchPath = outBoxPath;
                    batch.LastClientDispatchAt = nowUtc;
                    batch.ClientDispatchLeaseUntil = null;
                    batch.LastError = string.Empty;
                    batch.UpdatedAt = nowUtc;
                    context.SwHandoffPackageRecords.Add(new SwHandoffPackageRecord
                    {
                        BatchId = batch.Id,
                        BatchReference = batch.BatchReference,
                        BusinessType = batch.BusinessType,
                        SourceInvoiceId = batch.SourceInvoiceId,
                        SourceDocumentType = batch.SourceDocumentType,
                        SourceDocumentId = batch.SourceDocumentId,
                        InvoiceNo = batch.InvoiceNo,
                        CompanyScope = batch.CompanyScope,
                        StationKey = stationKey,
                        PackageType = "ClientDispatch",
                        Direction = "ExportedToClient",
                        FilePath = outBoxPath,
                        CreatedOnMachine = Environment.MachineName,
                        PayloadFileCount = payloadFileCount,
                        AttachmentFileCount = batch.AttachmentFileCount,
                        WarningCount = batch.WarningCount,
                        ContentDigest = batch.SubmitPackageDigest,
                        CreatedAt = nowUtc,
                        ManifestJson = string.Empty
                    });
                    try
                    {
                        await context.SaveChangesAsync(token);
                    }
                    catch (DbUpdateConcurrencyException exception)
                    {
                        throw new ServiceConcurrencyException(
                            "客户端派发状态已被其他操作修改，未确认本次派发。",
                            exception);
                    }
                    return true;
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        private async Task MarkClientDispatchFailedAsync(
            int batchId,
            string operationId,
            string errorMessage,
            string? publishedPath,
            CancellationToken cancellationToken)
        {
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var batch = await _businessDataAccessScope
                    .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                    .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken);
                if (batch == null || batch.Status != SingleWindowBatchStatusCatalog.ClientDispatching ||
                    !string.Equals(batch.ClientDispatchOperationId, operationId, StringComparison.Ordinal))
                {
                    return;
                }

                batch.Status = SingleWindowBatchStatusCatalog.ClientDispatchFailed;
                batch.ClientDispatchLeaseUntil = null;
                batch.ClientDispatchPath = publishedPath ?? string.Empty;
                batch.LastError = TruncateDispatchError(errorMessage);
                batch.UpdatedAt = _clock.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A recovery or retry won the lease race.  Never downgrade its
                // state with a late failure callback.
            }
            catch (Exception trackingException)
            {
                _logger.LogError(
                    trackingException,
                    "Marking Single Window client dispatch {BatchId} as failed failed",
                    batchId);
            }
        }

        private static string BuildDispatchFailureMessage(Exception exception, bool publishedFilesRetained)
        {
            string prefix = publishedFilesRetained
                ? "报文已写入官方客户端 OutBox，但派发状态确认失败；请先检查 OutBox，确认官方客户端是否已读取后再重试。"
                : "写入官方客户端 OutBox 失败。";

            // Only service exceptions whose messages are deliberately authored for
            // users may cross the persistence boundary.  Raw IO, database and
            // process messages can disclose paths, SQL or host details.
            if (exception is UserVisibleInfrastructureException or
                ServiceValidationException or
                PermissionDeniedException or
                ResourceConflictException)
            {
                string detail = exception.Message?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    return TruncateDispatchError($"{prefix} {detail}");
                }
            }

            return prefix;
        }

        private static string TruncateDispatchError(string? value)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? "写入官方客户端目录失败。"
                : value.Trim();
            return normalized.Length <= 2000 ? normalized : normalized[..2000];
        }

        private static string ResolveConfiguredRoot(
            SwClientProfile profile,
            SingleWindowBusinessType businessType)
        {
            string resolved = SingleWindowClientProfilePathResolver.ResolveConfiguredRoot(profile, businessType);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new ServiceValidationException("本机操作卡尚未配置该业务的官方单一窗口客户端目录。");
            }

            return SingleWindowClientProfilePathResolver.NormalizeClientRootPath(resolved);
        }

        private static void EnsureBatchBelongsToCurrentStation(
            SwSubmissionBatch batch,
            SwClientProfile profile,
            string stationKey,
            SingleWindowBusinessType businessType)
        {
            if (profile.Id <= 0 || !profile.IsEnabled)
            {
                throw new ServiceValidationException("请先完成本持卡机的公司抬头、操作卡和官方客户端目录配置。");
            }

            if (!string.Equals(batch.AssignedStationKey, stationKey, StringComparison.Ordinal) ||
                !string.Equals(profile.StationKey, stationKey, StringComparison.Ordinal))
            {
                throw new PermissionDeniedException("该提交包不属于当前持卡机。");
            }
            if (!string.Equals(batch.AssignedProfileKey, profile.ProfileKey, StringComparison.Ordinal) ||
                !string.Equals(batch.AssignedCardIdentifier, profile.CardIdentifier, StringComparison.Ordinal))
            {
                throw new PermissionDeniedException(
                    "该批次绑定了其他公司或操作卡档案，请先切换到导入该批次的操作档案。");
            }

            if (!string.Equals(batch.CompanyScope, profile.CompanyScope, StringComparison.OrdinalIgnoreCase))
            {
                throw new PermissionDeniedException("提交包公司抬头与本机操作卡绑定公司不一致。");
            }

            bool canHandle = businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => profile.CanSubmitCustomsCoo,
                SingleWindowBusinessType.AgentConsignment => profile.CanSubmitAgentConsignment,
                _ => false
            };
            if (!canHandle)
            {
                throw new PermissionDeniedException("本机操作卡未启用该单一窗口业务能力。");
            }
        }

        private async Task<SingleWindowWorkingPackage> EnsureWorkingPackageAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            string restoredDirectory = Path.Combine(
                _pathProvider.SingleWindowRoot,
                "Inbox",
                batch.BatchReference);
            PathBoundaryHelper.EnsureWithinRoot(
                restoredDirectory,
                _pathProvider.SingleWindowRoot,
                "单一窗口提交包恢复目录越界。");
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                restoredDirectory,
                _pathProvider.SingleWindowRoot,
                "单一窗口提交包恢复目录无效。");

            if (Directory.Exists(restoredDirectory))
            {
                try
                {
                    var manifest = await ValidateWorkingDirectoryAsync(restoredDirectory, batch, cancellationToken);
                    return new SingleWindowWorkingPackage(restoredDirectory, manifest);
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
                {
                    AtomicFileHelper.TryDeleteDirectory(restoredDirectory);
                    EnsureWorkingDirectoryRemoved(restoredDirectory);
                }
            }

            string packagePath = await ResolveLocalSubmitPackageAsync(context, batch, cancellationToken);
            Directory.CreateDirectory(restoredDirectory);
            try
            {
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    restoredDirectory,
                    _pathProvider.SingleWindowRoot,
                    "单一窗口提交包恢复目录无效。");
                await ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, restoredDirectory, cancellationToken);
                var manifest = await ValidateWorkingDirectoryAsync(restoredDirectory, batch, cancellationToken);
                return new SingleWindowWorkingPackage(restoredDirectory, manifest);
            }
            catch
            {
                AtomicFileHelper.TryDeleteDirectory(restoredDirectory);
                EnsureWorkingDirectoryRemoved(restoredDirectory);
                throw;
            }
        }

        private async Task<string> ResolveLocalSubmitPackageAsync(
            AppDbContext context,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            string? managedPath = TryResolveManagedSubmitPackagePath(
                batch.SubmitPackagePath,
                _pathProvider.SingleWindowRoot);
            if (managedPath != null)
            {
                string localDigest = await SingleWindowPackageIntegrity.ComputeFileSha256Async(
                    managedPath,
                    cancellationToken);
                if (string.Equals(
                        localDigest,
                        batch.SubmitPackageArchiveSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return managedPath;
                }
            }

            var archive = await context.SwSubmitPackageArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.BatchId == batch.Id, cancellationToken)
                ?? throw new ResourceNotFoundException("共享数据库中缺少该批次的提交包归档，无法在本操作机恢复。");
            if (archive.Content == null || archive.Content.LongLength != archive.SizeBytes || archive.SizeBytes <= 0)
            {
                throw new InvalidDataException("共享数据库中的提交包归档大小无效。");
            }

            string archiveDigest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(archive.Content));
            if (!string.Equals(archiveDigest, archive.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archiveDigest, batch.SubmitPackageArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("共享数据库中的提交包归档摘要不匹配。");
            }

            string cachePath = Path.Combine(
                _pathProvider.SingleWindowRoot,
                "PackageCache",
                $"{batch.BatchReference}.swpkg");
            PathBoundaryHelper.EnsureWithinRoot(
                cachePath,
                _pathProvider.SingleWindowRoot,
                "单一窗口提交包缓存路径越界。");
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                cachePath,
                _pathProvider.SingleWindowRoot,
                "单一窗口提交包缓存路径无效。");
            await AtomicFileHelper.WriteFileAtomicAsync(
                cachePath,
                (tempPath, token) => File.WriteAllBytesAsync(tempPath, archive.Content, token),
                cancellationToken);
            return cachePath;
        }

        /// <summary>
        /// Database paths may originate from a desktop file picker.  They are
        /// metadata only: dispatch can read a local package directly only when
        /// the path is a normal file below the managed SingleWindow root.  Any
        /// external, traversal, directory, or link-like path falls back to the
        /// verified database archive instead of becoming a file-read primitive.
        /// </summary>
        internal static string? TryResolveManagedSubmitPackagePath(
            string? storedPath,
            string singleWindowRoot)
        {
            if (string.IsNullOrWhiteSpace(storedPath) || string.IsNullOrWhiteSpace(singleWindowRoot))
            {
                return null;
            }

            string raw = storedPath.Trim();
            if (ContainsTraversalSegment(raw))
            {
                return null;
            }

            string root;
            string candidate;
            try
            {
                root = Path.GetFullPath(singleWindowRoot);
                bool rooted = Path.IsPathRooted(raw);
                if (rooted && !Path.IsPathFullyQualified(raw))
                {
                    return null;
                }

                candidate = Path.GetFullPath(rooted ? raw : Path.Combine(root, raw));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            if (string.Equals(candidate, root, PathBoundaryHelper.PathComparison) ||
                !PathBoundaryHelper.IsWithinRoot(candidate, root))
            {
                return null;
            }

            string fileName = Path.GetFileName(candidate);
            if (!CrossPlatformFileNamePolicy.IsSafeFileName(fileName))
            {
                return null;
            }

            try
            {
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    candidate,
                    root,
                    "单一窗口提交包本地路径无效。");
                FileAttributes attributes = File.GetAttributes(candidate);
                if ((attributes & FileAttributes.Directory) != 0 ||
                    (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }

                return File.Exists(candidate) ? candidate : null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool ContainsTraversalSegment(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, ".", StringComparison.Ordinal) ||
                                string.Equals(segment, "..", StringComparison.Ordinal));
        }

        private static async Task<SingleWindowPackageManifest> ValidateWorkingDirectoryAsync(
            string workingDirectory,
            SwSubmissionBatch batch,
            CancellationToken cancellationToken)
        {
            string manifestPath = Path.Combine(workingDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("恢复的单一窗口提交包缺少 manifest.json。", manifestPath);
            }

            var manifest = JsonSerializer.Deserialize<SingleWindowPackageManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken))
                ?? throw new InvalidDataException("恢复的单一窗口提交包 manifest 无效。");
            await SingleWindowPackageIntegrity.ValidateAsync(
                workingDirectory,
                manifest,
                SingleWindowPackageType.SubmitPackage,
                cancellationToken);

            bool matches = string.Equals(manifest.BatchReference, batch.BatchReference, StringComparison.Ordinal) &&
                           manifest.SourceInvoiceId == batch.SourceInvoiceId &&
                           manifest.SourceDocumentId == batch.SourceDocumentId &&
                           manifest.SubmissionVersion == batch.SubmissionVersion &&
                           string.Equals(manifest.BusinessType.ToString(), batch.BusinessType, StringComparison.Ordinal) &&
                           string.Equals(manifest.CompanyScope, batch.CompanyScope, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(manifest.ContentDigest, batch.SubmitPackageDigest, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                throw new InvalidDataException("恢复的单一窗口提交包与领取工单的批次绑定不一致。");
            }

            return manifest;
        }

        private static async Task<IReadOnlyList<string>> CopyPayloadFilesToOutBoxAsync(
            string sourceDirectory,
            SingleWindowPackageManifest manifest,
            string outBoxDirectory,
            string batchReference,
            CancellationToken cancellationToken)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                outBoxDirectory,
                "单一窗口派发暂存目录无效。");
            Directory.CreateDirectory(outBoxDirectory);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                outBoxDirectory,
                "单一窗口派发暂存目录无效。");
            var targetFiles = new List<string>();
            try
            {
                foreach (var payload in manifest.PayloadFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string sourcePath = PathBoundaryHelper.ResolveProtocolRelativePath(
                        sourceDirectory,
                        payload.RelativePath,
                        "单一窗口提交报文路径越界。");
                    if (!string.Equals(Path.GetExtension(sourcePath), ".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("官方客户端只接受提交包中声明的 XML 报文。");
                    }

                    string fileName = Path.GetFileName(sourcePath);
                    string targetPath = BuildOutBoxFilePath(outBoxDirectory, fileName, batchReference);
                    await FileCopyHelper.CopyAsync(sourcePath, targetPath, overwrite: false, cancellationToken);
                    targetFiles.Add(targetPath);
                }

                if (targetFiles.Count == 0)
                {
                    throw new InvalidDataException("提交包没有可写入交接 OutBox 的 XML 报文。");
                }

                return targetFiles;
            }
            catch
            {
                foreach (string targetFile in targetFiles)
                {
                    AtomicFileHelper.TryDeleteFile(targetFile);
                }

                throw;
            }
        }

        internal static async Task<IReadOnlyList<string>> PublishPayloadFilesAsync(
            IReadOnlyList<string> stagedFiles,
            string outBoxDirectory,
            string batchReference,
            CancellationToken cancellationToken,
            Action<int, string>? beforeCommit = null)
        {
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                outBoxDirectory,
                "官方客户端 OutBox 目录无效。");
            Directory.CreateDirectory(outBoxDirectory);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                outBoxDirectory,
                "官方客户端 OutBox 目录无效。");
            var publishedFiles = new List<string>(stagedFiles?.Count ?? 0);
            var publishPlans = new List<(string PendingPath, string TargetPath)>(stagedFiles?.Count ?? 0);
            var reservedTargets = new HashSet<string>(PhysicalPathComparison.Comparer);
            try
            {
                foreach (string stagedFile in stagedFiles ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string targetPath = BuildOutBoxFilePath(
                        outBoxDirectory,
                        Path.GetFileName(stagedFile),
                        batchReference,
                        reservedTargets);
                    reservedTargets.Add(targetPath);
                    string pendingPath = targetPath + $".pending-{Guid.NewGuid():N}";
                    await FileCopyHelper.CopyAsync(
                        stagedFile,
                        pendingPath,
                        overwrite: false,
                        cancellationToken);
                    publishPlans.Add((pendingPath, targetPath));
                }

                for (int index = 0; index < publishPlans.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var plan = publishPlans[index];
                    beforeCommit?.Invoke(index, plan.TargetPath);
                    PathBoundaryHelper.EnsureNoLinkLikeComponents(
                        outBoxDirectory,
                        "官方客户端 OutBox 目录无效。");
                    PathBoundaryHelper.EnsureNoLinkLikeComponents(
                        plan.PendingPath,
                        "官方客户端待发布文件路径无效。");
                    PathBoundaryHelper.EnsureNoLinkLikeComponents(
                        plan.TargetPath,
                        "官方客户端目标文件路径无效。");
                    File.Move(plan.PendingPath, plan.TargetPath, overwrite: false);
                    publishedFiles.Add(plan.TargetPath);
                }

                return publishedFiles;
            }
            catch (Exception exception)
            {
                // File.Move is the commit point.  If an exception is raised after
                // the move but before publishedFiles.Add (or if a filesystem
                // callback observes the move and interrupts the operation), the
                // target is still a committed official-client payload.  Inspect
                // each plan before removing pending files so that narrow window
                // cannot be mistaken for a fully rolled-back publish.
                var reconciled = new HashSet<string>(publishedFiles, PhysicalPathComparison.Comparer);
                foreach (var plan in publishPlans)
                {
                    if (reconciled.Contains(plan.TargetPath) ||
                        !File.Exists(plan.TargetPath) ||
                        File.Exists(plan.PendingPath))
                    {
                        continue;
                    }

                    reconciled.Add(plan.TargetPath);
                }

                foreach (var plan in publishPlans)
                {
                    AtomicFileHelper.TryDeleteFile(plan.PendingPath);
                }
                // A file that has entered the official OutBox may already have
                // been consumed.  Never delete it automatically; surface the
                // committed paths so the caller can require reconciliation.
                if (reconciled.Count > 0)
                {
                    throw new ClientDispatchPublicationException(
                        exception,
                        publishPlans
                            .Select(plan => plan.TargetPath)
                            .Where(reconciled.Contains)
                            .ToArray());
                }

                throw;
            }
        }

        private sealed record SingleWindowWorkingPackage(
            string Directory,
            SingleWindowPackageManifest Manifest);

        private sealed record ClientDispatchReservation(
            int BatchId,
            string BatchReference,
            SingleWindowBusinessType BusinessType,
            int AttachmentFileCount,
            string OperationId,
            int PayloadFileCount,
            string PayloadDigest);

        private sealed class ClientDispatchPublicationException : Exception
        {
            public ClientDispatchPublicationException(
                Exception innerException,
                IReadOnlyList<string> publishedFiles)
                : base("官方客户端 OutBox 仅完成了部分发布，必须人工核对后再重试。", innerException)
            {
                PublishedFiles = publishedFiles;
            }

            public IReadOnlyList<string> PublishedFiles { get; }
        }
    }
}
