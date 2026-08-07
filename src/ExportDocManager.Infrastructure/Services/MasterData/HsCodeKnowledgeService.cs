using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService : IHsCodeKnowledgeService
    {
        private const string PackageSchemaVersion = "1.0";
        private const long MaximumPackageBytes = 100L * 1024 * 1024;
        private const int SearchExampleCandidateLimit = 2000;
        private const int SearchMasterCandidateLimit = 1000;
        private const int DatabaseInClauseBatchSize = 400;
        private const int KnowledgeResolutionBatchSize = 500;
        private const long MaximumKnowledgeEntryBytes = 100L * 1024L * 1024L;
        private const long MaximumKnowledgeExpandedBytes = 300L * 1024L * 1024L;
        // History discovery is an interactive review screen, not an export job. Keep each
        // request bounded so a growing invoice archive cannot turn one page load into a
        // full-database materialization. Users can narrow the window with a keyword.
        private const int HistoryRecentSourceLimit = 2500;
        private const int HistoryKeywordSourceLimit = 5000;
        private const int MaximumHistoryKeywordLength = 200;
        private const int MaximumKnowledgeQueryLength = 500;
        private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public HsCodeKnowledgeService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(new DatabaseConnectionSettings());
        }

    }
}
