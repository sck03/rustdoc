namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateInvoiceDocumentPaths() =>
            new Dictionary<string, object>
            {
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
                            summary = "Delete a draft invoice",
                            operationId = "deleteInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Deleted draft invoice.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." },
                                ["409"] = new { description = "Only Draft invoices may be physically deleted; formal and cancelled records must be retained." }
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
                    ["/api/system/data-maintenance/invoices/{id}"] = new
                    {
                        get = new
                        {
                            summary = "Preview an invoice before administrator data cleanup",
                            operationId = "getInvoiceDataMaintenancePreview",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Invoice cleanup eligibility and retention guidance.",
                                    content = JsonContent("ApiInvoiceDataMaintenancePreviewResponse")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator permission required." },
                                ["404"] = new { description = "Invoice not found." }
                            }
                        }
                    },
                    ["/api/system/data-maintenance/invoices/{id}/purge"] = new
                    {
                        post = new
                        {
                            summary = "Purge a cancelled invoice through audited administrator maintenance",
                            operationId = "purgeCancelledInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoicePurgeRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Cancelled invoice purged and MaintenancePurge audit record retained.",
                                    content = JsonContent("ApiInvoicePurgeResponse")
                                },
                                ["400"] = new { description = "Invalid confirmation or maintenance reason." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator permission required." },
                                ["404"] = new { description = "Invoice not found." },
                                ["409"] = new { description = "Only cancelled invoices may be purged through this endpoint." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/unverify"] = new
                    {
                        post = new
                        {
                            summary = "Move a reversible formal invoice back to draft",
                            operationId = "unverifyInvoice",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceUnverifyRequest")
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
                                ["409"] = new { description = "Invoice is not in a reversible formal status, is cancelled, or could not be updated." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/status"] = new
                    {
                        post = new
                        {
                            summary = "Advance or cancel an invoice status",
                            operationId = "transitionInvoiceStatus",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiInvoiceStatusTransitionRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Invoice status changed.", content = JsonContent("ApiInvoiceSaveResponse") },
                                ["400"] = new { description = "Invalid transition or payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Manage permission required for cancellation." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." },
                                ["409"] = new { description = "Invoice was changed by another user." }
                            }
                        }
                    },
                    ["/api/invoices/{id}/status-history"] = new
                    {
                        get = new
                        {
                            summary = "List invoice status history",
                            operationId = "listInvoiceStatusHistory",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Status history ordered from newest to oldest.", content = JsonContent("ApiInvoiceStatusHistoryDtoArray") },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Invoice not found or outside the current user's business scope." }
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
                                    description = "Package checksum and invoice import preview. Preview checks existing invoices by company scope + InvoiceNo + Type and does not read payment/reimbursement data.",
                                    content = JsonContent("ApiInvoiceTransferPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid package path, package format, or checksum." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Package file was not found." },
                                ["503"] = new { description = "The package preview dependency failed." }
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
                                ["400"] = new { description = "Invalid, empty, or checksum-invalid package upload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["503"] = new { description = "The package preview dependency failed." }
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
                                    description = "Package was imported into the runtime database according to the selected conflict action. Actual/customs records remain independent by company scope + InvoiceNo + Type.",
                                    content = JsonContent("ApiInvoiceTransferImportResponse")
                                },
                                ["400"] = new { description = "Invalid package path, package format, conflict action, or checksum." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Package file was not found." },
                                ["503"] = new { description = "The database or package import dependency is unavailable." }
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
                                QueryParameter("endDateExclusive", "string", "date-time", "Exclusive shipment end boundary, normally the next day at midnight."),
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
                                ["202"] = new
                                {
                                    description = "Queried invoice/customs Excel export job accepted for the user-selected path.",
                                    content = JsonContent("BackgroundJobSnapshot")
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
                                QueryParameter("newInvoiceNo", "string", null, "Optional replacement invoice number.")
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Uploaded package imported.", content = JsonContent("ApiInvoiceTransferImportResponse") },
                                ["400"] = new { description = "Invalid package upload, conflict action, or checksum." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["503"] = new { description = "The database or package import dependency is unavailable." }
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
                                ["202"] = new { description = "Controlled queried-invoice Excel download job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Invalid download request." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
            };
    }
}
