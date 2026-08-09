namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSingleWindowDocumentSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiCustomsCooDocumentDto"] = SingleWindowCustomsCooDocumentSchema(),
                        ["ApiCustomsCooItemDto"] = SingleWindowCustomsCooItemSchema(),
                        ["ApiCustomsCooNonpartyCorpDto"] = SingleWindowCustomsCooNonpartyCorpSchema(),
                        ["ApiCustomsCooAttachmentDto"] = SingleWindowCustomsCooAttachmentSchema(),
                        ["ApiCustomsCooDocumentSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "document", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["document"] = RefSchema("ApiCustomsCooDocumentDto"),
                                ["message"] = StringProperty("Save result message.")
                            }
                        },
                        ["ApiCustomsCooProducerProfileDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "ciqRegNo",
                                "prdcEtpsName",
                                "prdcEtpsConcEr",
                                "prdcEtpsTel",
                                "producer",
                                "producerTel",
                                "producerFax",
                                "producerEmail",
                                "producerSertFlag",
                                "lastInvoiceNo",
                                "lastContractNo",
                                "lastSourceStyleNo",
                                "createdAt",
                                "updatedAt",
                                "lastUsedAt"
                            },
                            properties = MergeProperties(
                                SchemaProperties(
                                    stringProperties:
                                    [
                                        "CiqRegNo",
                                        "PrdcEtpsName",
                                        "PrdcEtpsConcEr",
                                        "PrdcEtpsTel",
                                        "Producer",
                                        "ProducerTel",
                                        "ProducerFax",
                                        "ProducerEmail",
                                        "ProducerSertFlag",
                                        "LastInvoiceNo",
                                        "LastContractNo",
                                        "LastSourceStyleNo"
                                    ],
                                    integerProperties: ["Id"],
                                    dateTimeProperties: ["CreatedAt", "UpdatedAt", "LastUsedAt"]),
                                new Dictionary<string, object>())
                        },
                        ["ApiCustomsCooProducerProfileResponse"] = new
                        {
                            type = "object",
                            required = new[] { "profile", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["profile"] = RefSchema("ApiCustomsCooProducerProfileDto"),
                                ["storagePolicy"] = StringProperty("Producer profile storage policy summary.")
                            }
                        },
                        ["ApiCustomsCooProducerProfileListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "items", "totalCount", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["items"] = RefArraySchema("ApiCustomsCooProducerProfileDto"),
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["storagePolicy"] = StringProperty("Producer profile storage policy summary.")
                            }
                        },
                        ["ApiCustomsCooProducerProfileInputDto"] = ObjectSchema(SchemaProperties(
                            stringProperties:
                            [
                                "CiqRegNo",
                                "PrdcEtpsName",
                                "PrdcEtpsConcEr",
                                "PrdcEtpsTel",
                                "Producer",
                                "ProducerTel",
                                "ProducerFax",
                                "ProducerEmail",
                                "ProducerSertFlag",
                                "LastInvoiceNo",
                                "LastContractNo",
                                "LastSourceStyleNo"
                            ])),
                        ["ApiCustomsCooProducerProfileSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "profile" },
                            properties = new Dictionary<string, object>
                            {
                                ["profile"] = RefSchema("ApiCustomsCooProducerProfileInputDto")
                            }
                        },
                        ["ApiCustomsCooProducerProfileSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "profile", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["profile"] = RefSchema("ApiCustomsCooProducerProfileDto"),
                                ["message"] = StringProperty("Save result message."),
                                ["storagePolicy"] = StringProperty("Producer profile storage policy summary.")
                            }
                        },
                        ["ApiSingleWindowLockedFieldDto"] = new
                        {
                            type = "object",
                            required = new[] { "key", "displayName", "currentValue", "suggestedValue" },
                            properties = new Dictionary<string, object>
                            {
                                ["key"] = StringProperty("Stable locked field key."),
                                ["displayName"] = StringProperty("Human-readable field name."),
                                ["currentValue"] = StringProperty("Current manually overridden value."),
                                ["suggestedValue"] = StringProperty("Current suggested value from the source invoice.")
                            }
                        },
                        ["ApiSingleWindowLockedFieldsResponse"] = new
                        {
                            type = "object",
                            required = new[] { "count", "fields" },
                            properties = new Dictionary<string, object>
                            {
                                ["count"] = new { type = "integer", format = "int32" },
                                ["fields"] = RefArraySchema("ApiSingleWindowLockedFieldDto")
                            }
                        },
                        ["ApiSingleWindowUnlockFieldsRequest"] = new
                        {
                            type = "object",
                            required = new[] { "fieldKeys" },
                            properties = new Dictionary<string, object>
                            {
                                ["fieldKeys"] = StringArrayProperty("Locked field keys to restore to suggested values.")
                            }
                        },
                        ["ApiCustomsCooUnlockFieldsResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "changedCount", "document", "lockedFields", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["changedCount"] = new { type = "integer", format = "int32" },
                                ["document"] = RefSchema("ApiCustomsCooDocumentDto"),
                                ["lockedFields"] = RefArraySchema("ApiSingleWindowLockedFieldDto"),
                                ["message"] = StringProperty("Unlock result message.")
                            }
                        },
                        ["ApiAgentConsignmentDocumentDto"] = SingleWindowAgentConsignmentDocumentSchema(),
                        ["ApiAgentConsignmentDocumentSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "document", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["document"] = RefSchema("ApiAgentConsignmentDocumentDto"),
                                ["message"] = StringProperty("Save result message.")
                            }
                        },
                        ["ApiAgentConsignmentUnlockFieldsResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "changedCount", "document", "lockedFields", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["changedCount"] = new { type = "integer", format = "int32" },
                                ["document"] = RefSchema("ApiAgentConsignmentDocumentDto"),
                                ["lockedFields"] = RefArraySchema("ApiSingleWindowLockedFieldDto"),
                                ["message"] = StringProperty("Unlock result message.")
                            }
                        },
                        ["SingleWindowEditorNavigationTarget"] = new
                        {
                            type = "object",
                            required = new[] { "groupKey", "propertyKey", "goodsLineNo" },
                            properties = new Dictionary<string, object>
                            {
                                ["groupKey"] = StringProperty("Editor group key."),
                                ["propertyKey"] = StringProperty("Editor property key."),
                                ["goodsLineNo"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowExportIssue"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "groupKey",
                                "groupDisplayName",
                                "message",
                                "severity",
                                "canAutoRepair",
                                "navigationTarget"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["groupKey"] = StringProperty("Issue group key."),
                                ["groupDisplayName"] = StringProperty("Issue group display name."),
                                ["message"] = StringProperty("Issue message."),
                                ["severity"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Issue severity: 0=Info, 1=Warning, 2=Error.",
                                    @enum = new[] { 0, 1, 2 }
                                },
                                ["canAutoRepair"] = new { type = "boolean" },
                                ["navigationTarget"] = RefSchema("SingleWindowEditorNavigationTarget")
                            }
                        },
                        ["SingleWindowExportIssueGroup"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "groupKey",
                                "groupDisplayName",
                                "canAutoRepair",
                                "issues",
                                "errorCount",
                                "warningCount",
                                "infoCount"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["groupKey"] = StringProperty("Issue group key."),
                                ["groupDisplayName"] = StringProperty("Issue group display name."),
                                ["canAutoRepair"] = new { type = "boolean" },
                                ["issues"] = RefArraySchema("SingleWindowExportIssue"),
                                ["errorCount"] = new { type = "integer", format = "int32" },
                                ["warningCount"] = new { type = "integer", format = "int32" },
                                ["infoCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowExportReview"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "businessType",
                                "invoiceId",
                                "invoiceNo",
                                "contractNo",
                                "draftRevision",
                                "manualLockedFieldCount",
                                "sourceDiffCount",
                                "sourceDiffSummary",
                                "groups",
                                "totalErrorCount",
                                "totalWarningCount",
                                "hasIssues"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["businessType"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Single Window business type: 0=CustomsCoo, 1=AgentConsignment.",
                                    @enum = new[] { 0, 1 }
                                },
                                ["invoiceId"] = new { type = "integer", format = "int32" },
                                ["invoiceNo"] = StringProperty("Source invoice number."),
                                ["contractNo"] = StringProperty("Source contract number."),
                                ["draftRevision"] = new { type = "integer", format = "int32" },
                                ["manualLockedFieldCount"] = new { type = "integer", format = "int32" },
                                ["sourceDiffCount"] = new { type = "integer", format = "int32" },
                                ["sourceDiffSummary"] = StringProperty("Source difference summary."),
                                ["groups"] = RefArraySchema("SingleWindowExportIssueGroup"),
                                ["totalErrorCount"] = new { type = "integer", format = "int32" },
                                ["totalWarningCount"] = new { type = "integer", format = "int32" },
                                ["hasIssues"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSingleWindowRepairGroupsRequest"] = new
                        {
                            type = "object",
                            required = new[] { "groupKeys" },
                            properties = new Dictionary<string, object>
                            {
                                ["groupKeys"] = StringArrayProperty("Issue group keys selected for automatic repair.")
                            }
                        },
                        ["ApiSingleWindowRepairGroupsResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "repairedGroupCount", "review", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["repairedGroupCount"] = new { type = "integer", format = "int32" },
                                ["review"] = RefSchema("SingleWindowExportReview"),
                                ["message"] = StringProperty("Repair result message.")
                            }
                        },
                        ["ApiSingleWindowSubmitPackageRequest"] = new
                        {
                            type = "object",
                            required = new[] { "stationAssignmentCode" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("Optional user-selected .swpkg save path. When omitted or blank, the sidecar writes under the runtime data root SingleWindow/Outbox directory."),
                                ["stationAssignmentCode"] = StringProperty("Sensitive assignment code copied from the exact SQLite card-station operation profile that must receive this package.")
                            }
                        },
                        ["ApiSingleWindowImportPackageRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("User-selected .swpkg file path to import."),
                                ["workingDirectory"] = StringProperty("Optional extraction root. Submit packages default to runtime data root SingleWindow/Inbox; receipt packages default to SingleWindow/ReceiptInbox."),
                                ["keepWorkingDirectory"] = new
                                {
                                    type = "boolean",
                                    description = "Whether to keep the extracted working directory. Submit package imports always keep it for dispatch."
                                }
                            }
                        },
                        ["ApiSingleWindowReceiptPackageExportRequest"] = new
                        {
                            type = "object",
                            required = new[] { "businessType", "receiptFiles" },
                            properties = new Dictionary<string, object>
                            {
                                ["businessType"] = StringProperty("Single Window business type: CustomsCoo/coo or AgentConsignment/acd."),
                                ["batchReference"] = StringProperty("Single Window batch reference to put into the receipt package manifest."),
                                ["invoiceNo"] = StringProperty("Related invoice number to put into the receipt package manifest."),
                                ["receiptFiles"] = StringArrayProperty("User-selected receipt XML file paths to include."),
                                ["packagePath"] = StringProperty("Optional .swpkg save path. When omitted or blank, the sidecar writes under the runtime data root SingleWindow/Outbox directory.")
                            }
                        },
                        ["SingleWindowPackageFile"] = new
                        {
                            type = "object",
                            required = new[] { "relativePath", "mediaType", "description", "sizeBytes", "sha256" },
                            properties = new Dictionary<string, object>
                            {
                                ["relativePath"] = StringProperty("Package-relative file path."),
                                ["mediaType"] = StringProperty("Payload media type."),
                                ["description"] = StringProperty("File description."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["sha256"] = StringProperty("Uppercase SHA-256 digest of the file content.")
                            }
                        },
                        ["SingleWindowPackageManifest"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "schemaVersion",
                                "packageId",
                                "packageType",
                                "businessType",
                                "batchReference",
                                "sourceInvoiceId",
                                "sourceDocumentId",
                                "sourceDocumentType",
                                "submissionVersion",
                                "draftRevision",
                                "sourceBaselineHash",
                                "invoiceNo",
                                "contractNo",
                                "companyScope",
                                "snapshotSha256",
                                "sourcePackageDigest",
                                "receiptReferenceNo",
                                "contentDigest",
                                "stationKey",
                                "cardIdentifier",
                                "clientProfileKey",
                                "clientProfileName",
                                "assignmentNonce",
                                "authenticationAlgorithm",
                                "authenticationTag",
                                "createdAt",
                                "createdOnMachine",
                                "payloadFiles",
                                "attachmentFiles",
                                "warnings"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["schemaVersion"] = StringProperty("Package manifest schema version."),
                                ["packageId"] = StringProperty("Unique package identifier."),
                                ["packageType"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Single Window package type: 0=SubmitPackage, 1=ReceiptPackage.",
                                    @enum = new[] { 0, 1 }
                                },
                                ["businessType"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Single Window business type: 0=CustomsCoo, 1=AgentConsignment.",
                                    @enum = new[] { 0, 1 }
                                },
                                ["batchReference"] = StringProperty("Single Window batch reference."),
                                ["sourceInvoiceId"] = new { type = "integer", format = "int32" },
                                ["sourceDocumentId"] = new { type = "integer", format = "int32" },
                                ["sourceDocumentType"] = StringProperty("Draft document type."),
                                ["submissionVersion"] = new { type = "integer", format = "int32" },
                                ["draftRevision"] = new { type = "integer", format = "int32" },
                                ["sourceBaselineHash"] = StringProperty("Source baseline hash."),
                                ["invoiceNo"] = StringProperty("Source invoice number."),
                                ["contractNo"] = StringProperty("Source contract number."),
                                ["companyScope"] = StringProperty("Company identity bound to the package."),
                                ["snapshotSha256"] = StringProperty("Submit snapshot SHA-256 digest."),
                                ["sourcePackageDigest"] = StringProperty("Original submit package content digest for receipt packages."),
                                ["receiptReferenceNo"] = StringProperty("Official reference number shared by every receipt in a receipt package."),
                                ["contentDigest"] = StringProperty("Canonical manifest content digest."),
                                ["stationKey"] = StringProperty("Pre-assigned card-station key for submit and receipt packages."),
                                ["cardIdentifier"] = StringProperty("Pre-assigned non-secret operation-card identifier."),
                                ["clientProfileKey"] = StringProperty("Pre-assigned stable operation profile key."),
                                ["clientProfileName"] = StringProperty("Pre-assigned operation-card profile name."),
                                ["assignmentNonce"] = StringProperty("Unique one-time assignment nonce."),
                                ["authenticationAlgorithm"] = StringProperty("Package authentication algorithm; currently HMAC-SHA256."),
                                ["authenticationTag"] = StringProperty("HMAC authentication tag bound to package content and the assigned station profile."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["createdOnMachine"] = StringProperty("Machine that created the package."),
                                ["payloadFiles"] = RefArraySchema("SingleWindowPackageFile"),
                                ["attachmentFiles"] = RefArraySchema("SingleWindowPackageFile"),
                                ["warnings"] = StringArrayProperty("Warnings collected during payload generation.")
                            }
                        },
                        ["ApiSingleWindowHandoffPackageResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "packagePath", "manifest", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["packagePath"] = StringProperty("Generated .swpkg file name; server absolute paths are not returned."),
                                ["manifest"] = RefSchema("SingleWindowPackageManifest"),
                                ["trackingBatchId"] = new { type = "integer", format = "int32", nullable = true },
                                ["storagePolicy"] = StringProperty("Submit package storage policy summary."),
                                ["message"] = StringProperty("Export result message.")
                            }
                        },
                        ["SingleWindowReceiptParseResult"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "businessType",
                                "receiptKind",
                                "referenceNo",
                                "receiptCode",
                                "receiptMessage",
                                "businessStatus",
                                "sourceFileName"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["businessType"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Single Window business type: 0=CustomsCoo, 1=AgentConsignment.",
                                    @enum = new[] { 0, 1 }
                                },
                                ["receiptKind"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Receipt kind enum.",
                                    @enum = new[] { 0, 1, 2, 3, 4, 5 }
                                },
                                ["referenceNo"] = StringProperty("External receipt reference number."),
                                ["receiptCode"] = StringProperty("Receipt code."),
                                ["receiptMessage"] = StringProperty("Receipt message."),
                                ["businessStatus"] = new
                                {
                                    type = "integer",
                                    format = "int32",
                                    description = "Receipt business status enum.",
                                    @enum = new[] { 0, 1, 2, 3, 4, 5, 6 }
                                },
                                ["occurredAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["sourceFileName"] = StringProperty("Source receipt file name.")
                            }
                        },
                        ["ApiSingleWindowImportedPackageResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "packagePath",
                                "workingDirectory",
                                "workingDirectoryKept",
                                "manifest",
                                "parsedReceipts",
                                "trackingStatus",
                                "persistedReceiptCount",
                                "storagePolicy",
                                "message"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["packagePath"] = StringProperty("Imported .swpkg file name; server absolute paths are not returned."),
                                ["workingDirectory"] = StringProperty("Reserved for desktop-local display; server paths are not returned."),
                                ["workingDirectoryKept"] = new { type = "boolean" },
                                ["manifest"] = RefSchema("SingleWindowPackageManifest"),
                                ["parsedReceipts"] = RefArraySchema("SingleWindowReceiptParseResult"),
                                ["trackingBatchId"] = new { type = "integer", format = "int32", nullable = true },
                                ["trackingStatus"] = StringProperty("Tracking status after import."),
                                ["persistedReceiptCount"] = new { type = "integer", format = "int32" },
                                ["storagePolicy"] = StringProperty("Import package storage policy summary."),
                                ["message"] = StringProperty("Import result message.")
                            }
                        },
            };
    }
}
