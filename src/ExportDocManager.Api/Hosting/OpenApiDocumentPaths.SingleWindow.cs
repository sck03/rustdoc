namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSingleWindowPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/single-window/operation-center"] = new
                    {
                        get = new
                        {
                            summary = "List Single Window operation center rows",
                            operationId = "listSingleWindowOperationCenter",
                            parameters = new object[]
                            {
                                QueryParameter("businessType", "string", null, "Optional Single Window business type filter."),
                                QueryParameter("status", "string", null, "Optional batch status filter."),
                                QueryParameter("keyword", "string", null, "Keyword for invoice, contract, batch reference, receipt, machine, or client profile."),
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size capped by the service.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged Single Window operation center rows.",
                                    content = JsonContent("SingleWindowOperationCenterPageResult")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/single-window/coo/{invoiceId}"] = SingleWindowDocumentPath(
                        "Get Customs COO draft document",
                        "getCustomsCooDocument",
                        "Save Customs COO draft document",
                        "saveCustomsCooDocument",
                        "ApiCustomsCooDocumentDto",
                        "ApiCustomsCooDocumentSaveResponse"),
                    ["/api/single-window/coo/{invoiceId}/build-defaults"] = SingleWindowBuildDefaultsPath(
                        "Build Customs COO default draft document",
                        "buildCustomsCooDefaults",
                        "ApiCustomsCooDocumentDto"),
                    ["/api/single-window/coo/{invoiceId}/locked-fields"] = SingleWindowLockedFieldsPath(
                        "Get Customs COO locked fields",
                        "getCustomsCooLockedFields"),
                    ["/api/single-window/coo/{invoiceId}/unlock-fields"] = SingleWindowUnlockFieldsPath(
                        "Unlock Customs COO fields",
                        "unlockCustomsCooFields",
                        "ApiCustomsCooUnlockFieldsResponse"),
                    ["/api/single-window/coo/{invoiceId}/submit-package/save-to-path"] = SingleWindowSubmitPackagePath(
                        "Save Customs COO submit package to a desktop-selected path",
                        "saveCustomsCooSubmitPackageToPath"),
                    ["/api/single-window/coo/{invoiceId}/submit-package/download"] = SingleWindowSubmitPackageDownloadPath(
                        "Download Customs COO submit package",
                        "downloadCustomsCooSubmitPackage"),
                    ["/api/single-window/coo/producer-profiles"] = new
                    {
                        get = new
                        {
                            summary = "List Customs COO producer profiles",
                            operationId = "listCustomsCooProducerProfiles",
                            parameters = new object[]
                            {
                                QueryParameter("keyword", "string", null, "Keyword for CIQ registration number, enterprise name, contact, phone, producer text, invoice number, contract number, or style number.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Producer profiles from the runtime database, used only by Customs COO item producer fill-in.",
                                    content = JsonContent("ApiCustomsCooProducerProfileListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Create or remember Customs COO producer profile",
                            operationId = "createCustomsCooProducerProfile",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiCustomsCooProducerProfileSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Producer profile was saved to the runtime database. The sidecar does not read payment or reimbursement documents.",
                                    content = JsonContent("ApiCustomsCooProducerProfileSaveResponse")
                                },
                                ["400"] = new { description = "Invalid producer profile payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Producer profile could not be saved." }
                            }
                        }
                    },
                    ["/api/single-window/coo/producer-profiles/{id}"] = new
                    {
                        get = new
                        {
                            summary = "Get Customs COO producer profile",
                            operationId = "getCustomsCooProducerProfile",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Producer profile id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Producer profile detail with runtime database storage policy.",
                                    content = JsonContent("ApiCustomsCooProducerProfileResponse")
                                },
                                ["400"] = new { description = "Invalid producer profile id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Producer profile was not found." }
                            }
                        },
                        put = new
                        {
                            summary = "Update Customs COO producer profile",
                            operationId = "updateCustomsCooProducerProfile",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Producer profile id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiCustomsCooProducerProfileSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Producer profile was updated in the runtime database.",
                                    content = JsonContent("ApiCustomsCooProducerProfileSaveResponse")
                                },
                                ["400"] = new { description = "Invalid producer profile id or payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Producer profile was not found." },
                                ["409"] = new { description = "Producer profile could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete Customs COO producer profile",
                            operationId = "deleteCustomsCooProducerProfile",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Producer profile id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Producer profile was deleted from the runtime database.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid producer profile id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Producer profile was not found." },
                                ["409"] = new { description = "Producer profile could not be deleted." }
                            }
                        }
                    },
                    ["/api/single-window/acd/{invoiceId}"] = SingleWindowDocumentPath(
                        "Get Agent Consignment draft document",
                        "getAgentConsignmentDocument",
                        "Save Agent Consignment draft document",
                        "saveAgentConsignmentDocument",
                        "ApiAgentConsignmentDocumentDto",
                        "ApiAgentConsignmentDocumentSaveResponse"),
                    ["/api/single-window/acd/{invoiceId}/build-defaults"] = SingleWindowBuildDefaultsPath(
                        "Build Agent Consignment default draft document",
                        "buildAgentConsignmentDefaults",
                        "ApiAgentConsignmentDocumentDto"),
                    ["/api/single-window/acd/{invoiceId}/locked-fields"] = SingleWindowLockedFieldsPath(
                        "Get Agent Consignment locked fields",
                        "getAgentConsignmentLockedFields"),
                    ["/api/single-window/acd/{invoiceId}/unlock-fields"] = SingleWindowUnlockFieldsPath(
                        "Unlock Agent Consignment fields",
                        "unlockAgentConsignmentFields",
                        "ApiAgentConsignmentUnlockFieldsResponse"),
                    ["/api/single-window/acd/{invoiceId}/submit-package/save-to-path"] = SingleWindowSubmitPackagePath(
                        "Save Agent Consignment submit package to a desktop-selected path",
                        "saveAgentConsignmentSubmitPackageToPath"),
                    ["/api/single-window/acd/{invoiceId}/submit-package/download"] = SingleWindowSubmitPackageDownloadPath(
                        "Download Agent Consignment submit package",
                        "downloadAgentConsignmentSubmitPackage"),
                    ["/api/single-window/packages/import"] = SingleWindowImportPackagePath(
                        "Import Single Window submit package",
                        "importSingleWindowSubmitPackage",
                        "Submit package imported and extracted for local operation center dispatch."),
                    ["/api/single-window/receipts/import"] = SingleWindowImportPackagePath(
                        "Import Single Window receipt package",
                        "importSingleWindowReceiptPackage",
                        "Receipt package imported, parsed, and written back to local tracking records."),
                    ["/api/single-window/packages/upload"] = SingleWindowUploadPackagePath(
                        "Upload Single Window submit package",
                        "uploadSingleWindowSubmitPackage"),
                    ["/api/single-window/receipts/upload"] = SingleWindowUploadPackagePath(
                        "Upload Single Window receipt package",
                        "uploadSingleWindowReceiptPackage"),
                    ["/api/single-window/receipts/save-package-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Export Single Window receipt package",
                            operationId = "saveSingleWindowReceiptPackageToPath",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowReceiptPackageExportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Single Window receipt package exported.",
                                    content = JsonContent("ApiSingleWindowHandoffPackageResponse")
                                },
                                ["400"] = new { description = "Invalid business type, receipt files, or package path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot export the receipt package." },
                                ["409"] = new { description = "Receipt package could not be exported." }
                            }
                        }
                    },
                    ["/api/single-window/receipts/download-package"] = new
                    {
                        post = new
                        {
                            summary = "Download Single Window receipt package",
                            operationId = "downloadSingleWindowReceiptPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowReceiptPackageExportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Single Window receipt package attachment.", content = BinaryContent() },
                                ["400"] = new { description = "Invalid business type or receipt files." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot download the receipt package." },
                                ["409"] = new { description = "Receipt package could not be generated." }
                            }
                        }
                    },
                    ["/api/single-window/client-profile/default"] = new
                    {
                        get = new
                        {
                            summary = "Get default Single Window client profile",
                            operationId = "getSingleWindowDefaultClientProfile",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Default Single Window client directory profile.",
                                    content = JsonContent("ApiSingleWindowClientProfileResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        put = new
                        {
                            summary = "Save default Single Window client profile",
                            operationId = "saveSingleWindowDefaultClientProfile",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowClientProfileSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Default client directory profile saved.",
                                    content = JsonContent("ApiSingleWindowClientProfileSaveResponse")
                                },
                                ["400"] = new { description = "Invalid directory profile payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Client profile could not be saved." }
                            }
                        }
                    },
                    ["/api/single-window/client/dispatch"] = new
                    {
                        post = new
                        {
                            summary = "Dispatch Single Window batch to client import directory",
                            operationId = "dispatchSingleWindowBatchToClient",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowClientDispatchRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Batch payload files copied to the client OutBox.",
                                    content = JsonContent("SingleWindowClientDispatchResult")
                                },
                                ["400"] = new { description = "Invalid batch id or missing import root path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot dispatch the batch." },
                                ["404"] = new { description = "Single Window batch not found." },
                                ["409"] = new { description = "Batch could not be dispatched." }
                            }
                        }
                    },
                    ["/api/single-window/client/collect-receipts"] = new
                    {
                        post = new
                        {
                            summary = "Collect Single Window receipt files from client directory",
                            operationId = "collectSingleWindowClientReceipts",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowReceiptCollectionRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Receipt files matched for the batch.",
                                    content = JsonContent("SingleWindowReceiptCollectionResult")
                                },
                                ["400"] = new { description = "Invalid batch id or missing receipt root path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot collect receipts for the batch." },
                                ["404"] = new { description = "Single Window batch not found." },
                                ["409"] = new { description = "Receipt files could not be collected." }
                            }
                        }
                    },
                    ["/api/single-window/export-review/{businessType}/{invoiceId}"] = new
                    {
                        get = new
                        {
                            summary = "Build Single Window export review",
                            operationId = "getSingleWindowExportReview",
                            parameters = new object[]
                            {
                                PathParameter("businessType", "string", null, "Single Window business type: CustomsCoo or AgentConsignment."),
                                PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Single Window submit review built from invoice data and current draft state.",
                                    content = JsonContent("SingleWindowExportReview")
                                },
                                ["400"] = new { description = "Invalid business type or invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot access the invoice." },
                                ["404"] = new { description = "Source invoice not found." },
                                ["409"] = new { description = "Review could not be built." }
                            }
                        }
                    },
                    ["/api/single-window/coo/{invoiceId}/export-review"] = new
                    {
                        post = new
                        {
                            summary = "Build Customs COO export review",
                            operationId = "buildCustomsCooExportReview",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Customs COO submit review built from invoice data and current draft state.",
                                    content = JsonContent("SingleWindowExportReview")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot access the invoice." },
                                ["404"] = new { description = "Source invoice not found." },
                                ["409"] = new { description = "Review could not be built." }
                            }
                        }
                    },
                    ["/api/single-window/acd/{invoiceId}/export-review"] = new
                    {
                        post = new
                        {
                            summary = "Build Agent Consignment export review",
                            operationId = "buildAgentConsignmentExportReview",
                            parameters = new object[]
                            {
                                PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Agent Consignment submit review built from invoice data and current draft state.",
                                    content = JsonContent("SingleWindowExportReview")
                                },
                                ["400"] = new { description = "Invalid invoice id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot access the invoice." },
                                ["404"] = new { description = "Source invoice not found." },
                                ["409"] = new { description = "Review could not be built." }
                            }
                        }
                    },
                    ["/api/single-window/export-review/{businessType}/{invoiceId}/repair"] = new
                    {
                        post = new
                        {
                            summary = "Repair Single Window export review groups",
                            operationId = "repairSingleWindowExportReviewGroups",
                            parameters = new object[]
                            {
                                PathParameter("businessType", "string", null, "Single Window business type: CustomsCoo or AgentConsignment."),
                                PathParameter("invoiceId", "integer", "int32", "Source invoice id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowRepairGroupsRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Repair result and rebuilt review.",
                                    content = JsonContent("ApiSingleWindowRepairGroupsResponse")
                                },
                                ["400"] = new { description = "Invalid business type, invoice id, or repair group list." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot access the invoice." },
                                ["404"] = new { description = "Source invoice not found." },
                                ["409"] = new { description = "Repair could not be completed." }
                            }
                        }
                    },
                    ["/api/single-window/reference-catalog"] = new
                    {
                        get = new
                        {
                            summary = "Get Single Window reference catalog",
                            operationId = "getSingleWindowReferenceCatalog",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Effective Single Window reference catalog.",
                                    content = JsonContent("ApiSingleWindowReferenceCatalogResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        put = new
                        {
                            summary = "Update Single Window reference catalog override",
                            operationId = "updateSingleWindowReferenceCatalog",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSingleWindowReferenceCatalogSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Saved Single Window reference catalog override.",
                                    content = JsonContent("ApiSingleWindowReferenceCatalogSaveResponse")
                                },
                                ["400"] = new { description = "Invalid catalog payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage Single Window reference catalogs." },
                                ["409"] = new { description = "Catalog could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Reset Single Window reference catalog override",
                            operationId = "resetSingleWindowReferenceCatalog",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Override was cleared and bundled catalog is effective.",
                                    content = JsonContent("ApiSingleWindowReferenceCatalogSaveResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage Single Window reference catalogs." },
                                ["409"] = new { description = "Catalog override could not be reset." }
                            }
                        }
                    },
                    ["/api/single-window/reference-catalog/import-json"] = new
                    {
                        post = new
                        {
                            summary = "Upload and import Single Window reference catalog JSON",
                            operationId = "importSingleWindowReferenceCatalogJson",
                            requestBody = new
                            {
                                required = true,
                                content = BinaryContent()
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Uploaded JSON catalog was validated and saved as runtime override.",
                                    content = JsonContent("ApiSingleWindowReferenceCatalogSaveResponse")
                                },
                                ["400"] = new { description = "Invalid JSON catalog payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage Single Window reference catalogs." },
                                ["409"] = new { description = "Catalog could not be imported." }
                            }
                        }
                    },
                    ["/api/single-window/coo/issuing-authorities"] = new
                    {
                        get = new
                        {
                            summary = "Get Customs COO issuing authority options",
                            operationId = "getCustomsCooIssuingAuthorities",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Customs COO issuing authority options with application addresses.",
                                    content = JsonContent("ApiSingleWindowIssuingAuthorityCatalogResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/single-window/coo/editor-options"] = new
                    {
                        get = new
                        {
                            summary = "Get Customs COO editor options",
                            operationId = "getCustomsCooEditorOptions",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Customs COO editor option catalogs for certificate, declaration, trade, package, and origin fields.",
                                    content = JsonContent("ApiCustomsCooEditorOptionsResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/single-window/reference-catalog/excel/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview uploaded Single Window reference catalog Excel rows",
                            operationId = "previewSingleWindowReferenceCatalogExcelImport",
                            parameters = new object[]
                            {
                                QueryParameter("catalogKey", "string", null, "Reference catalog page key to import into.", required: true),
                                QueryParameter("fileName", "string", null, "Uploaded workbook file name for extension validation."),
                                QueryParameter("sheetName", "string", null, "Worksheet name. The first worksheet is used when omitted."),
                                QueryParameter("headerRowNumber", "integer", "int32", "Header row number used for automatic column matching. Defaults to the row before dataStartRowNumber."),
                                QueryParameter("dataStartRowNumber", "integer", "int32", "First data row number. Defaults to 2."),
                                QueryParameter("codeColumn", "integer", "int32", "Column number for code."),
                                QueryParameter("englishNameColumn", "integer", "int32", "Column number for English name."),
                                QueryParameter("chineseNameColumn", "integer", "int32", "Column number for Chinese name."),
                                QueryParameter("acdCodeColumn", "integer", "int32", "Column number for ACD currency code."),
                                QueryParameter("alphaCodeColumn", "integer", "int32", "Column number for alpha currency code."),
                                QueryParameter("nameColumn", "integer", "int32", "Column number for name."),
                                QueryParameter("descriptionColumn", "integer", "int32", "Column number for description."),
                                QueryParameter("valueColumn", "integer", "int32", "Column number for value."),
                                QueryParameter("aliasesColumn", "integer", "int32", "Column number for aliases.")
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
                                    description = "Uploaded workbook was parsed in memory and preview rows were returned.",
                                    content = JsonContent("ApiSingleWindowReferenceCatalogExcelImportPreviewResponse")
                                },
                                ["400"] = new { description = "Invalid workbook, worksheet, catalog key, or column mapping." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage Single Window reference catalogs." },
                                ["409"] = new { description = "Workbook could not be parsed." }
                            }
                        }
                    },
                    ["/api/single-window/collaboration"] = new
                    {
                        get = new
                        {
                            summary = "List Single Window collaboration tickets",
                            operationId = "listSingleWindowCollaboration",
                            parameters = new object[]
                            {
                                QueryParameter("businessType", "string", null, "Optional Single Window business type filter."),
                                QueryParameter("status", "string", null, "Optional collaboration ticket status filter."),
                                QueryParameter("keyword", "string", null, "Keyword for ticket fields."),
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size capped by the service."),
                                QueryParameter("includeDisabledWorkstations", "boolean", null, "Whether disabled workstations should be returned.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged Single Window collaboration tickets and workstation list.",
                                    content = JsonContent("SingleWindowCollaborationPageResult")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/single-window/collaboration/workstations"] = new
                    {
                        get = new
                        {
                            summary = "List Single Window workstations",
                            operationId = "listSingleWindowWorkstations",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Single Window workstation rows.",
                                    content = JsonArrayContent("SingleWindowWorkstationRow")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/single-window/operation-center/{batchId}"] = new
                    {
                        get = new
                        {
                            summary = "Get Single Window operation center detail",
                            operationId = "getSingleWindowOperationCenterDetail",
                            parameters = new object[]
                            {
                                PathParameter("batchId", "integer", "int32", "Single Window submission batch id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Single Window operation center detail.",
                                    content = JsonContent("SingleWindowOperationCenterDetail")
                                },
                                ["400"] = new { description = "Invalid batch id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Single Window submission batch not found." }
                            }
                        }
                    }
            };
    }
}
