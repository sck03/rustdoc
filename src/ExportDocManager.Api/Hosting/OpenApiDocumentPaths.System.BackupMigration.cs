namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateBackupMigrationSystemPaths() =>
            new Dictionary<string, object>
            {
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
                                ["404"] = new { description = "The selected backup file was not found." },
                                ["409"] = new { description = "Another database restore is already pending." },
                                ["503"] = new { description = "The database restore dependency is unavailable or failed." }
                            }
                        }
                    },
                    ["/api/backup/disaster-recovery/status"] = new
                    {
                        get = new
                        {
                            summary = "Get holding-station disaster recovery status",
                            operationId = "getDisasterRecoveryStatus",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "SQLite holding-station disaster recovery availability and storage policy.",
                                    content = JsonContent("ApiDisasterRecoveryStatusResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage disaster recovery." }
                            }
                        }
                    },
                    ["/api/backup/disaster-recovery/create"] = new
                    {
                        post = new
                        {
                            summary = "Create encrypted holding-station disaster recovery package",
                            operationId = "createDisasterRecoveryPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiDisasterRecoveryCreateRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "An encrypted recovery package was created under Backups/DisasterRecovery.",
                                    content = JsonContent("ApiDisasterRecoveryPackageResponse")
                                },
                                ["400"] = new { description = "Missing/invalid package password, unsupported recovery mode, or invalid package request." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator or trusted desktop access is missing." },
                                ["503"] = new { description = "Recovery prerequisites or the local filesystem are unavailable." }
                            }
                        }
                    },
                    ["/api/backup/disaster-recovery/restore"] = new
                    {
                        post = new
                        {
                            summary = "Schedule encrypted holding-station disaster recovery",
                            operationId = "restoreDisasterRecoveryPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiDisasterRecoveryRestoreRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The verified recovery payload was staged for offline restore before database settings are loaded on next start.",
                                    content = JsonContent("ApiDisasterRecoveryRestoreResponse")
                                },
                                ["400"] = new { description = "Missing fields, invalid password/package, or confirmation text is not RECOVER." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator or trusted desktop access is missing." },
                                ["404"] = new { description = "The recovery package was not found." },
                                ["409"] = new { description = "Another restore is already pending." },
                                ["503"] = new { description = "Recovery staging or the local filesystem is unavailable." }
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
                                ["400"] = new { description = "WebDAV is not configured." },
                                ["503"] = new { description = "The WebDAV endpoint or connection test is unavailable." }
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
                                ["400"] = new { description = "WebDAV is disabled or not configured." },
                                ["404"] = new { description = "No local database backup is available." },
                                ["503"] = new { description = "The WebDAV endpoint or upload is unavailable." }
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
                                ["400"] = new { description = "WebDAV is disabled or not configured." },
                                ["503"] = new { description = "The WebDAV endpoint or remote list is unavailable." }
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
                                ["400"] = new { description = "Invalid cloud backup payload/file name, disabled WebDAV, missing configuration, or invalid selected file." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage database backups." },
                                ["503"] = new { description = "The WebDAV endpoint or download is unavailable." }
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
                            summary = "Queue PostgreSQL physical backup creation",
                            operationId = "createPostgreSqlPhysicalBackup",
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "The PostgreSQL custom-format dump job was accepted.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage PostgreSQL maintenance." },
                                ["400"] = new { description = "PostgreSQL is not configured." },
                                ["503"] = new { description = "pg_dump is missing or PostgreSQL backup failed." },
                                ["429"] = new { description = "The background job queue is full." }
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
                                ["404"] = new { description = "The selected backup could not be found." },
                                ["503"] = new { description = "The restore plan dependency or filesystem is unavailable." }
                            }
                        }
                    },
                    ["/api/postgresql-maintenance/backups/download-ticket"] = new
                    {
                        post = new
                        {
                            summary = "Create a short-lived streaming download URL for a managed PostgreSQL physical backup",
                            operationId = "createPostgreSqlPhysicalBackupDownloadTicket",
                            parameters = new object[] { QueryParameter("fileName", "string", null, "Managed .dump backup file name.", required: true) },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Short-lived same-origin streaming download URL.", content = JsonContent("ApiDownloadTicket") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator settings permission is required." },
                                ["404"] = new { description = "The selected backup does not exist." },
                                ["426"] = new { description = "HTTPS or an explicitly trusted HTTP deployment is required." }
                            }
                        }
                    },
                    ["/api/postgresql-maintenance/backups/restore"] = new
                    {
                        post = new
                        {
                            summary = "Stage restore of a managed PostgreSQL physical backup",
                            operationId = "restorePostgreSqlPhysicalBackup",
                            requestBody = new { required = true, content = JsonContent("ApiPostgreSqlDatabaseRestoreRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Database restore staged for the next service start.", content = JsonContent("ApiServerMigrationRestoreResponse") },
                                ["400"] = new { description = "Explicit RESTORE DATABASE confirmation and a valid custom-format dump are required." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator settings permission is required." },
                                ["404"] = new { description = "The selected backup does not exist." },
                                ["409"] = new { description = "Another restore is already pending." },
                                ["503"] = new { description = "The database restore dependency or staging filesystem is unavailable." }
                            }
                        }
                    },
                    ["/api/postgresql-maintenance/backups/upload-restore"] = new
                    {
                        post = new
                        {
                            summary = "Upload and stage restore of a PostgreSQL physical backup",
                            operationId = "uploadAndRestorePostgreSqlPhysicalBackup",
                            parameters = new object[]
                            {
                                new { name = ApiEndpointRouteBuilderExtensions.PostgreSqlBackupFileNameHeader, @in = "header", required = true, schema = new { type = "string" } },
                                new { name = ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader, @in = "header", required = true, schema = new { type = "string" } },
                                new { name = ApiEndpointRouteBuilderExtensions.SensitiveOperationTicketHeader, @in = "header", required = true, schema = new { type = "string" } }
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Uploaded database restore staged for the next service start.", content = JsonContent("ApiServerMigrationRestoreResponse") },
                                ["400"] = new { description = "Explicit RESTORE DATABASE confirmation and a valid custom-format dump are required." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator settings permission is required." },
                                ["409"] = new { description = "Another restore is already pending." },
                                ["503"] = new { description = "The uploaded dump could not be staged or the restore dependency is unavailable." },
                                ["413"] = new { description = "The database backup exceeds the upload limit." }
                            }
                        }
                    },
                    ["/api/server-migration/status"] = new
                    {
                        get = new
                        {
                            summary = "Get encrypted server migration status",
                            operationId = "getServerMigrationStatus",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "PostgreSQL tools and restore state.", content = JsonContent("ApiServerMigrationStatusResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator settings permission is required." }
                            }
                        }
                    },
                    ["/api/server-migration/authorization"] = new
                    {
                        post = new
                        {
                            summary = "Re-authenticate and issue a one-time restore upload ticket",
                            operationId = "authorizeServerMigrationOperation",
                            requestBody = new { required = true, content = JsonContent("ApiSensitiveOperationAuthorizationRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Five-minute one-time upload ticket.", content = JsonContent("ApiSensitiveOperationAuthorizationResponse") },
                                ["400"] = new { description = "Unknown sensitive operation." },
                                ["401"] = new { description = "Missing session or invalid current password." },
                                ["403"] = new { description = "Disaster recovery management permission is required." },
                                ["429"] = new { description = "Re-authentication attempts are temporarily rate limited." }
                            }
                        }
                    },
                    ["/api/server-migration/packages"] = new
                    {
                        post = new
                        {
                            summary = "Start an encrypted server migration package background job",
                            operationId = "createServerMigrationPackage",
                            requestBody = new { required = true, content = JsonContent("ApiServerMigrationCreateRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new { description = "Migration package job accepted.", content = JsonContent("BackgroundJobSnapshot") },
                                ["400"] = new { description = "Explicit MIGRATE confirmation is required." },
                                ["401"] = new { description = "Missing session or invalid current password." },
                                ["403"] = new { description = "Disaster recovery management permission is required." },
                                ["429"] = new { description = "Re-authentication is rate limited or the background job queue is full." }
                            }
                        }
                    },
                    ["/api/server-migration/restore"] = new
                    {
                        post = new
                        {
                            summary = "Upload and stage an encrypted server migration restore",
                            operationId = "stageServerMigrationRestore",
                            parameters = new object[]
                            {
                                new { name = ApiEndpointRouteBuilderExtensions.ServerMigrationFileNameHeader, @in = "header", required = true, schema = new { type = "string" } },
                                new { name = ApiEndpointRouteBuilderExtensions.ServerMigrationPasswordHeader, @in = "header", required = true, schema = new { type = "string", format = "password" } },
                                new { name = ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader, @in = "header", required = true, schema = new { type = "string" } },
                                new { name = ApiEndpointRouteBuilderExtensions.SensitiveOperationTicketHeader, @in = "header", required = true, schema = new { type = "string" } }
                            },
                            requestBody = new { required = true, content = BinaryContent() },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Migration restore was staged for the next service start.", content = JsonContent("ApiServerMigrationRestoreResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Administrator settings permission is required." },
                                ["400"] = new { description = "The package, password, deployment mode, or confirmation is invalid." },
                                ["409"] = new { description = "Another migration restore is already pending." },
                                ["503"] = new { description = "Migration validation, PostgreSQL tools, or staging is unavailable." },
                                ["413"] = new { description = "The migration package exceeds the upload limit." }
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
                                ["404"] = new { description = "The target user does not exist or is inactive." },
                                ["409"] = new { description = "Ownership rows were changed concurrently." },
                                ["503"] = new { description = "The shared database is unavailable." }
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
                                ["403"] = new { description = "The current user cannot create support packages or the desktop token is invalid." },
                                ["503"] = new { description = "The runtime filesystem or optional database backup is unavailable." }
                            }
                        }
                    },
                    ["/api/support-package/download"] = new
                    {
                        post = new
                        {
                            summary = "Queue diagnostic support package for native streaming download",
                            operationId = "downloadSupportPackage",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSupportPackageRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["202"] = new
                                {
                                    description = "The redacted diagnostic support ZIP job was accepted.",
                                    content = JsonContent("BackgroundJobSnapshot")
                                },
                                ["400"] = new { description = "Optional files require explicit confirmation text." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot download support packages." },
                                ["426"] = new { description = "Support packages containing optional database backups or sample files require HTTPS or an explicitly trusted HTTP deployment." },
                                ["429"] = new { description = "The background job queue is full." }
                            }
                        }
                    },
            };
    }
}
