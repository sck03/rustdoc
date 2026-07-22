namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateMasterDataPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/master-data/customers"] = MasterDataListPath(
                        "List customers",
                        "listCustomers",
                        "ApiCustomerDto",
                        "Create customer",
                        "createCustomer"),
                    ["/api/master-data/customers/{id}"] = MasterDataDetailPath(
                        "Get customer",
                        "getCustomer",
                        "Update customer",
                        "updateCustomer",
                        "Delete customer",
                        "deleteCustomer",
                        "ApiCustomerDto",
                        "Customer id."),
                    ["/api/master-data/exporters"] = MasterDataListPath(
                        "List exporters",
                        "listExporters",
                        "ApiExporterDto",
                        "Create exporter",
                        "createExporter"),
                    ["/api/master-data/exporters/{id}"] = MasterDataDetailPath(
                        "Get exporter",
                        "getExporter",
                        "Update exporter",
                        "updateExporter",
                        "Delete exporter",
                        "deleteExporter",
                        "ApiExporterDto",
                        "Exporter id."),
                    ["/api/master-data/payees"] = MasterDataListPath(
                        "List payees",
                        "listPayees",
                        "ApiPayeeDto",
                        "Create payee",
                        "createPayee"),
                    ["/api/master-data/payees/{id}"] = MasterDataDetailPath(
                        "Get payee",
                        "getPayee",
                        "Update payee",
                        "updatePayee",
                        "Delete payee",
                        "deletePayee",
                        "ApiPayeeDto",
                        "Payee id."),
                    ["/api/master-data/products"] = MasterDataPagedListPath(
                        "List products",
                        "listProducts",
                        "ApiProductDto",
                        "Create product",
                        "createProduct"),
                    ["/api/master-data/products/{id}"] = MasterDataDetailPath(
                        "Get product",
                        "getProduct",
                        "Update product",
                        "updateProduct",
                        "Delete product",
                        "deleteProduct",
                        "ApiProductDto",
                        "Product id."),
                    ["/api/master-data/ports"] = MasterDataListPath(
                        "List ports",
                        "listPorts",
                        "ApiPortDto",
                        "Create port",
                        "createPort"),
                    ["/api/master-data/ports/{id}"] = MasterDataDetailPath(
                        "Get port",
                        "getPort",
                        "Update port",
                        "updatePort",
                        "Delete port",
                        "deletePort",
                        "ApiPortDto",
                        "Port id."),
                    ["/api/master-data/units"] = MasterDataListPath(
                        "List units",
                        "listUnits",
                        "ApiUnitDto",
                        "Create unit",
                        "createUnit"),
                    ["/api/master-data/units/{id}"] = MasterDataDetailPath(
                        "Get unit",
                        "getUnit",
                        "Update unit",
                        "updateUnit",
                        "Delete unit",
                        "deleteUnit",
                        "ApiUnitDto",
                        "Unit id."),
                    ["/api/master-data/hs-codes"] = new
                    {
                        get = new
                        {
                            summary = "List HS codes",
                            operationId = "listHsCodes",
                            parameters = new object[]
                            {
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size capped by the API endpoint."),
                                QueryParameter("keyword", "string", null, "Keyword for HS code, normalized code, name, or description.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged HS code search results.",
                                    content = JsonContent("ApiPagedResponseOfApiHsCodeDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Create HS code",
                            operationId = "createHsCode",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new
                                {
                                    description = "Created HS code.",
                                    content = JsonContent("ApiHsCodeDto")
                                },
                                ["400"] = new { description = "Invalid HS code payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "HS code could not be saved." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/import-path"] = new
                    {
                        post = new
                        {
                            summary = "Import HS codes from an explicitly selected desktop Excel path",
                            operationId = "importHsCodesFromPath",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeImportPathRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "HS codes were imported into the current runtime data root database.",
                                    content = JsonContent("ApiHsCodeImportResponse")
                                },
                                ["400"] = new { description = "Invalid import path or workbook format." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Import workbook was not found." },
                                ["409"] = new { description = "Workbook could not be imported." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/import-preview-path"] = new
                    {
                        post = new
                        {
                            summary = "Analyze an HS code workbook selected by the desktop user",
                            operationId = "previewHsCodesImportFromPath",
                            requestBody = new { required = true, content = JsonContent("ApiHsCodeImportPreviewPathRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Detected columns and import differences.", content = JsonContent("ApiHsCodeImportPreviewResponse") },
                                ["400"] = new { description = "Invalid request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Workbook was not found." },
                                ["409"] = new { description = "Workbook could not be analyzed." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/import-preview-upload"] = new
                    {
                        post = new
                        {
                            summary = "Upload and analyze an HS code workbook without committing it",
                            operationId = "previewHsCodesImportUpload",
                            parameters = new object[]
                            {
                                QueryParameter("fileName", "string", null, "Original workbook file name."),
                                QueryParameter("mode", "string", null, "Incremental or CompleteSnapshot."),
                                QueryParameter("sourceName", "string", null, "Human readable data source."),
                                QueryParameter("effectiveYear", "integer", "int32", "Optional effective year.")
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Detected columns and import differences.", content = JsonContent("ApiHsCodeImportPreviewResponse") },
                                ["400"] = new { description = "Invalid workbook." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Workbook could not be analyzed." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/import-commit"] = new
                    {
                        post = new
                        {
                            summary = "Commit a previously reviewed HS code import preview",
                            operationId = "commitHsCodesImport",
                            requestBody = new { required = true, content = JsonContent("ApiHsCodeImportCommitRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Import commit result.", content = JsonContent("ApiHsCodeImportCommitResponse") },
                                ["400"] = new { description = "Invalid preview token." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Preview expired." },
                                ["409"] = new { description = "Import could not be committed." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/import-upload"] = new
                    {
                        post = new
                        {
                            summary = "Upload and import HS code Excel workbook",
                            operationId = "uploadHsCodesImportFile",
                            parameters = new object[]
                            {
                                QueryParameter("fileName", "string", null, "Original .xlsx or .xlsm file name for validation and diagnostics.")
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
                                    description = "Uploaded workbook was staged under runtime Cache/HsCodeImports and imported into the current database.",
                                    content = JsonContent("ApiHsCodeImportResponse")
                                },
                                ["400"] = new { description = "Invalid file name, empty body, or workbook format." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Workbook could not be imported." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/search-remote"] = new
                    {
                        get = new
                        {
                            summary = "Search HS codes from the configured online source",
                            operationId = "searchRemoteHsCodes",
                            parameters = new object[]
                            {
                                QueryParameter("keyword", "string", null, "HS code or product keyword.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Remote HS code search results returned in memory.",
                                    content = JsonContent("ApiHsCodeSearchResponse")
                                },
                                ["400"] = new { description = "Missing search keyword." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Remote search failed." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/remote-health"] = new
                    {
                        get = new
                        {
                            summary = "Check the configured HS code remote source",
                            operationId = "getHsCodeRemoteHealth",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Remote source health.", content = JsonContent("ApiHsCodeRemoteHealthResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/fetch-remote-detail"] = new
                    {
                        post = new
                        {
                            summary = "Fetch remote HS code detail",
                            operationId = "fetchRemoteHsCodeDetail",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Remote detail was fetched and returned in memory.",
                                    content = JsonContent("ApiHsCodeDto")
                                },
                                ["400"] = new { description = "Missing detail URL or invalid payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Remote detail could not be fetched." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/resolve-remote-detail"] = new
                    {
                        post = new
                        {
                            summary = "Resolve remote HS code detail and replace expired records",
                            operationId = "resolveRemoteHsCodeDetail",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Remote detail was resolved; expired rows may be removed and replacement rows returned.",
                                    content = JsonContent("ApiHsCodeRemoteDetailResolutionResponse")
                                },
                                ["400"] = new { description = "Missing detail URL or invalid payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Remote detail could not be resolved." }
                            }
                        }
                    },
                    ["/api/invoices/hs-codes/{code}"] = new
                    {
                        get = new
                        {
                            summary = "Get a trusted HS code for invoice item matching",
                            operationId = "getInvoiceHsCode",
                            parameters = new object[] { PathParameter("code", "string", null, "HS code.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "HS code detail.", content = JsonContent("ApiHsCodeDto") },
                                ["400"] = new { description = "Missing HS code." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "HS code not found." }
                            }
                        }
                    },
                    ["/api/invoices/hs-knowledge/search"] = new
                    {
                        get = new
                        {
                            summary = "Search trusted HS knowledge from an invoice item",
                            operationId = "searchInvoiceHsCodeKnowledge",
                            parameters = new object[]
                            {
                                QueryParameter("query", "string", null, "Invoice item name, material, use, or specification."),
                                QueryParameter("maxResults", "integer", "int32", "Maximum number of candidates.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Ranked local knowledge candidates.", content = JsonContent("HsCodeKnowledgeSearchResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/invoices/hs-knowledge/feedback"] = new
                    {
                        post = new
                        {
                            summary = "Record an invoice HS classification choice",
                            operationId = "recordInvoiceHsCodeKnowledgeFeedback",
                            requestBody = new { required = true, content = JsonContent("HsCodeKnowledgeFeedbackInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Feedback recorded.", content = JsonContent("ApiCommandResponse") },
                                ["400"] = new { description = "Invalid feedback or inactive current code." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/search"] = new
                    {
                        get = new
                        {
                            summary = "Search the local HS declaration knowledge base",
                            operationId = "searchHsCodeKnowledge",
                            parameters = new object[]
                            {
                                QueryParameter("query", "string", null, "Ordinary product name, material, use, or specification."),
                                QueryParameter("maxResults", "integer", "int32", "Maximum number of candidates.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Ranked local knowledge candidates.", content = JsonContent("HsCodeKnowledgeSearchResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/examples"] = new
                    {
                        get = new
                        {
                            summary = "List declaration examples",
                            operationId = "listHsCodeKnowledgeExamples",
                            parameters = new object[]
                            {
                                QueryParameter("keyword", "string", null, "Example keyword."),
                                QueryParameter("pageNumber", "integer", "int32", "Page number."),
                                QueryParameter("pageSize", "integer", "int32", "Page size.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Declaration examples.", content = JsonContent("HsCodeKnowledgeExamplePage") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Save a declaration example",
                            operationId = "saveHsCodeKnowledgeExample",
                            requestBody = new { required = true, content = JsonContent("HsCodeKnowledgeExampleInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Declaration example saved.", content = JsonContent("HsCodeKnowledgeExample") },
                                ["400"] = new { description = "Invalid example or inactive current code." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/examples/{id}"] = new
                    {
                        delete = new
                        {
                            summary = "Delete a declaration example",
                            operationId = "deleteHsCodeKnowledgeExample",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "Declaration example id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["204"] = new { description = "Declaration example deleted." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Declaration example not found." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/examples/delete-batch"] = new
                    {
                        post = new
                        {
                            summary = "Delete declaration examples in a managed batch",
                            operationId = "deleteHsCodeKnowledgeExamplesBatch",
                            requestBody = new { required = true, content = JsonContent("HsCodeKnowledgeExampleDeleteBatchInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Examples deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Manage permission required." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/feedback"] = new
                    {
                        post = new
                        {
                            summary = "Record a local HS search choice",
                            operationId = "recordHsCodeKnowledgeFeedback",
                            requestBody = new { required = true, content = JsonContent("HsCodeKnowledgeFeedbackInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Feedback recorded.", content = JsonContent("ApiCommandResponse") },
                                ["400"] = new { description = "Invalid feedback or inactive current code." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/history-candidates"] = new
                    {
                        get = new
                        {
                            summary = "Discover candidates from historical business data",
                            operationId = "discoverHsCodeHistoryCandidates",
                            parameters = new object[]
                            {
                                QueryParameter("keyword", "string", null, "Optional filter."),
                                QueryParameter("pageNumber", "integer", "int32", "One-based page number."),
                                QueryParameter("pageSize", "integer", "int32", "Page size, up to 100.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Paged historical learning candidates.", content = JsonContent("HsCodeHistoryCandidatePage") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/remote-candidates"] = new
                    {
                        get = new
                        {
                            summary = "List remote declaration candidates awaiting review",
                            operationId = "listHsCodeRemoteCandidates",
                            parameters = new object[]
                            {
                                QueryParameter("status", "string", null, "Pending, Confirmed, or Ignored."),
                                QueryParameter("keyword", "string", null, "Candidate filter."),
                                QueryParameter("pageNumber", "integer", "int32", "Page number."),
                                QueryParameter("pageSize", "integer", "int32", "Page size.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Remote candidate page.", content = JsonContent("HsCodeRemoteCandidatePage") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/remote-candidates/review-batch"] = new
                    {
                        post = new
                        {
                            summary = "Confirm or ignore multiple remote candidates",
                            operationId = "reviewHsCodeRemoteCandidatesBatch",
                            requestBody = new { required = true, content = JsonContent("HsCodeRemoteCandidateBatchReviewInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Batch review completed.", content = JsonContent("ApiCommandResponse") },
                                ["400"] = new { description = "Inactive current code or invalid review." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/remote-candidates/reset"] = new
                    {
                        post = new
                        {
                            summary = "Reset reviewed remote candidates to pending",
                            operationId = "resetHsCodeRemoteCandidates",
                            requestBody = new { required = true, content = JsonContent("HsCodeRemoteCandidateResetInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Candidates reset.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/remote-candidates/review"] = new
                    {
                        post = new
                        {
                            summary = "Confirm or ignore a remote candidate",
                            operationId = "reviewHsCodeRemoteCandidate",
                            requestBody = new { required = true, content = JsonContent("HsCodeRemoteCandidateReviewInput") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Review completed.", content = JsonContent("ApiCommandResponse") },
                                ["400"] = new { description = "Inactive current code or invalid review." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Remote candidate not found." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/export"] = new
                    {
                        get = new
                        {
                            summary = "Export the local HS knowledge package",
                            operationId = "exportHsCodeKnowledge",
                            parameters = new object[] { QueryParameter("since", "string", "date-time", "Optional incremental export timestamp.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "HS knowledge package.", content = BinaryContent() },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-knowledge/import"] = new
                    {
                        post = new
                        {
                            summary = "Import an HS knowledge package",
                            operationId = "importHsCodeKnowledge",
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "HS knowledge package imported.", content = JsonContent("HsCodeKnowledgeImportResponse") },
                                ["400"] = new { description = "Invalid or tampered package." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/{code}"] = new
                    {
                        get = new
                        {
                            summary = "Get HS code",
                            operationId = "getHsCode",
                            parameters = new object[]
                            {
                                PathParameter("code", "string", null, "HS code.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "HS code detail.",
                                    content = JsonContent("ApiHsCodeDto")
                                },
                                ["400"] = new { description = "Missing HS code." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "HS code not found." }
                            }
                        },
                        put = new
                        {
                            summary = "Update HS code",
                            operationId = "updateHsCode",
                            parameters = new object[]
                            {
                                PathParameter("code", "string", null, "HS code.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Updated HS code.",
                                    content = JsonContent("ApiHsCodeDto")
                                },
                                ["400"] = new { description = "Invalid HS code payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "HS code not found." },
                                ["409"] = new { description = "HS code could not be saved." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/by-id/{id}"] = new
                    {
                        delete = new
                        {
                            summary = "Delete HS code",
                            operationId = "deleteHsCode",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "HS code id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Deleted HS code.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid HS code id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "HS code not found." },
                                ["409"] = new { description = "HS code could not be deleted." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/delete-batch"] = new
                    {
                        post = new
                        {
                            summary = "Delete selected HS codes",
                            operationId = "deleteHsCodesBatch",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeBatchDeleteRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Selected HS codes were deleted from the current runtime data root database.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "No valid HS code ids were selected." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "No selected HS code exists." },
                                ["409"] = new { description = "Selected HS codes could not be deleted." }
                            }
                        }
                    },
                    ["/api/master-data/hs-codes/clear-all"] = new
                    {
                        post = new
                        {
                            summary = "Clear all local HS codes",
                            operationId = "clearAllHsCodes",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiHsCodeClearAllRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "All local HS codes were cleared from the current runtime data root database.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Missing confirmation text." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only administrators can clear the local HS code library." },
                                ["409"] = new { description = "HS code library could not be cleared." }
                            }
                        }
                    },
            };
    }
}
