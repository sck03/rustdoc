using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public const string SubmitPackageStoragePolicy =
            "未提供 packagePath 时，提交包默认写入运行数据根 SingleWindow/Outbox；传入 packagePath 时按用户显式选择路径保存。Templates/OcrModels 等稳定资源仍在程序根目录，日志保存到运行数据根 Logs，授权状态保存到运行数据根 Security。";

        public const string ImportPackageStoragePolicy =
            "导入包必须来自用户显式选择的 .swpkg 路径；提交包默认解包并保留到运行数据根 SingleWindow/Inbox，回执包默认使用运行数据根 SingleWindow/ReceiptInbox 作为短生命周期工作目录。Templates/OcrModels 等稳定资源仍在程序根目录，日志保存到运行数据根 Logs，授权状态保存到运行数据根 Security。";

        public const string ReceiptPackageStoragePolicy =
            "未提供 packagePath 时，回执包默认写入运行数据根 SingleWindow/Outbox；回执源文件必须来自用户显式选择路径。Templates/OcrModels 等稳定资源仍在程序根目录，日志保存到运行数据根 Logs，授权状态保存到运行数据根 Security。";

        public static ApiSingleWindowHandoffPackageResponse FromHandoffPackageResult(
            SingleWindowHandoffPackageResult result,
            string message)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiSingleWindowHandoffPackageResponse(
                true,
                result.PackagePath ?? string.Empty,
                result.Manifest ?? new SingleWindowPackageManifest(),
                result.TrackingBatchId,
                SubmitPackageStoragePolicy,
                string.IsNullOrWhiteSpace(message) ? "单一窗口提交包已导出。" : message);
        }

        public static ApiSingleWindowHandoffPackageResponse FromReceiptPackageResult(
            SingleWindowHandoffPackageResult result,
            string message)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiSingleWindowHandoffPackageResponse(
                true,
                result.PackagePath ?? string.Empty,
                result.Manifest ?? new SingleWindowPackageManifest(),
                result.TrackingBatchId,
                ReceiptPackageStoragePolicy,
                string.IsNullOrWhiteSpace(message) ? "单一窗口回执包已导出。" : message);
        }

        public static ApiSingleWindowImportedPackageResponse FromImportedPackage(
            string packagePath,
            SingleWindowImportedPackage imported,
            bool workingDirectoryKept,
            string message)
        {
            ArgumentNullException.ThrowIfNull(imported);

            return new ApiSingleWindowImportedPackageResponse(
                true,
                packagePath ?? string.Empty,
                imported.WorkingDirectory ?? string.Empty,
                workingDirectoryKept,
                imported.Manifest ?? new SingleWindowPackageManifest(),
                imported.ParsedReceipts ?? [],
                imported.TrackingBatchId,
                imported.TrackingStatus ?? string.Empty,
                imported.PersistedReceiptCount,
                ImportPackageStoragePolicy,
                string.IsNullOrWhiteSpace(message) ? "单一窗口交接包已导入。" : message);
        }
    }
}
