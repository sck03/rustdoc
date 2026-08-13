using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.MasterData;

public sealed class UnsupportedHsCodeImportService : IHsCodeImportService
{
    private static InfrastructureServiceException CreateException() =>
        new("当前产品未安装 Excel 能力模块，无法导入 HS 编码年度库。");

    public Task ImportAsync(string filePath) => Task.FromException(CreateException());

    public Task<HsCodeImportPreview> PreviewImportAsync(
        string filePath,
        HsCodeImportMode mode = HsCodeImportMode.Incremental,
        string? sourceName = null,
        int? effectiveYear = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<HsCodeImportPreview>(CreateException());

    public Task<HsCodeImportCommitResult> CommitImportAsync(
        HsCodeImportPreview preview,
        CancellationToken cancellationToken = default) =>
        Task.FromException<HsCodeImportCommitResult>(CreateException());
}
