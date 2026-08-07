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
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
        private static readonly IReadOnlyDictionary<string, string> Synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["T-SHIRT"] = "T恤衫",
            ["TSHIRT"] = "T恤衫",
            ["T恤"] = "T恤衫",
            ["男士"] = "男式",
            ["男款"] = "男式",
            ["MENS"] = "男式",
            ["MEN'S"] = "男式",
            ["女士"] = "女式",
            ["女款"] = "女式",
            ["WOMENS"] = "女式",
            ["WOMEN'S"] = "女式",
            ["全棉"] = "100%棉",
            ["纯棉"] = "100%棉",
            ["COTTON"] = "棉",
            ["针织物"] = "针织",
            ["KNITTED"] = "针织"
        };
        private static readonly IReadOnlyDictionary<string, string> RelatedTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["针织"] = "钩编",
            ["钩编"] = "针织"
        };

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
