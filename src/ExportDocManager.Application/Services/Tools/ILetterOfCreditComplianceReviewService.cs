using System.Globalization;
using System.Text;

namespace ExportDocManager.Services.Tools
{
    public sealed class LetterOfCreditComplianceReviewDraft
    {
        public string InvoiceNo { get; init; } = string.Empty;

        public string ContractNo { get; init; } = string.Empty;

        public string InvoiceType { get; init; } = string.Empty;

        public string LetterOfCreditNo { get; init; } = string.Empty;

        public string LetterOfCreditSourcePath { get; init; } = string.Empty;

        public string LetterOfCreditContent { get; init; } = string.Empty;

        public string IssuingBank { get; init; } = string.Empty;

        public decimal TotalAmount { get; init; }

        public string Currency { get; init; } = string.Empty;

        public string PortOfLoading { get; init; } = string.Empty;

        public string PortOfDestination { get; init; } = string.Empty;

        public string PaymentTerms { get; init; } = string.Empty;

        public string TradeTerms { get; init; } = string.Empty;

        public string TransportMode { get; init; } = string.Empty;

        public string SpecialTerms { get; init; } = string.Empty;
    }

    public sealed class LetterOfCreditComplianceReviewResult
    {
        public string ReportText { get; init; } = string.Empty;

        public string ContextSummary { get; init; } = string.Empty;

        public bool LetterOfCreditContentTruncated { get; init; }
    }

    public interface ILetterOfCreditComplianceReviewService
    {
        bool HasReviewContext(LetterOfCreditComplianceReviewDraft draft);

        string BuildReviewContent(
            LetterOfCreditComplianceReviewDraft draft,
            out bool letterOfCreditContentTruncated);

        Task<LetterOfCreditComplianceReviewResult> ReviewAsync(
            LetterOfCreditComplianceReviewDraft draft,
            CancellationToken cancellationToken = default);
    }

    public sealed class LetterOfCreditComplianceReviewService : ILetterOfCreditComplianceReviewService
    {
        private const int MaxLetterOfCreditContentLength = 12000;
        private const string ReviewPrompt = "请输出结构化的信用证合规审查报告。";

        private readonly IAIService _aiService;

        public LetterOfCreditComplianceReviewService(IAIService aiService)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        }

        public bool HasReviewContext(LetterOfCreditComplianceReviewDraft draft)
        {
            if (draft == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(draft.LetterOfCreditContent)
                || !string.IsNullOrWhiteSpace(draft.LetterOfCreditNo)
                || !string.IsNullOrWhiteSpace(draft.SpecialTerms);
        }

        public string BuildReviewContent(
            LetterOfCreditComplianceReviewDraft draft,
            out bool letterOfCreditContentTruncated)
        {
            ArgumentNullException.ThrowIfNull(draft);

            string letterOfCreditSection = BuildLetterOfCreditSection(draft, out letterOfCreditContentTruncated);
            string totalAmountText = FormatAmount(draft.TotalAmount);

            var builder = new StringBuilder();
            builder.AppendLine("请基于以下信用证信息与发票数据进行合规审查，列出不符点、风险等级、依据和修改建议。");
            builder.AppendLine();
            builder.AppendLine("【审查边界】");
            builder.AppendLine("仅使用当前请求中的发票/信用证草稿字段进行审查。");
            builder.AppendLine("同一发票号下的实际数据与报关数据可能独立存在，不要推断或合并另一口径。");
            builder.AppendLine("不要引用付款/报销单据作为本次信用证审查依据。");
            builder.AppendLine();
            builder.AppendLine("【信用证信息】");
            builder.AppendLine(letterOfCreditSection);
            builder.AppendLine();
            builder.AppendLine("【发票/单据信息】");
            builder.AppendLine($"发票号: {NormalizeText(draft.InvoiceNo)}");
            builder.AppendLine($"数据口径: {NormalizeText(draft.InvoiceType)}");
            builder.AppendLine($"合同号: {NormalizeText(draft.ContractNo)}");
            builder.AppendLine($"信用证号: {NormalizeText(draft.LetterOfCreditNo)}");
            builder.AppendLine($"开证行: {NormalizeText(draft.IssuingBank)}");
            builder.AppendLine($"金额: {totalAmountText} {NormalizeText(draft.Currency)}");
            builder.AppendLine($"起运港: {NormalizeText(draft.PortOfLoading)}");
            builder.AppendLine($"目的港: {NormalizeText(draft.PortOfDestination)}");
            builder.AppendLine($"付款方式: {NormalizeText(draft.PaymentTerms)}");
            builder.AppendLine($"贸易条款: {NormalizeText(draft.TradeTerms)}");
            builder.AppendLine($"运输方式: {NormalizeText(draft.TransportMode)}");
            builder.Append($"特别条款: {NormalizeText(draft.SpecialTerms)}");

            return builder.ToString();
        }

        public async Task<LetterOfCreditComplianceReviewResult> ReviewAsync(
            LetterOfCreditComplianceReviewDraft draft,
            CancellationToken cancellationToken = default)
        {
            if (!HasReviewContext(draft))
            {
                throw new ArgumentException("请先导入信用证文本，或至少补充信用证号/信用证要求后再进行审查。", nameof(draft));
            }

            string content = BuildReviewContent(draft, out bool truncated);
            string report = await _aiService.AnalyzeComplianceAsync(
                ReviewPrompt,
                content,
                cancellationToken);

            return new LetterOfCreditComplianceReviewResult
            {
                ReportText = report ?? string.Empty,
                ContextSummary = BuildContextSummary(draft),
                LetterOfCreditContentTruncated = truncated
            };
        }

        private static string BuildLetterOfCreditSection(
            LetterOfCreditComplianceReviewDraft draft,
            out bool truncated)
        {
            truncated = false;
            string sourcePath = string.IsNullOrWhiteSpace(draft.LetterOfCreditSourcePath)
                ? "未记录来源文件"
                : draft.LetterOfCreditSourcePath.Trim();

            if (!string.IsNullOrWhiteSpace(draft.LetterOfCreditContent))
            {
                string content = TrimForPrompt(
                    draft.LetterOfCreditContent,
                    MaxLetterOfCreditContentLength,
                    out truncated);

                return $"来源文件: {sourcePath}\n信用证文本:\n{content}";
            }

            return $"来源文件: {sourcePath}\n" +
                   "未导入信用证原文，仅提供以下摘要字段进行辅助审查，结论可信度较低。\n" +
                   $"信用证号: {NormalizeText(draft.LetterOfCreditNo)}\n" +
                   $"特别条款: {NormalizeText(draft.SpecialTerms)}";
        }

        private static string BuildContextSummary(LetterOfCreditComplianceReviewDraft draft)
        {
            string invoiceNo = string.IsNullOrWhiteSpace(draft?.InvoiceNo)
                ? "未填写发票号"
                : draft.InvoiceNo.Trim();
            string invoiceType = string.IsNullOrWhiteSpace(draft?.InvoiceType)
                ? "未填写口径"
                : draft.InvoiceType.Trim();
            string letterOfCreditNo = string.IsNullOrWhiteSpace(draft?.LetterOfCreditNo)
                ? "未填写信用证号"
                : draft.LetterOfCreditNo.Trim();

            return $"{invoiceNo} / {invoiceType} / {letterOfCreditNo}";
        }

        private static string TrimForPrompt(string text, int maxLength, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            truncated = true;
            return text[..maxLength] + "\n\n[已截断剩余内容，以控制请求体积]";
        }

        private static string NormalizeText(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static string FormatAmount(decimal amount)
        {
            return amount.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
