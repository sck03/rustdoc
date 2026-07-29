using System.Data;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowTrackingService :
        ISingleWindowTrackingService,
        ISingleWindowOperationCenterService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private readonly ISingleWindowStationIdentityService _stationIdentity;
        private readonly ISingleWindowClientProfileService _clientProfileService;
        private readonly bool _isSqlite;

        public SingleWindowTrackingService(
            IDbContextFactory<AppDbContext> contextFactory,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope,
            ISingleWindowStationIdentityService stationIdentity,
            ISingleWindowClientProfileService clientProfileService)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
            _stationIdentity = stationIdentity ?? throw new ArgumentNullException(nameof(stationIdentity));
            _clientProfileService = clientProfileService ?? throw new ArgumentNullException(nameof(clientProfileService));
            _isSqlite = !DatabaseModeHelper.UsesPostgreSql(normalizedSettings);
        }

        public async Task<SingleWindowSubmissionReservation> ReserveSubmissionAsync(
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            string sourceDocumentType,
            int draftRevision,
            string sourceBaselineHash,
            string invoiceNo,
            string contractNo,
            string companyScope,
            CancellationToken cancellationToken = default)
        {
            if (sourceInvoiceId <= 0 || sourceDocumentId <= 0)
            {
                throw new InvalidOperationException("生成单一窗口提交包前必须先保存有效的来源单据。");
            }

            if (string.IsNullOrWhiteSpace(companyScope))
            {
                throw new InvalidOperationException("发票缺少公司抬头，无法匹配对应操作卡和持卡机。");
            }

            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await AppDbContextExecution.ExecuteInTransactionAsync(
                        _contextFactory,
                        async (context, token) =>
                        {
                            bool canAccess = await _businessDataAccessScope.CanAccessInvoiceAsync(
                                    context,
                                    sourceInvoiceId,
                                    token)
                                .ConfigureAwait(false);
                            if (!canAccess && _businessDataAccessScope.ShouldFilterBusinessData())
                            {
                                throw new UnauthorizedAccessException("无权限为该发票预留单一窗口提交版本。");
                            }

                            int submissionVersion = await ResolveNextSubmissionVersionCoreAsync(
                                    context,
                                    businessType,
                                    sourceInvoiceId,
                                    sourceDocumentId,
                                    token)
                                .ConfigureAwait(false);
                            string batchReference = BuildBatchReference(businessType, submissionVersion);
                            DateTime nowUtc = DateTime.UtcNow;
                            var batch = new SwSubmissionBatch
                            {
                                BatchReference = batchReference,
                                BusinessType = businessType.ToString(),
                                SourceInvoiceId = sourceInvoiceId,
                                SourceDocumentId = sourceDocumentId,
                                SourceDocumentType = sourceDocumentType?.Trim() ?? string.Empty,
                                SubmissionVersion = submissionVersion,
                                DraftRevision = Math.Max(1, draftRevision),
                                SourceBaselineHash = sourceBaselineHash?.Trim() ?? string.Empty,
                                InvoiceNo = invoiceNo?.Trim() ?? string.Empty,
                                ContractNo = contractNo?.Trim() ?? string.Empty,
                                CompanyScope = companyScope?.Trim() ?? string.Empty,
                                Status = SingleWindowBatchStatusCatalog.Preparing,
                                CreatedOnMachine = Environment.MachineName,
                                CreatedAt = nowUtc,
                                UpdatedAt = nowUtc
                            };
                            await context.SwSubmissionBatches.AddAsync(batch, token).ConfigureAwait(false);
                            await context.SaveChangesAsync(token).ConfigureAwait(false);
                            return new SingleWindowSubmissionReservation
                            {
                                BatchId = batch.Id,
                                BatchReference = batch.BatchReference,
                                SubmissionVersion = batch.SubmissionVersion
                            };
                        },
                        IsolationLevel.Serializable,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsReservationConcurrencyConflict(ex))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(15 * attempt), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("无法为单一窗口提交包预留唯一版本，请稍后重试。");
        }

        public async Task MarkSubmissionReservationFailedAsync(
            int batchId,
            string errorMessage,
            CancellationToken cancellationToken = default)
        {
            if (batchId <= 0)
            {
                return;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var batch = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                .FirstOrDefaultAsync(item => item.Id == batchId, cancellationToken)
                .ConfigureAwait(false);
            if (batch == null || batch.Status != SingleWindowBatchStatusCatalog.Preparing)
            {
                return;
            }

            batch.Status = SingleWindowBatchStatusCatalog.Failed;
            batch.LastError = Truncate(errorMessage, 2000);
            batch.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<SingleWindowPackageBinding> ResolveReceiptPackageBindingAsync(
            SingleWindowBusinessType businessType,
            string batchReference,
            string invoiceNo,
            CancellationToken cancellationToken = default)
        {
            string normalizedReference = batchReference?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedReference))
            {
                throw new ArgumentException("单一窗口批次号不能为空。", nameof(batchReference));
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var batches = _businessDataAccessScope.ApplySubmissionBatchScope(
                context.SwSubmissionBatches.AsNoTracking(),
                context);
            string businessTypeText = businessType.ToString();
            var batch = await batches
                .Where(item => item.BatchReference == normalizedReference && item.BusinessType == businessTypeText)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("未找到当前用户可访问的单一窗口提交批次。");

            string normalizedInvoiceNo = invoiceNo?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedInvoiceNo) &&
                !string.Equals(batch.InvoiceNo, normalizedInvoiceNo, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("回执文件对应的发票号与提交批次不一致。");
            }

            if (batch.SourceInvoiceId <= 0 ||
                batch.SubmissionVersion <= 0 ||
                string.IsNullOrWhiteSpace(batch.SubmitPackageDigest))
            {
                throw new InvalidOperationException("提交批次缺少回执绑定所需的来源或摘要信息。");
            }

            if (string.IsNullOrWhiteSpace(batch.AssignedStationKey))
            {
                throw new InvalidOperationException("提交批次尚未由持卡操作机领取并发送，不能生成回执包。");
            }

            if (!_isSqlite)
            {
                throw new InvalidOperationException("回执包只能在独立 SQLite 持卡机上生成。");
            }

            string currentStationKey = await _stationIdentity
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);
            var currentProfile = await _clientProfileService.GetActiveAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(batch.AssignedStationKey, currentStationKey, StringComparison.Ordinal) ||
                !string.Equals(batch.AssignedProfileKey, currentProfile.ProfileKey, StringComparison.Ordinal) ||
                !string.Equals(batch.AssignedCardIdentifier, currentProfile.CardIdentifier, StringComparison.Ordinal) ||
                !string.Equals(batch.CompanyScope, currentProfile.CompanyScope, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("回执包只能由导入并发送原提交包的持卡机和操作卡生成。");
            }

            return new SingleWindowPackageBinding
            {
                BatchId = batch.Id,
                BatchReference = batch.BatchReference,
                BusinessType = businessType,
                SourceInvoiceId = batch.SourceInvoiceId,
                SourceDocumentId = batch.SourceDocumentId,
                SourceDocumentType = batch.SourceDocumentType,
                SubmissionVersion = batch.SubmissionVersion,
                DraftRevision = batch.DraftRevision,
                SourceBaselineHash = batch.SourceBaselineHash,
                InvoiceNo = batch.InvoiceNo,
                ContractNo = batch.ContractNo,
                CompanyScope = batch.CompanyScope,
                SubmitPackageDigest = batch.SubmitPackageDigest,
                AssignedStationKey = batch.AssignedStationKey,
                AssignedProfileKey = batch.AssignedProfileKey,
                AssignedCardIdentifier = batch.AssignedCardIdentifier,
                ClientProfileName = batch.ClientProfileName
            };
        }

        private async Task<int> ResolveNextSubmissionVersionCoreAsync(
            AppDbContext context,
            SingleWindowBusinessType businessType,
            int sourceInvoiceId,
            int sourceDocumentId,
            CancellationToken cancellationToken)
        {
            string businessTypeText = businessType.ToString();

            var batches = context.SwSubmissionBatches
                .AsNoTracking()
                .Where(item => item.BusinessType == businessTypeText);
            batches = _businessDataAccessScope.ApplySubmissionBatchScope(batches, context);

            if (sourceInvoiceId > 0)
            {
                batches = batches.Where(item => item.SourceInvoiceId == sourceInvoiceId);
            }
            else if (sourceDocumentId > 0)
            {
                batches = batches.Where(item => item.SourceDocumentId == sourceDocumentId);
            }

            int currentVersion = await batches
                .Select(item => (int?)item.SubmissionVersion)
                .MaxAsync(cancellationToken)
                ?? 0;

            return Math.Max(1, currentVersion + 1);
        }

        private static string BuildBatchReference(
            SingleWindowBusinessType businessType,
            int submissionVersion)
        {
            string prefix = businessType == SingleWindowBusinessType.CustomsCoo ? "COO" : "ACD";
            string versionText = $"V{Math.Max(1, submissionVersion):000}";
            string guidPart = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            return $"{prefix}-{versionText}-{DateTime.UtcNow:yyyyMMddHHmmss}-{guidPart}".ToUpperInvariant();
        }

        private static bool IsReservationConcurrencyConflict(Exception exception)
        {
            if (exception is PostgresException postgres && postgres.SqlState is "40001" or "23505")
            {
                return true;
            }

            if (exception is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 19)
            {
                return true;
            }

            return exception is DbUpdateException updateException &&
                   updateException.InnerException != null &&
                   IsReservationConcurrencyConflict(updateException.InnerException);
        }

        private static string Truncate(string value, int maxLength)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }
    }
}
