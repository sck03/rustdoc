using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooDefaultProfileApplicator
    {
        public static void Apply(CooMappedDocument document, CustomsCooDefaultProfile defaults)
        {
            if (document == null || defaults == null)
            {
                return;
            }

            document.ApplName = ApplyConfiguredDefault(document.ApplName, defaults.ApplName);
            document.Applicant = ApplyConfiguredDefault(document.Applicant, defaults.Applicant);
            document.ApplTel = ApplyConfiguredDefault(document.ApplTel, defaults.ApplTel);
            document.OrgCode = ApplyConfiguredDefault(document.OrgCode, defaults.OrgCode);
            document.FetchPlace = ApplyConfiguredDefault(document.FetchPlace, defaults.FetchPlace);
            document.AplAdd = ApplyConfiguredDefault(document.AplAdd, defaults.AplAdd);
            document.Warnings = RefreshWarnings(document.Warnings, document);
        }

        private static IReadOnlyList<string> RefreshWarnings(IReadOnlyList<string> warnings, CooMappedDocument document)
        {
            var retained = (warnings ?? [])
                .Where(message => !ShouldSuppressMissingWarning(message, document))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return retained;
        }

        private static bool ShouldSuppressMissingWarning(string message, CooMappedDocument document)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return (!string.IsNullOrWhiteSpace(document.ApplName) && message.Contains("申报员姓名(ApplName)缺失。", StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(document.Applicant) && message.Contains("申报员身份证号(Applicant)缺失。", StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(document.ApplTel) && message.Contains("申报员联系电话(ApplTel)缺失。", StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(document.OrgCode) && message.Contains("签证机构代码(OrgCode)缺失。", StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(document.FetchPlace) && message.Contains("领证机构代码(FetchPlace)缺失。", StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(document.AplAdd) && message.Contains("申请地址(AplAdd)缺失。", StringComparison.Ordinal));
        }

        private static string ApplyConfiguredDefault(string currentValue, string defaultValue)
        {
            string normalizedDefault = NormalizeStoredDefault(defaultValue);
            return string.IsNullOrWhiteSpace(normalizedDefault)
                ? currentValue
                : normalizedDefault;
        }

        private static string NormalizeStoredDefault(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
