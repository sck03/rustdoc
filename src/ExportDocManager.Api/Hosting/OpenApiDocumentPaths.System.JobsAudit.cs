namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateJobsAuditSystemPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/jobs"] = new
                    {
                        get = new
                        {
                            summary = "List background jobs",
                            operationId = "listJobs",
                            parameters = new object[]
                            {
                                QueryParameter("status", "string", null, "Optional job status filter."),
                                QueryParameter("keyword", "string", null, "Keyword for job id, title, kind, status text, detail, output path, or error."),
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size capped by the job store.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged background job list for the authenticated user.",
                                    content = JsonContent("ApiPagedResponseOfBackgroundJobSnapshot")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/jobs/{jobId}"] = new
                    {
                        get = new
                        {
                            summary = "Get background job",
                            operationId = "getJob",
                            parameters = new object[]
                            {
                                PathParameter("jobId", "string", null, "Background job id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Background job detail.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid job id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Background job not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete finished background job",
                            operationId = "deleteJob",
                            parameters = new object[]
                            {
                                PathParameter("jobId", "string", null, "Background job id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Finished background job history was deleted.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid job id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "The job is still active or does not exist." }
                            }
                        }
                    },
                    ["/api/jobs/finished"] = new
                    {
                        delete = new
                        {
                            summary = "Clear finished background jobs",
                            operationId = "clearFinishedJobs",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Finished background job history was cleared.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/jobs/{jobId}/cancel"] = new
                    {
                        post = new
                        {
                            summary = "Cancel background job",
                            operationId = "cancelJob",
                            parameters = new object[]
                            {
                                PathParameter("jobId", "string", null, "Background job id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Cancellation was requested.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid job id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "The job cannot be canceled." }
                            }
                        }
                    },
                    ["/api/jobs/{jobId}/retry"] = new
                    {
                        post = new
                        {
                            summary = "Retry background job",
                            operationId = "retryJob",
                            parameters = new object[]
                            {
                                PathParameter("jobId", "string", null, "Background job id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "A new background job was accepted from the retry descriptor.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Invalid job id or retry request values." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Background job or referenced source record was not found." },
                                ["409"] = new { description = "The job cannot be retried." }
                            }
                        }
                    },
                    ["/api/custom-options/{optionType}"] = new
                    {
                        get = new
                        {
                            summary = "List custom form options",
                            operationId = "listCustomOptions",
                            parameters = new object[]
                            {
                                PathParameter("optionType", "string", null, "Legacy EditableComboBox option type.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Predefined and user-saved options for a form field. Values are read from built-in constants and the runtime data root database CustomOptions table.",
                                    content = JsonContent("ApiCustomOptionListResponse")
                                },
                                ["400"] = new { description = "Unsupported option type." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Save a custom form option",
                            operationId = "saveCustomOption",
                            parameters = new object[]
                            {
                                PathParameter("optionType", "string", null, "Legacy EditableComboBox option type.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiCustomOptionSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Updated predefined and user-saved options. The sidecar writes only the runtime data root database CustomOptions table.",
                                    content = JsonContent("ApiCustomOptionListResponse")
                                },
                                ["400"] = new { description = "Unsupported option type or blank option value." },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/audit-logs"] = new
                    {
                        get = new
                        {
                            summary = "List audit logs",
                            operationId = "listAuditLogs",
                            parameters = new object[]
                            {
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size. The repository caps this to the shared maximum."),
                                QueryParameter("invoiceKeyword", "string", null, "Invoice-related keyword."),
                                QueryParameter("entityName", "string", null, "Entity name filter."),
                                QueryParameter("action", "string", null, "Audit action filter."),
                                QueryParameter("userId", "string", null, "Operator keyword."),
                                QueryParameter("startTime", "string", "date-time", "Inclusive start timestamp."),
                                QueryParameter("endTime", "string", "date-time", "Inclusive end timestamp."),
                                QueryParameter("keyword", "string", null, "Keyword for entity, entity id, user, old values, or new values.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged audit log list for the authenticated local user.",
                                    content = JsonContent("ApiPagedResponseOfApiAuditLogDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/audit-logs/save-to-path"] = new
                    {
                        post = new
                        {
                            summary = "Save audit logs to a Tauri-selected path",
                            operationId = "saveAuditLogsToPath",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiAuditLogPathExportRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Audit logs exported to the user-selected .xlsx path.",
                                    content = JsonContent("ApiAuditLogCommandResponse")
                                },
                                ["400"] = new { description = "Invalid export request or destination path." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only administrators can export full audit logs from a trusted Tauri desktop path." }
                            }
                        }
                    },
                    ["/api/jobs/{jobId}/download"] = new
                    {
                        get = new
                        {
                            summary = "Download completed browser export job result",
                            operationId = "downloadJobResult",
                            parameters = new object[]
                            {
                                PathParameter("jobId", "string", null, "Background job id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Completed job output attachment from the controlled runtime export directory.",
                                    content = BinaryContent()
                                },
                                ["400"] = new { description = "Invalid job id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "The job result is unavailable or is not a controlled browser download." },
                                ["426"] = new { description = "Sensitive migration or support output requires HTTPS or an explicitly trusted HTTP deployment." }
                            }
                        }
                    },
                    ["/api/jobs/{jobId}/download-ticket"] = new
                    {
                        post = new
                        {
                            summary = "Create a short-lived native download URL for a completed job",
                            operationId = "createJobDownloadTicket",
                            parameters = new object[]
                            {
                                PathParameter("jobId", "string", null, "Background job id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Short-lived download URL.", content = JsonContent("ApiDownloadTicket") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "The job result is unavailable or unauthorized." },
                                ["426"] = new { description = "Sensitive migration or support output requires HTTPS or an explicitly trusted HTTP deployment." }
                            }
                        }
                    },
                    ["/api/audit-logs/download"] = new
                    {
                        post = new
                        {
                            summary = "Download audit logs as an Excel file",
                            operationId = "downloadAuditLogs",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiAuditLogFilterRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Audit logs returned as a browser download.",
                                    content = new Dictionary<string, object>
                                    {
                                        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new
                                        {
                                            schema = new { type = "string", format = "binary" }
                                        }
                                    }
                                },
                                ["400"] = new { description = "Invalid download request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only administrators can download audit logs." }
                            }
                        }
                    },
                    ["/api/audit-logs/delete"] = new
                    {
                        post = new
                        {
                            summary = "Delete audit logs by criteria",
                            operationId = "deleteAuditLogsByCriteria",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiAuditLogDeleteRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Audit logs matching the supplied criteria were deleted.",
                                    content = JsonContent("ApiAuditLogCommandResponse")
                                },
                                ["400"] = new { description = "Missing explicit confirmation, filter criteria, or invalid request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only administrators can delete audit logs." }
                            }
                        }
                    },
                    ["/api/audit-logs/cleanup"] = new
                    {
                        post = new
                        {
                            summary = "Cleanup old audit logs",
                            operationId = "cleanupAuditLogs",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiAuditLogCleanupRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Audit logs older than the retention window were deleted.",
                                    content = JsonContent("ApiAuditLogCommandResponse")
                                },
                                ["400"] = new { description = "Invalid retention or missing explicit confirmation." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only administrators can cleanup audit logs." }
                            }
                        }
                    },
            };
    }
}
