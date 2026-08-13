using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.BrowserRuntime;

public sealed class BrowserRuntimeDiagnosticContributor : IRuntimeDependencyDiagnosticContributor
{
    private readonly IAppPathProvider _pathProvider;
    private readonly BrowserExecutableResolver _resolver;

    public BrowserRuntimeDiagnosticContributor(
        IAppPathProvider pathProvider,
        BrowserExecutableResolver resolver)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public IReadOnlyList<RuntimeDependencyDiagnostic> Inspect() =>
        [InspectReportRenderer(), InspectBrowserAutomation()];

    private RuntimeDependencyDiagnostic InspectBrowserAutomation() => Inspect(
        "browser-automation",
        "受控网页自动化",
        "optional",
        "Playwright 将连接隔离 Chromium 服务执行 HS 查询降级；浏览器容器不持有数据库维护凭据。",
        "Playwright 将通过受控 Chromium/CDP 连接执行 HS 查询降级；进程由应用登记、限流和退出清理。",
        "HS 本地库和静态 HTTP 查询仍可使用，只有动态网页降级不可用。");

    private RuntimeDependencyDiagnostic InspectReportRenderer() => Inspect(
        "report-renderer",
        "报表 PDF 浏览器",
        "feature",
        "报表 PDF 将由隔离 Chromium 服务生成；临时报表目录以只读方式共享给浏览器容器。",
        "报表 PDF 浏览器可执行文件已就绪。",
        "HTML 预览仍可使用，但 PDF 生成不可用。");

    private RuntimeDependencyDiagnostic Inspect(
        string key,
        string label,
        string requirement,
        string remoteMessage,
        string localMessage,
        string missingSuffix)
    {
        try
        {
            if (BrowserCdpEndpointPolicy.TryResolve(out Uri? endpoint))
            {
                return new RuntimeDependencyDiagnostic(
                    key, label, requirement, "ready", true,
                    endpoint.ToString().TrimEnd('/'), remoteMessage);
            }

            return new RuntimeDependencyDiagnostic(
                key, label, requirement, "ready", true,
                _resolver.Resolve(), localMessage);
        }
        catch (ServiceException ex)
        {
            return new RuntimeDependencyDiagnostic(
                key, label, requirement, "missing", false,
                Path.GetFullPath(_pathProvider.BrowserRoot),
                $"浏览器运行时配置不可用：{ex.Message} {missingSuffix}");
        }
    }
}
