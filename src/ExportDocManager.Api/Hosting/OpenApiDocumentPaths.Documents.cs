namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateDocumentsPaths() =>
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
                                    description = "Report templates resolved from the program root Templates directory.",
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
                                    description = "Report template was created under the program root Templates directory.",
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
                                QueryParameter("templatePath", "string", null, "Resolved template path under program root Templates or an explicit configured path.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template HTML content. Editable templates are constrained to program root Templates or explicit catalog entries.",
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
                                    description = "Report template was saved atomically under the program root Templates directory or an explicit catalog entry.",
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
                                QueryParameter("templatePath", "string", null, "Template path under program root Templates.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Report template was deleted from the program root Templates directory.",
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
                                    description = "Report template was renamed under the program root Templates directory.",
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
                                    description = "Report template package was imported through runtime Cache/TemplatePackages and written to program root Templates.",
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
                                    description = "Uploaded package was staged under runtime Cache/TemplatePackages and imported into program root Templates.",
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
                                    description = "Rendered report HTML using Templates under the program root or an explicit template path.",
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
                                content = JsonContent("ApiReportHtmlPreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Rendered payment/reimbursement HTML using Templates/Internal under the program root or an explicit template path. Payment data is rendered from the payment domain and does not load invoice/customs documents.",
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
                                    description = "Report PDF background job was accepted. The sidecar reads Templates under the program root and writes only to the explicit destination path.",
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
                                content = JsonContent("ApiReportPdfRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "Payment/reimbursement PDF background job was accepted. The sidecar reads Templates/Internal and the browser renderer from the program root, renders payment data only, and writes only to the explicit destination path.",
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
                            requestBody = new { required = true, content = JsonContent("ApiReportPdfRequest") },
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
                                ["400"] = new { description = "Invalid invoice id, template list, report type, or recipient address." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "SMTP settings are not configured." }
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
                    ["/api/invoices"] = new
                    {
                        get = new
                        {
                            summary = "List invoices",
                            operationId = "listInvoices",
                            parameters = new object[]
                            {
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size. The repository caps this to the shared maximum."),
                                QueryParameter("keyword", "string", null, "Keyword for invoice number, contract number, customer, exporter, ports, or destination."),
                                QueryParameter("sortColumn", "string", null, "Shared invoice list sort column."),
                                QueryParameter("ascending", "boolean", null, "Whether sort order is ascending.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged invoice list for the authenticated local user.",
                                    content = JsonContent("ApiPagedResponseOfApiInvoiceListItemDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Create invoice",
                            operationId = "createInvoice",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceDetailDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new
                                {
                                    description = "Created invoice.",
                                    content = JsonContent("ApiInvoiceSaveResponse")
                                },
                                ["400"] = new { description = "Invalid invoice payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Invoice could not be saved." }
                            }
                        }
                    },
                    ["/api/invoices/{id}"] = new
                    {
                        get = new
                        {
                            summary = "Get invoice detail",
                            operationId = "getInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Invoice detail with line items for the authenticated local user.",
                                    content = JsonContent("ApiInvoiceDetailDto")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." }
                            }
                        },
                        put = new
                        {
                            summary = "Update invoice",
                            operationId = "updateInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceDetailDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Updated invoice.",
                                    content = JsonContent("ApiInvoiceSaveResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id or payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." },
                                ["409"] = new { description = "Invoice could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete invoice",
                            operationId = "deleteInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Deleted invoice.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/clone"] = new
                    {
                        post = new
                        {
                            summary = "Clone invoice",
                            operationId = "cloneInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Source invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceCloneRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Cloned invoice.",
                                    content = JsonContent("ApiInvoiceCloneResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id or clone payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/unverify"] = new
                    {
                        post = new
                        {
                            summary = "Move a locked invoice back to draft",
                            operationId = "unverifyInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Invoice status was reset to Draft. The endpoint only updates the current invoice status and does not read payment/reimbursement data.",
                                    content = JsonContent("ApiInvoiceSaveResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." },
                                ["409"] = new { description = "Invoice is not in a locked status or could not be updated." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/clone-type"] = new
                    {
                        post = new
                        {
                            summary = "Clone invoice as another trade data type with the same invoice number",
                            operationId = "cloneInvoiceAsType",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Source invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceCloneTypeRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Created an independent invoice record for the target actual/customs data type. The endpoint only reads the source invoice id and never reads payment/reimbursement data.",
                                    content = JsonContent("ApiInvoiceCloneTypeResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id or target invoice type." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." },
                                ["409"] = new { description = "The target invoice number and type already exists, so no data is overwritten." }
                            }
                        }
                    },
                    ["/api/invoices/shipping-marks/image"] = new
                    {
                        post = new
                        {
                            summary = "Save a visual shipping mark image",
                            operationId = "saveShippingMarkImage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiShippingMarkImageSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "PNG shipping mark image was saved under the runtime data root Marks directory. The invoice draft stores only the returned path.",
                                    content = JsonContent("ApiShippingMarkImageSaveResponse")
                                },
                                ["400"] = new { description = "Image data URL is missing or invalid." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/invoices/shipping-marks/image/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview a saved visual shipping mark image",
                            operationId = "previewShippingMarkImage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiShippingMarkImagePreviewRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Shipping mark image data URL. The sidecar only reads files under the runtime data root Marks directory.",
                                    content = JsonContent("ApiShippingMarkImagePreviewResponse")
                                },
                                ["400"] = new { description = "Image path is blank, invalid, or outside the runtime Marks directory." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Shipping mark image file was not found." },
                                ["409"] = new { description = "Shipping mark image could not be previewed." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/transfer-package/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Export an invoice transfer package",
                            operationId = "saveInvoiceTransferPackageToPath",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Source invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceTransferPathRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Invoice transfer package was written to the explicit user-selected .edpkg path. Temporary files are constrained to the runtime data root.",
                                    content = JsonContent("ApiInvoiceTransferExportResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id or package path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." },
                                ["404"] = new { description = "Invoice was not found." },
                                ["409"] = new { description = "Package could not be exported." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/transfer-package/download"] = new
                    {
                        post = new
                        {
                            summary = "Download an invoice transfer package",
                            operationId = "downloadInvoiceTransferPackage",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Source invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Invoice transfer package attachment.", content = BinaryContent() },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice was not found." },
                                ["409"] = new { description = "Package could not be generated." }
                            }
                        }
                    },
                    ["/api/invoices/transfer-package/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview an invoice transfer package",
                            operationId = "previewInvoiceTransferPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceTransferPathRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Package checksum and invoice import preview. Preview checks existing invoices by InvoiceNo + Type and does not read payment/reimbursement data.",
                                    content = JsonContent("ApiInvoiceTransferPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid package path or package format." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Package file was not found." },
                                ["409"] = new { description = "Package could not be previewed." }
                            }
                        }
                    },
                    ["/api/invoices/transfer-package/upload/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview an uploaded invoice transfer package",
                            operationId = "previewUploadedInvoiceTransferPackage",
                            parameters = new object[]
                            {
                                QueryParameter("fileName", "string", null, "Uploaded .edpkg file name.")
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Uploaded package preview.", content = JsonContent("ApiInvoiceTransferPreviewResponse") },
                                ["400"] = new { description = "Invalid or empty package upload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Package could not be previewed." }
                            }
                        }
                    },
                    ["/api/invoices/transfer-package/import"] = new
                    {
                        post = new
                        {
                            summary = "Import an invoice transfer package",
                            operationId = "importInvoiceTransferPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceTransferImportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Package was imported into the runtime database according to the selected conflict action. Actual/customs records remain independent by InvoiceNo + Type.",
                                    content = JsonContent("ApiInvoiceTransferImportResponse")
                                },
                                ["400"] = new { description = "Invalid package path, package format, or conflict action." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Package file was not found." },
                                ["409"] = new { description = "Package checksum failed or import could not be completed." }
                            }
                        }
                    },
                    ["/api/invoices/profit-analysis"] = new
                    {
                        post = new
                        {
                            summary = "Analyze invoice profit from an in-memory invoice draft",
                            operationId = "analyzeInvoiceProfit",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceProfitAnalysisRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Profit analysis calculated from the invoice draft in the request. The sidecar does not read payment/reimbursement data and does not persist the result.",
                                    content = JsonContent("ApiInvoiceProfitAnalysisResponse")
                                },
                                ["400"] = new { description = "Invalid invoice draft." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/query/invoices"] = new
                    {
                        get = new
                        {
                            summary = "Query invoices with the legacy query-form filters",
                            operationId = "listQueriedInvoices",
                            parameters = new object[]
                            {
                                QueryParameter("startDate", "string", "date-time", "Inclusive shipment start date."),
                                QueryParameter("endDate", "string", "date-time", "Inclusive shipment end date."),
                                QueryParameter("customerId", "integer", "int32", "Customer id filter."),
                                QueryParameter("exporterId", "integer", "int32", "Exporter id filter."),
                                QueryParameter("keyword", "string", null, "Keyword for invoice number, contract number, customer, exporter, destination, transport, style name, style number, or HS code."),
                                QueryParameter("contractNo", "string", null, "Contract number keyword."),
                                QueryParameter("invoiceType", "string", null, "Invoice type filter, for example actual or customs data."),
                                QueryParameter("transportMode", "string", null, "Transport mode filter."),
                                QueryParameter("styleName", "string", null, "Line-item style name keyword."),
                                QueryParameter("styleNo", "string", null, "Line-item style number keyword."),
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size. The repository caps this to the shared maximum.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged invoice/customs query rows. This endpoint reads invoice-domain data and does not load payment or reimbursement records.",
                                    content = JsonContent("ApiPagedResponseOfApiQueryInvoiceRowDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/query/invoices/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Export queried invoice rows",
                            operationId = "saveQueriedInvoicesToPath",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiQueryInvoiceExportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Queried invoice/customs rows exported to the user-selected .xlsx path.",
                                    content = JsonContent("ApiQueryInvoiceExportResponse")
                                },
                                ["400"] = new { description = "Invalid export request or destination path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Trusted desktop token required." }
                            }
                        }
                    },
                    ["/api/invoices/transfer-package/upload/import"] = new
                    {
                        post = new
                        {
                            summary = "Import an uploaded invoice transfer package",
                            operationId = "importUploadedInvoiceTransferPackage",
                            parameters = new object[]
                            {
                                QueryParameter("fileName", "string", null, "Uploaded .edpkg file name."),
                                QueryParameter("conflictAction", "string", null, "Skip, Overwrite, NewInvoiceNo, or AppendItems."),
                                QueryParameter("newInvoiceNo", "string", null, "Optional replacement invoice number."),
                                QueryParameter("allowInvalidChecksum", "boolean", null, "Whether checksum failure is accepted.")
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Uploaded package imported.", content = JsonContent("ApiInvoiceTransferImportResponse") },
                                ["400"] = new { description = "Invalid package upload or conflict action." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Checksum failed or package could not be imported." }
                            }
                        }
                    },
                    ["/api/query/invoices/download"] = new
                    {
                        post = new
                        {
                            summary = "Download queried invoice rows",
                            operationId = "downloadQueriedInvoices",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiQueryInvoiceFilterRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Queried invoice Excel attachment.", content = BinaryContent() },
                                ["400"] = new { description = "Invalid download request." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/payments"] = new
                    {
                        get = new
                        {
                            summary = "List payments",
                            operationId = "listPayments",
                            parameters = new object[]
                            {
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size. The repository caps this to the shared maximum."),
                                QueryParameter("keyword", "string", null, "Keyword for invoice number, payer, payee, project, bank, goods, country, or notes.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged payment list for the authenticated local user.",
                                    content = JsonContent("ApiPagedResponseOfApiPaymentDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Create payment",
                            operationId = "createPayment",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new
                                {
                                    description = "Created payment.",
                                    content = JsonContent("ApiPaymentSaveResponse")
                                },
                                ["400"] = new { description = "Invalid payment payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Payment could not be saved." }
                            }
                        }
                    },
                    ["/api/payments/{id}"] = new
                    {
                        get = new
                        {
                            summary = "Get payment detail",
                            operationId = "getPayment",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Payment id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Payment detail for the authenticated local user.",
                                    content = JsonContent("ApiPaymentDto")
                                },
                                ["400"] = new { description = "Invalid payment id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment not found or outside the current user's business scope." }
                            }
                        },
                        put = new
                        {
                            summary = "Update payment",
                            operationId = "updatePayment",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Payment id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Updated payment.",
                                    content = JsonContent("ApiPaymentSaveResponse")
                                },
                                ["400"] = new { description = "Invalid payment id or payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment not found or outside the current user's business scope." },
                                ["409"] = new { description = "Payment could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete payment",
                            operationId = "deletePayment",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Payment id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Deleted payment.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid payment id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment not found or outside the current user's business scope." }
                            }
                        }
                    },
            };
    }
}
