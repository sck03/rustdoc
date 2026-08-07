using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        private static void ValidatePackageContent(
            IReadOnlyList<HsCode> codes,
            IReadOnlyList<HsCodeDeclarationExample> examples,
            IReadOnlyList<HsCodeReplacementRelation> replacements,
            IReadOnlyList<HsCodeSearchFeedback> feedback)
        {
            if (codes.Count > 500_000 || examples.Count > 1_000_000 || replacements.Count > 1_000_000 || feedback.Count > 1_000_000)
                throw new InvalidDataException("HS知识库记录数量超过安全限制。");
            if (codes.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.Code)) ||
                                  HsCodeTextHelper.NormalizeCode(item.Code).Length > 20 ||
                                  string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200 ||
                                  (item.SourceName?.Length ?? 0) > 200 ||
                                  (item.Description?.Length ?? 0) > 500 ||
                                  (item.Elements?.Length ?? 0) > 500 ||
                                  (item.Notes?.Length ?? 0) > 1000))
                throw new InvalidDataException("HS知识库包含无效或过长的编码字段。");
            if (codes.Any(item => string.Equals(item.Status, HsCodeValidityPolicy.ActiveStatus, StringComparison.OrdinalIgnoreCase) &&
                                  !HsCodeValidityPolicy.IsTrustedActive(item)))
                throw new InvalidDataException("HS知识库包含缺少来源、适用年度或验证时间的有效编码。");
            if (examples.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode)) ||
                                      string.IsNullOrWhiteSpace(item.ProductName) || item.ProductName.Length > 300 ||
                                      (item.Specification?.Length ?? 0) > 1500 ||
                                      (item.Source?.Length ?? 0) > 100 ||
                                      (item.ResolutionStatus?.Length ?? 0) > 30))
                throw new InvalidDataException("HS知识库包含无效或过长的申报实例字段。");
            if (replacements.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.OldCode)) ||
                                          string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.NewCode)) ||
                                          HsCodeTextHelper.NormalizeCode(item.OldCode).Length > 20 ||
                                          HsCodeTextHelper.NormalizeCode(item.NewCode).Length > 20 ||
                                          (item.Source?.Length ?? 0) > 100))
                throw new InvalidDataException("HS知识库包含无效的编码替代关系。");
            if (feedback.Any(item => string.IsNullOrWhiteSpace(HsCodeTextHelper.NormalizeCode(item.CandidateCode)) ||
                                      HsCodeTextHelper.NormalizeCode(item.CandidateCode).Length > 20 ||
                                      (item.QueryText?.Length ?? 0) > 500 ||
                                      (item.ProductName?.Length ?? 0) > 300 ||
                                      (item.Specification?.Length ?? 0) > 1500 ||
                                      item.AcceptedCount < 0 || item.RejectedCount < 0))
                throw new InvalidDataException("HS知识库包含无效的学习记录。");

            bool hasDuplicateCodes = codes
                .Select(item => HsCodeTextHelper.NormalizeCode(item.Code))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            bool hasDuplicateExamples = examples
                .Select(item => BuildFingerprint(
                    HsCodeTextHelper.NormalizeCode(item.RawReportedHsCode),
                    (item.ProductName ?? string.Empty).Trim(),
                    (item.Specification ?? string.Empty).Trim()))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            bool hasDuplicateRelations = replacements
                .Select(item => new ReplacementRelationKey(
                    HsCodeTextHelper.NormalizeCode(item.OldCode),
                    HsCodeTextHelper.NormalizeCode(item.NewCode),
                    item.EffectiveYear))
                .GroupBy(value => value)
                .Any(group => group.Count() > 1);
            bool hasDuplicateFeedback = feedback
                .Select(item => BuildFingerprint(
                    NormalizeSearchText(item.QueryText),
                    HsCodeTextHelper.NormalizeCode(item.CandidateCode),
                    item.ProductName,
                    item.Specification))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            if (hasDuplicateCodes || hasDuplicateExamples || hasDuplicateRelations || hasDuplicateFeedback)
                throw new InvalidDataException("HS知识库包含重复的业务记录。");
        }

        private static string NormalizeResolutionStatus(string status, string currentCode, string rawCode)
        {
            if (string.Equals(status, "ManuallyVerified", StringComparison.OrdinalIgnoreCase)) return "ManuallyVerified";
            if (!string.IsNullOrWhiteSpace(currentCode)) return string.Equals(currentCode, rawCode, StringComparison.OrdinalIgnoreCase) ? "Active" : "ObsoleteMapped";
            return "Unresolved";
        }
    }
}
