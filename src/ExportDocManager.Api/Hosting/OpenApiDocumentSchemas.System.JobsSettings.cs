namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateJobsSettingsSystemSchemas() =>
            new Dictionary<string, object>
            {
                        ["BackgroundJobSnapshot"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "jobId",
                                "kind",
                                "title",
                                "status",
                                "statusText",
                                "detailText",
                                "requestedBy",
                                "createdAt",
                                "updatedAt",
                                "outputPath",
                                "errorMessage",
                                "canCancel",
                                "canRetry",
                                "retryOperation",
                                "retryRequestJson"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["jobId"] = StringProperty("Background job id."),
                                ["kind"] = StringProperty("Job kind, for example ReportPdf, Ocr, Import, or Export."),
                                ["title"] = StringProperty("User-facing job title."),
                                ["status"] = StringProperty("Queued, Running, Succeeded, Failed, Canceling, or Canceled."),
                                ["progressPercent"] = new { type = "integer", format = "int32", nullable = true },
                                ["statusText"] = StringProperty("Short status text."),
                                ["detailText"] = StringProperty("Detailed status text."),
                                ["requestedBy"] = StringProperty("Username that requested the job."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["startedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["completedAt"] = new { type = "string", format = "date-time", nullable = true },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["outputPath"] = StringProperty("Optional output path for desktop/local jobs."),
                                ["errorMessage"] = StringProperty("Failure message if the job failed."),
                                ["canCancel"] = new { type = "boolean" },
                                ["canRetry"] = new { type = "boolean" },
                                ["retryOperation"] = StringProperty("OpenAPI operation id that can recreate the job when retry is supported."),
                                ["retryRequestJson"] = StringProperty("Normalized retry request JSON. It records explicit user paths and request values only; no default output path is synthesized.")
                            }
                        },
                        ["ApiPagedResponseOfBackgroundJobSnapshot"] = new
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
                                    items = RefSchema("BackgroundJobSnapshot")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSettingsSecretsDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "emailPasswordSet",
                                "webDavPasswordSet",
                                "postgreSqlPasswordSet",
                                "aiApiKeySet"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["emailPasswordSet"] = new { type = "boolean" },
                                ["webDavPasswordSet"] = new { type = "boolean" },
                                ["postgreSqlPasswordSet"] = new { type = "boolean" },
                                ["aiApiKeySet"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSettingsResponse"] = new
                        {
                            type = "object",
                            required = new[] { "settings", "secrets", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["settings"] = new
                                {
                                    type = "object",
                                    description = "AppSettings object. Secret string values are redacted in responses."
                                },
                                ["secrets"] = RefSchema("ApiSettingsSecretsDto"),
                                ["storagePolicy"] = StringProperty("Settings storage policy summary.")
                            }
                        },
                        ["ApiSettingsSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "settings" },
                            properties = new Dictionary<string, object>
                            {
                                ["settings"] = new
                                {
                                    type = "object",
                                    description = "AppSettings object to save."
                                },
                                ["updateSecrets"] = new
                                {
                                    type = "boolean",
                                    description = "When false, existing password/API key values are preserved."
                                }
                            }
                        },
                        ["ApiSettingsValidationRequest"] = new
                        {
                            type = "object",
                            required = new[] { "settings" },
                            properties = new Dictionary<string, object>
                            {
                                ["settings"] = new
                                {
                                    type = "object",
                                    additionalProperties = true,
                                    description = "AppSettings draft to validate. The sidecar does not persist this object."
                                },
                                ["updateSecrets"] = new
                                {
                                    type = "boolean",
                                    description = "When false, existing password/API key values are preserved during normalization and redacted in the response."
                                }
                            }
                        },
                        ["ApiSettingsValidationMessageDto"] = new
                        {
                            type = "object",
                            required = new[] { "level", "propertyName", "message", "isAutoFixable" },
                            properties = new Dictionary<string, object>
                            {
                                ["level"] = StringProperty("Validation level: info, warning, or error."),
                                ["propertyName"] = StringProperty("Dot-separated settings property path."),
                                ["message"] = StringProperty("Human-readable validation message."),
                                ["isAutoFixable"] = new { type = "boolean" }
                            }
                        },
                        ["ApiSettingsValidationResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "isValid",
                                "hasWarnings",
                                "canAutoFix",
                                "messages",
                                "normalizedSettings",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["isValid"] = new { type = "boolean" },
                                ["hasWarnings"] = new { type = "boolean" },
                                ["canAutoFix"] = new { type = "boolean" },
                                ["messages"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiSettingsValidationMessageDto")
                                },
                                ["normalizedSettings"] = new
                                {
                                    type = "object",
                                    additionalProperties = true,
                                    description = "Sanitized AppSettings draft after normalization. Secret string values are redacted."
                                },
                                ["storagePolicy"] = StringProperty("Settings validation storage policy.")
                            }
                        },
                        ["ApiSettingsSaveResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "requiresRestart",
                                "settings",
                                "secrets",
                                "message"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["requiresRestart"] = new { type = "boolean" },
                                ["settings"] = new
                                {
                                    type = "object",
                                    description = "Saved AppSettings object. Secret string values are redacted in responses."
                                },
                                ["secrets"] = RefSchema("ApiSettingsSecretsDto"),
                                ["message"] = StringProperty("Save result message.")
                            }
                        },
                        ["ApiAuditLogDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "entityName", "action", "entityId", "timestamp" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["entityName"] = StringProperty("Audited entity name."),
                                ["action"] = StringProperty("Audit action."),
                                ["entityId"] = StringProperty("Audited entity id."),
                                ["oldValues"] = StringProperty("Old values JSON."),
                                ["newValues"] = StringProperty("New values JSON."),
                                ["userId"] = StringProperty("Operator id or username."),
                                ["timestamp"] = new { type = "string", format = "date-time" },
                                ["oldValuesPreview"] = StringProperty("Compact old values preview."),
                                ["newValuesPreview"] = StringProperty("Compact new values preview.")
                            }
                        },
                        ["ApiPagedResponseOfApiAuditLogDto"] = new
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
                                    items = RefSchema("ApiAuditLogDto")
                                },
                                ["totalCount"] = new { type = "integer", format = "int32" },
                                ["pageNumber"] = new { type = "integer", format = "int32" },
                                ["pageSize"] = new { type = "integer", format = "int32" },
                                ["totalPages"] = new { type = "integer", format = "int32" },
                                ["hasPreviousPage"] = new { type = "boolean" },
                                ["hasNextPage"] = new { type = "boolean" }
                            }
                        },
                        ["ApiAuditLogFilterRequest"] = new
                        {
                            type = "object",
                            properties = AuditLogFilterProperties()
                        },
                        ["ApiAuditLogDeleteRequest"] = new
                        {
                            type = "object",
                            required = new[] { "confirmed" },
                            properties = MergeProperties(
                                AuditLogFilterProperties(),
                                new Dictionary<string, object>
                                {
                                    ["confirmed"] = new { type = "boolean", description = "Explicit confirmation from the filtered-result deletion dialog." }
                                })
                        },
                        ["ApiAuditLogCleanupRequest"] = new
                        {
                            type = "object",
                            required = new[] { "daysToKeep", "confirmed" },
                            properties = new Dictionary<string, object>
                            {
                                ["daysToKeep"] = new { type = "integer", format = "int32" },
                                ["maxCount"] = new { type = "integer", format = "int32" },
                                ["confirmed"] = new { type = "boolean", description = "Explicit confirmation from the retention cleanup dialog." }
                            }
                        },
                        ["ApiDownloadTicket"] = new
                        {
                            type = "object",
                            required = new[] { "token", "downloadUrl", "expiresAtUtc" },
                            properties = new Dictionary<string, object>
                            {
                                ["token"] = StringProperty("Random short-lived download token."),
                                ["downloadUrl"] = StringProperty("Same-origin native streaming download URL."),
                                ["expiresAtUtc"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiAuditLogCommandResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "affectedCount", "destinationPath", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("User-facing result message."),
                                ["affectedCount"] = new { type = "integer", format = "int32" },
                                ["destinationPath"] = StringProperty("Normalized user-selected export path, when an export was requested."),
                                ["storagePolicy"] = StringProperty("Runtime path policy for audit log management.")
                            }
                        },
                        ["ApiHealthResponse"] = new
                        {
                            type = "object",
                            required = new[] { "status", "checkedAt", "productVersion", "informationalVersion", "appRoot", "dataRoot", "databaseRoot", "runtimePaths", "runtimeDependencies" },
                            properties = new Dictionary<string, object>
                            {
                                ["status"] = StringProperty("Sidecar status."),
                                ["checkedAt"] = new { type = "string", format = "date-time" },
                                ["productVersion"] = StringProperty("Product semantic version."),
                                ["informationalVersion"] = StringProperty("Assembly informational version."),
                                ["appRoot"] = StringProperty("Program runtime directory; empty in the public lightweight response."),
                                ["dataRoot"] = StringProperty("Business writable data root; empty in the public lightweight response."),
                                ["databaseRoot"] = StringProperty("Database directory under the writable data root; empty in the public lightweight response."),
                                ["singleWindowRoot"] = StringProperty("Single Window writable data directory; empty in the public lightweight response."),
                                ["templateRoot"] = StringProperty("Bundled template directory under the program root; empty in the public lightweight response."),
                                ["ocrModelRoot"] = StringProperty("Bundled OCR model directory under the program root; empty in the public lightweight response."),
                                ["logRoot"] = StringProperty("Log directory under the writable data root; empty in the public lightweight response."),
                                ["databaseProvider"] = StringProperty("Current database mode."),
                                ["sqliteDatabasePath"] = StringProperty("Resolved SQLite database path for authorized diagnostics; empty in the public lightweight response."),
                                ["runtimePaths"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiRuntimePathInfo")
                                },
                                ["runtimeDependencies"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiRuntimeDependencyInfo")
                                },
                                ["storagePolicy"] = StringProperty("Public probe boundary or authorized runtime storage policy summary.")
                            }
                        },
                        ["ApiRuntimePathInfo"] = new
                        {
                            type = "object",
                            required = new[] { "key", "label", "path", "storageClass", "accessMode", "requirement", "exists", "description" },
                            properties = new Dictionary<string, object>
                            {
                                ["key"] = StringProperty("Stable runtime path identifier."),
                                ["label"] = StringProperty("User-facing path label."),
                                ["path"] = StringProperty("Resolved absolute path."),
                                ["storageClass"] = StringProperty("Path class such as program-resource, runtime-data, or database-file."),
                                ["accessMode"] = StringProperty("Expected access policy such as read-only, managed, or read-write."),
                                ["requirement"] = StringProperty("Runtime readiness class: core, feature, or optional."),
                                ["exists"] = new { type = "boolean" },
                                ["description"] = StringProperty("Short purpose and storage explanation.")
                            }
                        },
                        ["ApiRuntimeDependencyInfo"] = new
                        {
                            type = "object",
                            required = new[] { "key", "label", "requirement", "status", "ready", "resolvedPath", "message" },
                            properties = new Dictionary<string, object>
                            {
                                ["key"] = StringProperty("Stable runtime dependency identifier."),
                                ["label"] = StringProperty("User-facing dependency label."),
                                ["requirement"] = StringProperty("Dependency class: core, feature, or optional."),
                                ["status"] = StringProperty("Readiness state such as ready, missing, incomplete, disabled, or unsupported."),
                                ["ready"] = new { type = "boolean" },
                                ["resolvedPath"] = StringProperty("Resolved executable, model, or expected dependency path."),
                                ["message"] = StringProperty("User-facing readiness explanation.")
                            }
                        }
            };
    }
}
