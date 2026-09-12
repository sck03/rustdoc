using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting;

/// <summary>File templates keep their authoritative references in their validated V3 document.</summary>
internal sealed class ReportTemplateFileResourceReferences(IAppPathProvider pathProvider)
{
    private readonly ReportTemplatePathResolver _paths = new(pathProvider);

    public async Task<bool> ContainsAsync(string resourceId, Func<ReportDocumentType, bool>? canRead, CancellationToken cancellationToken)
    {
        foreach (ReportDocumentType type in Enum.GetValues<ReportDocumentType>())
        {
            if (canRead != null && !canRead(type)) continue;
            string category = type == ReportDocumentType.PaymentVoucher ? "Internal" : "Export";
            foreach (string root in new[] { _paths.GetUserTemplatesBaseDirectory(), _paths.GetBuiltInTemplatesBaseDirectory() })
            {
                foreach (string path in ControlledFileSystemEnumerator.EnumerateFiles(Path.Combine(root, category), cancellationToken)
                             .Where(path => string.Equals(Path.GetExtension(path), ReportTemplateFilePolicy.Extension, StringComparison.Ordinal)))
                {
                    string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    if (ReportTemplateV3ResourceReferenceParser.Parse(type, content).Any(resource => resource.Id == resourceId)) return true;
                }
            }
        }
        return false;
    }
}
