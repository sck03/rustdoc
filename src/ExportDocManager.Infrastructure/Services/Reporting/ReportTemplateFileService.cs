using System.Text;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    /// <summary>
    /// Transfers one validated HTML template between a user-selected file and
    /// the managed template catalog. Package import/export remains separate.
    /// </summary>
    public sealed class ReportTemplateFileService : IReportTemplateFileService
    {
        private const long MaximumTemplateFileBytes = 10L * 1024L * 1024L;
        private const string StoragePolicy =
            "单个 HTML 模板文件通过用户显式路径导入或导出；导入仍由模板服务写入运行数据根 Templates/，内置模板不会被改写。";

        private readonly IReportTemplateService _templateService;

        public ReportTemplateFileService(IReportTemplateService templateService)
        {
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        }

        public async Task<ReportTemplateFileExportResult> ExportAsync(
            ReportDocumentType reportType,
            string templatePath,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            string targetPath = NormalizeExportPath(filePath);
            var template = await _templateService.GetTemplateContentAsync(
                    reportType,
                    templatePath,
                    cancellationToken)
                .ConfigureAwait(false);

            await AtomicFileHelper.WriteAllTextAtomicAsync(
                    targetPath,
                    template.Content,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);

            return new ReportTemplateFileExportResult
            {
                FilePath = targetPath,
                Bytes = new FileInfo(targetPath).Length,
                StoragePolicy = StoragePolicy
            };
        }

        public async Task<ReportTemplateContentResult> ImportAsync(
            ReportDocumentType reportType,
            string templatePath,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            string sourcePath = NormalizeImportPath(filePath);
            string content = await File.ReadAllTextAsync(
                    sourcePath,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
            return await _templateService.SaveTemplateContentAsync(
                    reportType,
                    templatePath,
                    content,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static string NormalizeImportPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("模板文件路径不能为空。", nameof(filePath));
            }

            string fullPath = Path.GetFullPath(filePath.Trim());
            ReportTemplateFilePolicy.ValidateExistingTemplatePath(fullPath);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                fullPath,
                "模板文件不能经过符号链接、目录联接或其他重解析点。");
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("模板文件不存在。", fullPath);
            }

            var info = new FileInfo(fullPath);
            if (info.Length == 0)
            {
                throw new InvalidDataException("模板文件不能为空。");
            }

            if (info.Length > MaximumTemplateFileBytes)
            {
                throw new InvalidDataException("模板文件不能超过 10 MB。");
            }

            return fullPath;
        }

        private static string NormalizeExportPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("模板文件路径不能为空。", nameof(filePath));
            }

            string fullPath = ReportTemplateFilePolicy.NormalizeNewTemplatePath(filePath.Trim());
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("无法解析模板文件所在目录。", nameof(filePath));
            }

            Directory.CreateDirectory(directory);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(
                directory,
                "模板文件导出目录不能经过符号链接、目录联接或其他重解析点。");
            return fullPath;
        }
    }
}
