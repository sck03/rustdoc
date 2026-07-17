using ExportDocManager.DataAccess;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class ManualImportClientBridge :
        ISingleWindowClientProfileService,
        ISingleWindowClientBridge
    {
        private const string DefaultProfileName = "默认持卡机";
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ISingleWindowReceiptParser _singleWindowReceiptParser;
        private readonly BusinessDataAccessScope _businessDataAccessScope;
        private readonly IAppPathProvider _pathProvider;

        public ManualImportClientBridge(
            IDbContextFactory<AppDbContext> contextFactory,
            ISingleWindowReceiptParser singleWindowReceiptParser,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope,
            IAppPathProvider pathProvider)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _singleWindowReceiptParser = singleWindowReceiptParser ?? throw new ArgumentNullException(nameof(singleWindowReceiptParser));
            _ = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? throw new ArgumentNullException(nameof(businessDataAccessScope));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }
    }
}
