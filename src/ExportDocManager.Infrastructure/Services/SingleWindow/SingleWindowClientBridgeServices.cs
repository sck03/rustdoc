using System.Data;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge :
        ISingleWindowClientBridge
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ISingleWindowReceiptParser _singleWindowReceiptParser;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private readonly IAppPathProvider _pathProvider;
        private readonly ISingleWindowClientProfileService _clientProfileService;
        private readonly ISingleWindowStationIdentityService _stationIdentity;
        private readonly bool _isSqlite;
        private readonly ILogger<ManualImportClientBridge> _logger;
        private readonly IBusinessClock _clock;

        public ManualImportClientBridge(
            IDbContextFactory<AppDbContext> contextFactory,
            ISingleWindowReceiptParser singleWindowReceiptParser,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope,
            IAppPathProvider pathProvider,
            ISingleWindowClientProfileService clientProfileService,
            ISingleWindowStationIdentityService stationIdentity,
            ILogger<ManualImportClientBridge>? logger = null,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _singleWindowReceiptParser = singleWindowReceiptParser ?? throw new ArgumentNullException(nameof(singleWindowReceiptParser));
            _isSqlite = !DatabaseModeHelper.UsesPostgreSql(
                databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings)));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _clientProfileService = clientProfileService ?? throw new ArgumentNullException(nameof(clientProfileService));
            _stationIdentity = stationIdentity ?? throw new ArgumentNullException(nameof(stationIdentity));
            _logger = logger ?? NullLogger<ManualImportClientBridge>.Instance;
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        private void EnsureSqliteStation()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "官方单一窗口客户端和实体操作卡只支持 Windows 持卡机；macOS、Linux 和浏览器端只能制作或归档交接包。");
            }

            if (!_isSqlite)
            {
                throw new ServiceValidationException("官方单一窗口客户端只能由独立 SQLite 持卡机操作。");
            }
        }

        /// <summary>
        /// A dispatch is deliberately recovered to a terminal failure state when the
        /// process lease expires.  The official client may have consumed a file while
        /// the API was unavailable, so recovery never guesses that a visible payload is
        /// safe to delete or silently records a second hand-off.  The operator can
        /// inspect the OutBox and retry explicitly after the interrupted operation has
        /// been reconciled.
        /// </summary>
        public async Task<int> RecoverExpiredDispatchesAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_isSqlite || !OperatingSystem.IsWindows())
            {
                return 0;
            }

            DateTimeOffset nowUtc = _clock.UtcNow;
            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var batches = await _businessDataAccessScope
                .ApplySubmissionBatchScope(context.SwSubmissionBatches, context)
                .Where(batch => batch.Status == SingleWindowBatchStatusCatalog.ClientDispatching &&
                                batch.ClientDispatchLeaseUntil.HasValue &&
                                batch.ClientDispatchLeaseUntil.Value <= nowUtc)
                .OrderBy(batch => batch.ClientDispatchLeaseUntil)
                .Take(100)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            int recoveredCount = 0;
            foreach (SwSubmissionBatch candidate in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bool changed = await AppDbContextExecution.ExecuteInTransactionAsync(
                        _contextFactory,
                        async (transactionContext, token) =>
                        {
                            var batch = await _businessDataAccessScope
                                .ApplySubmissionBatchScope(transactionContext.SwSubmissionBatches, transactionContext)
                                .FirstOrDefaultAsync(item => item.Id == candidate.Id, token)
                                .ConfigureAwait(false);
                            if (batch == null ||
                                batch.Status != SingleWindowBatchStatusCatalog.ClientDispatching ||
                                !batch.ClientDispatchLeaseUntil.HasValue ||
                                batch.ClientDispatchLeaseUntil.Value > _clock.UtcNow)
                            {
                                return false;
                            }

                            batch.Status = SingleWindowBatchStatusCatalog.ClientDispatchFailed;
                            batch.ClientDispatchLeaseUntil = null;
                            batch.LastError = string.IsNullOrWhiteSpace(batch.ClientDispatchPath)
                                ? "客户端派发租约已过期，系统未自动认定报文已送达；请检查 OutBox 后重新派发。"
                                : "客户端派发租约已过期且已登记官方客户端目录；系统未自动认定报文已送达，请人工核对 OutBox 后再决定是否重试。";
                            batch.UpdatedAt = _clock.UtcNow;
                            await transactionContext.SaveChangesAsync(token).ConfigureAwait(false);
                            return true;
                        },
                        IsolationLevel.Serializable,
                        cancellationToken)
                        .ConfigureAwait(false);
                    if (changed)
                    {
                        recoveredCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Recovering expired Single Window dispatch {BatchId} failed",
                        candidate.Id);
                }
            }

            return recoveredCount;
        }
    }
}
