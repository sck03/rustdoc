using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public const string ClientBridgeStoragePolicy =
            "客户端目录档案保存在运行目录数据库；导入目录和回执目录来自用户配置或请求显式路径，提交包恢复目录使用运行数据根 SingleWindow/Inbox。Templates/OcrModels 等稳定资源仍在程序根目录，日志保存到运行数据根 Logs，授权状态保存到运行数据根 Security。";

        public static ApiSingleWindowClientProfileResponse FromClientProfile(
            SwClientProfile profile)
        {
            return new ApiSingleWindowClientProfileResponse(
                FromClientProfileDto(profile),
                ClientBridgeStoragePolicy);
        }

        public static ApiSingleWindowClientProfileSaveResponse FromSavedClientProfile(
            int id,
            SwClientProfile profile,
            string message)
        {
            return new ApiSingleWindowClientProfileSaveResponse(
                true,
                id,
                FromClientProfileDto(profile),
                ClientBridgeStoragePolicy,
                string.IsNullOrWhiteSpace(message) ? "单一窗口客户端目录档案已保存。" : message);
        }

        private static ApiSingleWindowClientProfileDto FromClientProfileDto(SwClientProfile profile)
        {
            profile ??= new SwClientProfile();
            return new ApiSingleWindowClientProfileDto(
                profile.Id,
                profile.ProfileName ?? string.Empty,
                profile.MachineName ?? string.Empty,
                profile.ImportRootPath ?? string.Empty,
                profile.ReceiptRootPath ?? string.Empty,
                profile.BusinessDirectoryOverridesJson ?? string.Empty,
                profile.CanSubmitCustomsCoo,
                profile.CanSubmitAgentConsignment,
                profile.IsEnabled,
                profile.UpdatedAt);
        }
    }
}
