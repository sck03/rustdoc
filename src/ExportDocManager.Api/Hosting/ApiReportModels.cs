namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiReportHtmlPreviewRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;
    }

    public sealed class ApiPaymentDraftReportHtmlPreviewRequest
    {
        public string ReportType { get; set; } = "PaymentVoucher";

        public string TemplatePath { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;

        public ApiPaymentDto Payment { get; set; }
    }

    public sealed class ApiInvoiceDraftReportHtmlPreviewRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;

        public ApiInvoiceDetailDto Invoice { get; set; }
    }

    public sealed class ApiReportPdfRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;

        public string DestinationPath { get; set; } = string.Empty;
    }

    public sealed class ApiInvoiceReportZipRequest
    {
        public List<int> InvoiceIds { get; set; } = new();

        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;

        public string DestinationPath { get; set; } = string.Empty;
    }

    public sealed class ApiInvoiceDocumentPackageItemRequest
    {
        public string Name { get; set; } = string.Empty;

        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;
    }

    public sealed class ApiInvoiceDocumentPackageRequest
    {
        public List<ApiInvoiceDocumentPackageItemRequest> Items { get; set; } = new();

        public bool IncludeMergedPdf { get; set; } = true;

        public bool CreateZip { get; set; } = true;

        public string DestinationPath { get; set; } = string.Empty;
    }

    public sealed class ApiInvoiceDocumentPackagePreviewRequest
    {
        public List<ApiInvoiceDocumentPackageItemRequest> Items { get; set; } = new();
    }

    public sealed class ApiInvoiceDocumentEmailRequest
    {
        public List<ApiInvoiceDocumentPackageItemRequest> Items { get; set; } = new();

        public bool IncludeMergedPdf { get; set; }

        public string ToAddress { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }

    public sealed class ApiReportHtmlPreviewResponse
    {
        public ApiReportHtmlPreviewResponse(
            int invoiceId,
            string reportType,
            string templatePath,
            bool withSeal,
            string html,
            string storagePolicy = "")
        {
            InvoiceId = invoiceId;
            ReportType = reportType;
            TemplatePath = templatePath ?? string.Empty;
            WithSeal = withSeal;
            Html = html ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public int InvoiceId { get; }

        public string ReportType { get; }

        public string TemplatePath { get; }

        public bool WithSeal { get; }

        public string Html { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiInvoiceDocumentPackagePreviewItemResponse
    {
        public ApiInvoiceDocumentPackagePreviewItemResponse(
            string name,
            string reportType,
            string templatePath,
            bool withSeal,
            string html)
        {
            Name = name ?? string.Empty;
            ReportType = reportType ?? string.Empty;
            TemplatePath = templatePath ?? string.Empty;
            WithSeal = withSeal;
            Html = html ?? string.Empty;
        }

        public string Name { get; }

        public string ReportType { get; }

        public string TemplatePath { get; }

        public bool WithSeal { get; }

        public string Html { get; }
    }

    public sealed class ApiInvoiceDocumentPackagePreviewResponse
    {
        public ApiInvoiceDocumentPackagePreviewResponse(
            int invoiceId,
            IReadOnlyList<ApiInvoiceDocumentPackagePreviewItemResponse> items,
            string storagePolicy)
        {
            InvoiceId = invoiceId;
            Items = items ?? Array.Empty<ApiInvoiceDocumentPackagePreviewItemResponse>();
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public int InvoiceId { get; }

        public IReadOnlyList<ApiInvoiceDocumentPackagePreviewItemResponse> Items { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiPaymentReportHtmlPreviewResponse
    {
        public ApiPaymentReportHtmlPreviewResponse(
            int paymentId,
            string reportType,
            string templatePath,
            bool withSeal,
            string html,
            string storagePolicy = "")
        {
            PaymentId = paymentId;
            ReportType = reportType ?? string.Empty;
            TemplatePath = templatePath ?? string.Empty;
            WithSeal = withSeal;
            Html = html ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public int PaymentId { get; }

        public string ReportType { get; }

        public string TemplatePath { get; }

        public bool WithSeal { get; }

        public string Html { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiReportTemplateDto
    {
        public ApiReportTemplateDto(
            string reportType,
            string displayName,
            string templatePath,
            bool withSealDefault)
        {
            ReportType = reportType ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            TemplatePath = templatePath ?? string.Empty;
            WithSealDefault = withSealDefault;
        }

        public string ReportType { get; }

        public string DisplayName { get; }

        public string TemplatePath { get; }

        public bool WithSealDefault { get; }
    }

    public sealed class ApiReportTemplateContentDto
    {
        public ApiReportTemplateContentDto(
            string reportType,
            string displayName,
            string templatePath,
            bool withSealDefault,
            string content,
            string storagePolicy)
        {
            ReportType = reportType ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            TemplatePath = templatePath ?? string.Empty;
            WithSealDefault = withSealDefault;
            Content = content ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public string ReportType { get; }

        public string DisplayName { get; }

        public string TemplatePath { get; }

        public bool WithSealDefault { get; }

        public string Content { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiReportTemplateSaveRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }

    public sealed class ApiReportTemplateCreateRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }

    public sealed class ApiReportTemplateRenameRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string TemplatePath { get; set; } = string.Empty;

        public string NewTemplatePath { get; set; } = string.Empty;
    }

    public sealed class ApiReportTemplatePreviewRequest
    {
        public string ReportType { get; set; } = "ExportDocument";

        public string Content { get; set; } = string.Empty;

        public bool WithSeal { get; set; } = true;
    }

    public sealed class ApiReportTemplatePreviewResponse
    {
        public ApiReportTemplatePreviewResponse(
            string reportType,
            bool withSeal,
            string html)
        {
            ReportType = reportType ?? string.Empty;
            WithSeal = withSeal;
            Html = html ?? string.Empty;
        }

        public string ReportType { get; }

        public bool WithSeal { get; }

        public string Html { get; }
    }

    public sealed class ApiReportTemplateStorageStatusResponse
    {
        public ApiReportTemplateStorageStatusResponse(
            string templateRoot,
            bool exists,
            bool writable,
            string message,
            string storagePolicy)
        {
            TemplateRoot = templateRoot ?? string.Empty;
            Exists = exists;
            Writable = writable;
            Message = message ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public string TemplateRoot { get; }

        public bool Exists { get; }

        public bool Writable { get; }

        public string Message { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiReportTemplatePackageExportRequest
    {
        public string PackagePath { get; set; } = string.Empty;
    }

    public sealed class ApiReportTemplatePackageExportResponse
    {
        public ApiReportTemplatePackageExportResponse(
            string packagePath,
            int templateCount,
            string storagePolicy)
        {
            PackagePath = packagePath ?? string.Empty;
            TemplateCount = templateCount;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public string PackagePath { get; }

        public int TemplateCount { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiReportTemplatePackageImportRequest
    {
        public string PackagePath { get; set; } = string.Empty;

        public string Strategy { get; set; } = "Merge";
    }

    public sealed class ApiReportTemplatePackageImportResponse
    {
        public ApiReportTemplatePackageImportResponse(
            int templateCount,
            string packageVersion,
            string storagePolicy)
        {
            TemplateCount = templateCount;
            PackageVersion = packageVersion ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public int TemplateCount { get; }

        public string PackageVersion { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiReportTemplateFieldDto
    {
        public ApiReportTemplateFieldDto(
            string reportType,
            string category,
            string label,
            string value)
        {
            ReportType = reportType ?? string.Empty;
            Category = category ?? string.Empty;
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string ReportType { get; }

        public string Category { get; }

        public string Label { get; }

        public string Value { get; }
    }

    public sealed class ApiReportTemplateFieldCatalogResponse
    {
        public ApiReportTemplateFieldCatalogResponse(
            string reportType,
            IReadOnlyList<string> categoryOrder,
            IReadOnlyList<ApiReportTemplateFieldDto> fields)
        {
            ReportType = reportType ?? string.Empty;
            CategoryOrder = categoryOrder ?? Array.Empty<string>();
            Fields = fields ?? Array.Empty<ApiReportTemplateFieldDto>();
        }

        public string ReportType { get; }

        public IReadOnlyList<string> CategoryOrder { get; }

        public IReadOnlyList<ApiReportTemplateFieldDto> Fields { get; }
    }
}
