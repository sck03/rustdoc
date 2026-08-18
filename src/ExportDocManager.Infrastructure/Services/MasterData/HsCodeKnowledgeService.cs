using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService : IHsCodeKnowledgeService
    {
        private const string PackageSchemaVersion = "1.0";
        private const int SearchExampleCandidateLimit = 2000;
        private const int SearchMasterCandidateLimit = 1000;
        private const int DatabaseInClauseBatchSize = 400;
        private const int KnowledgeResolutionBatchSize = 500;
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
        private readonly IBusinessClock _clock;

        public HsCodeKnowledgeService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            BusinessDataAccessScope businessDataAccessScope,
            IBusinessClock? clock = null)
        {
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

    }
}
