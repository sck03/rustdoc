namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateDocumentsSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiDashboardRecentInvoiceDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "invoiceNo", "status", "statusText", "type", "invoiceDate", "totalAmount", "customerNameEN" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["status"] = StringProperty("Invoice status code."),
                                ["statusText"] = StringProperty("Invoice status display text."),
                                ["type"] = StringProperty("Invoice type."),
                                ["invoiceDate"] = new { type = "string", format = "date-time" },
                                ["totalAmount"] = DecimalProperty("Invoice total amount."),
                                ["customerNameEN"] = StringProperty("Customer English name snapshot.")
                            }
                        },
                        ["ApiPdfMergeRequest"] = new
                        {
                            type = "object",
                            required = new[] { "sourceFiles", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["sourceFiles"] = StringArrayProperty("User-selected PDF source file paths."),
                                ["destinationPath"] = StringProperty("User-selected PDF output path. No default system-drive path is assigned by the sidecar.")
                            }
                        },
                        ["ApiLetterOfCreditImportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "filePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["filePath"] = StringProperty("User-selected letter of credit source path. The sidecar does not choose a default system-drive path.")
                            }
                        },
                        ["ApiLetterOfCreditImportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "sourcePath", "sourceDescription", "extractedText", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["sourcePath"] = StringProperty("Normalized source path that was read."),
                                ["sourceDescription"] = StringProperty("Source type description, such as text file, PDF, or image OCR."),
                                ["extractedText"] = StringProperty("Extracted letter of credit text returned to the caller; it is not persisted by the import endpoint."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiLetterOfCreditReviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "invoice" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto")
                            }
                        },
                        ["ApiLetterOfCreditReviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "reportText", "contextSummary", "letterOfCreditContentTruncated", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportText"] = StringProperty("AI generated letter-of-credit compliance review report."),
                                ["contextSummary"] = StringProperty("Short summary of the reviewed invoice draft and current invoice type."),
                                ["letterOfCreditContentTruncated"] = new { type = "boolean" },
                                ["storagePolicy"] = StringProperty("Path, storage, and data-domain policy for review.")
                            }
                        },
                        ["ApiInvoiceBookingSheetRequest"] = new
                        {
                            type = "object",
                            required = new[] { "invoiceId", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["destinationPath"] = StringProperty("User-selected .xlsx booking sheet output path.")
                            }
                        },
                        ["ApiExcelImportAnalysisReportDto"] = new
                        {
                            type = "object",
                            required = new[] { "schemaVersion", "analyzerId", "selectedWorksheetName", "confidence", "sheets", "fields", "issues" },
                            properties = new Dictionary<string, object>
                            {
                                ["schemaVersion"] = StringProperty("Excel analysis report schema version."),
                                ["analyzerId"] = StringProperty("Analyzer implementation that produced the report, for example rust-calamine or builtin-dotnet."),
                                ["selectedWorksheetName"] = StringProperty("Worksheet selected for invoice draft construction."),
                                ["confidence"] = new { type = "number", format = "decimal" },
                                ["sheets"] = RefArraySchema("ApiExcelImportSheetAnalysisDto"),
                                ["fields"] = RefArraySchema("ApiExcelImportFieldAnalysisDto"),
                                ["itemTable"] = RefSchema("ApiExcelImportItemTableAnalysisDto"),
                                ["issues"] = RefArraySchema("ApiExcelImportAnalysisIssueDto")
                            }
                        },
                        ["ApiReportTemplateDtoArray"] = new
                        {
                            type = "array",
                            items = RefSchema("ApiReportTemplateDto")
                        },
                        ["ApiReportTemplateDto"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "displayName", "templatePath", "withSealDefault" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["displayName"] = StringProperty("Template display name."),
                                ["templatePath"] = StringProperty("Resolved template path under program root Templates or an explicit configured path."),
                                ["withSealDefault"] = new { type = "boolean" }
                            }
                        },
                        ["ApiUserReportTemplateDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "reportType", "name", "contentHtml", "isActive", "isShared", "shareScope", "versionNumber", "canEdit" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher. User templates never mix the two data domains."),
                                ["name"] = StringProperty("User template name."),
                                ["contentHtml"] = StringProperty("HTML/Scriban content stored in the current database."),
                                ["isActive"] = new { type = "boolean" },
                                ["isShared"] = new { type = "boolean" },
                                ["shareScope"] = StringProperty("Private, Department, Company, or All."),
                                ["versionNumber"] = new { type = "integer", format = "int32" },
                                ["canEdit"] = new { type = "boolean" },
                                ["ownerUserId"] = new { type = "integer", format = "int32", nullable = true }
                            }
                        },
                        ["ApiUserReportTemplateSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "name", "contentHtml", "isActive", "isShared", "shareScope" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["name"] = StringProperty("User template name."),
                                ["contentHtml"] = StringProperty("HTML/Scriban content. PaymentVoucher and ExportDocument fields are validated separately."),
                                ["isActive"] = new { type = "boolean" },
                                ["isShared"] = new { type = "boolean" },
                                ["shareScope"] = StringProperty("Sharing scope: Private, Department, Company, or All."),
                                ["expectedVersion"] = new { type = "integer", format = "int32", description = "Expected current version for optimistic concurrency." },
                                ["sourceTemplatePath"] = StringProperty("Optional existing file-template path to copy when contentHtml is empty.")
                            }
                        },
                        ["ApiUserReportTemplateVersionDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "userReportTemplateId", "versionNumber", "changeType", "name", "contentHtml", "isActive", "isShared", "shareScope", "changedBy", "createdAt", "canRestore" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["userReportTemplateId"] = new { type = "integer", format = "int32" },
                                ["versionNumber"] = new { type = "integer", format = "int32" },
                                ["changeType"] = StringProperty("创建、更新或恢复。"),
                                ["name"] = StringProperty("Template name at this version."),
                                ["contentHtml"] = StringProperty("HTML/Scriban content snapshot."),
                                ["isActive"] = new { type = "boolean" },
                                ["isShared"] = new { type = "boolean" },
                                ["shareScope"] = StringProperty("Sharing scope: Private, Department, Company, or All."),
                                ["changedBy"] = StringProperty("Username that created this version."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["canRestore"] = new { type = "boolean" }
                            }
                        },
                        ["ApiReportTemplateContentDto"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "displayName", "templatePath", "withSealDefault", "content", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["displayName"] = StringProperty("Template display name."),
                                ["templatePath"] = StringProperty("Resolved template path under program root Templates or an explicit configured path."),
                                ["withSealDefault"] = new { type = "boolean" },
                                ["content"] = StringProperty("Editable HTML/Scriban template content."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for report templates.")
                            }
                        },
                        ["ApiReportTemplateStorageStatusResponse"] = new
                        {
                            type = "object",
                            required = new[] { "templateRoot", "exists", "writable", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["templateRoot"] = StringProperty("Resolved program-root Templates directory."),
                                ["exists"] = new { type = "boolean" },
                                ["writable"] = new { type = "boolean" },
                                ["message"] = StringProperty("User-facing diagnostic result."),
                                ["storagePolicy"] = StringProperty("Template storage and probe cleanup policy.")
                            }
                        },
                        ["ApiReportTemplateFieldDto"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "category", "label", "value" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["category"] = StringProperty("Designer category, such as 单据信息, 商品明细, or 付款报销."),
                                ["label"] = StringProperty("User-facing field label."),
                                ["value"] = StringProperty("Scriban expression inserted into the template.")
                            }
                        },
                        ["ApiReportTemplateFieldCatalogResponse"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "categoryOrder", "fields" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["categoryOrder"] = StringArrayProperty("Preferred designer category order."),
                                ["fields"] = RefArraySchema("ApiReportTemplateFieldDto")
                            }
                        },
                        ["ApiReportTemplateSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "templatePath", "content" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["templatePath"] = StringProperty("Target template path under program root Templates or an explicit configured path."),
                                ["content"] = StringProperty("HTML/Scriban template content to save atomically.")
                            }
                        },
                        ["ApiReportTemplateCreateRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["templatePath"] = StringProperty("Target template file name or path under the report type directory. When omitted, the sidecar creates a timestamped file under Templates."),
                                ["displayName"] = StringProperty("Optional display name used for the starter template heading.")
                            }
                        },
                        ["ApiReportTemplateRenameRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "templatePath", "newTemplatePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["templatePath"] = StringProperty("Current template path under program root Templates."),
                                ["newTemplatePath"] = StringProperty("New template file name or path under the same report type directory.")
                            }
                        },
                        ["ApiReportTemplatePackageExportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("Destination .edtpl path. Relative paths resolve to runtime DataRoot/TemplatePackages.")
                            }
                        },
                        ["ApiReportTemplatePackageExportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath", "templateCount", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("Resolved package path."),
                                ["templateCount"] = new { type = "integer", format = "int32" },
                                ["storagePolicy"] = StringProperty("Runtime storage policy for template packages.")
                            }
                        },
                        ["ApiReportTemplatePackageImportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath", "strategy" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("Source .edtpl or .zip path. Relative paths resolve to runtime DataRoot/TemplatePackages."),
                                ["strategy"] = StringProperty("Import strategy: Overwrite, Merge, or AddOnly.")
                            }
                        },
                        ["ApiReportTemplatePackageImportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "templateCount", "packageVersion", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["templateCount"] = new { type = "integer", format = "int32" },
                                ["packageVersion"] = StringProperty("Template package manifest version."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for template packages.")
                            }
                        },
                        ["ApiReportTemplatePreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "content", "withSeal" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["content"] = StringProperty("HTML/Scriban template content to render with sample data."),
                                ["withSeal"] = new { type = "boolean" }
                            }
                        },
                        ["ApiReportTemplatePreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal", "html" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type."),
                                ["withSeal"] = new { type = "boolean" },
                                ["html"] = StringProperty("Rendered sample HTML content.")
                            }
                        },
                        ["ApiReportHtmlPreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type. Invoice preview supports ExportDocument; payment/reimbursement preview supports PaymentVoucher."),
                                ["templatePath"] = StringProperty("Optional explicit template path. When omitted, the sidecar uses Templates under the program root."),
                                ["withSeal"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPaymentDraftReportHtmlPreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal", "payment" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type. Payment/reimbursement draft preview supports PaymentVoucher."),
                                ["templatePath"] = StringProperty("Optional explicit template path. When omitted, the sidecar uses Templates/Internal under the program root."),
                                ["withSeal"] = new { type = "boolean" },
                                ["payment"] = RefSchema("ApiPaymentDto")
                            }
                        },
                        ["ApiInvoiceDraftReportHtmlPreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal", "invoice" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type. Invoice draft preview supports ExportDocument."),
                                ["templatePath"] = StringProperty("Optional explicit template path. When omitted, the sidecar uses Templates/Export under the program root."),
                                ["withSeal"] = new { type = "boolean" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto")
                            }
                        },
                        ["ApiReportHtmlPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "invoiceId", "reportType", "templatePath", "withSeal", "html", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["reportType"] = StringProperty("Report type."),
                                ["templatePath"] = StringProperty("Resolved template path."),
                                ["withSeal"] = new { type = "boolean" },
                                ["html"] = StringProperty("Rendered HTML content."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for draft previews.")
                            }
                        },
                        ["ApiPaymentReportHtmlPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "paymentId", "reportType", "templatePath", "withSeal", "html", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["paymentId"] = new { type = "integer", format = "int32" },
                                ["reportType"] = StringProperty("Report type."),
                                ["templatePath"] = StringProperty("Resolved template path."),
                                ["withSeal"] = new { type = "boolean" },
                                ["html"] = StringProperty("Rendered HTML content."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for payment/reimbursement draft previews.")
                            }
                        },
                        ["ApiReportPdfRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type. Invoice PDF supports ExportDocument; payment/reimbursement PDF supports PaymentVoucher."),
                                ["templatePath"] = StringProperty("Optional explicit template path. When omitted, the sidecar uses Templates under the program root."),
                                ["withSeal"] = new { type = "boolean" },
                                ["destinationPath"] = StringProperty("User-selected PDF output path. The sidecar does not assign a default system-drive path.")
                            }
                        },
                        ["ApiInvoiceReportZipRequest"] = new
                        {
                            type = "object",
                            required = new[] { "invoiceIds", "reportType", "withSeal", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceIds"] = new
                                {
                                    type = "array",
                                    items = new { type = "integer", format = "int32" },
                                    description = "Invoice ids to render into PDFs before zipping. A single request supports up to 200 ids."
                                },
                                ["reportType"] = StringProperty("Report type. Batch invoice ZIP currently supports ExportDocument."),
                                ["templatePath"] = StringProperty("Optional explicit template path. When omitted, the sidecar uses Templates under the program root."),
                                ["withSeal"] = new { type = "boolean" },
                                ["destinationPath"] = StringProperty("User-selected ZIP output path. The sidecar does not assign a default system-drive path.")
                            }
                        },
                        ["ApiInvoiceDocumentPackageItemRequest"] = new
                        {
                            type = "object",
                            required = new[] { "name", "reportType", "templatePath", "withSeal" },
                            properties = new Dictionary<string, object>
                            {
                                ["name"] = StringProperty("Document display name used in generated file names."),
                                ["reportType"] = StringProperty("Report type. The document package currently supports ExportDocument."),
                                ["templatePath"] = StringProperty("Selected template path under Templates/Export or another explicitly allowed template path."),
                                ["withSeal"] = new { type = "boolean" }
                            }
                        },
                        ["ApiInvoiceDocumentPackageRequest"] = new
                        {
                            type = "object",
                            required = new[] { "items", "includeMergedPdf", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceDocumentPackageItemRequest"),
                                    description = "Selected document templates for one invoice. A single request supports up to 20 templates."
                                },
                                ["includeMergedPdf"] = new
                                {
                                    type = "boolean",
                                    description = "Whether to include a merged PDF when multiple documents are generated."
                                },
                                ["createZip"] = new
                                {
                                    type = "boolean",
                                    description = "When true or omitted, destinationPath must be a user-selected .zip file. When false, destinationPath is a user-selected output directory and PDFs are copied into a batch folder created from the old BatchExport.OutputFolderPattern."
                                },
                                ["destinationPath"] = StringProperty("User-selected .zip output path or output directory. Temporary PDFs use the runtime data cache; final files are not written to a default system-drive path.")
                            }
                        },
                        ["ApiInvoiceDocumentPackagePreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "items" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceDocumentPackageItemRequest"),
                                    description = "Selected document templates for one invoice. A single preview request supports up to 20 templates and returns HTML in memory."
                                }
                            }
                        },
                        ["ApiInvoiceDocumentPackagePreviewItemResponse"] = new
                        {
                            type = "object",
                            required = new[] { "name", "reportType", "templatePath", "withSeal", "html" },
                            properties = new Dictionary<string, object>
                            {
                                ["name"] = StringProperty("Document display name."),
                                ["reportType"] = StringProperty("Report type."),
                                ["templatePath"] = StringProperty("Resolved template path."),
                                ["withSeal"] = new { type = "boolean" },
                                ["html"] = StringProperty("Rendered HTML content.")
                            }
                        },
                        ["ApiInvoiceDocumentPackagePreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "invoiceId", "items", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceDocumentPackagePreviewItemResponse"),
                                    description = "Rendered HTML previews in the same order as the request."
                                },
                                ["storagePolicy"] = StringProperty("Runtime storage policy for in-memory document package preview.")
                            }
                        },
                        ["ApiInvoiceDocumentEmailRequest"] = new
                        {
                            type = "object",
                            required = new[] { "items", "includeMergedPdf", "toAddress", "subject", "body" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceDocumentPackageItemRequest"),
                                    description = "Selected document templates for one invoice. A single request supports up to 20 templates."
                                },
                                ["includeMergedPdf"] = new
                                {
                                    type = "boolean",
                                    description = "Whether to attach a merged PDF when multiple documents are generated."
                                },
                                ["toAddress"] = StringProperty("Recipient email address. When empty, the sidecar uses the current invoice customer email if available."),
                                ["subject"] = StringProperty("Email subject. When empty, the sidecar uses Email.DocumentEmailSubjectTemplate with invoice placeholders."),
                                ["body"] = StringProperty("Email HTML body. When empty, the sidecar uses Email.DocumentEmailBodyTemplate with invoice placeholders.")
                            }
                        },
                        ["ApiInvoiceListItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceNo",
                                "contractNo",
                                "invoiceDate",
                                "customerName",
                                "exporterName",
                                "destinationCountry",
                                "portOfLoading",
                                "portOfDestination",
                                "currency",
                                "totalAmount",
                                "type",
                                "status"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["contractNo"] = StringProperty("Contract number."),
                                ["invoiceDate"] = new { type = "string", format = "date-time" },
                                ["customerName"] = StringProperty("Customer English name snapshot."),
                                ["exporterName"] = StringProperty("Exporter name snapshot."),
                                ["destinationCountry"] = StringProperty("Destination country."),
                                ["portOfLoading"] = StringProperty("Port of loading."),
                                ["portOfDestination"] = StringProperty("Port of destination."),
                                ["currency"] = StringProperty("Invoice currency."),
                                ["totalAmount"] = new { type = "number", format = "decimal" },
                                ["type"] = StringProperty("Invoice type."),
                                ["status"] = StringProperty("Invoice status.")
                            }
                        },
                        ["ApiPagedResponseOfApiInvoiceListItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceListItemDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiQueryInvoiceRowDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceNo",
                                "invoiceDate",
                                "contractNo",
                                "customerName",
                                "exporterName",
                                "destinationCountry",
                                "tradeTerms",
                                "shipmentDate",
                                "transportMode",
                                "totalCartons",
                                "totalQuantity",
                                "totalAmount",
                                "currency",
                                "type"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["invoiceDate"] = StringProperty("Invoice date formatted as yyyy-MM-dd for the legacy query grid."),
                                ["contractNo"] = StringProperty("Contract number."),
                                ["customerName"] = StringProperty("Customer English name snapshot."),
                                ["exporterName"] = StringProperty("Exporter name snapshot."),
                                ["destinationCountry"] = StringProperty("Destination country."),
                                ["tradeTerms"] = StringProperty("Trade terms."),
                                ["shipmentDate"] = StringProperty("Shipment date formatted as yyyy-MM-dd for the legacy query grid."),
                                ["transportMode"] = StringProperty("Transport mode."),
                                ["totalCartons"] = DecimalProperty("Total cartons."),
                                ["totalQuantity"] = DecimalProperty("Total quantity."),
                                ["totalAmount"] = DecimalProperty("Total amount."),
                                ["currency"] = StringProperty("Invoice currency."),
                                ["type"] = StringProperty("Invoice type.")
                            }
                        },
                        ["ApiPagedResponseOfApiQueryInvoiceRowDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiQueryInvoiceRowDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiQueryInvoiceFilterRequest"] = new
                        {
                            type = "object",
                            properties = QueryInvoiceFilterProperties()
                        },
                        ["ApiQueryInvoiceExportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "destinationPath" },
                            properties = MergeProperties(
                                QueryInvoiceFilterProperties(),
                                new Dictionary<string, object>
                                {
                                    ["destinationPath"] = StringProperty("User-selected .xlsx output path. The sidecar does not choose a default export directory.")
                                })
                        },
                        ["ApiQueryInvoiceExportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "exportedCount", "destinationPath", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("User-facing export result message."),
                                ["exportedCount"] = new { type = "integer", format = "int32" },
                                ["destinationPath"] = StringProperty("Normalized user-selected export path."),
                                ["storagePolicy"] = StringProperty("Runtime path and invoice/payment data-domain policy for query export.")
                            }
                        },
                        ["ApiInvoiceDetailDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceNo",
                                "contractNo",
                                "invoiceDate",
                                "shipmentDate",
                                "customerNameEN",
                                "exporterNameEN",
                                "currency",
                                "totalAmount",
                                "status",
                                "items"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["ownerUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["contractNo"] = StringProperty("Contract number."),
                                ["invoiceDate"] = new { type = "string", format = "date-time" },
                                ["letterOfCreditNo"] = StringProperty("Letter of credit number."),
                                ["letterOfCreditSourcePath"] = StringProperty("Letter of credit source path."),
                                ["letterOfCreditContent"] = StringProperty("Letter of credit content."),
                                ["issuingBank"] = StringProperty("Issuing bank."),
                                ["customsBrokerName"] = StringProperty("Customs broker name."),
                                ["customsBrokerCode"] = StringProperty("Customs broker code."),
                                ["paymentTerms"] = StringProperty("Payment terms."),
                                ["portOfLoading"] = StringProperty("Port of loading."),
                                ["portOfDestination"] = StringProperty("Port of destination."),
                                ["destinationCountry"] = StringProperty("Destination country."),
                                ["shippingMarks"] = StringProperty("Shipping marks."),
                                ["shippingMarksType"] = StringProperty("Shipping marks type."),
                                ["shippingMarksImage"] = StringProperty("Shipping marks image path."),
                                ["tradeTerms"] = StringProperty("Trade terms."),
                                ["transportMode"] = StringProperty("Transport mode."),
                                ["shipmentDate"] = new { type = "string", format = "date-time" },
                                ["exporterId"] = new { type = "integer", format = "int32" },
                                ["customerId"] = new { type = "integer", format = "int32" },
                                ["totalCartons"] = DecimalProperty("Total cartons."),
                                ["totalQuantity"] = DecimalProperty("Total quantity."),
                                ["totalGrossWeight"] = DecimalProperty("Total gross weight."),
                                ["totalNetWeight"] = DecimalProperty("Total net weight."),
                                ["totalVolume"] = DecimalProperty("Total volume."),
                                ["totalAmount"] = DecimalProperty("Total amount."),
                                ["totalPurchaseAmount"] = DecimalProperty("Total purchase amount."),
                                ["totalTaxRefundAmount"] = DecimalProperty("Total tax refund amount."),
                                ["totalProfit"] = DecimalProperty("Total profit."),
                                ["currency"] = StringProperty("Invoice currency."),
                                ["specialTerms"] = StringProperty("Special terms."),
                                ["type"] = StringProperty("Invoice type."),
                                ["supervisionMode"] = StringProperty("Supervision mode."),
                                ["customerNameEN"] = StringProperty("Customer English name snapshot."),
                                ["customerAddressEN"] = StringProperty("Customer English address snapshot."),
                                ["notifyPartyName"] = StringProperty("Notify party name."),
                                ["notifyPartyAddress"] = StringProperty("Notify party address."),
                                ["exporterNameEN"] = StringProperty("Exporter English name snapshot."),
                                ["exporterNameCN"] = StringProperty("Exporter Chinese name snapshot."),
                                ["exporterAddressEN"] = StringProperty("Exporter English address snapshot."),
                                ["exporterAddressCN"] = StringProperty("Exporter Chinese address snapshot."),
                                ["exporterCreditCode"] = StringProperty("Exporter credit code."),
                                ["exporterCustomsCode"] = StringProperty("Exporter customs code."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["bankAccount"] = StringProperty("Bank account."),
                                ["swiftCode"] = StringProperty("SWIFT code."),
                                ["exchangeRate"] = new { type = "number", format = "decimal", nullable = true },
                                ["status"] = StringProperty("Invoice status."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64."),
                                ["spare1"] = StringProperty("Spare field 1."),
                                ["spare2"] = StringProperty("Spare field 2."),
                                ["spare3"] = StringProperty("Spare field 3."),
                                ["customFieldsJson"] = StringProperty("Custom fields JSON."),
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiInvoiceItemDto")
                                }
                            }
                        },
                        ["ApiInvoiceItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "invoiceId",
                                "styleNo",
                                "styleName",
                                "quantity",
                                "unitPrice",
                                "totalPrice"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["poNumber"] = StringProperty("PO number."),
                                ["styleNo"] = StringProperty("Style number."),
                                ["styleName"] = StringProperty("Style name."),
                                ["fabricComposition"] = StringProperty("Fabric composition."),
                                ["styleNameCN"] = StringProperty("Chinese style name."),
                                ["brand"] = StringProperty("Brand."),
                                ["hsCode"] = StringProperty("HS code."),
                                ["origin"] = StringProperty("Origin."),
                                ["quantity"] = DecimalProperty("Quantity."),
                                ["unitEN"] = StringProperty("English unit."),
                                ["unitCN"] = StringProperty("Chinese unit."),
                                ["pcsPerCtn"] = DecimalProperty("Pieces per carton."),
                                ["cartons"] = DecimalProperty("Cartons."),
                                ["ctnUnitEN"] = StringProperty("English carton unit."),
                                ["ctnUnitCN"] = StringProperty("Chinese carton unit."),
                                ["length"] = DecimalProperty("Carton length."),
                                ["width"] = DecimalProperty("Carton width."),
                                ["height"] = DecimalProperty("Carton height."),
                                ["volume"] = DecimalProperty("Volume."),
                                ["gwPerCtn"] = DecimalProperty("Gross weight per carton."),
                                ["nwPerCtn"] = DecimalProperty("Net weight per carton."),
                                ["gwTotal"] = DecimalProperty("Total gross weight."),
                                ["nwTotal"] = DecimalProperty("Total net weight."),
                                ["unitPrice"] = DecimalProperty("Unit price."),
                                ["totalPrice"] = DecimalProperty("Total price."),
                                ["purchasePrice"] = DecimalProperty("Purchase price."),
                                ["purchaseTotal"] = DecimalProperty("Purchase total."),
                                ["taxRebateRate"] = DecimalProperty("Tax rebate rate."),
                                ["taxRefundAmount"] = DecimalProperty("Calculated tax refund amount."),
                                ["spare1"] = StringProperty("Spare field 1."),
                                ["spare2"] = StringProperty("Spare field 2."),
                                ["spare3"] = StringProperty("Spare field 3."),
                                ["customFieldsJson"] = StringProperty("Custom fields JSON.")
                            }
                        },
                        ["ApiInvoiceSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "isUpdate", "invoice" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["isUpdate"] = new { type = "boolean" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto")
                            }
                        },
                        ["ApiInvoiceCloneRequest"] = new
                        {
                            type = "object",
                            required = new[] { "newInvoiceNo" },
                            properties = new Dictionary<string, object>
                            {
                                ["newInvoiceNo"] = StringProperty("New invoice number for the clone."),
                                ["options"] = new
                                {
                                    type = "object",
                                    nullable = true,
                                    properties = new Dictionary<string, object>
                                    {
                                        ["copyHeader"] = new { type = "boolean" },
                                        ["copyItems"] = new { type = "boolean" },
                                        ["resetDates"] = new { type = "boolean" },
                                        ["resetStatus"] = new { type = "boolean" },
                                        ["clearAmounts"] = new { type = "boolean" }
                                    }
                                }
                            }
                        },
                        ["ApiInvoiceCloneResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "invoice", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto"),
                                ["message"] = StringProperty("Clone result message.")
                            }
                        },
                        ["ApiInvoiceCloneTypeRequest"] = new
                        {
                            type = "object",
                            required = new[] { "targetType" },
                            properties = new Dictionary<string, object>
                            {
                                ["targetType"] = StringProperty("Target trade data type for the clone. Supported values are 实际数据 and 报关数据."),
                                ["options"] = new
                                {
                                    type = "object",
                                    nullable = true,
                                    properties = new Dictionary<string, object>
                                    {
                                        ["copyHeader"] = new { type = "boolean" },
                                        ["copyItems"] = new { type = "boolean" },
                                        ["resetDates"] = new { type = "boolean" },
                                        ["resetStatus"] = new { type = "boolean" },
                                        ["clearAmounts"] = new { type = "boolean" }
                                    }
                                }
                            }
                        },
                        ["ApiInvoiceCloneTypeResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "invoice", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto"),
                                ["message"] = StringProperty("Clone result message.")
                            }
                        },
                        ["ApiInvoiceTransferPathRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("User-selected .edpkg package path.")
                            }
                        },
                        ["ApiInvoiceTransferImportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath", "conflictAction" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("User-selected .edpkg package path."),
                                ["conflictAction"] = StringProperty("Conflict action: Skip, Overwrite, NewInvoiceNo, or AppendItems."),
                                ["newInvoiceNo"] = StringProperty("Optional invoice number used when conflictAction is NewInvoiceNo."),
                                ["allowInvalidChecksum"] = new { type = "boolean" }
                            }
                        },
                        ["ApiInvoiceTransferPreviewDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "invoiceNo",
                                "type",
                                "itemCount",
                                "customerExists",
                                "exporterExists",
                                "invoiceExists",
                                "invoiceMatches",
                                "existingInvoiceId"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["invoiceNo"] = StringProperty("Package invoice number."),
                                ["type"] = StringProperty("Package invoice type, such as 实际数据 or 报关数据."),
                                ["itemCount"] = new { type = "integer", format = "int32" },
                                ["customerExists"] = new { type = "boolean" },
                                ["exporterExists"] = new { type = "boolean" },
                                ["invoiceExists"] = new { type = "boolean" },
                                ["invoiceMatches"] = new { type = "boolean" },
                                ["existingInvoiceId"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiInvoiceTransferPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "checksumValid", "checksumMessage", "preview", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["checksumValid"] = new { type = "boolean" },
                                ["checksumMessage"] = StringProperty("Checksum validation result."),
                                ["preview"] = RefSchema("ApiInvoiceTransferPreviewDto"),
                                ["storagePolicy"] = StringProperty("Runtime path and data-domain policy for invoice transfer packages.")
                            }
                        },
                        ["ApiInvoiceTransferExportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "invoiceId", "packagePath", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["packagePath"] = StringProperty("Normalized user-selected .edpkg output path."),
                                ["storagePolicy"] = StringProperty("Runtime path and data-domain policy for invoice transfer package export."),
                                ["message"] = StringProperty("Export result message.")
                            }
                        },
                        ["ApiInvoiceTransferImportResultDto"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "finalInvoiceNo", "actionTaken" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Import result message."),
                                ["invoiceId"] = new { type = "integer", format = "int32", nullable = true },
                                ["finalInvoiceNo"] = StringProperty("Final invoice number after import."),
                                ["actionTaken"] = StringProperty("Conflict action applied by the import.")
                            }
                        },
                        ["ApiInvoiceTransferImportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "result", "preview", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["result"] = RefSchema("ApiInvoiceTransferImportResultDto"),
                                ["preview"] = RefSchema("ApiInvoiceTransferPreviewDto"),
                                ["storagePolicy"] = StringProperty("Runtime path and data-domain policy for invoice transfer package import."),
                                ["message"] = StringProperty("Import response message.")
                            }
                        },
                        ["ApiInvoiceProfitAnalysisRequest"] = new
                        {
                            type = "object",
                            required = new[] { "invoice" },
                            properties = new Dictionary<string, object>
                            {
                                ["invoice"] = RefSchema("ApiInvoiceDetailDto")
                            }
                        },
                        ["ApiInvoiceProfitAnalysisResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "currency",
                                "salesTotal",
                                "exchangeRate",
                                "salesRmb",
                                "purchaseCost",
                                "taxRefund",
                                "grossProfit",
                                "margin",
                                "salesTotalText",
                                "exchangeRateText",
                                "salesRmbText",
                                "purchaseCostText",
                                "taxRefundText",
                                "grossProfitText",
                                "marginText",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["currency"] = StringProperty("Invoice currency from the request draft."),
                                ["salesTotal"] = DecimalProperty("Sales total in the invoice currency."),
                                ["exchangeRate"] = NullableDecimalProperty("Exchange rate used to convert sales to RMB. Null when unset and the invoice currency is not RMB/CNY."),
                                ["salesRmb"] = DecimalProperty("Sales total converted to RMB."),
                                ["purchaseCost"] = DecimalProperty("Purchase cost in RMB."),
                                ["taxRefund"] = DecimalProperty("Tax refund in RMB."),
                                ["grossProfit"] = DecimalProperty("Estimated gross profit in RMB."),
                                ["margin"] = DecimalProperty("Gross margin as a decimal ratio."),
                                ["salesTotalText"] = StringProperty("Legacy WinForms-compatible sales total text."),
                                ["exchangeRateText"] = StringProperty("Legacy WinForms-compatible exchange rate text."),
                                ["salesRmbText"] = StringProperty("Legacy WinForms-compatible RMB sales text."),
                                ["purchaseCostText"] = StringProperty("Legacy WinForms-compatible purchase cost text."),
                                ["taxRefundText"] = StringProperty("Legacy WinForms-compatible tax refund text."),
                                ["grossProfitText"] = StringProperty("Legacy WinForms-compatible gross profit text."),
                                ["marginText"] = StringProperty("Legacy WinForms-compatible margin text."),
                                ["storagePolicy"] = StringProperty("Path, storage, and data-domain policy for review.")
                            }
                        },
                        ["ApiPaymentDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "invoiceNo", "paymentDate", "payeeName", "payerName", "rowVersion" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["ownerUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["invoiceNo"] = StringProperty("Invoice number."),
                                ["shipmentDate"] = new { type = "string", format = "date-time" },
                                ["payeeId"] = new { type = "integer", format = "int32" },
                                ["department"] = StringProperty("Department."),
                                ["project"] = StringProperty("Project."),
                                ["usdAmount"] = DecimalProperty("USD amount."),
                                ["cnyAmount"] = DecimalProperty("CNY amount."),
                                ["paymentMethod"] = StringProperty("Payment method."),
                                ["payeeName"] = StringProperty("Payee name."),
                                ["payerName"] = StringProperty("Payer name."),
                                ["bankName"] = StringProperty("Bank name."),
                                ["accountNo"] = StringProperty("Account number."),
                                ["notes"] = StringProperty("Notes."),
                                ["paymentDate"] = new { type = "string", format = "date-time" },
                                ["goodsName"] = StringProperty("Goods name."),
                                ["quantity"] = StringProperty("Quantity text."),
                                ["shipmentCountry"] = StringProperty("Shipment country."),
                                ["receiptDate"] = new { type = "string", format = "date-time" },
                                ["travelExpense"] = DecimalProperty("Travel expense."),
                                ["businessEntertainmentExpense"] = DecimalProperty("Business entertainment expense."),
                                ["telephoneExpense"] = DecimalProperty("Telephone expense."),
                                ["officeExpense"] = DecimalProperty("Office expense."),
                                ["repairExpense"] = DecimalProperty("Repair expense."),
                                ["freightMiscExpense"] = DecimalProperty("Freight miscellaneous expense."),
                                ["inspectionExpense"] = DecimalProperty("Inspection expense."),
                                ["otherExpense"] = DecimalProperty("Other expense."),
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiPaymentSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "payment" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["payment"] = RefSchema("ApiPaymentDto")
                            }
                        },
                        ["ApiPagedResponseOfApiPaymentDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiPaymentDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiHsCodeDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "code", "normalizedCode", "name" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("HS code."),
                                ["normalizedCode"] = StringProperty("Normalized HS code."),
                                ["name"] = StringProperty("HS code name."),
                                ["unit"] = StringProperty("Legal unit."),
                                ["description"] = StringProperty("Description."),
                                ["elements"] = StringProperty("Declaration elements."),
                                ["supervisionConditions"] = StringProperty("Supervision conditions."),
                                ["inspectionCategory"] = StringProperty("Inspection category."),
                                ["rebateRate"] = StringProperty("Rebate rate."),
                                ["updateTime"] = new { type = "string", format = "date-time", nullable = true },
                                ["detailUrl"] = StringProperty("Source detail URL."),
                                ["status"] = StringProperty("Active, ReferenceOnly, SuspectedObsolete, or Obsolete."),
                                ["sourceName"] = StringProperty("Data source name."),
                                ["effectiveYear"] = new { type = "integer", format = "int32", nullable = true },
                                ["lastVerifiedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["replacedByCodes"] = StringProperty("Comma-separated replacement candidates."),
                                ["normalTariffRate"] = StringProperty("China general import tariff rate."),
                                ["preferentialTariffRate"] = StringProperty("China preferential or MFN import tariff rate."),
                                ["exportTariffRate"] = StringProperty("China export tariff rate."),
                                ["consumptionTaxRate"] = StringProperty("China consumption tax rate."),
                                ["valueAddedTaxRate"] = StringProperty("China import VAT rate."),
                                ["notes"] = StringProperty("Source remarks for the HS code."),
                                ["remoteRecordKind"] = StringProperty("StandardCode or DeclarationExample for remote evidence."),
                                ["instanceCount"] = new { type = "integer", format = "int32", nullable = true },
                                ["summaryUrl"] = StringProperty("Remote declaration-example summary URL."),
                                ["evidenceUrl"] = StringProperty("Remote evidence URL."),
                                ["observedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["recommendedKeywords"] = new { type = "array", items = new { type = "string" } },
                                ["personalPostalTaxCode"] = StringProperty("Personal postal tax code from remote detail."),
                                ["ciqEntries"] = new { type = "array", items = RefSchema("ApiHsCodeRemoteReferenceEntry") },
                                ["classificationEntries"] = new { type = "array", items = RefSchema("ApiHsCodeRemoteReferenceEntry") },
                                ["declarationExampleCount"] = new { type = "integer", format = "int32" },
                                ["rowVersion"] = StringProperty("Concurrency row version encoded as base64.")
                            }
                        },
                        ["ApiHsCodeRemoteReferenceEntry"] = new
                        {
                            type = "object",
                            required = new[] { "code", "name" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = StringProperty("Remote reference code."),
                                ["name"] = StringProperty("Remote reference name.")
                            }
                        },
                        ["HsCodeKnowledgeSearchItem"] = new
                        {
                            type = "object",
                            required = new[] { "currentCode", "rawCode", "name", "specification", "standardName", "resolutionStatus", "score", "exampleCount", "confirmedCount", "replacementCandidates", "matchReasons", "conflictWarnings", "standardSource", "canUse" },
                            properties = new Dictionary<string, object>
                            {
                                ["currentCode"] = StringProperty("Current locally valid code, when resolved."),
                                ["rawCode"] = StringProperty("Original reported code."),
                                ["name"] = StringProperty("Best declaration example name."),
                                ["specification"] = StringProperty("Declaration specification."),
                                ["standardName"] = StringProperty("Current local tariff name."),
                                ["resolutionStatus"] = StringProperty("Resolution status."),
                                ["score"] = new { type = "integer", format = "int32" },
                                ["exampleCount"] = new { type = "integer", format = "int32" },
                                ["confirmedCount"] = new { type = "integer", format = "int32" },
                                ["replacementCandidates"] = new { type = "array", items = new { type = "string" } },
                                ["matchReasons"] = new { type = "array", items = new { type = "string" } },
                                ["conflictWarnings"] = new { type = "array", items = new { type = "string" } },
                                ["standardSource"] = StringProperty("Trusted annual tariff source."),
                                ["effectiveYear"] = new { type = "integer", format = "int32", nullable = true },
                                ["lastVerifiedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["canUse"] = new { type = "boolean" }
                            }
                        },
                        ["HsCodeKnowledgeSearchResponse"] = new
                        {
                            type = "object",
                            required = new[] { "query", "items", "localExampleCount", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["query"] = StringProperty("Normalized query."),
                                ["items"] = new { type = "array", items = RefSchema("HsCodeKnowledgeSearchItem") },
                                ["localExampleCount"] = new { type = "integer", format = "int32" },
                                ["message"] = StringProperty("Operator-facing result message.")
                            }
                        },
                        ["HsCodeKnowledgeExample"] = new
                        {
                            type = "object",
                            required = new[] { "id", "rawReportedHsCode", "productName", "source", "resolutionStatus", "isManuallyVerified", "useCount", "updatedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["rawReportedHsCode"] = StringProperty("Original reported code."),
                                ["resolvedCurrentHsCode"] = StringProperty("Current code, nullable."),
                                ["productName"] = StringProperty("Declared product name."),
                                ["specification"] = StringProperty("Declaration specification."),
                                ["source"] = StringProperty("Evidence source."),
                                ["sourceYear"] = new { type = "integer", format = "int32", nullable = true },
                                ["resolutionStatus"] = StringProperty("Resolution status."),
                                ["isManuallyVerified"] = new { type = "boolean" },
                                ["useCount"] = new { type = "integer", format = "int32" },
                                ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["HsCodeKnowledgeExamplePage"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("HsCodeKnowledgeExample") },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["HsCodeKnowledgeExampleInput"] = new
                        {
                            type = "object",
                            required = new[] { "id", "rawReportedHsCode", "resolvedCurrentHsCode", "productName", "specification", "source", "resolutionStatus", "isManuallyVerified" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["rawReportedHsCode"] = StringProperty("Original reported code."),
                                ["resolvedCurrentHsCode"] = StringProperty("Current code, optional."),
                                ["productName"] = StringProperty("Declared product name."),
                                ["specification"] = StringProperty("Declaration specification."),
                                ["source"] = StringProperty("Evidence source."),
                                ["sourceYear"] = new { type = "integer", format = "int32", nullable = true },
                                ["resolutionStatus"] = StringProperty("Resolution status."),
                                ["isManuallyVerified"] = new { type = "boolean" }
                            }
                        },
                        ["HsCodeKnowledgeFeedbackInput"] = new
                        {
                            type = "object",
                            required = new[] { "queryText", "productName", "specification", "candidateCode", "accepted" },
                            properties = new Dictionary<string, object>
                            {
                                ["queryText"] = StringProperty("Search query."),
                                ["productName"] = StringProperty("Selected product name."),
                                ["specification"] = StringProperty("Selected specification."),
                                ["candidateCode"] = StringProperty("Selected code."),
                                ["accepted"] = new { type = "boolean" }
                            }
                        },
                        ["HsCodeHistoryLearningCandidate"] = new
                        {
                            type = "object",
                            required = new[] { "fingerprint", "rawCode", "currentCode", "productName", "specification", "source", "sourceCount", "variantCount", "variantSamples", "resolutionStatus", "replacementCandidates", "canConfirm" },
                            properties = new Dictionary<string, object>
                            {
                                ["fingerprint"] = StringProperty("Candidate fingerprint."),
                                ["rawCode"] = StringProperty("Original code."),
                                ["currentCode"] = StringProperty("Current code candidate."),
                                ["productName"] = StringProperty("Product name."),
                                ["specification"] = StringProperty("Specification."),
                                ["source"] = StringProperty("Historical source."),
                                ["sourceCount"] = new { type = "integer", format = "int32" },
                                ["variantCount"] = new { type = "integer", format = "int32" },
                                ["variantSamples"] = new { type = "array", items = new { type = "string" } },
                                ["resolutionStatus"] = StringProperty("Resolution status."),
                                ["replacementCandidates"] = new { type = "array", items = new { type = "string" } },
                                ["canConfirm"] = new { type = "boolean" }
                            }
                        },
                        ["HsCodeHistoryCandidatePage"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = new Dictionary<string, string> { ["$ref"] = "#/components/schemas/HsCodeHistoryLearningCandidate" } },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["HsCodeRemoteCandidate"] = new
                        {
                            type = "object",
                            required = new[] { "id", "queryText", "rawReportedHsCode", "productName", "source", "reviewStatus", "resolutionStatus", "seenCount", "firstSeenAt", "lastSeenAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["queryText"] = StringProperty("Remote query."),
                                ["rawReportedHsCode"] = StringProperty("Original remote code."),
                                ["suggestedCurrentHsCode"] = StringProperty("Suggested local code, nullable."),
                                ["productName"] = StringProperty("Remote product name."),
                                ["specification"] = StringProperty("Remote declaration specification."),
                                ["source"] = StringProperty("Remote source."),
                                ["sourceUrl"] = StringProperty("Remote evidence URL."),
                                ["reviewStatus"] = StringProperty("Pending, Confirmed, or Ignored."),
                                ["resolutionStatus"] = StringProperty("Resolution status."),
                                ["seenCount"] = new { type = "integer", format = "int32" },
                                ["firstSeenAt"] = new { type = "string", format = "date-time" },
                                ["lastSeenAt"] = new { type = "string", format = "date-time" },
                                ["reviewedAt"] = new { type = "string", format = "date-time", nullable = true }
                            }
                        },
                        ["HsCodeRemoteCandidateReviewInput"] = new
                        {
                            type = "object",
                            required = new[] { "id", "currentCode", "confirmed" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["currentCode"] = StringProperty("Current active local code."),
                                ["confirmed"] = new { type = "boolean" }
                            }
                        },
                        ["HsCodeRemoteCandidateBatchReviewInput"] = new
                        {
                            type = "object",
                            required = new[] { "items" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("HsCodeRemoteCandidateReviewInput") }
                            }
                        },
                        ["HsCodeRemoteCandidateResetInput"] = new
                        {
                            type = "object",
                            required = new[] { "ids" },
                            properties = new Dictionary<string, object>
                            {
                                ["ids"] = new { type = "array", items = new { type = "integer", format = "int32" } }
                            }
                        },
                        ["HsCodeKnowledgeExampleDeleteBatchInput"] = new
                        {
                            type = "object",
                            required = new[] { "ids" },
                            properties = new Dictionary<string, object>
                            {
                                ["ids"] = new { type = "array", items = new { type = "integer", format = "int32" } }
                            }
                        },
                        ["HsCodeRemoteCandidatePage"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "pageNumber", "pageSize", "reviewStatus" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new { type = "array", items = RefSchema("HsCodeRemoteCandidate") },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["reviewStatus"] = StringProperty("Candidate review status.")
                            }
                        },
                        ["HsCodeKnowledgeImportResult"] = new
                        {
                            type = "object",
                            required = new[] { "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["addedHsCodes"] = new { type = "integer", format = "int32" },
                                ["updatedHsCodes"] = new { type = "integer", format = "int32" },
                                ["addedExamples"] = new { type = "integer", format = "int32" },
                                ["updatedExamples"] = new { type = "integer", format = "int32" },
                                ["addedReplacements"] = new { type = "integer", format = "int32" },
                                ["addedFeedback"] = new { type = "integer", format = "int32" },
                                ["message"] = StringProperty("Import summary.")
                            }
                        },
                        ["HsCodeKnowledgeImportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "fileName", "hsCodeCount", "exampleCount", "replacementCount", "feedbackCount", "warnings", "result" },
                            properties = new Dictionary<string, object>
                            {
                                ["fileName"] = StringProperty("Package file name."),
                                ["hsCodeCount"] = new { type = "integer", format = "int32" },
                                ["exampleCount"] = new { type = "integer", format = "int32" },
                                ["replacementCount"] = new { type = "integer", format = "int32" },
                                ["feedbackCount"] = new { type = "integer", format = "int32" },
                                ["warnings"] = new { type = "array", items = new { type = "string" } },
                                ["result"] = RefSchema("HsCodeKnowledgeImportResult")
                            }
                        },
                        ["ApiHsCodeImportPathRequest"] = new
                        {
                            type = "object",
                            required = new[] { "filePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["filePath"] = StringProperty("User selected .xlsx, .xlsm, .xltx, .xltm, or .xls workbook path.")
                            }
                        },
                        ["ApiHsCodeImportPreviewPathRequest"] = new
                        {
                            type = "object",
                            required = new[] { "filePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["filePath"] = StringProperty("Explicitly selected workbook path."),
                                ["mode"] = StringProperty("Incremental or CompleteSnapshot."),
                                ["sourceName"] = StringProperty("Human readable source name."),
                                ["effectiveYear"] = new { type = "integer", format = "int32", nullable = true }
                            }
                        },
                        ["ApiHsCodeImportCommitRequest"] = new
                        {
                            type = "object",
                            required = new[] { "token" },
                            properties = new Dictionary<string, object> { ["token"] = StringProperty("Server-side preview token.") }
                        },
                        ["ApiHsCodeImportColumnMappingDto"] = new
                        {
                            type = "object",
                            required = new[] { "field", "header", "columnNumber", "confidence" },
                            properties = new Dictionary<string, object>
                            {
                                ["field"] = StringProperty("Normalized field name."),
                                ["header"] = StringProperty("Detected workbook header."),
                                ["columnNumber"] = new { type = "integer", format = "int32" },
                                ["confidence"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiHsCodeImportPreviewItemDto"] = new
                        {
                            type = "object",
                            required = new[] { "changeType", "rowNumber", "item", "changedFields", "replacementCandidates", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["changeType"] = StringProperty("Add, Update, Unchanged, SuspectedObsolete, Conflict, or Invalid."),
                                ["rowNumber"] = new { type = "integer", format = "int32" },
                                ["item"] = RefSchema("ApiHsCodeDto"),
                                ["changedFields"] = new { type = "array", items = new { type = "string" } },
                                ["replacementCandidates"] = new { type = "array", items = new { type = "string" } },
                                ["message"] = StringProperty("Operator-facing difference message.")
                            }
                        },
                        ["ApiHsCodeImportPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "token", "fileName", "mode", "sourceName", "worksheetName", "headerRowNumber", "confidence", "columns", "items", "addCount", "updateCount", "unchangedCount", "suspectedObsoleteCount", "conflictCount", "invalidCount", "warnings", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["token"] = StringProperty("Server-side preview token."),
                                ["fileName"] = StringProperty("Workbook file name."),
                                ["mode"] = StringProperty("Import mode."),
                                ["sourceName"] = StringProperty("Data source name."),
                                ["effectiveYear"] = new { type = "integer", format = "int32", nullable = true },
                                ["worksheetName"] = StringProperty("Detected worksheet."),
                                ["headerRowNumber"] = new { type = "integer", format = "int32" },
                                ["confidence"] = new { type = "integer", format = "int32" },
                                ["columns"] = new { type = "array", items = RefSchema("ApiHsCodeImportColumnMappingDto") },
                                ["items"] = new { type = "array", items = RefSchema("ApiHsCodeImportPreviewItemDto") },
                                ["addCount"] = new { type = "integer", format = "int32" },
                                ["updateCount"] = new { type = "integer", format = "int32" },
                                ["unchangedCount"] = new { type = "integer", format = "int32" },
                                ["suspectedObsoleteCount"] = new { type = "integer", format = "int32" },
                                ["conflictCount"] = new { type = "integer", format = "int32" },
                                ["invalidCount"] = new { type = "integer", format = "int32" },
                                ["warnings"] = new { type = "array", items = new { type = "string" } },
                                ["storagePolicy"] = StringProperty("Runtime storage policy.")
                            }
                        },
                        ["ApiHsCodeImportCommitResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "addedCount", "updatedCount", "unchangedCount", "suspectedObsoleteCount", "skippedCount", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["addedCount"] = new { type = "integer", format = "int32" },
                                ["updatedCount"] = new { type = "integer", format = "int32" },
                                ["unchangedCount"] = new { type = "integer", format = "int32" },
                                ["suspectedObsoleteCount"] = new { type = "integer", format = "int32" },
                                ["skippedCount"] = new { type = "integer", format = "int32" },
                                ["message"] = StringProperty("Commit summary.")
                            }
                        },
                        ["ApiHsCodeRemoteHealthResponse"] = new
                        {
                            type = "object",
                            required = new[] { "source", "available", "checkedAt", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["source"] = StringProperty("Remote source name."),
                                ["available"] = new { type = "boolean" },
                                ["checkedAt"] = new { type = "string", format = "date-time" },
                                ["message"] = StringProperty("Health summary.")
                            }
                        },
                        ["ApiHsCodeClearAllRequest"] = new
                        {
                            type = "object",
                            required = new[] { "confirmation" },
                            properties = new Dictionary<string, object>
                            {
                                ["confirmation"] = StringProperty("Confirmation text. Must be CLEAR.")
                            }
                        },
                        ["ApiHsCodeBatchDeleteRequest"] = new
                        {
                            type = "object",
                            required = new[] { "ids" },
                            properties = new Dictionary<string, object>
                            {
                                ["ids"] = new
                                {
                                    type = "array",
                                    items = new { type = "integer", format = "int32" }
                                }
                            }
                        },
                        ["ApiHsCodeImportResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "fileName", "totalCount", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["fileName"] = StringProperty("Imported workbook file name."),
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["message"] = StringProperty("Import result message."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for HS code imports.")
                            }
                        },
                        ["ApiHsCodeSearchResponse"] = new
                        {
                            type = "object",
                            required = new[] { "items", "count", "source", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiHsCodeDto")
                                },
                                ["count"] = new { type = "integer", format = "int32" },
                                ["source"] = StringProperty("Search source."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for remote HS code search."),
                                ["standardCodeCount"] = new { type = "integer", format = "int32" },
                                ["declarationExampleCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiHsCodeRemoteDetailResolutionResponse"] = new
                        {
                            type = "object",
                            required = new[] { "items", "removedItems", "updatedCount", "removedCount", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiHsCodeDto")
                                },
                                ["removedItems"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiHsCodeDto")
                                },
                                ["updatedCount"] = new { type = "integer", format = "int32" },
                                ["removedCount"] = new { type = "integer", format = "int32" },
                                ["message"] = StringProperty("Resolution message for the operator."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for remote HS code detail resolution.")
                            }
                        },
                        ["ApiPagedResponseOfApiHsCodeDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "items",
                                "totalCount",
                                "pageNumber",
                                "pageSize",
                                "totalPages",
                                "hasPreviousPage",
                                "hasNextPage"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiHsCodeDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
            };
    }
}
