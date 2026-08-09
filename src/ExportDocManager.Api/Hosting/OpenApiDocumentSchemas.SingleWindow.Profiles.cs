namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSingleWindowProfileSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiSingleWindowClientProfileDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "stationKey",
                                "profileKey",
                                "profileName",
                                "companyScope",
                                "cardIdentifier",
                                "stationAssignmentCode",
                                "customsCooClientRootPath",
                                "agentConsignmentClientRootPath",
                                "canSubmitCustomsCoo",
                                "canSubmitAgentConsignment",
                                "isEnabled",
                                "isActive",
                                "updatedAt"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["stationKey"] = StringProperty("Stable identity of this SQLite card station."),
                                ["profileKey"] = StringProperty("Stable operation-card profile key."),
                                ["profileName"] = StringProperty("Client profile name."),
                                ["companyScope"] = StringProperty("Company identity bound to this operation card."),
                                ["cardIdentifier"] = StringProperty("Non-secret card label or identifier."),
                                ["stationAssignmentCode"] = StringProperty("Sensitive assignment code used by the office system to bind and authenticate packages for this exact station profile."),
                                ["customsCooClientRootPath"] = StringProperty("Independent official client root for Customs COO."),
                                ["agentConsignmentClientRootPath"] = StringProperty("Independent official client root for agent consignment."),
                                ["canSubmitCustomsCoo"] = new { type = "boolean" },
                                ["canSubmitAgentConsignment"] = new { type = "boolean" },
                                ["isEnabled"] = new { type = "boolean" },
                                ["isActive"] = new { type = "boolean" },
                                ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiSingleWindowClientProfilesResponse"] = new
                        {
                            type = "object",
                            required = new[] { "profiles", "activeProfileKey", "storagePolicy", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["profiles"] = RefArraySchema("ApiSingleWindowClientProfileDto"),
                                ["activeProfileKey"] = StringProperty("Currently active operation profile key."),
                                ["storagePolicy"] = StringProperty("Client bridge storage policy summary."),
                                ["message"] = StringProperty("Profile operation result message.")
                            }
                        },
                        ["ApiSingleWindowClientProfileSaveRequest"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "profileKey",
                                "profileName",
                                "companyScope",
                                "cardIdentifier",
                                "customsCooClientRootPath",
                                "agentConsignmentClientRootPath",
                                "canSubmitCustomsCoo",
                                "canSubmitAgentConsignment"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["profileKey"] = StringProperty("Existing profile key, or empty when creating a profile."),
                                ["profileName"] = StringProperty("Operation-card profile display name."),
                                ["companyScope"] = StringProperty("Required company identity bound to the card."),
                                ["cardIdentifier"] = StringProperty("Required non-secret card label or identifier."),
                                ["customsCooClientRootPath"] = StringProperty("Independent local official client root for Customs COO."),
                                ["agentConsignmentClientRootPath"] = StringProperty("Independent local official client root for agent consignment."),
                                ["canSubmitCustomsCoo"] = new { type = "boolean" },
                                ["canSubmitAgentConsignment"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSingleWindowClientDispatchRequest"] = new
                        {
                            type = "object",
                            required = new[] { "batchId" },
                            properties = new Dictionary<string, object>
                            {
                                ["batchId"] = new { type = "integer", format = "int32" }
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
                                ["batchId"] = new { type = "integer", format = "int32" }
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
            };
    }
}
