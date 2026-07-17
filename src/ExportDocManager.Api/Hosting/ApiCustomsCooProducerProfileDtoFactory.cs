using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public const string CustomsCooProducerProfileStoragePolicy =
            "COO生产企业资料只写运行数据根数据库 CustomsCooProducerProfiles 表，随当前 data root 和数据库配置迁移；接口只服务单一窗口 COO 商品行生产商回填，不读取付款/报销单据，不写系统用户数据目录、全局数据目录或系统盘默认落点。";

        public static ApiCustomsCooProducerProfileDto FromCustomsCooProducerProfile(
            CustomsCooProducerProfile profile)
        {
            if (profile == null)
            {
                return new ApiCustomsCooProducerProfileDto();
            }

            return new ApiCustomsCooProducerProfileDto
            {
                Id = profile.Id,
                CiqRegNo = profile.CiqRegNo ?? string.Empty,
                PrdcEtpsName = profile.PrdcEtpsName ?? string.Empty,
                PrdcEtpsConcEr = profile.PrdcEtpsConcEr ?? string.Empty,
                PrdcEtpsTel = profile.PrdcEtpsTel ?? string.Empty,
                Producer = profile.Producer ?? string.Empty,
                ProducerTel = profile.ProducerTel ?? string.Empty,
                ProducerFax = profile.ProducerFax ?? string.Empty,
                ProducerEmail = profile.ProducerEmail ?? string.Empty,
                ProducerSertFlag = profile.ProducerSertFlag ?? string.Empty,
                LastInvoiceNo = profile.LastInvoiceNo ?? string.Empty,
                LastContractNo = profile.LastContractNo ?? string.Empty,
                LastSourceStyleNo = profile.LastSourceStyleNo ?? string.Empty,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt,
                LastUsedAt = profile.LastUsedAt
            };
        }

        public static CustomsCooProducerProfileInput ToCustomsCooProducerProfileInput(
            ApiCustomsCooProducerProfileDto profile)
        {
            return new CustomsCooProducerProfileInput
            {
                CiqRegNo = profile?.CiqRegNo ?? string.Empty,
                PrdcEtpsName = profile?.PrdcEtpsName ?? string.Empty,
                PrdcEtpsConcEr = profile?.PrdcEtpsConcEr ?? string.Empty,
                PrdcEtpsTel = profile?.PrdcEtpsTel ?? string.Empty,
                Producer = profile?.Producer ?? string.Empty,
                ProducerTel = profile?.ProducerTel ?? string.Empty,
                ProducerFax = profile?.ProducerFax ?? string.Empty,
                ProducerEmail = profile?.ProducerEmail ?? string.Empty,
                ProducerSertFlag = profile?.ProducerSertFlag ?? string.Empty,
                LastInvoiceNo = profile?.LastInvoiceNo ?? string.Empty,
                LastContractNo = profile?.LastContractNo ?? string.Empty,
                LastSourceStyleNo = profile?.LastSourceStyleNo ?? string.Empty
            };
        }

        public static CustomsCooProducerProfileInput ToCustomsCooProducerProfileInput(
            ApiCustomsCooProducerProfileInputDto profile)
        {
            return new CustomsCooProducerProfileInput
            {
                CiqRegNo = profile?.CiqRegNo ?? string.Empty,
                PrdcEtpsName = profile?.PrdcEtpsName ?? string.Empty,
                PrdcEtpsConcEr = profile?.PrdcEtpsConcEr ?? string.Empty,
                PrdcEtpsTel = profile?.PrdcEtpsTel ?? string.Empty,
                Producer = profile?.Producer ?? string.Empty,
                ProducerTel = profile?.ProducerTel ?? string.Empty,
                ProducerFax = profile?.ProducerFax ?? string.Empty,
                ProducerEmail = profile?.ProducerEmail ?? string.Empty,
                ProducerSertFlag = profile?.ProducerSertFlag ?? string.Empty,
                LastInvoiceNo = profile?.LastInvoiceNo ?? string.Empty,
                LastContractNo = profile?.LastContractNo ?? string.Empty,
                LastSourceStyleNo = profile?.LastSourceStyleNo ?? string.Empty
            };
        }

        public static ApiCustomsCooProducerProfileResponse FromCustomsCooProducerProfileResponse(
            CustomsCooProducerProfile profile)
        {
            return new ApiCustomsCooProducerProfileResponse(
                FromCustomsCooProducerProfile(profile),
                CustomsCooProducerProfileStoragePolicy);
        }

        public static ApiCustomsCooProducerProfileListResponse FromCustomsCooProducerProfileList(
            IEnumerable<CustomsCooProducerProfile> profiles)
        {
            var items = (profiles ?? Enumerable.Empty<CustomsCooProducerProfile>())
                .Where(item => item != null)
                .Select(FromCustomsCooProducerProfile)
                .ToArray();

            return new ApiCustomsCooProducerProfileListResponse(
                items,
                items.Length,
                CustomsCooProducerProfileStoragePolicy);
        }

        public static ApiCustomsCooProducerProfileSaveResponse FromSavedCustomsCooProducerProfile(
            CustomsCooProducerProfile profile,
            string message)
        {
            return new ApiCustomsCooProducerProfileSaveResponse(
                true,
                profile?.Id ?? 0,
                FromCustomsCooProducerProfile(profile),
                string.IsNullOrWhiteSpace(message) ? "生产企业资料已保存。" : message,
                CustomsCooProducerProfileStoragePolicy);
        }
    }
}
