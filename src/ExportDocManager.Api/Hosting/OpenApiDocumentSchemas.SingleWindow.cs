namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSingleWindowSchemas() =>
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
                            required = Array.Empty<string>(),
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("Optional user-selected .swpkg save path. When omitted or blank, the sidecar writes under the runtime data root SingleWindow/Outbox directory.")
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
                            required = new[] { "relativePath", "mediaType", "description" },
                            properties = new Dictionary<string, object>
                            {
                                ["relativePath"] = StringProperty("Package-relative file path."),
                                ["mediaType"] = StringProperty("Payload media type."),
                                ["description"] = StringProperty("File description.")
                            }
                        },
                        ["SingleWindowPackageManifest"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "schemaVersion",
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
                                "createdAt",
                                "createdOnMachine",
                                "payloadFiles",
                                "attachmentFiles",
                                "warnings"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["schemaVersion"] = StringProperty("Package manifest schema version."),
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
                                ["packagePath"] = StringProperty("Resolved .swpkg file path."),
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
                                ["packagePath"] = StringProperty("Imported .swpkg file path."),
                                ["workingDirectory"] = StringProperty("Extraction working directory path."),
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
                        ["ApiSingleWindowClientProfileDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "profileName",
                                "machineName",
                                "importRootPath",
                                "receiptRootPath",
                                "businessDirectoryOverridesJson",
                                "canSubmitCustomsCoo",
                                "canSubmitAgentConsignment",
                                "isEnabled",
                                "updatedAt"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["profileName"] = StringProperty("Client profile name."),
                                ["machineName"] = StringProperty("Machine name."),
                                ["importRootPath"] = StringProperty("Configured import root path."),
                                ["receiptRootPath"] = StringProperty("Configured receipt root path."),
                                ["businessDirectoryOverridesJson"] = StringProperty("Business-specific directory overrides JSON."),
                                ["canSubmitCustomsCoo"] = new { type = "boolean" },
                                ["canSubmitAgentConsignment"] = new { type = "boolean" },
                                ["isEnabled"] = new { type = "boolean" },
                                ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiSingleWindowClientProfileResponse"] = new
                        {
                            type = "object",
                            required = new[] { "profile", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["profile"] = RefSchema("ApiSingleWindowClientProfileDto"),
                                ["storagePolicy"] = StringProperty("Client bridge storage policy summary.")
                            }
                        },
                        ["ApiSingleWindowClientProfileSaveRequest"] = new
                        {
                            type = "object",
                            required = Array.Empty<string>(),
                            properties = new Dictionary<string, object>
                            {
                                ["importRootPath"] = StringProperty("User-selected import root path. If receiptRootPath is omitted, this path is used for both."),
                                ["receiptRootPath"] = StringProperty("User-selected receipt root path. If importRootPath is omitted, this path is used for both."),
                                ["businessType"] = StringProperty("Optional business type for a business-specific directory override: CustomsCoo/coo or AgentConsignment/acd.")
                            }
                        },
                        ["ApiSingleWindowClientProfileSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "profile", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["profile"] = RefSchema("ApiSingleWindowClientProfileDto"),
                                ["storagePolicy"] = StringProperty("Client bridge storage policy summary."),
                                ["message"] = StringProperty("Save result message.")
                            }
                        },
                        ["ApiSingleWindowClientDispatchRequest"] = new
                        {
                            type = "object",
                            required = new[] { "batchId" },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["importRootPath"] = StringProperty("Optional import root path. When omitted, the saved configured directory is used."),
                                ["profileName"] = StringProperty("Optional client profile name to record on the batch.")
                            }
                        },
                        ["SingleWindowClientDispatchResult"] = new
                        {
                            type = "object",
                            required = new[] { "batchId", "batchReference", "targetDirectory", "profileName", "payloadFileCount", "attachmentFileCount" },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["batchReference"] = StringProperty("Single Window batch reference."),
                                ["targetDirectory"] = StringProperty("Client OutBox directory where payload files were copied."),
                                ["profileName"] = StringProperty("Client profile name recorded on the batch."),
                                ["payloadFileCount"] = new { type = "integer", format = "int32" },
                                ["attachmentFileCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiSingleWindowReceiptCollectionRequest"] = new
                        {
                            type = "object",
                            required = new[] { "batchId" },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["receiptRootPath"] = StringProperty("Optional receipt root path. When omitted, the saved configured directory is used.")
                            }
                        },
                        ["SingleWindowReceiptCollectionResult"] = new
                        {
                            type = "object",
                            required = new[] { "batchId", "batchReference", "receiptRootPath", "receiptFiles" },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["batchReference"] = StringProperty("Single Window batch reference."),
                                ["receiptRootPath"] = StringProperty("Receipt root path that was scanned."),
                                ["receiptFiles"] = StringArrayProperty("Matched receipt files.")
                            }
                        },
                        ["SingleWindowReferenceCountryEntry"] = new
                        {
                            type = "object",
                            required = new[] { "code", "englishName", "chineseName", "aliases" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = StringProperty("Country code."),
                                ["englishName"] = StringProperty("English country name."),
                                ["chineseName"] = StringProperty("Chinese country name."),
                                ["aliases"] = StringArrayProperty("Additional aliases.")
                            }
                        },
                        ["SingleWindowReferenceAcdCountryEntry"] = new
                        {
                            type = "object",
                            required = new[] { "code", "chineseName", "englishName", "aliases" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = StringProperty("ACD country code."),
                                ["chineseName"] = StringProperty("Chinese country name."),
                                ["englishName"] = StringProperty("English country name."),
                                ["aliases"] = StringArrayProperty("Additional aliases.")
                            }
                        },
                        ["SingleWindowReferenceCurrencyEntry"] = new
                        {
                            type = "object",
                            required = new[] { "code", "acdCode", "alphaCode", "aliases" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = StringProperty("Currency numeric code."),
                                ["acdCode"] = StringProperty("ACD currency code."),
                                ["alphaCode"] = StringProperty("Currency alpha code."),
                                ["aliases"] = StringArrayProperty("Additional aliases.")
                            }
                        },
                        ["SingleWindowReferenceAcdTradeModeEntry"] = new
                        {
                            type = "object",
                            required = new[] { "code", "name", "description", "aliases" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = StringProperty("ACD trade mode code."),
                                ["name"] = StringProperty("Trade mode name."),
                                ["description"] = StringProperty("Trade mode description."),
                                ["aliases"] = StringArrayProperty("Additional aliases.")
                            }
                        },
                        ["SingleWindowReferenceTransportModeEntry"] = new
                        {
                            type = "object",
                            required = new[] { "value", "aliases" },
                            properties = new Dictionary<string, object>
                            {
                                ["value"] = StringProperty("Transport mode value."),
                                ["aliases"] = StringArrayProperty("Additional aliases.")
                            }
                        },
                        ["SingleWindowReferencePortEntry"] = new
                        {
                            type = "object",
                            required = new[] { "value", "aliases" },
                            properties = new Dictionary<string, object>
                            {
                                ["value"] = StringProperty("Port value."),
                                ["aliases"] = StringArrayProperty("Additional aliases.")
                            }
                        },
                        ["SingleWindowReferenceCatalogModel"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "countries",
                                "acdCountries",
                                "currencies",
                                "acdTradeModes",
                                "transportModes",
                                "ports"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["countries"] = RefArraySchema("SingleWindowReferenceCountryEntry"),
                                ["acdCountries"] = RefArraySchema("SingleWindowReferenceAcdCountryEntry"),
                                ["currencies"] = RefArraySchema("SingleWindowReferenceCurrencyEntry"),
                                ["acdTradeModes"] = RefArraySchema("SingleWindowReferenceAcdTradeModeEntry"),
                                ["transportModes"] = RefArraySchema("SingleWindowReferenceTransportModeEntry"),
                                ["ports"] = RefArraySchema("SingleWindowReferencePortEntry")
                            }
                        },
                        ["ApiSingleWindowReferenceCatalogResponse"] = new
                        {
                            type = "object",
                            required = new[] { "catalog", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["catalog"] = RefSchema("SingleWindowReferenceCatalogModel"),
                                ["storagePolicy"] = StringProperty("Reference catalog storage policy summary.")
                            }
                        },
                        ["ApiSingleWindowReferenceCatalogSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "catalog" },
                            properties = new Dictionary<string, object>
                            {
                                ["catalog"] = RefSchema("SingleWindowReferenceCatalogModel")
                            }
                        },
                        ["ApiSingleWindowReferenceCatalogSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "catalog", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["catalog"] = RefSchema("SingleWindowReferenceCatalogModel"),
                                ["message"] = StringProperty("Save or reset result message."),
                                ["storagePolicy"] = StringProperty("Reference catalog storage policy summary.")
                            }
                        },
                        ["ApiSingleWindowIssuingAuthorityOptionDto"] = new
                        {
                            type = "object",
                            required = new[] { "code", "label", "applicationAddress" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = StringProperty("Four digit issuing authority code."),
                                ["label"] = StringProperty("Display label such as code plus authority name."),
                                ["applicationAddress"] = StringProperty("Default application address for the authority.")
                            }
                        },
                        ["ApiSingleWindowIssuingAuthorityCatalogResponse"] = new
                        {
                            type = "object",
                            required = new[] { "options", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["options"] = RefArraySchema("ApiSingleWindowIssuingAuthorityOptionDto"),
                                ["storagePolicy"] = StringProperty("Issuing authority catalog storage policy summary.")
                            }
                        },
                        ["ApiCustomsCooOptionDto"] = new
                        {
                            type = "object",
                            required = new[] { "value", "label" },
                            properties = new Dictionary<string, object>
                            {
                                ["value"] = StringProperty("Option value saved to the COO draft."),
                                ["label"] = StringProperty("Display label shown to the operator.")
                            }
                        },
                        ["ApiCustomsCooOriginCriteriaOptionSetDto"] = new
                        {
                            type = "object",
                            required = new[] { "certType", "originCriteria", "options" },
                            properties = new Dictionary<string, object>
                            {
                                ["certType"] = StringProperty("COO certificate type code."),
                                ["originCriteria"] = StringProperty("Origin criteria value for sub-option sets; empty for top-level criteria sets."),
                                ["options"] = RefArraySchema("ApiCustomsCooOptionDto")
                            }
                        },
                        ["ApiCustomsCooEditorOptionsResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "applyTypeOptions",
                                "certStatusOptions",
                                "certTypeOptions",
                                "producerSecretOptions",
                                "exhibitFlagOptions",
                                "thirdPartyInvoiceOptions",
                                "predictFlagOptions",
                                "promiseOptions",
                                "currencyOptions",
                                "cooTradeModeOptions",
                                "goodsItemFlagOptions",
                                "packTypeOptions",
                                "goodsTaxRateOptions",
                                "packUnitOptions",
                                "originCriteriaOptionSets",
                                "originCriteriaSubOptionSets",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["applyTypeOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["certStatusOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["certTypeOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["producerSecretOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["exhibitFlagOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["thirdPartyInvoiceOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["predictFlagOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["promiseOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["currencyOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["cooTradeModeOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["goodsItemFlagOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["packTypeOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["goodsTaxRateOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["packUnitOptions"] = RefArraySchema("ApiCustomsCooOptionDto"),
                                ["originCriteriaOptionSets"] = RefArraySchema("ApiCustomsCooOriginCriteriaOptionSetDto"),
                                ["originCriteriaSubOptionSets"] = RefArraySchema("ApiCustomsCooOriginCriteriaOptionSetDto"),
                                ["storagePolicy"] = StringProperty("Customs COO editor option storage policy summary.")
                            }
                        },
                        ["ApiSingleWindowReferenceCatalogExcelColumnMappingDto"] = new
                        {
                            type = "object",
                            required = new[] { "fieldKey", "label", "columnNumber", "required" },
                            properties = new Dictionary<string, object>
                            {
                                ["fieldKey"] = StringProperty("Reference catalog field key."),
                                ["label"] = StringProperty("Field label displayed to the user."),
                                ["columnNumber"] = new { type = "integer", format = "int32" },
                                ["required"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSingleWindowReferenceCatalogExcelImportPreviewResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "catalogKey",
                                "sheetName",
                                "sheetNames",
                                "headerRowNumber",
                                "dataStartRowNumber",
                                "columnMappings",
                                "catalog",
                                "rowCount",
                                "message",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["catalogKey"] = StringProperty("Reference catalog page key."),
                                ["sheetName"] = StringProperty("Worksheet used for the preview."),
                                ["sheetNames"] = StringArrayProperty("Available worksheet names."),
                                ["headerRowNumber"] = new { type = "integer", format = "int32" },
                                ["dataStartRowNumber"] = new { type = "integer", format = "int32" },
                                ["columnMappings"] = RefArraySchema("ApiSingleWindowReferenceCatalogExcelColumnMappingDto"),
                                ["catalog"] = RefSchema("SingleWindowReferenceCatalogModel"),
                                ["rowCount"] = new { type = "integer", format = "int32" },
                                ["message"] = StringProperty("Import preview message."),
                                ["storagePolicy"] = StringProperty("Reference catalog Excel import storage policy summary.")
                            }
                        },
                        ["SingleWindowOperationTicketRow"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "ticketId",
                                "businessType",
                                "sourceInvoiceId",
                                "documentId",
                                "status",
                                "priority",
                                "requestedAt"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["ticketId"] = new { type = "integer", format = "int32" },
                                ["businessType"] = StringProperty("Single Window business type."),
                                ["sourceInvoiceId"] = new { type = "integer", format = "int32" },
                                ["documentId"] = new { type = "integer", format = "int32" },
                                ["batchId"] = new { type = "integer", format = "int32", nullable = true },
                                ["status"] = StringProperty("Collaboration ticket status."),
                                ["requestedBy"] = StringProperty("Requester."),
                                ["assignedOperator"] = StringProperty("Assigned operator."),
                                ["assignedWorkstationId"] = new { type = "integer", format = "int32", nullable = true },
                                ["priority"] = new { type = "integer", format = "int32" },
                                ["requestedAt"] = new { type = "string", format = "date-time" },
                                ["assignedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["submittedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["completedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["lastError"] = StringProperty("Last submission error.")
                            }
                        },
                        ["SingleWindowWorkstationRow"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "workstationId",
                                "machineName",
                                "operatorName",
                                "canSubmitAgentConsignment",
                                "canSubmitCustomsCoo",
                                "isEnabled",
                                "updatedAt"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["workstationId"] = new { type = "integer", format = "int32" },
                                ["machineName"] = StringProperty("Machine name."),
                                ["profileId"] = new { type = "integer", format = "int32", nullable = true },
                                ["operatorName"] = StringProperty("Current operator name."),
                                ["canSubmitAgentConsignment"] = new { type = "boolean" },
                                ["canSubmitCustomsCoo"] = new { type = "boolean" },
                                ["isEnabled"] = new { type = "boolean" },
                                ["remarks"] = StringProperty("Workstation remarks."),
                                ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["SingleWindowCollaborationPageResult"] = new
                        {
                            type = "object",
                            required = new[] { "tickets", "workstations", "totalTicketCount", "pageNumber", "pageSize" },
                            properties = new Dictionary<string, object>
                            {
                                ["tickets"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationTicketRow")
                                },
                                ["workstations"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowWorkstationRow")
                                },
                                ["totalTicketCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowOperationCenterRow"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "batchId",
                                "batchReference",
                                "submissionVersion",
                                "draftRevision",
                                "businessType",
                                "invoiceNo",
                                "contractNo",
                                "status",
                                "createdAt",
                                "updatedAt",
                                "receiptCount"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["batchReference"] = StringProperty("Single Window submission batch reference."),
                                ["submissionVersion"] = new { type = "integer", format = "int32" },
                                ["draftRevision"] = new { type = "integer", format = "int32" },
                                ["businessType"] = StringProperty("Single Window business type."),
                                ["invoiceNo"] = StringProperty("Source invoice number."),
                                ["contractNo"] = StringProperty("Source contract number."),
                                ["status"] = StringProperty("Submission batch status."),
                                ["referenceNo"] = StringProperty("External Single Window reference number."),
                                ["lastReceiptCode"] = StringProperty("Last receipt code."),
                                ["lastReceiptMessage"] = StringProperty("Last receipt message."),
                                ["createdOnMachine"] = StringProperty("Machine that created the batch."),
                                ["submitPackagePath"] = StringProperty("Submit package path as recorded in the database."),
                                ["clientProfileName"] = StringProperty("Single Window client profile name."),
                                ["clientDispatchPath"] = StringProperty("Client dispatch path as recorded in the database."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["receiptCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowOperationCenterPageResult"] = new
                        {
                            type = "object",
                            required = new[] { "rows", "totalCount", "pageNumber", "pageSize", "totalPages" },
                            properties = new Dictionary<string, object>
                            {
                                ["rows"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationCenterRow")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["SingleWindowOperationCenterPackageRecord"] = new
                        {
                            type = "object",
                            required = new[] { "packageType", "direction", "filePath", "createdAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["packageType"] = StringProperty("Package type."),
                                ["direction"] = StringProperty("Package direction."),
                                ["filePath"] = StringProperty("Package file path as recorded in the database."),
                                ["createdOnMachine"] = StringProperty("Machine that created the package record."),
                                ["payloadFileCount"] = new { type = "integer", format = "int32" },
                                ["attachmentFileCount"] = new { type = "integer", format = "int32" },
                                ["warningCount"] = new { type = "integer", format = "int32" },
                                ["createdAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["SingleWindowOperationCenterReceiptRecord"] = new
                        {
                            type = "object",
                            required = new[] { "receiptKind", "businessStatus", "sourceFileName", "importedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["receiptKind"] = StringProperty("Receipt kind."),
                                ["referenceNo"] = StringProperty("External Single Window reference number."),
                                ["receiptCode"] = StringProperty("Receipt code."),
                                ["receiptMessage"] = StringProperty("Receipt message."),
                                ["businessStatus"] = StringProperty("Receipt business status."),
                                ["sourceFileName"] = StringProperty("Source receipt file name."),
                                ["importedAt"] = new { type = "string", format = "date-time" },
                                ["occurredAt"] = new { type = "string", format = "date-time", nullable = true }
                            }
                        },
                        ["SingleWindowOperationCenterDetail"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "batchId",
                                "batchReference",
                                "submissionVersion",
                                "draftRevision",
                                "businessType",
                                "invoiceNo",
                                "contractNo",
                                "status",
                                "createdAt",
                                "updatedAt",
                                "packageRecords",
                                "receiptRecords"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" },
                                ["batchReference"] = StringProperty("Single Window submission batch reference."),
                                ["submissionVersion"] = new { type = "integer", format = "int32" },
                                ["draftRevision"] = new { type = "integer", format = "int32" },
                                ["businessType"] = StringProperty("Single Window business type."),
                                ["invoiceNo"] = StringProperty("Source invoice number."),
                                ["contractNo"] = StringProperty("Source contract number."),
                                ["status"] = StringProperty("Submission batch status."),
                                ["referenceNo"] = StringProperty("External Single Window reference number."),
                                ["submitPackagePath"] = StringProperty("Submit package path as recorded in the database."),
                                ["lastReceiptPackagePath"] = StringProperty("Last receipt package path as recorded in the database."),
                                ["workingDirectoryPath"] = StringProperty("Working directory path as recorded in the database."),
                                ["clientProfileName"] = StringProperty("Single Window client profile name."),
                                ["clientDispatchPath"] = StringProperty("Client dispatch path as recorded in the database."),
                                ["createdOnMachine"] = StringProperty("Machine that created the batch."),
                                ["payloadFileCount"] = new { type = "integer", format = "int32" },
                                ["attachmentFileCount"] = new { type = "integer", format = "int32" },
                                ["warningCount"] = new { type = "integer", format = "int32" },
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["lastReceiptAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["lastClientDispatchAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["packageRecords"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationCenterPackageRecord")
                                },
                                ["receiptRecords"] = new
                                {
                                    type = "array",
                                    items = RefSchema("SingleWindowOperationCenterReceiptRecord")
                                }
                            }
                        },
            };
    }
}