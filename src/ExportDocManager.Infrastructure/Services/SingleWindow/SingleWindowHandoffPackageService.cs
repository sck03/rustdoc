using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
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
            string stationAssignmentCode,
            string savePath,
            CancellationToken cancellationToken = default)
        {
            var stationAssignment = SingleWindowStationAssignmentCode.Decode(stationAssignmentCode);
            string targetPath = PackagePathHelper.NormalizePackagePath(savePath, ".swpkg", nameof(savePath));
            bool targetExisted = File.Exists(targetPath);
            string tempDirectory = RuntimeCachePathHelper.CreateUniqueDirectory(
                _pathProvider,
                "SingleWindowPackages",
                "sw-submit");
            int reservationBatchId = 0;

            try
            {
                Directory.CreateDirectory(tempDirectory);

                var payloads = new List<PayloadBuildResult>();
                IReadOnlyList<SingleWindowAttachmentSource> attachments;
                object snapshot;
                List<string> warnings = [];
                string invoiceNo;
                string contractNo;
                string companyScope;
                int sourceDocumentId = 0;
                string sourceDocumentType = string.Empty;
                int draftRevision = 0;
                string sourceBaselineHash = string.Empty;
                CooMappedDocument? customsCooDocument = null;

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
                        customsCooDocument = mapped;
                        attachments = mapped.Attachments;
                        invoiceNo = source.Invoice?.InvoiceNo ?? string.Empty;
                        contractNo = source.Invoice?.ContractNo ?? string.Empty;
                        companyScope = source.Invoice?.CompanyScope ?? string.Empty;
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
                        companyScope = source.Invoice?.CompanyScope ?? string.Empty;
                        draftRevision = source.ExistingDocument?.DraftRevision ?? 0;
                        sourceBaselineHash = source.ExistingDocument?.SourceBaselineHash ?? string.Empty;
                        sourceDocumentId = await TryPersistAgentConsignmentDocumentAsync(source, mapped, cancellationToken);
                        sourceDocumentType = sourceDocumentId > 0 ? nameof(AgentConsignmentDocument) : string.Empty;
                        break;
                    }
                    default:
                        throw new ServiceValidationException("不支持的单一窗口业务类型。");
                }

                attachments = SingleWindowAttachmentResourcePolicy.ValidateAndSelect(attachments);
                EnsureAssignmentMatchesDocument(stationAssignment, businessType, companyScope);

                var reservation = await _singleWindowTrackingService.ReserveSubmissionAsync(
                    businessType,
                    invoiceId,
                    sourceDocumentId,
                    sourceDocumentType,
                    Math.Max(1, draftRevision),
                    sourceBaselineHash,
                    invoiceNo,
                    contractNo,
                    companyScope,
                    cancellationToken);
                reservationBatchId = reservation.BatchId;
                int submissionVersion = reservation.SubmissionVersion;

                string snapshotPath = Path.Combine(tempDirectory, "snapshot.json");
                await File.WriteAllTextAsync(
                    snapshotPath,
                    JsonSerializer.Serialize(snapshot, snapshot.GetType(), JsonOptions),
                    cancellationToken);
                string snapshotSha256 = await SingleWindowPackageIntegrity.ComputeFileSha256Async(snapshotPath, cancellationToken);

                string payloadDirectory = Path.Combine(tempDirectory, "payloads");
                Directory.CreateDirectory(payloadDirectory);

                var payloadFiles = new List<SingleWindowPackageFile>();
                foreach (var payload in payloads)
                {
                    string payloadPath = Path.Combine(payloadDirectory, payload.FileName);
                    await File.WriteAllTextAsync(payloadPath, payload.Content, cancellationToken);
                    payloadFiles.Add(await SingleWindowPackageIntegrity.DescribeFileAsync(
                        payloadPath,
                        PathBoundaryHelper.ToProtocolRelativePath("payloads", payload.FileName),
                        payload.MediaType,
                        payload.FileName,
                        cancellationToken));
                    warnings.AddRange(payload.Warnings);
                }

                if (customsCooDocument != null && attachments.Count > 0)
                {
                    var usedPayloadFileNames = payloadFiles
                        .Select(file => Path.GetFileName(file.RelativePath))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var attachment in attachments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string suggestedFileName = SingleWindowPayloadFileNameHelper.BuildBaseFileName(
                            Path.GetFileNameWithoutExtension(attachment.FileName),
                            "coo-attachment",
                            ".xml");
                        string payloadFileName = CopyFileToPackageDirectory(
                            attachment.FilePath,
                            usedPayloadFileNames,
                            suggestedFileName);
                        string payloadPath = Path.Combine(payloadDirectory, payloadFileName);
                        await using (var output = new FileStream(
                            payloadPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            await _customsCooPayloadGenerator.WriteAttachmentXmlAsync(
                                customsCooDocument,
                                attachment,
                                output,
                                cancellationToken).ConfigureAwait(false);
                        }
                        payloadFiles.Add(await SingleWindowPackageIntegrity.DescribeFileAsync(
                            payloadPath,
                            PathBoundaryHelper.ToProtocolRelativePath("payloads", payloadFileName),
                            "application/xml",
                            attachment.Description,
                            cancellationToken));
                    }
                }

                var attachmentFiles = await CopyAttachmentsAsync(tempDirectory, attachments, cancellationToken);
                var manifest = new SingleWindowPackageManifest
                {
                    PackageType = SingleWindowPackageType.SubmitPackage,
                    BusinessType = businessType,
                    BatchReference = reservation.BatchReference,
                    SourceInvoiceId = invoiceId,
                    SourceDocumentId = sourceDocumentId,
                    SourceDocumentType = sourceDocumentType,
                    SubmissionVersion = submissionVersion,
                    DraftRevision = Math.Max(1, draftRevision),
                    SourceBaselineHash = sourceBaselineHash ?? string.Empty,
                    InvoiceNo = invoiceNo,
                    ContractNo = contractNo,
                    CompanyScope = companyScope,
                    SnapshotSha256 = snapshotSha256,
                    StationKey = stationAssignment.StationKey,
                    CardIdentifier = stationAssignment.CardIdentifier,
                    ClientProfileKey = stationAssignment.ProfileKey,
                    ClientProfileName = stationAssignment.ProfileName,
                    AssignmentNonce = Guid.NewGuid().ToString("N"),
                    AuthenticationAlgorithm = SingleWindowPackageIntegrity.AuthenticationAlgorithm,
                    PayloadFiles = payloadFiles,
                    AttachmentFiles = attachmentFiles,
                    Warnings = warnings.Distinct(StringComparer.Ordinal).ToList()
                };
                manifest.ContentDigest = SingleWindowPackageIntegrity.ComputeContentDigest(manifest);
                manifest.AuthenticationTag = SingleWindowPackageIntegrity.ComputeAuthenticationTag(
                    manifest,
                    stationAssignment.AuthenticationSecret);

                await File.WriteAllTextAsync(
                    Path.Combine(tempDirectory, "manifest.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions),
                    cancellationToken);

                await ZipArchiveHelper.CreateFromDirectoryAsync(tempDirectory, targetPath, cancellationToken);

                int trackingBatchId = await _singleWindowTrackingService.RecordSubmitPackageExportAsync(
                    targetPath,
                    manifest,
                    stationAssignment.AuthenticationSecret,
                    cancellationToken);
                if (trackingBatchId != reservation.BatchId)
                {
                    throw new ServiceConcurrencyException("单一窗口提交包跟踪批次与版本预留不一致。");
                }

                return new SingleWindowHandoffPackageResult
                {
                    PackagePath = targetPath,
                    Manifest = manifest,
                    TrackingBatchId = trackingBatchId
                };
            }
            catch (Exception ex)
            {
                if (reservationBatchId > 0)
                {
                    try
                    {
                        await _singleWindowTrackingService.MarkSubmissionReservationFailedAsync(
                            reservationBatchId,
                            ex.Message,
                            CancellationToken.None);
                    }
                    catch (Exception trackingException)
                    {
                        Serilog.Log.Error(
                            trackingException,
                            "Marking failed single-window reservation {BatchId} failed",
                            reservationBatchId);
                    }
                }

                if (!targetExisted)
                {
                    AtomicFileHelper.TryDeleteFile(targetPath);
                }

                throw;
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
            return ImportPackageAsync(
                packagePath,
                workingDirectory,
                SingleWindowPackageType.SubmitPackage,
                cancellationToken);
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
                var binding = await _singleWindowTrackingService.ResolveReceiptPackageBindingAsync(
                    businessType,
                    batchReference,
                    invoiceNo,
                    cancellationToken);
                Directory.CreateDirectory(tempDirectory);
                string receiptsDirectory = Path.Combine(tempDirectory, "receipts");
                var copiedFiles = await CopyReceiptFilesAsync(receiptsDirectory, receiptFiles ?? [], cancellationToken);
                if (copiedFiles.Count == 0)
                {
                    throw new InvalidDataException("没有可打包的有效单一窗口回执文件。");
                }

                var parsedReceiptReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string receiptFile in receiptFiles ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string content = await File.ReadAllTextAsync(receiptFile, cancellationToken).ConfigureAwait(false);
                    var parsed = _singleWindowReceiptParser.Parse(
                        binding.BusinessType,
                        content,
                        Path.GetFileName(receiptFile));
                    string referenceNo = parsed?.ReferenceNo?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(referenceNo))
                    {
                        parsedReceiptReferences.Add(referenceNo);
                    }
                }
                if (parsedReceiptReferences.Count > 1)
                {
                    throw new InvalidDataException("所选回执文件包含多个不同的官方业务编号，不能打入同一批次回执包。" );
                }

                var manifest = new SingleWindowPackageManifest
                {
                    PackageType = SingleWindowPackageType.ReceiptPackage,
                    BusinessType = binding.BusinessType,
                    BatchReference = binding.BatchReference,
                    SourceInvoiceId = binding.SourceInvoiceId,
                    SourceDocumentId = binding.SourceDocumentId,
                    SourceDocumentType = binding.SourceDocumentType,
                    SubmissionVersion = binding.SubmissionVersion,
                    DraftRevision = binding.DraftRevision,
                    SourceBaselineHash = binding.SourceBaselineHash,
                    InvoiceNo = binding.InvoiceNo,
                    ContractNo = binding.ContractNo,
                    CompanyScope = binding.CompanyScope,
                    SourcePackageDigest = binding.SubmitPackageDigest,
                    ReceiptReferenceNo = parsedReceiptReferences.SingleOrDefault() ?? string.Empty,
                    StationKey = binding.AssignedStationKey,
                    CardIdentifier = binding.AssignedCardIdentifier,
                    ClientProfileKey = binding.AssignedProfileKey,
                    ClientProfileName = binding.ClientProfileName,
                    AssignmentNonce = Guid.NewGuid().ToString("N"),
                    AuthenticationAlgorithm = SingleWindowPackageIntegrity.AuthenticationAlgorithm,
                    PayloadFiles = copiedFiles
                };
                manifest.ContentDigest = SingleWindowPackageIntegrity.ComputeContentDigest(manifest);
                manifest.AuthenticationTag = SingleWindowPackageIntegrity.ComputeAuthenticationTag(
                    manifest,
                    binding.AuthenticationSecret);

                await File.WriteAllTextAsync(
                    Path.Combine(tempDirectory, "manifest.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions),
                    cancellationToken);

                await ZipArchiveHelper.CreateFromDirectoryAsync(tempDirectory, targetPath, cancellationToken);

                int trackingBatchId = await _singleWindowTrackingService.RecordReceiptPackageExportAsync(
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
            return ImportPackageAsync(
                packagePath,
                workingDirectory,
                SingleWindowPackageType.ReceiptPackage,
                cancellationToken);
        }

        private static void EnsureAssignmentMatchesDocument(
            SingleWindowStationAssignment assignment,
            SingleWindowBusinessType businessType,
            string companyScope)
        {
            if (!string.Equals(
                    assignment.CompanyScope,
                    companyScope?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new PermissionDeniedException(
                    "持卡机授权码绑定的公司抬头与当前单据不一致。" );
            }

            bool canHandle = businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => assignment.CanSubmitCustomsCoo,
                SingleWindowBusinessType.AgentConsignment => assignment.CanSubmitAgentConsignment,
                _ => false
            };
            if (!canHandle)
            {
                throw new PermissionDeniedException("目标操作档案未启用当前单一窗口业务。" );
            }
        }
    }
}
