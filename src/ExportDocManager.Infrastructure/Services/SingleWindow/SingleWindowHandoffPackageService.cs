using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowHandoffPackageService : ISingleWindowHandoffPackageService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly ICustomsCooSourceAssembler _customsCooSourceAssembler;
        private readonly IAgentConsignmentSourceAssembler _agentConsignmentSourceAssembler;
        private readonly ICustomsCooFieldMapper _customsCooFieldMapper;
        private readonly IAgentConsignmentFieldMapper _agentConsignmentFieldMapper;
        private readonly ISingleWindowXmlValidator _xmlValidator;
        private readonly ICustomsCooPayloadGenerator _customsCooPayloadGenerator;
        private readonly IAgentConsignmentPayloadGenerator _agentConsignmentPayloadGenerator;
        private readonly ISingleWindowReceiptParser _singleWindowReceiptParser;
        private readonly ISingleWindowDocumentPersistenceService _singleWindowDocumentPersistenceService;
        private readonly ISingleWindowTrackingService _singleWindowTrackingService;
        private readonly ISettingsService _settingsService;
        private readonly IAppPathProvider _pathProvider;

        public SingleWindowHandoffPackageService(
            ICustomsCooSourceAssembler customsCooSourceAssembler,
            IAgentConsignmentSourceAssembler agentConsignmentSourceAssembler,
            ICustomsCooFieldMapper customsCooFieldMapper,
            IAgentConsignmentFieldMapper agentConsignmentFieldMapper,
            ISingleWindowXmlValidator xmlValidator,
            ICustomsCooPayloadGenerator customsCooPayloadGenerator,
            IAgentConsignmentPayloadGenerator agentConsignmentPayloadGenerator,
            ISingleWindowReceiptParser singleWindowReceiptParser,
            ISingleWindowDocumentPersistenceService singleWindowDocumentPersistenceService,
            ISingleWindowTrackingService singleWindowTrackingService,
            ISettingsService settingsService,
            IAppPathProvider pathProvider)
        {
            _customsCooSourceAssembler = customsCooSourceAssembler ?? throw new ArgumentNullException(nameof(customsCooSourceAssembler));
            _agentConsignmentSourceAssembler = agentConsignmentSourceAssembler ?? throw new ArgumentNullException(nameof(agentConsignmentSourceAssembler));
            _customsCooFieldMapper = customsCooFieldMapper ?? throw new ArgumentNullException(nameof(customsCooFieldMapper));
            _agentConsignmentFieldMapper = agentConsignmentFieldMapper ?? throw new ArgumentNullException(nameof(agentConsignmentFieldMapper));
            _xmlValidator = xmlValidator ?? throw new ArgumentNullException(nameof(xmlValidator));
            _customsCooPayloadGenerator = customsCooPayloadGenerator ?? throw new ArgumentNullException(nameof(customsCooPayloadGenerator));
            _agentConsignmentPayloadGenerator = agentConsignmentPayloadGenerator ?? throw new ArgumentNullException(nameof(agentConsignmentPayloadGenerator));
            _singleWindowReceiptParser = singleWindowReceiptParser ?? throw new ArgumentNullException(nameof(singleWindowReceiptParser));
            _singleWindowDocumentPersistenceService = singleWindowDocumentPersistenceService ?? throw new ArgumentNullException(nameof(singleWindowDocumentPersistenceService));
            _singleWindowTrackingService = singleWindowTrackingService ?? throw new ArgumentNullException(nameof(singleWindowTrackingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public async Task<SingleWindowHandoffPackageResult> ExportSubmitPackageAsync(
            SingleWindowBusinessType businessType,
            int invoiceId,
            string savePath,
            CancellationToken cancellationToken = default)
        {
            string targetPath = PackagePathHelper.NormalizePackagePath(savePath, ".swpkg", nameof(savePath));
            string tempDirectory = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "SingleWindowPackages",
                "sw-submit");

            try
            {
                Directory.CreateDirectory(tempDirectory);

                var payloads = new List<PayloadBuildResult>();
                IReadOnlyList<SingleWindowAttachmentSource> attachments;
                object snapshot;
                List<string> warnings = [];
                string invoiceNo;
                string contractNo;
                int sourceDocumentId = 0;
                string sourceDocumentType = string.Empty;
                int draftRevision = 0;
                string sourceBaselineHash = string.Empty;

                switch (businessType)
                {
                    case SingleWindowBusinessType.CustomsCoo:
                    {
                        var source = await _customsCooSourceAssembler.BuildAsync(invoiceId, cancellationToken);
                        snapshot = source;
                        var mapped = _customsCooFieldMapper.Map(source);
                        CustomsCooDefaultProfileApplicator.Apply(
                            mapped,
                            _settingsService.Settings?.SingleWindow?.CustomsCooDefaults);
                        warnings.AddRange(mapped.Warnings);
                        warnings.AddRange(_xmlValidator.ValidateForBuild(businessType, mapped));
                        payloads.Add(_customsCooPayloadGenerator.BuildCertificateXml(mapped));
                        payloads.AddRange(_customsCooPayloadGenerator.BuildAttachmentXmls(mapped));
                        attachments = mapped.Attachments;
                        invoiceNo = source.Invoice?.InvoiceNo ?? string.Empty;
                        contractNo = source.Invoice?.ContractNo ?? string.Empty;
                        draftRevision = source.ExistingDocument?.DraftRevision ?? 0;
                        sourceBaselineHash = source.ExistingDocument?.SourceBaselineHash ?? string.Empty;
                        sourceDocumentId = await TryPersistCustomsCooDocumentAsync(source, mapped, cancellationToken);
                        sourceDocumentType = sourceDocumentId > 0 ? nameof(CustomsCooDocument) : string.Empty;
                        break;
                    }
                    case SingleWindowBusinessType.AgentConsignment:
                    {
                        var source = await _agentConsignmentSourceAssembler.BuildAsync(invoiceId, cancellationToken);
                        snapshot = source;
                        var mapped = _agentConsignmentFieldMapper.Map(source);
                        warnings.AddRange(mapped.Warnings);
                        warnings.AddRange(_xmlValidator.ValidateForBuild(businessType, mapped));
                        payloads.Add(_agentConsignmentPayloadGenerator.BuildRequestXml(mapped));
                        attachments = source.Attachments;
                        invoiceNo = source.Invoice?.InvoiceNo ?? string.Empty;
                        contractNo = source.Invoice?.ContractNo ?? string.Empty;
                        draftRevision = source.ExistingDocument?.DraftRevision ?? 0;
                        sourceBaselineHash = source.ExistingDocument?.SourceBaselineHash ?? string.Empty;
                        sourceDocumentId = await TryPersistAgentConsignmentDocumentAsync(source, mapped, cancellationToken);
                        sourceDocumentType = sourceDocumentId > 0 ? nameof(AgentConsignmentDocument) : string.Empty;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("不支持的单一窗口业务类型。");
                }

                int submissionVersion = await ResolveNextSubmissionVersionAsync(
                    businessType,
                    invoiceId,
                    sourceDocumentId,
                    cancellationToken);

                await File.WriteAllTextAsync(
                    Path.Combine(tempDirectory, "snapshot.json"),
                    JsonSerializer.Serialize(snapshot, snapshot.GetType(), JsonOptions),
                    cancellationToken);

                string payloadDirectory = Path.Combine(tempDirectory, "payloads");
                Directory.CreateDirectory(payloadDirectory);

                var payloadFiles = new List<SingleWindowPackageFile>();
                foreach (var payload in payloads)
                {
                    string payloadPath = Path.Combine(payloadDirectory, payload.FileName);
                    await File.WriteAllTextAsync(payloadPath, payload.Content, cancellationToken);
                    payloadFiles.Add(new SingleWindowPackageFile
                    {
                        RelativePath = Path.Combine("payloads", payload.FileName),
                        MediaType = payload.MediaType,
                        Description = payload.FileName
                    });
                    warnings.AddRange(payload.Warnings);
                }

                var attachmentFiles = await CopyAttachmentsAsync(tempDirectory, attachments, cancellationToken);
                var manifest = new SingleWindowPackageManifest
                {
                    PackageType = SingleWindowPackageType.SubmitPackage,
                    BusinessType = businessType,
                    BatchReference = BuildBatchReference(businessType, submissionVersion),
                    SourceInvoiceId = invoiceId,
                    SourceDocumentId = sourceDocumentId,
                    SourceDocumentType = sourceDocumentType,
                    SubmissionVersion = submissionVersion,
                    DraftRevision = Math.Max(1, draftRevision),
                    SourceBaselineHash = sourceBaselineHash ?? string.Empty,
                    InvoiceNo = invoiceNo,
                    ContractNo = contractNo,
                    PayloadFiles = payloadFiles,
                    AttachmentFiles = attachmentFiles,
                    Warnings = warnings.Distinct(StringComparer.Ordinal).ToList()
                };

                await File.WriteAllTextAsync(
                    Path.Combine(tempDirectory, "manifest.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions),
                    cancellationToken);

                await ZipArchiveHelper.CreateFromDirectoryAsync(tempDirectory, targetPath, cancellationToken);

                int? trackingBatchId = await TryRecordSubmitPackageExportAsync(
                    targetPath,
                    manifest,
                    cancellationToken);

                return new SingleWindowHandoffPackageResult
                {
                    PackagePath = targetPath,
                    Manifest = manifest,
                    TrackingBatchId = trackingBatchId
                };
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(tempDirectory);
            }
        }

        public Task<SingleWindowImportedPackage> ImportSubmitPackageAsync(
            string packagePath,
            string workingDirectory = "",
            CancellationToken cancellationToken = default)
        {
            return ImportPackageAsync(packagePath, workingDirectory, cancellationToken);
        }

        public async Task<SingleWindowHandoffPackageResult> ExportReceiptPackageAsync(
            SingleWindowBusinessType businessType,
            string batchReference,
            string invoiceNo,
            IReadOnlyList<string> receiptFiles,
            string savePath,
            CancellationToken cancellationToken = default)
        {
            string targetPath = PackagePathHelper.NormalizePackagePath(savePath, ".swpkg", nameof(savePath));
            string tempDirectory = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "SingleWindowPackages",
                "sw-receipt");

            try
            {
                Directory.CreateDirectory(tempDirectory);
                string receiptsDirectory = Path.Combine(tempDirectory, "receipts");
                var copiedFiles = await CopyReceiptFilesAsync(receiptsDirectory, receiptFiles ?? [], cancellationToken);

                var manifest = new SingleWindowPackageManifest
                {
                    PackageType = SingleWindowPackageType.ReceiptPackage,
                    BusinessType = businessType,
                    BatchReference = batchReference ?? string.Empty,
                    InvoiceNo = invoiceNo ?? string.Empty,
                    PayloadFiles = copiedFiles
                };

                await File.WriteAllTextAsync(
                    Path.Combine(tempDirectory, "manifest.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions),
                    cancellationToken);

                await ZipArchiveHelper.CreateFromDirectoryAsync(tempDirectory, targetPath, cancellationToken);

                int? trackingBatchId = await TryRecordReceiptPackageExportAsync(
                    targetPath,
                    manifest,
                    cancellationToken);

                return new SingleWindowHandoffPackageResult
                {
                    PackagePath = targetPath,
                    Manifest = manifest,
                    TrackingBatchId = trackingBatchId
                };
            }
            finally
            {
                AtomicFileHelper.TryDeleteDirectory(tempDirectory);
            }
        }

        public Task<SingleWindowImportedPackage> ImportReceiptPackageAsync(
            string packagePath,
            string workingDirectory = "",
            CancellationToken cancellationToken = default)
        {
            return ImportPackageAsync(packagePath, workingDirectory, cancellationToken);
        }
    }
}
