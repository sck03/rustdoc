using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateReportDocumentSchemas() =>
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
                                ["sourcePath"] = StringProperty("Desktop import returns the normalized user-selected path; browser upload returns only the safe original file name and never exposes a server temporary path."),
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
                            required = new[] { "reportType", "displayName", "templatePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["displayName"] = StringProperty("Template display name."),
                                ["templatePath"] = StringProperty("Resolved path from the managed built-in or user template catalog."),
                                ["withSealDefault"] = new
                                {
                                    type = "boolean",
                                    description = "Default customs-seal option for ExportDocument templates. Omitted for payment/reimbursement templates."
                                }
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
                            required = new[] { "reportType", "displayName", "templatePath", "content", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["displayName"] = StringProperty("Template display name."),
                                ["templatePath"] = StringProperty("Resolved path from the managed built-in or user template catalog."),
                                ["withSealDefault"] = new
                                {
                                    type = "boolean",
                                    description = "Default customs-seal option for ExportDocument templates. Omitted for payment/reimbursement templates."
                                },
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
                                ["templateRoot"] = StringProperty("Resolved template directory for trusted desktop clients; empty for browser clients."),
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
                                ["templatePath"] = StringProperty("Target path under the runtime-data user template directory for the selected report type."),
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
                                ["templatePath"] = StringProperty("Current path from the managed built-in or user template catalog."),
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
                            required = new[] { "reportType", "content" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type: ExportDocument or PaymentVoucher."),
                                ["content"] = StringProperty("HTML/Scriban template content to render with sample data."),
                                ["withSeal"] = new
                                {
                                    type = "boolean",
                                    description = "Customs-seal option for ExportDocument previews. Omit for payment/reimbursement previews."
                                }
                            }
                        },
                        ["ApiReportTemplatePreviewResponse"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "html" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type."),
                                ["withSeal"] = new
                                {
                                    type = "boolean",
                                    description = "Effective customs-seal option for ExportDocument previews. Omitted for payment/reimbursement previews."
                                },
                                ["html"] = StringProperty("Rendered sample HTML content.")
                            }
                        },
                        ["ApiReportHtmlPreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type. Invoice preview supports ExportDocument."),
                                ["templatePath"] = StringProperty("Optional managed export-document template path. When omitted, the default Export template is used."),
                                ["withSeal"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPaymentReportHtmlPreviewRequest"] = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["templatePath"] = StringProperty("Optional managed payment/reimbursement template path. Export-document templates are rejected.")
                            }
                        },
                        ["ApiPaymentDraftReportHtmlPreviewRequest"] = new
                        {
                            type = "object",
                            required = new[] { "payment" },
                            properties = new Dictionary<string, object>
                            {
                                ["templatePath"] = StringProperty("Optional managed payment/reimbursement template path. Export-document templates are rejected."),
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
                                ["templatePath"] = StringProperty("Optional managed export-document template path. Payment/reimbursement templates are rejected."),
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
                            required = new[] { "paymentId", "reportType", "templatePath", "html", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["paymentId"] = new { type = "integer", format = "int32" },
                                ["reportType"] = StringProperty("Report type."),
                                ["templatePath"] = StringProperty("Resolved template path."),
                                ["html"] = StringProperty("Rendered HTML content."),
                                ["storagePolicy"] = StringProperty("Runtime storage policy for payment/reimbursement previews. Seal data is never loaded or returned.")
                            }
                        },
                        ["ApiReportPdfRequest"] = new
                        {
                            type = "object",
                            required = new[] { "reportType", "withSeal", "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["reportType"] = StringProperty("Report type. Invoice PDF supports ExportDocument."),
                                ["templatePath"] = StringProperty("Optional managed export-document template path. When omitted, the default Export template is used."),
                                ["withSeal"] = new { type = "boolean" },
                                ["destinationPath"] = StringProperty("User-selected PDF output path. The sidecar does not assign a default system-drive path.")
                            }
                        },
                        ["ApiPaymentReportPdfRequest"] = new
                        {
                            type = "object",
                            required = new[] { "destinationPath" },
                            properties = new Dictionary<string, object>
                            {
                                ["templatePath"] = StringProperty("Optional managed payment/reimbursement template path."),
                                ["destinationPath"] = StringProperty("User-selected PDF output path. Payment/reimbursement requests do not contain seal fields.")
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
                                ["templatePath"] = StringProperty("Optional managed export-document template path. When omitted, the default Export template is used."),
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
            };
    }
}
