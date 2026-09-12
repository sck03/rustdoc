using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting;

/// <summary>Orders template commits, resource claims and recycling for one data root.</summary>
internal sealed class ReportTemplateStorageLock(IAppPathProvider pathProvider)
{
    private readonly string _path = Path.Combine(pathProvider.DataRoot, "Locks", "report-template-storage.lock");

    public Task<FileStream> AcquireAsync(CancellationToken cancellationToken) =>
        CrossProcessFileLock.AcquireAsync(_path, cancellationToken);
}
