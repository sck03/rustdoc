using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;
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
    }
}
