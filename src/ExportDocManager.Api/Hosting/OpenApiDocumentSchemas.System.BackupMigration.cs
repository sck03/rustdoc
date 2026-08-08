namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateBackupMigrationSystemSchemas() =>
            new Dictionary<string, object>
            {
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
                        ["ApiDisasterRecoveryStatusResponse"] = new
                        {
                            type = "object",
                            required = new[] { "supported", "usesSqlite", "pendingRestore", "recoveryRoot", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["supported"] = new { type = "boolean" },
                                ["usesSqlite"] = new { type = "boolean" },
                                ["pendingRestore"] = new { type = "boolean" },
                                ["recoveryRoot"] = StringProperty("Runtime data root Backups/DisasterRecovery directory."),
                                ["message"] = StringProperty("Current recovery availability or pending state."),
                                ["storagePolicy"] = StringProperty("Encrypted package inclusion/exclusion and license policy.")
                            }
                        },
                        ["ApiDisasterRecoveryCreateRequest"] = new
                        {
                            type = "object",
                            required = new[] { "password" },
                            properties = new Dictionary<string, object>
                            {
                                ["password"] = new { type = "string", format = "password", minLength = 12, maxLength = 128 }
                            }
                        },
                        ["ApiDisasterRecoveryPackageResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "fileName", "filePath", "sizeBytes", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Package creation result."),
                                ["fileName"] = StringProperty("Generated .edmrecovery file name."),
                                ["filePath"] = StringProperty("Full local package path under the runtime data root."),
                                ["sizeBytes"] = new { type = "integer", format = "int64" },
                                ["storagePolicy"] = StringProperty("Encrypted package inclusion/exclusion and license policy.")
                            }
                        },
                        ["ApiDisasterRecoveryRestoreRequest"] = new
                        {
                            type = "object",
                            required = new[] { "packagePath", "password", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["packagePath"] = StringProperty("Local .edmrecovery path selected by the trusted desktop shell."),
                                ["password"] = new { type = "string", format = "password", minLength = 12, maxLength = 128 },
                                ["confirmationText"] = StringProperty("Must be RECOVER to schedule offline disaster recovery.")
                            }
                        },
                        ["ApiDisasterRecoveryRestoreResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "restartRequired", "message", "packageFileName", "safetyBackupRoot", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["restartRequired"] = new { type = "boolean" },
                                ["message"] = StringProperty("Restore staging result and restart requirement."),
                                ["packageFileName"] = StringProperty("Selected encrypted recovery package file name."),
                                ["safetyBackupRoot"] = StringProperty("Safety copy directory created before offline replacement."),
                                ["storagePolicy"] = StringProperty("Encrypted package inclusion/exclusion and license policy.")
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
                        ["ApiPostgreSqlDatabaseRestoreRequest"] = new
                        {
                            type = "object",
                            required = new[] { "backupFileName", "adminPassword", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["backupFileName"] = StringProperty("Managed PostgreSQL .dump backup file name."),
                                ["adminPassword"] = new { type = "string", format = "password" },
                                ["confirmationText"] = StringProperty("Must equal RESTORE DATABASE.")
                            }
                        },
                        ["ApiServerMigrationStatusResponse"] = new
                        {
                            type = "object",
                            required = new[] { "supported", "postgreSqlConfigured", "toolsReady", "pendingRestore", "packageRoot", "message", "storagePolicy", "restorePhase", "restoreDetail" },
                            properties = new Dictionary<string, object>
                            {
                                ["supported"] = new { type = "boolean" },
                                ["postgreSqlConfigured"] = new { type = "boolean" },
                                ["toolsReady"] = new { type = "boolean" },
                                ["pendingRestore"] = new { type = "boolean" },
                                ["packageRoot"] = StringProperty("Runtime data root for encrypted server migration packages."),
                                ["message"] = StringProperty("Current migration readiness message."),
                                ["storagePolicy"] = StringProperty("Encrypted server migration package boundary."),
                                ["restorePhase"] = StringProperty("Last restore phase."),
                                ["restoreDetail"] = StringProperty("Last restore result or progress detail."),
                                ["restoreUpdatedAtUtc"] = new { type = "string", format = "date-time", nullable = true }
                            }
                        },
                        ["ApiServerMigrationCreateRequest"] = new
                        {
                            type = "object",
                            required = new[] { "password", "adminPassword", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["password"] = StringProperty("Strong package password, 12-128 characters with upper, lower, digit, and symbol."),
                                ["adminPassword"] = new { type = "string", format = "password" },
                                ["confirmationText"] = StringProperty("Must equal MIGRATE.")
                            }
                        },
                        ["ApiSensitiveOperationAuthorizationRequest"] = new
                        {
                            type = "object",
                            required = new[] { "action", "adminPassword" },
                            properties = new Dictionary<string, object>
                            {
                                ["action"] = StringProperty("restore-database or restore-server."),
                                ["adminPassword"] = new { type = "string", format = "password" }
                            }
                        },
                        ["ApiSensitiveOperationAuthorizationResponse"] = new
                        {
                            type = "object",
                            required = new[] { "action", "ticket", "expiresAtUtc" },
                            properties = new Dictionary<string, object>
                            {
                                ["action"] = StringProperty("Authorized sensitive operation."),
                                ["ticket"] = StringProperty("Single-use five-minute upload authorization ticket."),
                                ["expiresAtUtc"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiServerMigrationRestoreResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "restartRequired", "automaticRestartScheduled", "message", "packageFileName", "safetyBackupRoot", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["restartRequired"] = new { type = "boolean" },
                                ["automaticRestartScheduled"] = new { type = "boolean" },
                                ["message"] = StringProperty("Restore scheduling result message."),
                                ["packageFileName"] = StringProperty("Uploaded .edmmigration file name."),
                                ["safetyBackupRoot"] = StringProperty("Recovery-time safety backup directory."),
                                ["storagePolicy"] = StringProperty("Encrypted server migration package boundary.")
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
                                "totalOtherBusinessData",
                                "unassignedOtherBusinessData",
                                "owners",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["totalInvoices"] = new { type = "integer", format = "int32" },
                                ["unassignedInvoices"] = new { type = "integer", format = "int32" },
                                ["totalPayments"] = new { type = "integer", format = "int32" },
                                ["unassignedPayments"] = new { type = "integer", format = "int32" },
                                ["totalOtherBusinessData"] = new { type = "integer", format = "int32" },
                                ["unassignedOtherBusinessData"] = new { type = "integer", format = "int32" },
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
                                "isActive",
                                "invoiceCount",
                                "paymentCount",
                                "otherBusinessDataCount"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["userId"] = new { type = "integer", format = "int32" },
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" },
                                ["invoiceCount"] = new { type = "integer", format = "int32" },
                                ["paymentCount"] = new { type = "integer", format = "int32" },
                                ["otherBusinessDataCount"] = new { type = "integer", format = "int32" }
                            }
                        },
                        ["ApiSharedDatabaseOwnershipTransferRequest"] = new
                        {
                            type = "object",
                            required = new[] { "toUserId", "includeInvoices", "includePayments", "includeOtherBusinessData", "onlyUnassigned", "confirmationText" },
                            properties = new Dictionary<string, object>
                            {
                                ["fromUserId"] = new { type = "integer", format = "int32", nullable = true },
                                ["toUserId"] = new { type = "integer", format = "int32" },
                                ["includeInvoices"] = new { type = "boolean" },
                                ["includePayments"] = new { type = "boolean" },
                                ["includeOtherBusinessData"] = new { type = "boolean" },
                                ["onlyUnassigned"] = new { type = "boolean" },
                                ["departmentId"] = StringProperty("Optional department scope override. Defaults to target user department."),
                                ["companyScope"] = StringProperty("Optional company scope override. Defaults to target user company scope."),
                                ["confirmationText"] = StringProperty("Must be TRANSFER OWNERSHIP to confirm ownership reassignment.")
                            }
                        },
                        ["ApiSharedDatabaseOwnershipTransferResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "updatedInvoices", "updatedPayments", "updatedOtherBusinessData", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Ownership transfer result message."),
                                ["updatedInvoices"] = new { type = "integer", format = "int32" },
                                ["updatedPayments"] = new { type = "integer", format = "int32" },
                                ["updatedOtherBusinessData"] = new { type = "integer", format = "int32" },
                                ["storagePolicy"] = StringProperty("Ownership transfer storage and data-domain policy.")
                            }
                        },
            };
    }
}
