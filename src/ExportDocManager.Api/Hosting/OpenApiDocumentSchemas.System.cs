namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSystemSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiLoginRequest"] = new
                        {
                            type = "object",
                            required = new[] { "username" },
                            properties = new Dictionary<string, object>
                            {
                                ["username"] = StringProperty("Login username."),
                                ["password"] = StringProperty("Login password or SQLite database password.")
                            }
                        },
                        ["ApiLoginResponse"] = new
                        {
                            type = "object",
                            required = new[] { "tokenType", "accessToken", "expiresAt", "user" },
                            properties = new Dictionary<string, object>
                            {
                                ["tokenType"] = StringProperty("Bearer token type."),
                                ["accessToken"] = StringProperty("Short-lived local sidecar token."),
                                ["expiresAt"] = new { type = "string", format = "date-time" },
                                ["user"] = RefSchema("ApiUserDto")
                            }
                        },
                        ["ApiUserDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "username", "role", "isActive", "capabilities" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" },
                                ["capabilities"] = RefSchema("ApiUserCapabilitiesDto")
                            }
                        },
                        ["ApiUserCapabilitiesDto"] = new
                        {
                            type = "object",
                            required = new[] { "canManageSettings", "canManageUsers", "canViewAllBusinessData", "canUseDocumentWorkspace", "canUseSalesWorkspace", "productEdition", "enabledModules", "moduleAccess" },
                            properties = new Dictionary<string, object>
                            {
                                ["canManageSettings"] = new { type = "boolean" },
                                ["canManageUsers"] = new { type = "boolean" },
                                ["canViewAllBusinessData"] = new { type = "boolean" },
                                ["canUseDocumentWorkspace"] = new { type = "boolean" },
                                ["canUseSalesWorkspace"] = new { type = "boolean" },
                                ["productEdition"] = StringProperty("Document, Sales, or Full product edition."),
                                ["enabledModules"] = StringArrayProperty("Effective module keys captured when the user logged in."),
                                ["moduleAccess"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiModuleAccessDto")
                                }
                            }
                        },
                        ["ApiModuleAccessDto"] = new
                        {
                            type = "object",
                            required = new[] { "moduleKey", "accessLevel" },
                            properties = new Dictionary<string, object>
                            {
                                ["moduleKey"] = StringProperty("Effective module key."),
                                ["accessLevel"] = StringProperty("view, operate, or manage.")
                            }
                        },
                        ["ApiUserAccountDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "username", "role", "permissionTemplateCode", "permissionTemplateName", "isActive" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["permissionTemplateId"] = new { type = "integer", format = "int32", nullable = true },
                                ["permissionTemplateCode"] = StringProperty("Assigned permission template code."),
                                ["permissionTemplateName"] = StringProperty("Assigned permission template name."),
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" }
                            }
                        },
                        ["ApiUserListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "users", "roles", "permissionTemplates" },
                            properties = new Dictionary<string, object>
                            {
                                ["users"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiUserAccountDto")
                                },
                                ["roles"] = StringArrayProperty("Available user roles."),
                                ["permissionTemplates"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiPermissionTemplateOptionDto")
                                }
                            }
                        },
                        ["ApiUserSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "username", "role", "isActive" },
                            properties = new Dictionary<string, object>
                            {
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["permissionTemplateId"] = new { type = "integer", format = "int32", nullable = true },
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" },
                                ["resetPassword"] = StringProperty("Initial or reset password. Required when creating a user.")
                            }
                        },
                        ["ApiUserSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "user" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Save result message."),
                                ["user"] = RefSchema("ApiUserAccountDto")
                            }
                        },
                        ["ApiPermissionTemplateOptionDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "code", "name", "isSystem", "isActive" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("Stable permission template code."),
                                ["name"] = StringProperty("Permission template display name."),
                                ["isSystem"] = new { type = "boolean" },
                                ["isActive"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPermissionModuleDefinitionDto"] = new
                        {
                            type = "object",
                            required = new[] { "key", "name", "group", "workspace", "sortOrder", "isTechnical" },
                            properties = new Dictionary<string, object>
                            {
                                ["key"] = StringProperty("Stable module key."),
                                ["name"] = StringProperty("Module display name."),
                                ["group"] = StringProperty("Module group."),
                                ["workspace"] = StringProperty("document, sales, or common."),
                                ["sortOrder"] = new { type = "integer", format = "int32" },
                                ["isTechnical"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPermissionTemplateModuleDto"] = new
                        {
                            type = "object",
                            required = new[] { "moduleKey", "accessLevel" },
                            properties = new Dictionary<string, object>
                            {
                                ["moduleKey"] = StringProperty("Stable module key."),
                                ["accessLevel"] = StringProperty("view, operate, or manage.")
                            }
                        },
                        ["ApiPermissionTemplateDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "code", "name", "description", "isSystem", "isActive", "updatedAt", "modules" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("Stable permission template code."),
                                ["name"] = StringProperty("Permission template display name."),
                                ["description"] = StringProperty("Permission template description."),
                                ["isSystem"] = new { type = "boolean" },
                                ["isActive"] = new { type = "boolean" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["modules"] = new { type = "array", items = RefSchema("ApiPermissionTemplateModuleDto") }
                            }
                        },
                        ["ApiPermissionTemplateCatalogResponse"] = new
                        {
                            type = "object",
                            required = new[] { "modules", "templates", "accessLevels", "applyPolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["modules"] = new { type = "array", items = RefSchema("ApiPermissionModuleDefinitionDto") },
                                ["templates"] = new { type = "array", items = RefSchema("ApiPermissionTemplateDto") },
                                ["accessLevels"] = StringArrayProperty("Supported access levels."),
                                ["applyPolicy"] = StringProperty("Permission snapshot apply policy.")
                            }
                        },
                        ["ApiPermissionTemplateSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "code", "name", "isActive", "modules" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("Unique permission template code."),
                                ["name"] = StringProperty("Permission template display name."),
                                ["description"] = StringProperty("Permission template description."),
                                ["isActive"] = new { type = "boolean" },
                                ["modules"] = new { type = "array", items = RefSchema("ApiPermissionTemplateModuleDto") }
                            }
                        },
                        ["ApiSystemLogCleanupResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "deletedAuditLogs",
                                "deletedTextLogs",
                                "deletedTextLogsByAge",
                                "deletedTextLogsByCount",
                                "logRoot",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Manual system log cleanup result message."),
                                ["deletedAuditLogs"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogs"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogsByAge"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogsByCount"] = new { type = "integer", format = "int32" },
                                ["logRoot"] = StringProperty("Program root logs directory."),
                                ["storagePolicy"] = StringProperty("Manual log cleanup storage and data-domain policy.")
                            }
                        },
                        ["ApiLicenseStatusResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "isRegistered",
                                "isTrialExpired",
                                "trialDays",
                                "daysRemaining",
                                "machineId",
                                "message",
                                "expireDate",
                                "licenseStoragePath",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["isRegistered"] = new { type = "boolean" },
                                ["isTrialExpired"] = new { type = "boolean" },
                                ["trialDays"] = new { type = "integer", format = "int32" },
                                ["daysRemaining"] = new { type = "integer", format = "int32" },
                                ["machineId"] = StringProperty("Runtime license machine id shown to the user for key generation."),
                                ["message"] = StringProperty("Human-readable license status."),
                                ["expireDate"] = new { type = "string", format = "date-time" },
                                ["licenseStoragePath"] = StringProperty("Runtime data root Security/license.dat file path."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for Tauri/Web/API license state.")
                            }
                        },
                        ["ApiLicenseRegisterRequest"] = new
                        {
                            type = "object",
                            required = new[] { "licenseKey" },
                            properties = new Dictionary<string, object>
                            {
                                ["licenseKey"] = StringProperty("License key generated for the current machine id.")
                            }
                        },
                        ["ApiLicenseRegisterResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Registration result message."),
                                ["status"] = RefSchema("ApiLicenseStatusResponse")
                            }
                        },
                        ["ApiBackupListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "backups", "backupRoot", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["backups"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiBackupItemDto")
                                },
                                ["backupRoot"] = StringProperty("Runtime data root Backups directory."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for database backups.")
                            }
                        },
                        ["ApiBackupItemDto"] = new
                        {
                            type = "object",
                            required = new[] { "fileName", "fullPath", "sizeBytes", "createdAt", "lastWriteTime" },
                            properties = new Dictionary<string, object>
                            {
                                ["fileName"] = StringProperty("Backup file name under the backup root."),
                                ["fullPath"] = StringProperty("Full local backup path for desktop open-path actions."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["lastWriteTime"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiBackupCreateResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "backups", "backupRoot", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Backup command result message."),
                                ["backups"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiBackupItemDto")
                                },
                                ["backupRoot"] = StringProperty("Runtime data root Backups directory."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for database backups.")
                            }
                        },
                        ["ApiBackupCleanupRequest"] = new
                        {
                            type = "object",
                            required = new[] { "daysToKeep" },
                            properties = new Dictionary<string, object>
                            {
                                ["daysToKeep"] = new { type = "integer", format = "int32", minimum = 0 }
                            }
                        },
                        ["ApiBackupRestoreRequest"] = new
                        {
                            type = "object",
                            required = new[] { "backupFileName", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["backupFileName"] = StringProperty("Backup file name selected from the backup list. Paths are rejected."),
                                ["confirmationText"] = StringProperty("Must be RESTORE to confirm destructive database restore.")
                            }
                        },
                        ["ApiCloudBackupListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "backups", "backupRoot", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["backups"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiCloudBackupItemDto")
                                },
                                ["backupRoot"] = StringProperty("Runtime data root Backups directory where downloaded cloud backups are stored."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for WebDAV cloud backups.")
                            }
                        },
                        ["ApiCloudBackupItemDto"] = new
                        {
                            type = "object",
                            required = new[] { "fileName", "sizeBytes", "lastModified" },
                            properties = new Dictionary<string, object>
                            {
                                ["fileName"] = StringProperty("Remote ZIP backup file name on WebDAV. Paths are never exposed."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["lastModified"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiCloudBackupDownloadRequest"] = new
                        {
                            type = "object",
                            required = new[] { "remoteFileName" },
                            properties = new Dictionary<string, object>
                            {
                                ["remoteFileName"] = StringProperty("Remote ZIP backup file name selected from the WebDAV cloud backup list. Paths are rejected.")
                            }
                        },
                        ["ApiCloudBackupStatusResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "enabled",
                                "isConfigured",
                                "url",
                                "userName",
                                "latestBackupFileName",
                                "latestBackupSizeBytes",
                                "backupRoot",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["enabled"] = new { type = "boolean" },
                                ["isConfigured"] = new { type = "boolean" },
                                ["url"] = StringProperty("Saved WebDAV server URL from appsettings.json."),
                                ["userName"] = StringProperty("Saved WebDAV user name from appsettings.json."),
                                ["latestBackupFileName"] = StringProperty("Latest local database backup file name under the runtime data root Backups directory."),
                                ["latestBackupSizeBytes"] = new { type = "integer", format = "int64" },
                                ["backupRoot"] = StringProperty("Runtime data root Backups directory."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for WebDAV cloud backups.")
                            }
                        },
                        ["ApiCloudBackupCommandResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "remoteFileName",
                                "localBackupPath",
                                "sizeBytes",
                                "backupRoot",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Cloud backup command result message."),
                                ["remoteFileName"] = StringProperty("Remote backup file name uploaded to WebDAV."),
                                ["localBackupPath"] = StringProperty("Local runtime data root backup path that was uploaded."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["backupRoot"] = StringProperty("Runtime data root Backups directory."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for WebDAV cloud backups.")
                            }
                        },
                        ["ApiSharedDatabaseBackupItemDto"] = new
                        {
                            type = "object",
                            required = new[] { "fileName", "fullPath", "sizeBytes", "createdAt", "lastWriteTime" },
                            properties = new Dictionary<string, object>
                            {
                                ["fileName"] = StringProperty("Backup file name under the managed runtime backup root."),
                                ["fullPath"] = StringProperty("Full local backup path for desktop open-path actions."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["lastWriteTime"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiPostgreSqlPhysicalBackupListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "status", "backups" },
                            properties = new Dictionary<string, object>
                            {
                                ["status"] = RefSchema("ApiPostgreSqlMaintenanceStatusResponse"),
                                ["backups"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiSharedDatabaseBackupItemDto")
                                }
                            }
                        },
                        ["ApiPostgreSqlPhysicalBackupResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "fileName", "fullPath", "sizeBytes", "backupRoot", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("PostgreSQL physical backup result message."),
                                ["fileName"] = StringProperty("Custom-format dump file name."),
                                ["fullPath"] = StringProperty("Full local dump file path."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["backupRoot"] = StringProperty("Runtime data root Backups/PostgreSQL directory."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for PostgreSQL physical backups.")
                            }
                        },
                        ["ApiPostgreSqlRestorePlanRequest"] = new
                        {
                            type = "object",
                            required = new[] { "backupFileName", "targetDatabase", "applicationRole", "oldOwnerRoles" },
                            properties = new Dictionary<string, object>
                            {
                                ["backupFileName"] = StringProperty("PostgreSQL dump file name selected from the managed backup list. Paths are rejected."),
                                ["targetDatabase"] = StringProperty("Target PostgreSQL business database name."),
                                ["applicationRole"] = StringProperty("Application database role that should own restored objects."),
                                ["oldOwnerRoles"] = StringArrayProperty("Optional old owner roles for REASSIGN OWNED.")
                            }
                        },
                        ["ApiPostgreSqlRestorePlanResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "planRoot", "restoreScriptPath", "ownershipSqlPath", "backupFilePath", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Restore plan creation result message."),
                                ["planRoot"] = StringProperty("Runtime data root restore plan directory."),
                                ["restoreScriptPath"] = StringProperty("Generated restore script path."),
                                ["ownershipSqlPath"] = StringProperty("Generated post-restore ownership and grant SQL path."),
                                ["backupFilePath"] = StringProperty("Selected PostgreSQL dump path."),
                                ["storagePolicy"] = StringProperty("Path and review policy for PostgreSQL restore plans.")
                            }
                        },
                        ["ApiSharedDatabaseOwnershipSummaryResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "totalInvoices",
                                "unassignedInvoices",
                                "totalPayments",
                                "unassignedPayments",
                                "owners",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["totalInvoices"] = new { type = "integer", format = "int32" },
                                ["unassignedInvoices"] = new { type = "integer", format = "int32" },
                                ["totalPayments"] = new { type = "integer", format = "int32" },
                                ["unassignedPayments"] = new { type = "integer", format = "int32" },
                                ["owners"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiSharedDatabaseOwnerSummaryItemDto")
                                },
                                ["storagePolicy"] = StringProperty("Ownership transfer storage and data-domain policy.")
                            }
                        },
                        ["ApiSharedDatabaseOwnerSummaryItemDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "userId",
                                "username",
                                "fullName",
                                "role",
                                "departmentId",
                                "companyScope",
                                "invoiceCount",
                                "paymentCount"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["userId"] = new { type = "integer", format = "int32" },
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["invoiceCount"] = new { type = "integer", format = "int32" },
                                ["paymentCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiSharedDatabaseOwnershipTransferRequest"] = new
                        {
                            type = "object",
                            required = new[] { "toUserId", "includeInvoices", "includePayments", "onlyUnassigned", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["fromUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["toUserId"] = new { type = "integer", format = "int32" },
                                ["includeInvoices"] = new { type = "boolean" },
                                ["includePayments"] = new { type = "boolean" },
                                ["onlyUnassigned"] = new { type = "boolean" },
                                ["departmentId"] = StringProperty("Optional department scope override. Defaults to target user department."),
                                ["companyScope"] = StringProperty("Optional company scope override. Defaults to target user company scope."),
                                ["confirmationText"] = StringProperty("Must be TRANSFER OWNERSHIP to confirm ownership reassignment.")
                            }
                        },
                        ["ApiSharedDatabaseOwnershipTransferResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "updatedInvoices", "updatedPayments", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Ownership transfer result message."),
                                ["updatedInvoices"] = new { type = "integer", format = "int32" },
                                ["updatedPayments"] = new { type = "integer", format = "int32" },
                                ["storagePolicy"] = StringProperty("Ownership transfer storage and data-domain policy.")
                            }
                        },
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
                                ["appRoot"] = StringProperty("Program runtime directory."),
                                ["dataRoot"] = StringProperty("Business writable data root."),
                                ["databaseRoot"] = StringProperty("Database directory under the writable data root."),
                                ["singleWindowRoot"] = StringProperty("Single Window writable data directory."),
                                ["templateRoot"] = StringProperty("Bundled template directory under the program root."),
                                ["ocrModelRoot"] = StringProperty("Bundled OCR model directory under the program root."),
                                ["logRoot"] = StringProperty("Log directory under the writable data root."),
                                ["databaseProvider"] = StringProperty("Current database mode."),
                                ["sqliteDatabasePath"] = StringProperty("Resolved SQLite database path when SQLite is used."),
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
                                ["storagePolicy"] = StringProperty("Runtime storage policy summary.")
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
