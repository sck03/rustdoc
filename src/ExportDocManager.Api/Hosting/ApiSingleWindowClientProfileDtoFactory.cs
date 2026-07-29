using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public const string ClientBridgeStoragePolicy =
            "同一持卡机可在本机运行数据根 SQLite 中维护多个公司抬头与操作卡档案，但同一时刻只启用一个档案。稳定持卡机标识保存在运行数据根 Security/SingleWindow/station.id；各档案及 COO/代理委托目录彼此隔离。浏览器/PostgreSQL 网络版不读取或修改本机官方客户端目录。";

        public static ApiSingleWindowClientProfilesResponse FromClientProfiles(
            IReadOnlyList<SwClientProfile> profiles,
            string message = "")
        {
            var items = (profiles ?? [])
                .Select(FromClientProfileDto)
                .ToList();
            string activeProfileKey = items.FirstOrDefault(item => item.IsActive)?.ProfileKey
                ?? string.Empty;
            return new ApiSingleWindowClientProfilesResponse(
                items,
                activeProfileKey,
                ClientBridgeStoragePolicy,
                message ?? string.Empty);
        }

        private static ApiSingleWindowClientProfileDto FromClientProfileDto(SwClientProfile profile)
        {
            profile ??= new SwClientProfile();
            return new ApiSingleWindowClientProfileDto(
                profile.Id,
                profile.ProfileKey ?? string.Empty,
                profile.ProfileName ?? string.Empty,
                profile.CompanyScope ?? string.Empty,
                profile.CardIdentifier ?? string.Empty,
                profile.CustomsCooClientRootPath ?? string.Empty,
                profile.AgentConsignmentClientRootPath ?? string.Empty,
                profile.CanSubmitCustomsCoo,
                profile.CanSubmitAgentConsignment,
                profile.IsEnabled,
                profile.IsActive,
                profile.UpdatedAt);
        }
    }
}
