using ExportDocManager.Services;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Tools;
using ExportDocManager.Services.BrowserRuntime;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class RuntimeDependencyDiagnosticsService : IRuntimeDependencyDiagnosticsService
    {
        private readonly IAppPathProvider _pathProvider;

        public RuntimeDependencyDiagnosticsService(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public IReadOnlyList<RuntimeDependencyDiagnostic> Inspect()
        {
            return
            [
                InspectReportRenderer(),
                InspectBrowserAutomation(),
                InspectOcrRuntime(),
                InspectPostgreSqlTools()
            ];
        }

        private RuntimeDependencyDiagnostic InspectBrowserAutomation()
        {
            try
            {
                string executablePath = new BrowserExecutableResolver(_pathProvider).Resolve();
                return new RuntimeDependencyDiagnostic(
                    "browser-automation",
                    "受控网页自动化",
                    "optional",
                    "ready",
                    true,
                    executablePath,
                    "Playwright 将通过受控 Chromium/CDP 连接执行 HS 查询降级；进程由应用登记、限流和退出清理。");
            }
            catch (InvalidOperationException)
            {
                return new RuntimeDependencyDiagnostic(
                    "browser-automation",
                    "受控网页自动化",
                    "optional",
                    "missing",
                    false,
                    Path.GetFullPath(_pathProvider.BrowserRoot),
                    "未找到浏览器运行包；HS 本地库和静态 HTTP 查询仍可使用，只有动态网页降级不可用。");
            }
        }

        private RuntimeDependencyDiagnostic InspectReportRenderer()
        {
            try
            {
                string executablePath = new ChromiumHtmlToPdfService(_pathProvider).ResolveRendererExecutablePath();
                return new RuntimeDependencyDiagnostic(
                    "report-renderer",
                    "报表 PDF 浏览器",
                    "feature",
                    "ready",
                    true,
                    executablePath,
                    "报表 PDF 浏览器可执行文件已就绪。");
            }
            catch (InvalidOperationException)
            {
                return new RuntimeDependencyDiagnostic(
                    "report-renderer",
                    "报表 PDF 浏览器",
                    "feature",
                    "missing",
                    false,
                    Path.GetFullPath(_pathProvider.BrowserRoot),
                    "未找到随程序发布的浏览器运行包；HTML 预览仍可使用，但 PDF 生成不可用。");
            }
        }

        private RuntimeDependencyDiagnostic InspectOcrRuntime()
        {
            OcrRuntimeAvailability availability = OcrRuntimeAvailabilityInspector.Inspect(_pathProvider);
            return new RuntimeDependencyDiagnostic(
                "ocr-runtime",
                "智能 OCR",
                "optional",
                availability.Status,
                availability.Ready,
                Path.GetFullPath(availability.ModelBasePath),
                availability.Message);
        }

        private RuntimeDependencyDiagnostic InspectPostgreSqlTools()
        {
            PostgreSqlToolPaths tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            string resolvedPath = string.IsNullOrWhiteSpace(tools.BinRoot)
                ? Path.Combine(_pathProvider.ToolRoot, "PostgreSQL", "bin")
                : tools.BinRoot;

            if (tools.ToolsReady)
            {
                return new RuntimeDependencyDiagnostic(
                    "postgresql-tools",
                    "PostgreSQL 维护工具",
                    "optional",
                    "ready",
                    true,
                    Path.GetFullPath(resolvedPath),
                    "pg_dump、pg_restore 和 psql 已就绪。");
            }

            if (tools.AvailableToolCount > 0)
            {
                return new RuntimeDependencyDiagnostic(
                    "postgresql-tools",
                    "PostgreSQL 维护工具",
                    "optional",
                    "incomplete",
                    false,
                    Path.GetFullPath(resolvedPath),
                    $"PostgreSQL 客户端工具不完整，仅找到 {tools.AvailableToolCount}/3 个文件。");
            }

            return new RuntimeDependencyDiagnostic(
                "postgresql-tools",
                "PostgreSQL 维护工具",
                "optional",
                "missing",
                false,
                Path.GetFullPath(resolvedPath),
                "未安装 PostgreSQL 维护工具；SQLite 单机版和普通业务功能不受影响。");
        }
    }
}
