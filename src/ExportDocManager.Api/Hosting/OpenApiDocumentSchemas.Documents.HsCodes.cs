using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateHsCodeDocumentSchemas() =>
            new Dictionary<string, object>
            {
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
                             required = new[] { "items", "totalCount", "pageNumber", "pageSize", "isTruncated", "scannedSourceCount", "notice" },
                             properties = new Dictionary<string, object>
                             {
                                 ["items"] = new { type = "array", items = new Dictionary<string, string> { ["$ref"] = "#/components/schemas/HsCodeHistoryLearningCandidate" } },
                                 ["totalCount"] = new { type = "integer", format = "int32" },
                                 ["pageNumber"] = new { type = "integer", format = "int32" },
                                 ["pageSize"] = new { type = "integer", format = "int32" },
                                 ["isTruncated"] = new { type = "boolean", description = "Whether the bounded source window contained more records." },
                                 ["scannedSourceCount"] = new { type = "integer", format = "int32", description = "Number of source rows analyzed for this response." },
                                 ["notice"] = StringProperty("Human-readable explanation when the source window is bounded.")
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
                        ["ApiHsCodeRemoteSearchRequest"] = new
                        {
                            type = "object",
                            required = new[] { "keyword" },
                            properties = new Dictionary<string, object>
                            {
                                ["keyword"] = StringProperty("HS code, code prefix, product name, or search keyword.")
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
