using System.Text.Json;
using System.Text.Json.Serialization;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private const string PackageExtension = ".edtpl";
        private const string PackageSchemaVersion = "1.3";

        private const string StoragePolicy =
            "模板包导出路径来自用户显式输入；相对路径解析到运行数据根 TemplatePackages/。只打包和导入运行数据根 Templates/ 下的用户模板，内置模板保持只读；临时文件使用运行数据根 Cache/TemplatePackages。";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        private readonly IAppPathProvider _pathProvider;
        private readonly ISettingsService _settingsService;
        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ReportTemplatePackageReferencePolicy _referencePolicy;
        private readonly ReportTemplateCatalogLoader _catalogLoader;
        private readonly ILogger<ReportTemplatePackageService> _logger;
        private readonly IBusinessClock _clock;
        private readonly ReportTemplateStorageCoordinator _storageCoordinator;

        public ReportTemplatePackageService(
            IAppPathProvider pathProvider,
            ISettingsService settingsService,
            ILogger<ReportTemplatePackageService>? logger = null,
            IBusinessClock? clock = null)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _pathResolver = new ReportTemplatePathResolver(pathProvider);
            _referencePolicy = new ReportTemplatePackageReferencePolicy(_pathResolver);
            _logger = logger ?? NullLogger<ReportTemplatePackageService>.Instance;
            _catalogLoader = new ReportTemplateCatalogLoader(_pathResolver, _logger);
            _clock = clock ?? BusinessClock.CreateSystem();
            _storageCoordinator = new ReportTemplateStorageCoordinator(pathProvider, settingsService, _logger);
        }
    }
}
