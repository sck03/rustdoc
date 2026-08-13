using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    internal static class HsCodeKnowledgeFeedbackWriter
    {
        private const int MaximumKnowledgeQueryLength = 500;
        private const int AutomaticPublicationAcceptanceThreshold = 3;
        private const int MaximumInvoiceFeedbackCount = 100;

        public static async Task RecordInvoiceFeedbackAsync(
            AppDbContext context,
            IReadOnlyList<Item> invoiceItems,
            IReadOnlyList<HsCodeKnowledgeFeedbackInput>? inputs,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            var feedback = (inputs ?? Array.Empty<HsCodeKnowledgeFeedbackInput>())
                .Where(item => item != null)
                .Take(MaximumInvoiceFeedbackCount)
                .ToList();
            if (feedback.Count == 0)
            {
                return;
            }

            var items = invoiceItems ?? [];
            foreach (var input in feedback)
            {
                string code = HsCodeTextHelper.NormalizeCode(input.CandidateCode);
                Item? matchedItem = items.FirstOrDefault(item =>
                    string.Equals(HsCodeTextHelper.NormalizeCode(item.HSCode), code, StringComparison.OrdinalIgnoreCase));
                if (input.Accepted && matchedItem == null)
                {
                    // The user changed or removed the suggested code before saving. Do not
                    // create a learning record for a result that is absent from the final invoice.
                    continue;
                }

                string productName = matchedItem == null
                    ? input.ProductName
                    : FirstNonEmpty(matchedItem.StyleNameCN, matchedItem.StyleName, input.ProductName);
                string specification = matchedItem == null
                    ? input.Specification
                    : BuildSpecification(matchedItem, productName, input.Specification);
                await RecordInContextAsync(
                    context,
                    input with
                    {
                        ProductName = productName,
                        Specification = specification,
                        CandidateCode = code
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task RecordInContextAsync(
            AppDbContext context,
            HsCodeKnowledgeFeedbackInput input,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(input);

            string queryText = ValidateTextLength(input.QueryText, MaximumKnowledgeQueryLength, "查询条件");
            string productName = ValidateTextLength(input.ProductName, 300, "商品名称");
            string specification = ValidateTextLength(input.Specification, 1500, "规格与申报要素");
            string code = HsCodeTextHelper.NormalizeCode(input.CandidateCode);
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ServiceValidationException("确认结果必须包含HS编码。");
            }

            if (code.Length > 20 || !code.All(char.IsDigit))
            {
                throw new ArgumentException("HS 编码必须是最多 20 位数字。", nameof(input));
            }

            if (input.Accepted && !await HasTrustedActiveCodeAsync(context, code, cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceValidationException("确认适用前必须选择已验证年度税则中的当前有效编码。");
            }

            string fingerprint = HsCodeKnowledgeService.BuildFingerprint(
                HsCodeKnowledgeService.NormalizeSearchText(queryText),
                code,
                productName,
                specification);
            var entity = await context.HsCodeSearchFeedback
                .FirstOrDefaultAsync(item => item.Fingerprint == fingerprint, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (entity == null)
            {
                entity = new HsCodeSearchFeedback { Fingerprint = fingerprint };
                await context.HsCodeSearchFeedback.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }

            entity.QueryText = queryText;
            entity.ProductName = productName;
            entity.Specification = specification;
            entity.CandidateCode = code;
            if (input.Accepted)
            {
                entity.AcceptedCount++;
                entity.LastConfirmedAt = now;
            }
            else
            {
                entity.RejectedCount++;
            }
            entity.UpdatedAt = now;

            if (input.Accepted && entity.AcceptedCount >= AutomaticPublicationAcceptanceThreshold)
            {
                await HsCodeKnowledgeService.UpsertExampleInContextAsync(
                    context,
                    new HsCodeExampleInput(
                        0,
                        code,
                        code,
                        string.IsNullOrWhiteSpace(productName) ? queryText : productName,
                        specification,
                        "ConsensusConfirmed",
                        DateOnly.FromDateTime(DateTime.Today).Year,
                        "ManuallyVerified",
                        true),
                    now,
                    cancellationToken,
                    incrementUseCount: false).ConfigureAwait(false);
            }
        }

        private static Task<bool> HasTrustedActiveCodeAsync(
            AppDbContext context,
            string normalizedCode,
            CancellationToken cancellationToken)
        {
            return context.HsCodes.AsNoTracking().AnyAsync(item =>
                item.NormalizedCode == normalizedCode &&
                item.Status == HsCodeValidityPolicy.ActiveStatus &&
                item.SourceName != null && item.SourceName != string.Empty &&
                item.EffectiveYear >= 2000 && item.EffectiveYear <= 2100 &&
                item.LastVerifiedAt != null,
                cancellationToken);
        }

        private static string ValidateTextLength(string value, int maximumLength, string fieldName)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > maximumLength)
            {
                throw new ArgumentException($"{fieldName}不能超过 {maximumLength} 个字符。", fieldName);
            }

            return normalized;
        }

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

        private static string BuildSpecification(Item item, string productName, string? fallback)
        {
            var values = new[] { item.StyleName, item.FabricComposition, item.Brand }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => !string.Equals(value, productName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return values.Length > 0 ? string.Join(" · ", values) : fallback?.Trim() ?? string.Empty;
        }
    }
}
