namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSystemPaths() =>
            new Dictionary<string, object>
            {
                    ["/healthz"] = new
                    {
                        get = new
                        {
                            summary = "Health check",
                            operationId = "getHealth",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The API sidecar is running and runtime paths are available.",
                                    content = JsonContent("ApiHealthResponse")
                                }
                            }
                        }
                    },
                    ["/openapi/v1.json"] = new
                    {
                        get = new
                        {
                            summary = "OpenAPI document",
                            operationId = "getOpenApiDocument",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "OpenAPI document for the sidecar API.",
                                    content = JsonContent("OpenApiDocument")
                                }
                            }
                        }
                    },
                    ["/api/system/shutdown-maintenance"] = new
                    {
                        post = new
                        {
                            summary = "Run desktop shutdown maintenance",
                            operationId = "runShutdownMaintenance",
                            security = new[]
                            {
                                new Dictionary<string, string[]>
                                {
                                    ["DesktopAccess"] = Array.Empty<string>()
                                }
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Shutdown maintenance finished or returned a non-blocking maintenance failure result.",
                                    content = JsonContent("ApiShutdownMaintenanceResponse")
                                },
                                ["403"] = new
                                {
                                    description = "Missing or invalid desktop access token.",
                                    content = JsonContent("ApiErrorResponse")
                                }
                            }
                        }
                    },
                    ["/api/system/logs/cleanup"] = new
                    {
                        post = new
                        {
                            summary = "Clean system logs using saved retention settings",
                            operationId = "cleanupSystemLogs",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "System audit and text logs were cleaned according to saved settings.",
                                    content = JsonContent("ApiSystemLogCleanupResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new
                                {
                                    description = "Only administrators can clean system logs.",
                                    content = JsonContent("ApiErrorResponse")
                                },
                                ["409"] = new
                                {
                                    description = "Log cleanup failed.",
                                    content = JsonContent("ApiErrorResponse")
                                }
                            }
                        }
                    },
                    ["/api/system/license"] = new
                    {
                        get = new
                        {
                            summary = "Get license status",
                            operationId = "getLicenseStatus",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current Tauri/Web/API runtime license status and machine id.",
                                    content = JsonContent("ApiLicenseStatusResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/system/license/register"] = new
                    {
                        post = new
                        {
                            summary = "Register license",
                            operationId = "registerLicense",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiLicenseRegisterRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "License registration succeeded.",
                                    content = JsonContent("ApiLicenseRegisterResponse")
                                },
                                ["400"] = new
                                {
                                    description = "License key is missing, invalid, or for another machine.",
                                    content = JsonContent("ApiLicenseRegisterResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/auth/login"] = new
                    {
                        post = new
                        {
                            summary = "Login",
                            operationId = "login",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiLoginRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Authenticated local sidecar session.",
                                    content = JsonContent("ApiLoginResponse")
                                },
                                ["400"] = new { description = "Missing username." },
                                ["401"] = new { description = "Invalid username or password." },
                                ["503"] = new { description = "Database initialization failed." }
                            }
                        }
                    },
                    ["/api/auth/me"] = new
                    {
                        get = new
                        {
                            summary = "Current user",
                            operationId = "getCurrentUser",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current authenticated user.",
                                    content = JsonContent("ApiUserDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/auth/logout"] = new
                    {
                        post = new
                        {
                            summary = "Logout",
                            operationId = "logout",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Token revocation result.",
                                    content = JsonContent("ApiLogoutResponse")
                                }
                            }
                        }
                    },
                    ["/api/users"] = new
                    {
                        get = new
                        {
                            summary = "List users",
                            operationId = "listUsers",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User accounts and role catalog for administrators.",
                                    content = JsonContent("ApiUserListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." }
                            }
                        },
                        post = new
                        {
                            summary = "Create user",
                            operationId = "createUserAccount",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiUserSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User account was created.",
                                    content = JsonContent("ApiUserSaveResponse")
                                },
                                ["400"] = new { description = "Invalid user payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." },
                                ["409"] = new { description = "User account could not be saved." }
                            }
                        }
                    },
                    ["/api/users/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update user",
                            operationId = "updateUserAccount",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "User id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiUserSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User account was saved.",
                                    content = JsonContent("ApiUserSaveResponse")
                                },
                                ["400"] = new { description = "Invalid user payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." },
                                ["409"] = new { description = "User account could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete user",
                            operationId = "deleteUserAccount",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "User id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User account was deleted.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid user id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." },
                                ["404"] = new { description = "User account was not found." },
                                ["409"] = new { description = "User account could not be deleted." }
                            }
                        }
                    },
                    ["/api/permission-templates"] = new
                    {
                        get = new
                        {
                            summary = "List permission templates and module catalog",
                            operationId = "listPermissionTemplates",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template catalog.", content = JsonContent("ApiPermissionTemplateCatalogResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." }
                            }
                        },
                        post = new
                        {
                            summary = "Create permission template",
                            operationId = "createPermissionTemplate",
                            requestBody = new { required = true, content = JsonContent("ApiPermissionTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template was created.", content = JsonContent("ApiPermissionTemplateDto") },
                                ["400"] = new { description = "Invalid permission template payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." },
                                ["409"] = new { description = "Permission template could not be saved." }
                            }
                        }
                    },
                    ["/api/permission-templates/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update permission template",
                            operationId = "updatePermissionTemplate",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "Permission template id.") },
                            requestBody = new { required = true, content = JsonContent("ApiPermissionTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template was updated.", content = JsonContent("ApiPermissionTemplateDto") },
                                ["400"] = new { description = "Invalid permission template payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." },
                                ["404"] = new { description = "Permission template was not found." },
                                ["409"] = new { description = "Permission template could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete permission template",
                            operationId = "deletePermissionTemplate",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "Permission template id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template was deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." },
                                ["404"] = new { description = "Permission template was not found." },
                                ["409"] = new { description = "System or assigned templates cannot be deleted." }
                            }
                        }
                    },
                    ["/api/settings"] = new
                    {
                        get = new
                        {
                            summary = "Get settings",
                            operationId = "getSettings",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current local settings with secret values redacted.",
                                    content = JsonContent("ApiSettingsResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        put = new
                        {
                            summary = "Update settings",
                            operationId = "updateSettings",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSettingsSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Settings were saved to the program root appsettings.json.",
                                    content = JsonContent("ApiSettingsSaveResponse")
                                },
                                ["400"] = new { description = "Invalid settings payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage program settings." },
                                ["409"] = new { description = "Settings could not be saved." }
                            }
                        }
                    },
                    ["/api/settings/validate"] = new
                    {
                        post = new
                        {
                            summary = "Validate settings draft",
                            operationId = "validateSettings",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSettingsValidationRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Settings draft validation result. The sidecar does not save appsettings.json.",
                                    content = JsonContent("ApiSettingsValidationResponse")
                                },
                                ["400"] = new { description = "Invalid settings validation payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage program settings." }
                            }
                        }
                    },
                    ["/api/backup"] = new
                    {
                        get = new
                        {
                            summary = "List database backups",
                            operationId = "listDatabaseBackups",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Database backups under the runtime data root Backups directory.",
                                    content = JsonContent("ApiBackupListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." }
                            }
                        },
                        post = new
                        {
                            summary = "Create database backup",
                            operationId = "createDatabaseBackup",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "SQLite database backup was created in the runtime data root Backups directory.",
                                    content = JsonContent("ApiBackupCreateResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." }
                            }
                        }
                    },
                    ["/api/backup/cleanup"] = new
                    {
                        post = new
                        {
                            summary = "Clean old database backups",
                            operationId = "cleanupDatabaseBackups",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiBackupCleanupRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Old backups were cleaned according to the requested retention days.",
                                    content = JsonContent("ApiBackupCreateResponse")
                                },
                                ["400"] = new { description = "Invalid cleanup payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." }
                            }
                        }
                    },
                    ["/api/backup/restore"] = new
                    {
                        post = new
                        {
                            summary = "Restore database backup",
                            operationId = "restoreDatabaseBackup",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiBackupRestoreRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The database was restored from a known backup file.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid restore payload or confirmation text." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." },
                                ["409"] = new { description = "The database could not be restored." }
                            }
                        }
                    },
                    ["/api/backup/cloud/status"] = new
                    {
                        get = new
                        {
                            summary = "Get WebDAV cloud backup status",
                            operationId = "getCloudBackupStatus",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Saved WebDAV backup status and latest local backup under the runtime data root.",
                                    content = JsonContent("ApiCloudBackupStatusResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." }
                            }
                        }
                    },
                    ["/api/backup/cloud/test-connection"] = new
                    {
                        post = new
                        {
                            summary = "Test saved WebDAV cloud backup settings",
                            operationId = "testCloudBackupConnection",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The saved WebDAV settings were able to connect to the configured remote endpoint.",
                                    content = JsonContent("ApiCloudBackupCommandResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." },
                                ["409"] = new { description = "WebDAV is not configured or the connection test failed." }
                            }
                        }
                    },
                    ["/api/backup/cloud/upload-latest"] = new
                    {
                        post = new
                        {
                            summary = "Upload latest local database backup to WebDAV",
                            operationId = "uploadLatestDatabaseBackupToCloud",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The latest local SQLite backup ZIP under the runtime data root was uploaded to the saved WebDAV endpoint.",
                                    content = JsonContent("ApiCloudBackupCommandResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." },
                                ["409"] = new { description = "WebDAV is disabled, not configured, no backup exists, or the upload failed." }
                            }
                        }
                    },
                    ["/api/backup/cloud/backups"] = new
                    {
                        get = new
                        {
                            summary = "List WebDAV cloud database backups",
                            operationId = "listCloudDatabaseBackups",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "ZIP database backups currently visible on the saved WebDAV endpoint.",
                                    content = JsonContent("ApiCloudBackupListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." },
                                ["409"] = new { description = "WebDAV is disabled, not configured, or the remote list failed." }
                            }
                        }
                    },
                    ["/api/backup/cloud/download"] = new
                    {
                        post = new
                        {
                            summary = "Download WebDAV cloud database backup into runtime backup root",
                            operationId = "downloadCloudDatabaseBackup",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiCloudBackupDownloadRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The selected WebDAV ZIP backup was downloaded into the runtime data root Backups directory.",
                                    content = JsonContent("ApiCloudBackupCommandResponse")
                                },
                                ["400"] = new { description = "Invalid cloud backup download payload or file name." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." },
                                ["409"] = new { description = "WebDAV is disabled, not configured, or the download failed." }
                            }
                        }
                    },
                    ["/api/postgresql-maintenance/backups"] = new
                    {
                        get = new
                        {
                            summary = "List PostgreSQL physical backups",
                            operationId = "listPostgreSqlPhysicalBackups",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "PostgreSQL team database custom-format dump files under the runtime data root Backups/PostgreSQL directory.",
                                    content = JsonContent("ApiPostgreSqlPhysicalBackupListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage PostgreSQL maintenance." }
                            }
                        },
                        post = new
                        {
                            summary = "Create PostgreSQL physical backup",
                            operationId = "createPostgreSqlPhysicalBackup",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "A PostgreSQL custom-format dump was created under the runtime data root.",
                                    content = JsonContent("ApiPostgreSqlPhysicalBackupResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage PostgreSQL maintenance." },
                                ["409"] = new { description = "PostgreSQL is not configured, pg_dump is missing, or the backup failed." }
                            }
                        }
                    },
                    ["/api/postgresql-maintenance/restore-plan"] = new
                    {
                        post = new
                        {
                            summary = "Create PostgreSQL restore and ownership reassignment plan",
                            operationId = "createPostgreSqlRestorePlan",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPostgreSqlRestorePlanRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "A restore script and post-restore ownership SQL were generated under the runtime data root.",
                                    content = JsonContent("ApiPostgreSqlRestorePlanResponse")
                                },
                                ["400"] = new { description = "Invalid restore plan payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage PostgreSQL maintenance." },
                                ["409"] = new { description = "The selected backup could not be found or the restore plan could not be generated." }
                            }
                        }
                    },
                    ["/api/shared-database/ownership"] = new
                    {
                        get = new
                        {
                            summary = "Get shared database ownership summary",
                            operationId = "getSharedDatabaseOwnershipSummary",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Invoice and payment ownership counts grouped by user.",
                                    content = JsonContent("ApiSharedDatabaseOwnershipSummaryResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot inspect shared database ownership." }
                            }
                        }
                    },
                    ["/api/shared-database/ownership/transfer"] = new
                    {
                        post = new
                        {
                            summary = "Transfer invoice and payment ownership",
                            operationId = "transferSharedDatabaseOwnership",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSharedDatabaseOwnershipTransferRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Ownership fields were reassigned in one transaction.",
                                    content = JsonContent("ApiSharedDatabaseOwnershipTransferResponse")
                                },
                                ["400"] = new { description = "Invalid transfer payload or confirmation text." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot transfer shared database ownership." },
                                ["409"] = new { description = "Ownership could not be transferred." }
                            }
                        }
                    },
                    ["/api/support-package/save-to-runtime"] = new
                    {
                        post = new
                        {
                            summary = "Create diagnostic support package in the runtime data root for trusted desktop use",
                            operationId = "saveSupportPackageToRuntime",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSupportPackageRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "A redacted diagnostic support ZIP package was created under the runtime data root.",
                                    content = JsonContent("ApiSupportPackageResponse")
                                },
                                ["400"] = new { description = "Optional database backups or sample files require explicit confirmation text." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot create support packages or the desktop token is invalid." }
                            }
                        }
                    },
                    ["/api/support-package/download"] = new
                    {
                        post = new
                        {
                            summary = "Download diagnostic support package",
                            operationId = "downloadSupportPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSupportPackageRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "A redacted diagnostic support ZIP attachment.",
                                    content = BinaryContent()
                                },
                                ["400"] = new { description = "Optional files require explicit confirmation text." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot download support packages." }
                            }
                        }
                    },
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
                                ["404"] = new { description = "The job result is unavailable or is not a controlled browser download." }
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
