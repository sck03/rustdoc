using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public static class HsCodeValidityPolicy
    {
        public const string ActiveStatus = "Active";
        public const string ReferenceOnlyStatus = "ReferenceOnly";

        public static bool IsTrustedActive(HsCode item)
        {
            return item != null &&
                string.Equals(item.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.SourceName) &&
                IsValidEffectiveYear(item.EffectiveYear) &&
                item.LastVerifiedAt.HasValue;
        }

        public static bool IsValidEffectiveYear(int? year)
        {
            return year is >= 2000 and <= 2100;
        }

        public static void EnsureTrustedActiveMetadata(HsCode item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!string.Equals(item.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(item.SourceName))
                throw new InvalidOperationException("有效 HS 编码必须填写可信数据来源。");
            if (!IsValidEffectiveYear(item.EffectiveYear))
                throw new InvalidOperationException("有效 HS 编码必须填写 2000—2100 之间的适用年度。");
            if (!item.LastVerifiedAt.HasValue)
                throw new InvalidOperationException("有效 HS 编码必须包含最近验证时间。");
        }
    }
}
