using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    public sealed class ReportTemplateStorageDiagnosticsService : IReportTemplateStorageDiagnosticsService
    {
        private const string StoragePolicy =
            "程序根 Templates/ 仅保存随程序发布的只读内置模板；新建模板、编辑副本和模板包导入统一使用运行数据根 Templates/。可写性检查只在管理员显式触发时创建短生命周期探针并立即删除。";

        private readonly IAppPathProvider _pathProvider;

        public ReportTemplateStorageDiagnosticsService(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public async Task<ReportTemplateStorageStatus> CheckAsync(CancellationToken cancellationToken = default)
        {
            string templateRoot = Path.GetFullPath(Path.Combine(_pathProvider.DataRoot, "Templates"));
            string builtInTemplateRoot = Path.GetFullPath(_pathProvider.TemplateRoot);
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
                    Message = Directory.Exists(builtInTemplateRoot)
                        ? "内置模板目录可读取，用户模板目录可写；新建、编辑副本和导入功能可正常使用。"
                        : "用户模板目录可写，但未发现随程序发布的内置模板目录，请检查安装包资源。",
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
                    Message = $"用户模板目录不可写：{ex.Message} 请确认运行数据根位于有写入权限的目录，且 Templates 未被其它程序锁定。",
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
