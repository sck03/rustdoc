namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateReportDocumentPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/reports/user-templates"] = new
                    {
                        get = new
                        {
                            summary = "List user-owned and explicitly shared report designer templates",
                            operationId = "listUserReportTemplates",
                            parameters = new object[]
                            {
                                QueryParameter("reportType", "string", null, "Report type: ExportDocument or PaymentVoucher."),
                                QueryParameter("includeInactive", "boolean", null, "Include the current user's inactive templates.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "User templates filtered by owner/shared visibility.", content = JsonArrayContent("ApiUserReportTemplateDto") },
                                ["400"] = new { description = "Invalid report type." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot view report templates." }
                            }
                        },
                        post = new
                        {
                            summary = "Create a private or explicitly shared user report template",
                            operationId = "createUserReportTemplate",
                            requestBody = new { required = true, content = JsonContent("ApiUserReportTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "User template created.", content = JsonContent("ApiUserReportTemplateDto") },
                                ["400"] = new { description = "Invalid report type, content, or cross-domain field." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot design report templates." },
                                ["404"] = new { description = "The source default template was not found." }
                            }
                        }
                    },
                    ["/api/reports/user-templates/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update an owned user report template",
                            operationId = "updateUserReportTemplate",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "User template id.") },
                            requestBody = new { required = true, content = JsonContent("ApiUserReportTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "User template updated.", content = JsonContent("ApiUserReportTemplateDto") },
                                ["400"] = new { description = "Invalid report type, content, or cross-domain field." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only the owner or administrator can update the template." },
                                ["404"] = new { description = "User template was not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete an owned user report template",
                            operationId = "deleteUserReportTemplate",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "User template id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "User template deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only the owner or administrator can delete the template." },
                                ["404"] = new { description = "User template was not found." }
                            }
                        }
                    },
                    ["/api/reports/user-templates/{id}/versions"] = new
                    {
                        get = new
                        {
                            summary = "List user report template history",
                            operationId = "listUserReportTemplateVersions",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "User template id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Versions ordered newest first.", content = JsonArrayContent("ApiUserReportTemplateVersionDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot view template history." },
                                ["404"] = new { description = "Template or history was not found." }
                            }
                        }
                    },
                    ["/api/reports/user-templates/{id}/versions/{versionNumber}/restore"] = new
                    {
                        post = new
                        {
                            summary = "Restore a user report template version",
                            operationId = "restoreUserReportTemplateVersion",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "User template id."),
                                PathParameter("versionNumber", "integer", "int32", "Version number to restore.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "A new current version was created from the selected snapshot.", content = JsonContent("ApiUserReportTemplateDto") },
                                ["400"] = new { description = "Invalid template or version." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot restore this template." },
                                ["404"] = new { description = "Template or version was not found." }
                            }
                        }
                    },
                    ["/api/reports/templates"] = new
                    {
                        get = new
                        {
                            summary = "List report templates",
                            operationId = "listReportTemplates",
                            parameters = new object[]
                            {
                                QueryParameter("reportType", "string", null, "Report type: ExportDocument or PaymentVoucher.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report templates resolved from the read-only built-in catalog and runtime-data user catalog, filtered by report type.",
                                    content = JsonContent("ApiReportTemplateDtoArray")
                                },
                                ["400"] = new { description = "Invalid report type." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Create report template",
                            operationId = "createReportTemplate",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportTemplateCreateRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template was created under the runtime-data user directory for the selected report type.",
                                    content = JsonContent("ApiReportTemplateContentDto")
                                },
                                ["400"] = new { description = "Invalid report type, template path, or request body." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates or the path is not editable." },
                                ["409"] = new { description = "Template could not be created." }
                            }
                        }
                    },
                    ["/api/reports/templates/storage-check"] = new
                    {
                        post = new
                        {
                            summary = "Check report template directory writability",
                            operationId = "checkReportTemplateStorage",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Explicit Templates directory write probe completed and the temporary probe file was removed.",
                                    content = JsonContent("ApiReportTemplateStorageStatusResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates." }
                            }
                        }
                    },
                    ["/api/reports/templates/fields"] = new
                    {
                        get = new
                        {
                            summary = "Get report template field catalog",
                            operationId = "getReportTemplateFieldCatalog",
                            parameters = new object[]
                            {
                                QueryParameter("reportType", "string", null, "Report type: ExportDocument or PaymentVoucher.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template field catalog used by the visual designer. The catalog is static business metadata and does not read or write runtime files.",
                                    content = JsonContent("ApiReportTemplateFieldCatalogResponse")
                                },
                                ["400"] = new { description = "Invalid report type." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/reports/templates/content"] = new
                    {
                        get = new
                        {
                            summary = "Get report template HTML content",
                            operationId = "getReportTemplateContent",
                            parameters = new object[]
                            {
                                QueryParameter("reportType", "string", null, "Report type: ExportDocument or PaymentVoucher."),
                                QueryParameter("templatePath", "string", null, "Resolved path from the managed built-in or user template catalog.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template HTML content. The path must belong to the managed catalog and match the requested report type.",
                                    content = JsonContent("ApiReportTemplateContentDto")
                                },
                                ["400"] = new { description = "Invalid report type or template path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Template path is outside the editable template roots." },
                                ["404"] = new { description = "Template was not found." },
                                ["409"] = new { description = "Template content could not be read." }
                            }
                        },
                        put = new
                        {
                            summary = "Save report template HTML content",
                            operationId = "saveReportTemplateContent",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportTemplateSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template was saved atomically under the runtime-data user template directory; editing a built-in template creates a user copy.",
                                    content = JsonContent("ApiReportTemplateContentDto")
                                },
                                ["400"] = new { description = "Invalid report type, template path, or request body." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates or the path is not editable." },
                                ["404"] = new { description = "Template was not found." },
                                ["409"] = new { description = "Template could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete report template",
                            operationId = "deleteReportTemplate",
                            parameters = new object[]
                            {
                                QueryParameter("reportType", "string", null, "Report type: ExportDocument or PaymentVoucher."),
                                QueryParameter("templatePath", "string", null, "User template path from the managed catalog.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User template was deleted from the runtime-data template directory.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid report type or template path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates or the path is not editable." },
                                ["404"] = new { description = "Template was not found." },
                                ["409"] = new { description = "Template could not be deleted." }
                            }
                        }
                    },
                    ["/api/reports/templates/rename"] = new
                    {
                        post = new
                        {
                            summary = "Rename report template",
                            operationId = "renameReportTemplate",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportTemplateRenameRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User template was renamed within its report-type directory.",
                                    content = JsonContent("ApiReportTemplateContentDto")
                                },
                                ["400"] = new { description = "Invalid report type, template path, or request body." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates or the path is not editable." },
                                ["404"] = new { description = "Template was not found." },
                                ["409"] = new { description = "Template could not be renamed." }
                            }
                        }
                    },
                    ["/api/reports/templates/package/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Export report template package",
                            operationId = "saveReportTemplatePackageToPath",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportTemplatePackageExportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template package was written to the explicit destination path, or to runtime DataRoot/TemplatePackages for relative paths.",
                                    content = JsonContent("ApiReportTemplatePackageExportResponse")
                                },
                                ["400"] = new { description = "Invalid package path or request body." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates or the desktop token is invalid." },
                                ["409"] = new { description = "Template package could not be exported." }
                            }
                        }
                    },
                    ["/api/reports/templates/package/download"] = new
                    {
                        post = new
                        {
                            summary = "Download report template package for browser clients",
                            operationId = "downloadReportTemplatePackage",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template package bytes generated through runtime Cache/TemplatePackages and returned to the browser.",
                                    content = BinaryContent()
                                },
                                ["400"] = new { description = "Template package could not be prepared." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates." },
                                ["409"] = new { description = "Template package could not be exported." }
                            }
                        }
                    },
                    ["/api/reports/templates/package/import"] = new
                    {
                        post = new
                        {
                            summary = "Import report template package",
                            operationId = "importReportTemplatePackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportTemplatePackageImportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template package was imported through runtime Cache/TemplatePackages and written to runtime-data user Templates.",
                                    content = JsonContent("ApiReportTemplatePackageImportResponse")
                                },
                                ["400"] = new { description = "Invalid package path, strategy, or package content." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates." },
                                ["404"] = new { description = "Template package was not found." },
                                ["409"] = new { description = "Template package could not be imported." }
                            }
                        }
                    },
                    ["/api/reports/templates/package/upload"] = new
                    {
                        post = new
                        {
                            summary = "Upload and import report template package for browser clients",
                            operationId = "uploadReportTemplatePackage",
                            parameters = new object[]
                            {
                                QueryParameter("strategy", "string", null, "Import strategy: Overwrite, Merge, or AddOnly. Defaults to Merge."),
                                QueryParameter("fileName", "string", null, "Original .edtpl or .zip file name for validation and diagnostics.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = BinaryContent()
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Uploaded package was staged under runtime Cache/TemplatePackages and imported into runtime-data user Templates.",
                                    content = JsonContent("ApiReportTemplatePackageImportResponse")
                                },
                                ["400"] = new { description = "Invalid strategy, file name, empty body, or package content." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage report templates." },
                                ["409"] = new { description = "Template package could not be imported." }
                            }
                        }
                    },
                    ["/api/reports/templates/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview report template HTML from in-memory content",
                            operationId = "previewReportTemplateContent",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportTemplatePreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered sample report HTML. The request is rendered in memory and does not create runtime files.",
                                    content = JsonContent("ApiReportTemplatePreviewResponse")
                                },
                                ["400"] = new { description = "Invalid report type or template content." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Template content could not be rendered." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/html-preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview invoice report HTML",
                            operationId = "previewInvoiceReportHtml",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportHtmlPreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered export-document HTML using a managed Export template. Payment/reimbursement templates are rejected.",
                                    content = JsonContent("ApiReportHtmlPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id, report type, or preview request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice or template not found." },
                                ["409"] = new { description = "Report HTML could not be rendered." }
                            }
                        }
                    },
                    ["/api/reports/invoices/draft/html-preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview invoice draft report HTML",
                            operationId = "previewInvoiceReportDraftHtml",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceDraftReportHtmlPreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered invoice/customs draft HTML. The sidecar uses only the request invoice draft, Templates, and master-data snapshots; it does not load payment/reimbursement documents by invoice number and does not persist the draft.",
                                    content = JsonContent("ApiReportHtmlPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid invoice draft, report type, or preview request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Template not found." },
                                ["409"] = new { description = "Invoice draft report HTML could not be rendered." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/document-package/html-preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview invoice document package HTML",
                            operationId = "previewInvoiceDocumentPackageHtml",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceDocumentPackagePreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered multi-template invoice/customs document HTML. The sidecar returns HTML in memory, reads only the invoice/customs document domain, and does not create PDF, ZIP, cache files, or default export directories.",
                                    content = JsonContent("ApiInvoiceDocumentPackagePreviewResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id, template list, or report type." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice or template not found." },
                                ["409"] = new { description = "Document package HTML could not be rendered." }
                            }
                        }
                    },
                    ["/api/reports/payments/{paymentId}/html-preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview payment voucher report HTML",
                            operationId = "previewPaymentVoucherHtml",
                            parameters = new object[]
                            {
                                PathParameter("paymentId", "integer", "int32", "Payment or reimbursement id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentReportHtmlPreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered payment/reimbursement HTML using a managed Internal template. Export-document templates are rejected, and invoice/customs data is not loaded.",
                                    content = JsonContent("ApiPaymentReportHtmlPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid payment id, report type, or preview request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment or template not found." },
                                ["409"] = new { description = "Payment voucher HTML could not be rendered." }
                            }
                        }
                    },
                    ["/api/reports/payments/draft/html-preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview payment voucher draft HTML",
                            operationId = "previewPaymentVoucherDraftHtml",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentDraftReportHtmlPreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered payment/reimbursement draft HTML. The sidecar uses only the request payment draft, Templates/Internal, and master-data snapshots; it does not load invoice/customs documents by Payment.InvoiceNo and does not persist the draft.",
                                    content = JsonContent("ApiPaymentReportHtmlPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid payment draft, report type, or preview request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Template not found." },
                                ["409"] = new { description = "Payment voucher draft HTML could not be rendered." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/pdf/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice report PDF job",
                            operationId = "startInvoiceReportPdfSaveToPathJob",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiReportPdfRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Export-document PDF background job was accepted. A managed Export template is read and output is written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid invoice id, report type, or destination path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/pdf/download"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice report PDF browser download job",
                            operationId = "startInvoiceReportPdfDownloadJob",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new { required = true, content = JsonContent("ApiReportPdfRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled PDF download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid invoice id or report request." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/reports/payments/{paymentId}/pdf/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start payment voucher report PDF job",
                            operationId = "startPaymentVoucherPdfSaveToPathJob",
                            parameters = new object[]
                            {
                                PathParameter("paymentId", "integer", "int32", "Payment or reimbursement id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentReportPdfRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Payment/reimbursement PDF background job was accepted. A managed Internal template is read, payment data only is rendered, and output is written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid payment id, report type, or destination path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/reports/payments/{paymentId}/pdf/download"] = new
                    {
                        post = new
                        {
                            summary = "Start payment PDF browser download job",
                            operationId = "startPaymentVoucherPdfDownloadJob",
                            parameters = new object[]
                            {
                                PathParameter("paymentId", "integer", "int32", "Payment or reimbursement id.")
                            },
                            requestBody = new { required = true, content = JsonContent("ApiPaymentReportPdfRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled PDF download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid payment id or report request." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/document-package/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice document package job",
                            operationId = "startInvoiceDocumentPackageSaveToPathJob",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceDocumentPackageRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Single-invoice multi-template document package job was accepted. It reads only the invoice/customs document domain, uses runtime cache for temporary PDFs, and writes either the final ZIP to the explicit .zip path or document PDFs to a batch folder under the explicit destination directory.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid invoice id, template list, report type, or destination path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/document-package/download"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice document package browser download job",
                            operationId = "startInvoiceDocumentPackageDownloadJob",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new { required = true, content = JsonContent("ApiInvoiceDocumentPackageRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled document ZIP download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid invoice id or package request." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/reports/invoices/{invoiceId}/document-email"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice document email job",
                            operationId = "startInvoiceDocumentEmailJob",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceDocumentEmailRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Single-invoice multi-template document email job was accepted. Temporary PDFs are generated under the runtime data cache, sent through SMTP, and cleaned after completion without creating a default attachment directory.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid invoice id, template list, report type, recipient address, or SMTP configuration." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["503"] = new { description = "The configured SMTP service is unavailable or the background job failed." }
                            }
                        }
                    },
                    ["/api/reports/invoices/pdf-zip/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice report PDF ZIP job",
                            operationId = "startInvoiceReportPdfZipSaveToPathJob",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceReportZipRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Batch report ZIP job was accepted. Templates and browser renderer are read from the program root; temporary PDFs use the runtime data cache; final ZIP is written only to the explicit destination path.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid invoice ids, report type, or destination path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/reports/invoices/pdf-zip/download"] = new
                    {
                        post = new
                        {
                            summary = "Start invoice report ZIP browser download job",
                            operationId = "startInvoiceReportPdfZipDownloadJob",
                            requestBody = new { required = true, content = JsonContent("ApiInvoiceReportZipRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Controlled report ZIP download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid invoice ids or report request." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
            };
    }
}
