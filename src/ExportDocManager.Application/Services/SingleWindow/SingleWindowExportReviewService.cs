using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowExportReviewService : ISingleWindowExportReviewService
    {
        private readonly ICustomsCooSourceAssembler _customsCooSourceAssembler;
        private readonly IAgentConsignmentSourceAssembler _agentConsignmentSourceAssembler;
        private readonly ICustomsCooFieldMapper _customsCooFieldMapper;
        private readonly IAgentConsignmentFieldMapper _agentConsignmentFieldMapper;
        private readonly ISingleWindowXmlValidator _xmlValidator;
        private readonly ICustomsCooDocumentService _customsCooDocumentService;
        private readonly IAgentConsignmentDocumentService _agentConsignmentDocumentService;
        private readonly ISettingsService _settingsService;

        public SingleWindowExportReviewService(
            ICustomsCooSourceAssembler customsCooSourceAssembler,
            IAgentConsignmentSourceAssembler agentConsignmentSourceAssembler,
            ICustomsCooFieldMapper customsCooFieldMapper,
            IAgentConsignmentFieldMapper agentConsignmentFieldMapper,
            ISingleWindowXmlValidator xmlValidator,
            ICustomsCooDocumentService customsCooDocumentService,
            IAgentConsignmentDocumentService agentConsignmentDocumentService,
            ISettingsService settingsService)
        {
            _customsCooSourceAssembler = customsCooSourceAssembler ?? throw new ArgumentNullException(nameof(customsCooSourceAssembler));
            _agentConsignmentSourceAssembler = agentConsignmentSourceAssembler ?? throw new ArgumentNullException(nameof(agentConsignmentSourceAssembler));
            _customsCooFieldMapper = customsCooFieldMapper ?? throw new ArgumentNullException(nameof(customsCooFieldMapper));
            _agentConsignmentFieldMapper = agentConsignmentFieldMapper ?? throw new ArgumentNullException(nameof(agentConsignmentFieldMapper));
            _xmlValidator = xmlValidator ?? throw new ArgumentNullException(nameof(xmlValidator));
            _customsCooDocumentService = customsCooDocumentService ?? throw new ArgumentNullException(nameof(customsCooDocumentService));
            _agentConsignmentDocumentService = agentConsignmentDocumentService ?? throw new ArgumentNullException(nameof(agentConsignmentDocumentService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public async Task<SingleWindowExportReview> BuildSubmitReviewAsync(
            SingleWindowBusinessType businessType,
            int invoiceId,
            CancellationToken cancellationToken = default)
        {
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => await BuildCustomsCooReviewAsync(invoiceId, cancellationToken),
                SingleWindowBusinessType.AgentConsignment => await BuildAgentConsignmentReviewAsync(invoiceId, cancellationToken),
                _ => throw new InvalidOperationException("不支持的单一窗口业务类型。")
            };
        }

        public async Task<int> RepairGroupsAsync(
            SingleWindowBusinessType businessType,
            int invoiceId,
            IReadOnlyList<string> groupKeys,
            CancellationToken cancellationToken = default)
        {
            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => await RepairCustomsCooGroupsAsync(invoiceId, groupKeys, cancellationToken),
                SingleWindowBusinessType.AgentConsignment => await RepairAgentConsignmentGroupsAsync(invoiceId, groupKeys, cancellationToken),
                _ => 0
            };
        }

        private async Task<SingleWindowExportReview> BuildCustomsCooReviewAsync(int invoiceId, CancellationToken cancellationToken)
        {
            var source = await _customsCooSourceAssembler.BuildAsync(invoiceId, cancellationToken);
            var mapped = _customsCooFieldMapper.Map(source);
            CustomsCooDefaultProfileApplicator.Apply(
                mapped,
                _settingsService.Settings.SingleWindow.CustomsCooDefaults);
            var currentDocument = await _customsCooDocumentService.GetOrCreateAsync(invoiceId, cancellationToken);

            var issues = new List<(SingleWindowExportIssueSeverity Severity, string Message)>();
            issues.AddRange(mapped.Warnings.Select(message => (SingleWindowExportIssueSeverity.Warning, message)));
            issues.AddRange(_xmlValidator.ValidateForBuild(SingleWindowBusinessType.CustomsCoo, mapped)
                .Select(message => (SingleWindowExportIssueSeverity.Error, message)));
            issues.AddRange((mapped.Attachments ?? [])
                .Where(item => item != null && !item.Exists)
                .Select(item => (
                    SingleWindowExportIssueSeverity.Warning,
                    $"附件文件不存在或当前不可读取：{(string.IsNullOrWhiteSpace(item.FileName) ? item.FilePath : item.FileName)}")));

            return new SingleWindowExportReview
            {
                BusinessType = SingleWindowBusinessType.CustomsCoo,
                InvoiceId = invoiceId,
                InvoiceNo = currentDocument?.InvoiceNo ?? source.Invoice?.InvoiceNo ?? string.Empty,
                ContractNo = currentDocument?.ContractNo ?? source.Invoice?.ContractNo ?? string.Empty,
                DraftRevision = currentDocument?.DraftRevision ?? 0,
                ManualLockedFieldCount = currentDocument?.ManualLockedFieldCount ?? 0,
                SourceDiffCount = currentDocument?.SourceDiffCount ?? 0,
                SourceDiffSummary = currentDocument?.SourceDiffSummary ?? string.Empty,
                Groups = SingleWindowExportReviewHelper.BuildGroups(SingleWindowBusinessType.CustomsCoo, issues)
            };
        }

        private async Task<SingleWindowExportReview> BuildAgentConsignmentReviewAsync(int invoiceId, CancellationToken cancellationToken)
        {
            var source = await _agentConsignmentSourceAssembler.BuildAsync(invoiceId, cancellationToken);
            var mapped = _agentConsignmentFieldMapper.Map(source);
            var currentDocument = await _agentConsignmentDocumentService.GetOrCreateAsync(invoiceId, cancellationToken);

            var issues = new List<(SingleWindowExportIssueSeverity Severity, string Message)>();
            issues.AddRange(mapped.Warnings.Select(message => (SingleWindowExportIssueSeverity.Warning, message)));
            issues.AddRange(_xmlValidator.ValidateForBuild(SingleWindowBusinessType.AgentConsignment, mapped)
                .Select(message => (SingleWindowExportIssueSeverity.Error, message)));

            return new SingleWindowExportReview
            {
                BusinessType = SingleWindowBusinessType.AgentConsignment,
                InvoiceId = invoiceId,
                InvoiceNo = currentDocument?.InvoiceNo ?? source.Invoice?.InvoiceNo ?? string.Empty,
                ContractNo = currentDocument?.ContractNo ?? source.Invoice?.ContractNo ?? string.Empty,
                DraftRevision = currentDocument?.DraftRevision ?? 0,
                ManualLockedFieldCount = currentDocument?.ManualLockedFieldCount ?? 0,
                SourceDiffCount = currentDocument?.SourceDiffCount ?? 0,
                SourceDiffSummary = currentDocument?.SourceDiffSummary ?? string.Empty,
                Groups = SingleWindowExportReviewHelper.BuildGroups(SingleWindowBusinessType.AgentConsignment, issues)
            };
        }

        private async Task<int> RepairCustomsCooGroupsAsync(
            int invoiceId,
            IReadOnlyList<string> groupKeys,
            CancellationToken cancellationToken = default)
        {
            var document = await _customsCooDocumentService.GetOrCreateAsync(invoiceId, cancellationToken);
            var defaults = await _customsCooDocumentService.BuildDefaultsAsync(invoiceId, cancellationToken);
            int repairedGroupCount = SingleWindowExportReviewRepairHelper.RepairCustomsCooGroups(
                document,
                defaults,
                groupKeys);
            if (repairedGroupCount <= 0)
            {
                return 0;
            }

            await _customsCooDocumentService.SaveAsync(document, cancellationToken);
            return repairedGroupCount;
        }

        private async Task<int> RepairAgentConsignmentGroupsAsync(
            int invoiceId,
            IReadOnlyList<string> groupKeys,
            CancellationToken cancellationToken = default)
        {
            var document = await _agentConsignmentDocumentService.GetOrCreateAsync(invoiceId, cancellationToken);
            var defaults = await _agentConsignmentDocumentService.BuildDefaultsAsync(invoiceId, cancellationToken);
            int repairedGroupCount = SingleWindowExportReviewRepairHelper.RepairAgentConsignmentGroups(
                document,
                defaults,
                groupKeys);
            if (repairedGroupCount <= 0)
            {
                return 0;
            }

            await _agentConsignmentDocumentService.SaveAsync(document, cancellationToken);
            return repairedGroupCount;
        }
    }
}
