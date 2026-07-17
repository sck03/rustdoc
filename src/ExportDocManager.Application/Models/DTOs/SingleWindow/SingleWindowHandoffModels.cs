using System.Text.Json.Serialization;
using System.IO;
using System.Threading;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public sealed class SingleWindowPackageFile
    {
        public string RelativePath { get; init; } = string.Empty;

        public string MediaType { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
    }

    public sealed class SingleWindowPackageManifest
    {
        public string SchemaVersion { get; init; } = "1.0";

        public SingleWindowPackageType PackageType { get; init; }

        public SingleWindowBusinessType BusinessType { get; init; }

        public string BatchReference { get; init; } = string.Empty;

        public int SourceInvoiceId { get; init; }

        public int SourceDocumentId { get; init; }

        public string SourceDocumentType { get; init; } = string.Empty;

        public int SubmissionVersion { get; init; }

        public int DraftRevision { get; init; }

        public string SourceBaselineHash { get; init; } = string.Empty;

        public string InvoiceNo { get; init; } = string.Empty;

        public string ContractNo { get; init; } = string.Empty;

        public DateTime CreatedAt { get; init; } = DateTime.Now;

        public string CreatedOnMachine { get; init; } = Environment.MachineName;

        public IReadOnlyList<SingleWindowPackageFile> PayloadFiles { get; init; } = [];

        public IReadOnlyList<SingleWindowPackageFile> AttachmentFiles { get; init; } = [];

        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    public sealed class SingleWindowHandoffPackageResult
    {
        public string PackagePath { get; init; } = string.Empty;

        public SingleWindowPackageManifest Manifest { get; init; } = new();

        public int? TrackingBatchId { get; init; }
    }

    public sealed class SingleWindowImportedPackage : IDisposable
    {
        private bool _keepWorkingDirectory;

        public string WorkingDirectory { get; init; } = string.Empty;

        public SingleWindowPackageManifest Manifest { get; init; } = new();

        public IReadOnlyList<SingleWindowReceiptParseResult> ParsedReceipts { get; init; } = [];

        public int? TrackingBatchId { get; init; }

        public string TrackingStatus { get; init; } = string.Empty;

        public int PersistedReceiptCount { get; init; }

        public void KeepWorkingDirectory()
        {
            _keepWorkingDirectory = true;
        }

        public void Dispose()
        {
            if (_keepWorkingDirectory)
            {
                return;
            }

            TryDeleteWorkingDirectory(WorkingDirectory);
        }

        private static void TryDeleteWorkingDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            const int maxAttempts = 3;
            const int retryDelayMilliseconds = 50;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        return;
                    }

                    ResetDirectoryAttributes(directoryPath);
                    Directory.Delete(directoryPath, recursive: true);
                    return;
                }
                catch when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(retryDelayMilliseconds);
                }
                catch
                {
                    return;
                }
            }
        }

        private static void ResetDirectoryAttributes(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                ResetFileAttributes(filePath);
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
            {
                ResetFileAttributes(childDirectory);
            }

            ResetFileAttributes(directoryPath);
        }

        private static void ResetFileAttributes(string path)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
            catch
            {
            }
        }
    }

    public sealed class SingleWindowAttachmentSource
    {
        public string CertNo { get; init; } = string.Empty;

        public string CertType { get; init; } = string.Empty;

        public string AplRegNo { get; init; } = string.Empty;

        public string CiqRegNo { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string FilePath { get; init; } = string.Empty;

        public string MediaType { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string FileType { get; init; } = string.Empty;

        public string DocType { get; init; } = string.Empty;

        public bool IsDelay { get; init; }

        [JsonIgnore]
        public bool Exists => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
    }

    public sealed class CooSourceSnapshot
    {
        public Invoice Invoice { get; init; } = new();

        public IReadOnlyList<Item> Items { get; init; } = [];

        public Customer Customer { get; init; }

        public Exporter Exporter { get; init; }

        public CustomsCooDocument ExistingDocument { get; init; }

        public IReadOnlyList<SingleWindowAttachmentSource> Attachments { get; init; } = [];
    }

    public sealed class AcdSourceSnapshot
    {
        public Invoice Invoice { get; init; } = new();

        public IReadOnlyList<Item> Items { get; init; } = [];

        public Customer Customer { get; init; }

        public Exporter Exporter { get; init; }

        public AgentConsignmentDocument ExistingDocument { get; init; }

        public IReadOnlyList<SingleWindowAttachmentSource> Attachments { get; init; } = [];
    }

    public sealed class CooMappedDocument
    {
        public string CertNo { get; set; } = string.Empty;
        public string ApplyType { get; set; } = "0";
        public string CertStatus { get; set; } = "0";
        public string CertType { get; set; } = "C";
        public string EntMgrNo { get; set; } = string.Empty;
        public string CiqRegNo { get; set; } = string.Empty;
        public string AplRegNo { get; set; } = string.Empty;
        public string EtpsName { get; set; } = string.Empty;
        public string ApplName { get; set; } = string.Empty;
        public string Applicant { get; set; } = string.Empty;
        public string ApplTel { get; set; } = string.Empty;
        public string OrgCode { get; set; } = string.Empty;
        public string FetchPlace { get; set; } = string.Empty;
        public string AplAdd { get; set; } = string.Empty;
        public string InvDate { get; set; } = string.Empty;
        public string InvNo { get; set; } = string.Empty;
        public string AplDate { get; set; } = string.Empty;
        public string DestCountry { get; set; } = string.Empty;
        public string DestCountryCode { get; set; } = string.Empty;
        public string DestCountryName { get; set; } = string.Empty;
        public string Exporter { get; set; } = string.Empty;
        public string Consignee { get; set; } = string.Empty;
        public string GoodsSpecClause { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty;
        public string LoadPort { get; set; } = string.Empty;
        public string UnloadPort { get; set; } = string.Empty;
        public string TransMeans { get; set; } = string.Empty;
        public string TransName { get; set; } = string.Empty;
        public string TransCountryCode { get; set; } = string.Empty;
        public string TransCountryName { get; set; } = string.Empty;
        public string TransPort { get; set; } = string.Empty;
        public string DestPort { get; set; } = string.Empty;
        public string TransDetails { get; set; } = string.Empty;
        public string IntendExpDate { get; set; } = string.Empty;
        public string TradeModeCode { get; set; } = string.Empty;
        public string FobValue { get; set; } = string.Empty;
        public string TotalAmt { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string ContractNo { get; set; } = string.Empty;
        public string LcNo { get; set; } = string.Empty;
        public string SpecInvTerms { get; set; } = string.Empty;
        public string PriceTerms { get; set; } = string.Empty;
        public string Curr { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string Producer { get; set; } = string.Empty;
        public string ProducerSertFlag { get; set; } = string.Empty;
        public string ExhibitFlag { get; set; } = string.Empty;
        public string ThirdPartyInvFlag { get; set; } = string.Empty;
        public string ExporterTel { get; set; } = string.Empty;
        public string ExporterFax { get; set; } = string.Empty;
        public string ExporterEmail { get; set; } = string.Empty;
        public string ConsigneeTel { get; set; } = string.Empty;
        public string ConsigneeFax { get; set; } = string.Empty;
        public string ConsigneeEmail { get; set; } = string.Empty;
        public string PredictFlag { get; set; } = string.Empty;
        public string ExpDeclDate { get; set; } = string.Empty;
        public string OriCountryCode { get; set; } = string.Empty;
        public string OriCountry { get; set; } = string.Empty;
        public string ChkValidDate { get; set; } = string.Empty;
        public string EtpsConcEr { get; set; } = string.Empty;
        public string EtpsTel { get; set; } = string.Empty;
        public string EntryId { get; set; } = string.Empty;
        public string PrcsAssembly { get; set; } = string.Empty;
        public string OldCertNo { get; set; } = string.Empty;
        public string ModReason { get; set; } = string.Empty;
        public string ModColm { get; set; } = string.Empty;
        public string OldSituDesc { get; set; } = string.Empty;
        public string ModSituDesc { get; set; } = string.Empty;
        public string OldDeclDate { get; set; } = string.Empty;
        public string OldIssueDate { get; set; } = string.Empty;
        public string AplPromiseCode { get; set; } = "1";
        public IReadOnlyList<CooMappedGoodsItem> Goods { get; set; } = [];
        public IReadOnlyList<CooMappedNonpartyCorp> NonpartyCorps { get; set; } = [];
        public IReadOnlyList<SingleWindowAttachmentSource> Attachments { get; set; } = [];
        public IReadOnlyList<string> Warnings { get; set; } = [];
    }

    public sealed class CooMappedNonpartyCorp
    {
        public int SortNo { get; set; }
        public string EntName { get; set; } = string.Empty;
        public string EntAddr { get; set; } = string.Empty;
        public string EntCountryCode { get; set; } = string.Empty;
        public string EntCountryName { get; set; } = string.Empty;
    }

    public sealed class CooMappedGoodsItem
    {
        public string GoodsItemFlag { get; set; } = "N";
        public int GNo { get; set; }
        public string HSCode { get; set; } = string.Empty;
        public string GoodsName { get; set; } = string.Empty;
        public string GoodsNameE { get; set; } = string.Empty;
        public string PackQty { get; set; } = string.Empty;
        public string PackUnit { get; set; } = string.Empty;
        public string GoodsQty { get; set; } = string.Empty;
        public string GoodsQtyRef { get; set; } = string.Empty;
        public string GoodsUnitE { get; set; } = string.Empty;
        public string GoodsUnit { get; set; } = string.Empty;
        public string GoodsUnitRef { get; set; } = string.Empty;
        public string SecdGoodsQtyRef { get; set; } = string.Empty;
        public string SecdGoodsUnitRef { get; set; } = string.Empty;
        public string GrossWt { get; set; } = string.Empty;
        public string NetWt { get; set; } = string.Empty;
        public string WtUnit { get; set; } = string.Empty;
        public string InvPrice { get; set; } = string.Empty;
        public string InvValue { get; set; } = string.Empty;
        public string FobValue { get; set; } = string.Empty;
        public string ICompPrpr { get; set; } = string.Empty;
        public string GoodsDesc { get; set; } = string.Empty;
        public string OriCriteria { get; set; } = string.Empty;
        public string OriCriteriaRef { get; set; } = string.Empty;
        public string GoodsOriginCountry { get; set; } = string.Empty;
        public string GoodsOriginCountryEn { get; set; } = string.Empty;
        public string Producer { get; set; } = string.Empty;
        public string ProducerTel { get; set; } = string.Empty;
        public string ProducerFax { get; set; } = string.Empty;
        public string ProducerEmail { get; set; } = string.Empty;
        public string CiqRegNo { get; set; } = string.Empty;
        public string PrdcEtpsName { get; set; } = string.Empty;
        public string PrdcEtpsConcEr { get; set; } = string.Empty;
        public string PrdcEtpsTel { get; set; } = string.Empty;
        public string ProducerSertFlag { get; set; } = string.Empty;
        public string OriCriteriaSub { get; set; } = string.Empty;
        public string InvNo { get; set; } = string.Empty;
        public string PackType { get; set; } = "1";
        public string GoodsTaxRate { get; set; } = string.Empty;
    }

    public sealed class AcdMappedDocument
    {
        public string CopCusCode { get; set; } = string.Empty;
        public string Sign { get; set; } = string.Empty;
        public string OperType { get; set; } = "1";
        public string GName { get; set; } = string.Empty;
        public string CodeTS { get; set; } = string.Empty;
        public string DeclTotal { get; set; } = string.Empty;
        public string IEDate { get; set; } = string.Empty;
        public string ListNo { get; set; } = string.Empty;
        public string TradeMode { get; set; } = string.Empty;
        public string OriCountry { get; set; } = string.Empty;
        public string TradeCode { get; set; } = string.Empty;
        public string AgentCode { get; set; } = string.Empty;
        public string Curr { get; set; } = string.Empty;
        public string QtyOrWeight { get; set; } = string.Empty;
        public string PackingCondition { get; set; } = string.Empty;
        public string OtherNote { get; set; } = string.Empty;
        public string ConsignTele { get; set; } = string.Empty;
        public string EntryId { get; set; } = string.Empty;
        public string ReceiveDate { get; set; } = string.Empty;
        public string PaperInfo { get; set; } = string.Empty;
        public string OtherRecInfo { get; set; } = string.Empty;
        public string DeclarePrice { get; set; } = string.Empty;
        public string PromiseNote { get; set; } = string.Empty;
        public string DeclTele { get; set; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; set; } = [];
    }

    public sealed class PayloadBuildResult
    {
        public string FileName { get; init; } = string.Empty;

        public string FileExtension { get; init; } = ".xml";

        public string MediaType { get; init; } = "application/xml";

        public string Content { get; init; } = string.Empty;

        public IReadOnlyList<string> Warnings { get; init; } = [];
    }
}
