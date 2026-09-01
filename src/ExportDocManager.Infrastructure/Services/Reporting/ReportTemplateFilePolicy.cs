using System.Text;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting
{
    internal static class ReportTemplateFilePolicy
    {
        public const string Extension = ".html";

        public static string NormalizeNewTemplatePath(string templatePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);

            string fullPath = Path.GetFullPath(templatePath.Trim());
            string extension = Path.GetExtension(fullPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                fullPath = Path.ChangeExtension(fullPath, Extension);
            }
            else if (!string.Equals(extension, Extension, StringComparison.Ordinal))
            {
                throw new ArgumentException("报表模板扩展名必须使用小写 .html。", nameof(templatePath));
            }

            string fileName = Path.GetFileName(fullPath).Normalize(NormalizationForm.FormC);
            if (!CrossPlatformFileNamePolicy.IsSafeFileName(fileName))
            {
                throw new ArgumentException("报表模板文件名不符合跨平台规则或长度超过限制。", nameof(templatePath));
            }

            return Path.Combine(Path.GetDirectoryName(fullPath)!, fileName);
        }

        public static void ValidateExistingTemplatePath(string templatePath)
        {
            string fullPath = Path.GetFullPath(templatePath);
            if (!string.Equals(Path.GetExtension(fullPath), Extension, StringComparison.Ordinal) ||
                !CrossPlatformFileNamePolicy.IsSafeFileName(Path.GetFileName(fullPath)))
            {
                throw new InvalidDataException("报表模板必须使用安全文件名和小写 .html 扩展名。");
            }
        }

        public static void EnsureNoPortableCollision(string candidatePath, string? currentPath = null)
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? directory = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string candidateName = Path.GetFileName(candidate);
            foreach (string existingPath in ControlledFileSystemEnumerator.EnumerateImmediateFiles(directory))
            {
                if (!PortablePathKey.Comparer.Equals(Path.GetFileName(existingPath), candidateName))
                {
                    continue;
                }

                if (PhysicalPathComparison.AreSamePath(existingPath, candidate) ||
                    !string.IsNullOrWhiteSpace(currentPath) &&
                    PhysicalPathComparison.AreSamePath(existingPath, currentPath))
                {
                    continue;
                }

                throw new ResourceConflictException(
                    $"模板文件名与现有文件发生跨平台大小写或 Unicode 冲突：{candidateName}");
            }
        }

        public static IEnumerable<string> EnumerateTemplates(string root)
        {
            if (!Directory.Exists(root))
            {
                return [];
            }

            IReadOnlyList<string> files = ControlledFileSystemEnumerator.EnumerateFiles(root);
            string? invalidTemplate = files.FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetExtension(path), Extension, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(invalidTemplate))
            {
                throw new InvalidDataException(
                    $"报表模板扩展名必须使用小写 .html：{Path.GetFileName(invalidTemplate)}");
            }

            return files.Where(path =>
                string.Equals(Path.GetExtension(path), Extension, StringComparison.Ordinal));
        }
    }
}
