using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportTemplateStorageDiagnosticsService : IReportTemplateStorageDiagnosticsService
    {
        private const string StoragePolicy =
            "报表模板统一位于程序运行目录 Templates/；内置模板、新建模板、模板编辑和模板包导入都使用该目录。可写性检查只在管理员显式触发时创建一个短生命周期探针文件并立即删除，不写系统用户目录、系统级共享数据目录或系统临时目录。";

        private readonly IAppPathProvider _pathProvider;

        public ReportTemplateStorageDiagnosticsService(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public async Task<ReportTemplateStorageStatus> CheckAsync(CancellationToken cancellationToken = default)
        {
            string templateRoot = Path.GetFullPath(_pathProvider.TemplateRoot);
            string probePath = Path.Combine(templateRoot, $".edm-template-write-check-{Guid.NewGuid():N}.tmp");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(templateRoot);
                await File.WriteAllTextAsync(
                        probePath,
                        "ExportDocManager template storage write check",
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(probePath);

                return new ReportTemplateStorageStatus
                {
                    TemplateRoot = templateRoot,
                    Exists = Directory.Exists(templateRoot),
                    Writable = true,
                    Message = "模板目录可读写，新建、编辑和导入模板可继续使用 Templates 目录。",
                    StoragePolicy = StoragePolicy
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ReportTemplateStorageStatus
                {
                    TemplateRoot = templateRoot,
                    Exists = Directory.Exists(templateRoot),
                    Writable = false,
                    Message = $"模板目录不可写：{ex.Message} 请确认程序位于有写入权限的非系统盘运行目录，且 Templates 未被其它程序锁定。",
                    StoragePolicy = StoragePolicy
                };
            }
            finally
            {
                AtomicFileHelper.TryDeleteFile(probePath);
            }
        }
    }
}
