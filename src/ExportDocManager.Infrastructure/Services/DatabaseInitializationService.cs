using ExportDocManager.DataAccess;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Services.Infrastructure
{
    public class DatabaseInitializationService : IDatabaseInitializationService
    {
        private const long PostgreSqlInitializationLockId = 73190520260718;
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly DatabaseConnectionSettings _databaseSettings;
        private readonly DatabaseInitializationCoordinator _coordinator;

        public DatabaseInitializationService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            DatabaseConnectionSettings databaseSettings,
            DatabaseInitializationCoordinator coordinator)
        {
            _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        public Task<DatabaseInitializationResult> InitializeAsync(string username, string password)
        {
            return _coordinator.InitializeOnceAsync(() => InitializeCoreAsync(username, password));
        }

        private async Task<DatabaseInitializationResult> InitializeCoreAsync(string username, string password)
        {
            bool usesPostgreSql = DatabaseModeHelper.UsesPostgreSql(_databaseSettings);
            bool advisoryLockAcquired = false;
            AppDbContext context = null;

            try
            {
                context = await _dbContextFactory.CreateDbContextAsync();
                if (usesPostgreSql)
                {
                    await context.Database.OpenConnectionAsync().ConfigureAwait(false);
                    await context.Database.ExecuteSqlRawAsync(
                        $"SELECT pg_advisory_lock({PostgreSqlInitializationLockId});").ConfigureAwait(false);
                    advisoryLockAcquired = true;
                }

                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
                if (!usesPostgreSql)
                {
                    await ConfigureSingleProcessSqliteAsync(context).ConfigureAwait(false);
                }
                await EnsureInvoiceTypeSchemaAsync(context, usesPostgreSql).ConfigureAwait(false);
                await EnsureUserReportTemplateSchemaAsync(context, usesPostgreSql).ConfigureAwait(false);
                await EnsureContainerProjectOwnershipSchemaAsync(context, usesPostgreSql).ConfigureAwait(false);
                await EnsureSharedMasterDataConcurrencySchemaAsync(context, usesPostgreSql).ConfigureAwait(false);
                await EnsureHsCodeMetadataSchemaAsync(context, usesPostgreSql).ConfigureAwait(false);
                await EnsureHsCodeKnowledgeSchemaAsync(context, usesPostgreSql).ConfigureAwait(false);
                await EnsureQueryPerformanceIndexesAsync(context).ConfigureAwait(false);
                DbSeeder.SeedAuxiliaryData(
                    context,
                    _databaseSettings,
                    ResolveInitialAdminPassword(usesPostgreSql, username, password));

                return DatabaseInitializationResult.Success();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return DatabaseInitializationResult.Fail(
                    "数据库结构升级失败：同一发票号存在无法区分类型的重复记录，请先备份数据库并清理重复发票后重试。\n\n" + ex.Message,
                    shouldResetPassword: false);
            }
            catch (InvalidOperationException ex) when (usesPostgreSql)
            {
                return DatabaseInitializationResult.Fail(ex.Message, shouldResetPassword: false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return DatabaseInitializationResult.Fail(
                    "共享数据库结构升级失败：同一发票号存在无法区分类型的重复记录，请先备份数据库并清理重复发票后重试。\n\n" + ex.MessageText,
                    shouldResetPassword: false);
            }
            catch (NpgsqlException ex) when (usesPostgreSql)
            {
                return DatabaseInitializationResult.Fail(
                    "连接共享数据库失败，请检查 PostgreSQL 服务器地址、端口、数据库名、账号密码、网络可达性和数据库权限。\n\n" + ex.Message,
                    shouldResetPassword: false);
            }
            catch (DbException ex) when (usesPostgreSql)
            {
                return DatabaseInitializationResult.Fail(
                    "连接共享数据库失败，请检查数据库服务状态和连接配置。\n\n" + ex.Message,
                shouldResetPassword: false);
            }
            finally
            {
                if (advisoryLockAcquired)
                {
                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(
                            $"SELECT pg_advisory_unlock({PostgreSqlInitializationLockId});").ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                if (context != null)
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task ConfigureSingleProcessSqliteAsync(AppDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=10000;").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);
        }

        internal static string ResolveInitialAdminPassword(
            bool usesPostgreSql,
            string username,
            string password)
        {
            return usesPostgreSql &&
                   string.Equals((username ?? string.Empty).Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                ? password ?? string.Empty
                : string.Empty;
        }

        private const string DefaultInvoiceType = "实际数据";
        private const string InvoiceNoTypeIndexName = "IX_Invoices_InvoiceNo_Type";

        private static async Task EnsureContainerProjectOwnershipSchemaAsync(
            AppDbContext context,
            bool usesPostgreSql)
        {
            if (!context.Database.IsRelational()) return;

            if (usesPostgreSql)
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE "ContainerProjects" ADD COLUMN IF NOT EXISTS "OwnerUserId" integer NULL;
                    ALTER TABLE "ContainerProjects" ADD COLUMN IF NOT EXISTS "DepartmentId" character varying(50) NOT NULL DEFAULT '';
                    ALTER TABLE "ContainerProjects" ADD COLUMN IF NOT EXISTS "CompanyScope" character varying(50) NOT NULL DEFAULT '';
                    ALTER TABLE "ContainerProjects" ADD COLUMN IF NOT EXISTS "VersionNumber" integer NOT NULL DEFAULT 1;
                    CREATE INDEX IF NOT EXISTS "IX_ContainerProjects_OwnerUserId_UpdatedAt"
                        ON "ContainerProjects" ("OwnerUserId", "UpdatedAt");
                    """).ConfigureAwait(false);
                return;
            }

            await AddSqliteColumnIfMissingAsync(context, "ContainerProjects", "OwnerUserId", "INTEGER NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "ContainerProjects", "DepartmentId", "TEXT NOT NULL DEFAULT ''").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "ContainerProjects", "CompanyScope", "TEXT NOT NULL DEFAULT ''").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "ContainerProjects", "VersionNumber", "INTEGER NOT NULL DEFAULT 1").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS \"IX_ContainerProjects_OwnerUserId_UpdatedAt\" ON \"ContainerProjects\" (\"OwnerUserId\", \"UpdatedAt\")")
                .ConfigureAwait(false);
        }

        private static async Task EnsureSharedMasterDataConcurrencySchemaAsync(
            AppDbContext context,
            bool usesPostgreSql)
        {
            if (!context.Database.IsRelational()) return;

            if (usesPostgreSql)
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;
                    ALTER TABLE "Payees" ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;
                    ALTER TABLE "Ports" ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;
                    ALTER TABLE "Units" ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "RowVersion" bytea NULL;
                    """).ConfigureAwait(false);
                return;
            }

            string[] tableNames = ["Products", "Payees", "Ports", "Units", "HsCodes"];
            foreach (string tableName in tableNames)
            {
                await AddSqliteColumnIfMissingAsync(context, tableName, "RowVersion", "BLOB NULL")
                    .ConfigureAwait(false);
            }
        }

        private static async Task EnsureHsCodeMetadataSchemaAsync(AppDbContext context, bool usesPostgreSql)
        {
            if (!context.Database.IsRelational())
            {
                return;
            }

            if (usesPostgreSql)
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "Status" character varying(30) NOT NULL DEFAULT 'Active';
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "SourceName" character varying(200) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "EffectiveYear" integer NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "LastVerifiedAt" timestamp with time zone NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "ReplacedByCodes" character varying(500) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "NormalTariffRate" character varying(50) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "PreferentialTariffRate" character varying(50) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "ExportTariffRate" character varying(50) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "ConsumptionTaxRate" character varying(50) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "ValueAddedTaxRate" character varying(50) NULL;
                    ALTER TABLE "HsCodes" ADD COLUMN IF NOT EXISTS "Notes" character varying(1000) NULL;
                    CREATE INDEX IF NOT EXISTS "IX_HsCodes_Status" ON "HsCodes" ("Status");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodes_EffectiveYear_Status" ON "HsCodes" ("EffectiveYear", "Status");
                    """).ConfigureAwait(false);
                return;
            }

            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "Status", "TEXT NOT NULL DEFAULT 'Active'").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "SourceName", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "EffectiveYear", "INTEGER NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "LastVerifiedAt", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "ReplacedByCodes", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "NormalTariffRate", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "PreferentialTariffRate", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "ExportTariffRate", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "ConsumptionTaxRate", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "ValueAddedTaxRate", "TEXT NULL").ConfigureAwait(false);
            await AddSqliteColumnIfMissingAsync(context, "HsCodes", "Notes", "TEXT NULL").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_HsCodes_Status\" ON \"HsCodes\" (\"Status\")").ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_HsCodes_EffectiveYear_Status\" ON \"HsCodes\" (\"EffectiveYear\", \"Status\")").ConfigureAwait(false);
        }

        private static async Task EnsureHsCodeKnowledgeSchemaAsync(AppDbContext context, bool usesPostgreSql)
        {
            if (!context.Database.IsRelational()) return;
            if (usesPostgreSql)
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "HsCodeDeclarationExamples" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "Fingerprint" character varying(64) NOT NULL,
                        "RawReportedHsCode" character varying(20) NOT NULL,
                        "ResolvedCurrentHsCode" character varying(20) NULL,
                        "ProductName" character varying(300) NOT NULL,
                        "Specification" character varying(1500) NULL,
                        "SearchText" character varying(2000) NOT NULL,
                        "Source" character varying(100) NOT NULL,
                        "SourceYear" integer NULL,
                        "ResolutionStatus" character varying(30) NOT NULL,
                        "IsManuallyVerified" boolean NOT NULL DEFAULT FALSE,
                        "UseCount" integer NOT NULL DEFAULT 0,
                        "RejectedCount" integer NOT NULL DEFAULT 0,
                        "LastUsedAt" timestamp with time zone NULL,
                        "CreatedAt" timestamp with time zone NOT NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_Fingerprint" ON "HsCodeDeclarationExamples" ("Fingerprint");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_RawReportedHsCode" ON "HsCodeDeclarationExamples" ("RawReportedHsCode");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_ResolvedCurrentHsCode" ON "HsCodeDeclarationExamples" ("ResolvedCurrentHsCode");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_ResolutionStatus_UpdatedAt" ON "HsCodeDeclarationExamples" ("ResolutionStatus", "UpdatedAt");
                    CREATE TABLE IF NOT EXISTS "HsCodeReplacementRelations" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "OldCode" character varying(20) NOT NULL,
                        "NewCode" character varying(20) NOT NULL,
                        "EffectiveYear" integer NULL,
                        "Source" character varying(100) NOT NULL,
                        "Confidence" integer NOT NULL DEFAULT 0,
                        "IsManuallyVerified" boolean NOT NULL DEFAULT FALSE,
                        "CreatedAt" timestamp with time zone NOT NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeReplacementRelations_OldCode_NewCode_EffectiveYear" ON "HsCodeReplacementRelations" ("OldCode", "NewCode", "EffectiveYear");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeReplacementRelations_OldCode" ON "HsCodeReplacementRelations" ("OldCode");
                    CREATE TABLE IF NOT EXISTS "HsCodeSearchFeedback" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "Fingerprint" character varying(64) NOT NULL,
                        "QueryText" character varying(500) NOT NULL,
                        "ProductName" character varying(300) NULL,
                        "Specification" character varying(1500) NULL,
                        "CandidateCode" character varying(20) NOT NULL,
                        "AcceptedCount" integer NOT NULL DEFAULT 0,
                        "RejectedCount" integer NOT NULL DEFAULT 0,
                        "LastConfirmedAt" timestamp with time zone NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeSearchFeedback_Fingerprint" ON "HsCodeSearchFeedback" ("Fingerprint");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeSearchFeedback_CandidateCode" ON "HsCodeSearchFeedback" ("CandidateCode");
                    CREATE TABLE IF NOT EXISTS "HsCodeRemoteCandidates" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "Fingerprint" character varying(64) NOT NULL, "QueryText" character varying(500) NOT NULL,
                        "RawReportedHsCode" character varying(20) NOT NULL, "SuggestedCurrentHsCode" character varying(20) NULL,
                        "ProductName" character varying(300) NOT NULL, "Specification" character varying(1500) NULL,
                        "Source" character varying(100) NOT NULL, "SourceUrl" character varying(1000) NULL,
                        "ReviewStatus" character varying(30) NOT NULL, "ResolutionStatus" character varying(30) NOT NULL,
                        "SeenCount" integer NOT NULL DEFAULT 1, "FirstSeenAt" timestamp with time zone NOT NULL,
                        "LastSeenAt" timestamp with time zone NOT NULL, "ReviewedAt" timestamp with time zone NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_Fingerprint" ON "HsCodeRemoteCandidates" ("Fingerprint");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_ReviewStatus_LastSeenAt" ON "HsCodeRemoteCandidates" ("ReviewStatus", "LastSeenAt");
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_RawReportedHsCode" ON "HsCodeRemoteCandidates" ("RawReportedHsCode");
                    """).ConfigureAwait(false);
                return;
            }

            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "HsCodeDeclarationExamples" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_HsCodeDeclarationExamples" PRIMARY KEY AUTOINCREMENT,
                    "Fingerprint" TEXT NOT NULL, "RawReportedHsCode" TEXT NOT NULL, "ResolvedCurrentHsCode" TEXT NULL,
                    "ProductName" TEXT NOT NULL, "Specification" TEXT NULL, "SearchText" TEXT NOT NULL,
                    "Source" TEXT NOT NULL, "SourceYear" INTEGER NULL, "ResolutionStatus" TEXT NOT NULL,
                    "IsManuallyVerified" INTEGER NOT NULL DEFAULT 0, "UseCount" INTEGER NOT NULL DEFAULT 0,
                    "RejectedCount" INTEGER NOT NULL DEFAULT 0, "LastUsedAt" TEXT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_Fingerprint" ON "HsCodeDeclarationExamples" ("Fingerprint");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_RawReportedHsCode" ON "HsCodeDeclarationExamples" ("RawReportedHsCode");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_ResolvedCurrentHsCode" ON "HsCodeDeclarationExamples" ("ResolvedCurrentHsCode");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_ResolutionStatus_UpdatedAt" ON "HsCodeDeclarationExamples" ("ResolutionStatus", "UpdatedAt");
                CREATE TABLE IF NOT EXISTS "HsCodeReplacementRelations" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_HsCodeReplacementRelations" PRIMARY KEY AUTOINCREMENT,
                    "OldCode" TEXT NOT NULL, "NewCode" TEXT NOT NULL, "EffectiveYear" INTEGER NULL,
                    "Source" TEXT NOT NULL, "Confidence" INTEGER NOT NULL DEFAULT 0, "IsManuallyVerified" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeReplacementRelations_OldCode_NewCode_EffectiveYear" ON "HsCodeReplacementRelations" ("OldCode", "NewCode", "EffectiveYear");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeReplacementRelations_OldCode" ON "HsCodeReplacementRelations" ("OldCode");
                CREATE TABLE IF NOT EXISTS "HsCodeSearchFeedback" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_HsCodeSearchFeedback" PRIMARY KEY AUTOINCREMENT,
                    "Fingerprint" TEXT NOT NULL, "QueryText" TEXT NOT NULL, "ProductName" TEXT NULL,
                    "Specification" TEXT NULL, "CandidateCode" TEXT NOT NULL, "AcceptedCount" INTEGER NOT NULL DEFAULT 0,
                    "RejectedCount" INTEGER NOT NULL DEFAULT 0, "LastConfirmedAt" TEXT NULL, "UpdatedAt" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeSearchFeedback_Fingerprint" ON "HsCodeSearchFeedback" ("Fingerprint");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeSearchFeedback_CandidateCode" ON "HsCodeSearchFeedback" ("CandidateCode");
                CREATE TABLE IF NOT EXISTS "HsCodeRemoteCandidates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_HsCodeRemoteCandidates" PRIMARY KEY AUTOINCREMENT,
                    "Fingerprint" TEXT NOT NULL, "QueryText" TEXT NOT NULL, "RawReportedHsCode" TEXT NOT NULL,
                    "SuggestedCurrentHsCode" TEXT NULL, "ProductName" TEXT NOT NULL, "Specification" TEXT NULL,
                    "Source" TEXT NOT NULL, "SourceUrl" TEXT NULL, "ReviewStatus" TEXT NOT NULL,
                    "ResolutionStatus" TEXT NOT NULL, "SeenCount" INTEGER NOT NULL DEFAULT 1,
                    "FirstSeenAt" TEXT NOT NULL, "LastSeenAt" TEXT NOT NULL, "ReviewedAt" TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_Fingerprint" ON "HsCodeRemoteCandidates" ("Fingerprint");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_ReviewStatus_LastSeenAt" ON "HsCodeRemoteCandidates" ("ReviewStatus", "LastSeenAt");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_RawReportedHsCode" ON "HsCodeRemoteCandidates" ("RawReportedHsCode");
                """).ConfigureAwait(false);
        }

        private static async Task EnsureQueryPerformanceIndexesAsync(AppDbContext context)
        {
            if (!context.Database.IsRelational()) return;

            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Invoices_OwnerUserId_InvoiceDate_Id"
                    ON "Invoices" ("OwnerUserId", "InvoiceDate", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Invoices_CompanyScope_DepartmentId_InvoiceDate_Id"
                    ON "Invoices" ("CompanyScope", "DepartmentId", "InvoiceDate", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_StyleNo"
                    ON "Items" ("InvoiceId", "StyleNo");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_StyleName"
                    ON "Items" ("InvoiceId", "StyleName");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_HSCode"
                    ON "Items" ("InvoiceId", "HSCode");
                CREATE INDEX IF NOT EXISTS "IX_Products_ProductCode_NameEN_UpdatedAt_Id"
                    ON "Products" ("ProductCode", "NameEN", "UpdatedAt", "Id");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_IsManuallyVerified_UpdatedAt"
                    ON "HsCodeDeclarationExamples" ("IsManuallyVerified", "UpdatedAt");
                """).ConfigureAwait(false);
        }

        private static async Task AddSqliteColumnIfMissingAsync(AppDbContext context, string tableName, string columnName, string definition)
        {
            var connection = context.Database.GetDbConnection();
            bool shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync().ConfigureAwait(false);
            }
            try
            {
                bool exists = false;
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"PRAGMA table_info(\"{EscapeSqliteIdentifier(tableName)}\")";
                    await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
                if (!exists)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"ALTER TABLE \"{EscapeSqliteIdentifier(tableName)}\" ADD COLUMN \"{EscapeSqliteIdentifier(columnName)}\" {definition}";
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task EnsureUserReportTemplateSchemaAsync(AppDbContext context, bool usesPostgreSql)
        {
            if (!context.Database.IsRelational())
            {
                return;
            }

            if (usesPostgreSql)
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "UserReportTemplates" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "ReportType" character varying(40) NOT NULL,
                        "Name" character varying(150) NOT NULL,
                        "ContentHtml" text NOT NULL,
                        "OwnerUserId" integer NULL,
                        "DepartmentId" character varying(50) NOT NULL DEFAULT '',
                        "CompanyScope" character varying(50) NOT NULL DEFAULT '',
                        "IsShared" boolean NOT NULL DEFAULT FALSE,
                        "ShareScope" character varying(20) NOT NULL DEFAULT 'Private',
                        "IsActive" boolean NOT NULL DEFAULT TRUE,
                        "VersionNumber" integer NOT NULL DEFAULT 1,
                        "CreatedAt" timestamp with time zone NOT NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS "IX_UserReportTemplates_ReportType_Name_OwnerUserId"
                        ON "UserReportTemplates" ("ReportType", "Name", "OwnerUserId");
                    CREATE INDEX IF NOT EXISTS "IX_UserReportTemplates_CompanyScope_DepartmentId"
                        ON "UserReportTemplates" ("CompanyScope", "DepartmentId");
                    CREATE INDEX IF NOT EXISTS "IX_UserReportTemplates_IsShared_IsActive_ReportType"
                        ON "UserReportTemplates" ("IsShared", "IsActive", "ReportType");
                    CREATE TABLE IF NOT EXISTS "UserReportTemplateVersions" (
                        "Id" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                        "UserReportTemplateId" integer NOT NULL REFERENCES "UserReportTemplates"("Id") ON DELETE CASCADE,
                        "VersionNumber" integer NOT NULL,
                        "ChangeType" character varying(30) NOT NULL,
                        "Name" character varying(150) NOT NULL,
                        "ContentHtml" text NOT NULL,
                        "IsActive" boolean NOT NULL,
                        "IsShared" boolean NOT NULL,
                        "ShareScope" character varying(20) NOT NULL,
                        "ChangedBy" character varying(100) NOT NULL DEFAULT '',
                        "CreatedAt" timestamp with time zone NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserReportTemplateVersions_UserReportTemplateId_VersionNumber"
                        ON "UserReportTemplateVersions" ("UserReportTemplateId", "VersionNumber");
                    """).ConfigureAwait(false);
                return;
            }

            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "UserReportTemplates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserReportTemplates" PRIMARY KEY AUTOINCREMENT,
                    "ReportType" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "ContentHtml" TEXT NOT NULL,
                    "OwnerUserId" INTEGER NULL,
                    "DepartmentId" TEXT NOT NULL DEFAULT '',
                    "CompanyScope" TEXT NOT NULL DEFAULT '',
                    "IsShared" INTEGER NOT NULL DEFAULT 0,
                    "ShareScope" TEXT NOT NULL DEFAULT 'Private',
                    "IsActive" INTEGER NOT NULL DEFAULT 1,
                    "VersionNumber" INTEGER NOT NULL DEFAULT 1,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_UserReportTemplates_ReportType_Name_OwnerUserId"
                    ON "UserReportTemplates" ("ReportType", "Name", "OwnerUserId");
                CREATE INDEX IF NOT EXISTS "IX_UserReportTemplates_CompanyScope_DepartmentId"
                    ON "UserReportTemplates" ("CompanyScope", "DepartmentId");
                CREATE INDEX IF NOT EXISTS "IX_UserReportTemplates_IsShared_IsActive_ReportType"
                    ON "UserReportTemplates" ("IsShared", "IsActive", "ReportType");
                CREATE TABLE IF NOT EXISTS "UserReportTemplateVersions" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserReportTemplateVersions" PRIMARY KEY AUTOINCREMENT,
                    "UserReportTemplateId" INTEGER NOT NULL,
                    "VersionNumber" INTEGER NOT NULL,
                    "ChangeType" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "ContentHtml" TEXT NOT NULL,
                    "IsActive" INTEGER NOT NULL,
                    "IsShared" INTEGER NOT NULL,
                    "ShareScope" TEXT NOT NULL,
                    "ChangedBy" TEXT NOT NULL DEFAULT '',
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_UserReportTemplateVersions_UserReportTemplates_UserReportTemplateId"
                        FOREIGN KEY ("UserReportTemplateId") REFERENCES "UserReportTemplates" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserReportTemplateVersions_UserReportTemplateId_VersionNumber"
                    ON "UserReportTemplateVersions" ("UserReportTemplateId", "VersionNumber");
                """).ConfigureAwait(false);
        }

        private static async Task EnsureInvoiceTypeSchemaAsync(AppDbContext context, bool usesPostgreSql)
        {
            if (!context.Database.IsRelational())
            {
                return;
            }

            if (usesPostgreSql)
            {
                await EnsurePostgreSqlInvoiceTypeSchemaAsync(context).ConfigureAwait(false);
                return;
            }

            await EnsureSqliteInvoiceTypeSchemaAsync(context).ConfigureAwait(false);
        }

        private static async Task EnsureSqliteInvoiceTypeSchemaAsync(AppDbContext context)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Invoices\" SET \"Type\" = {DefaultInvoiceType} WHERE \"Type\" IS NULL OR TRIM(\"Type\") = ''").ConfigureAwait(false);

            await DropLegacySqliteInvoiceNoUniqueIndexesAsync(context).ConfigureAwait(false);

            await context.Database.ExecuteSqlRawAsync(
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"{InvoiceNoTypeIndexName}\" ON \"Invoices\" (\"InvoiceNo\", \"Type\")").ConfigureAwait(false);
        }

        private static async Task EnsurePostgreSqlInvoiceTypeSchemaAsync(AppDbContext context)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Invoices\" SET \"Type\" = {DefaultInvoiceType} WHERE \"Type\" IS NULL OR BTRIM(\"Type\") = ''").ConfigureAwait(false);

            await DropLegacyPostgreSqlInvoiceNoUniqueIndexesAsync(context).ConfigureAwait(false);

            await context.Database.ExecuteSqlRawAsync(
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"{InvoiceNoTypeIndexName}\" ON \"Invoices\" (\"InvoiceNo\", \"Type\")").ConfigureAwait(false);
        }

        private static async Task DropLegacySqliteInvoiceNoUniqueIndexesAsync(AppDbContext context)
        {
            var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync().ConfigureAwait(false);
            }

            try
            {
                var indexNames = new List<string>();
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA index_list('Invoices')";
                    await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        var name = reader["name"]?.ToString() ?? string.Empty;
                        var isUnique = Convert.ToInt32(reader["unique"]) == 1;
                        if (!isUnique ||
                            string.Equals(name, InvoiceNoTypeIndexName, StringComparison.Ordinal) ||
                            name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        indexNames.Add(name);
                    }
                }

                foreach (var indexName in indexNames)
                {
                    var columns = await ReadSqliteIndexColumnsAsync(connection, indexName).ConfigureAwait(false);
                    if (columns.Count == 1 &&
                        string.Equals(columns[0], "InvoiceNo", StringComparison.Ordinal))
                    {
                        await DropSqliteIndexAsync(connection, indexName).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task<IReadOnlyList<string>> ReadSqliteIndexColumnsAsync(DbConnection connection, string indexName)
        {
            var columns = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info({QuoteSqliteString(indexName)})";
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var columnName = reader["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    columns.Add(columnName);
                }
            }

            return columns;
        }

        private static async Task DropSqliteIndexAsync(DbConnection connection, string indexName)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP INDEX \"{EscapeSqliteIdentifier(indexName)}\"";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static async Task DropLegacyPostgreSqlInvoiceNoUniqueIndexesAsync(AppDbContext context)
        {
            var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync().ConfigureAwait(false);
            }

            try
            {
                var indexNames = new List<PostgreSqlIndexName>();
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        SELECT index_namespace.nspname AS schema_name, index_class.relname AS index_name
                        FROM pg_index index_info
                        JOIN pg_class table_class ON table_class.oid = index_info.indrelid
                        JOIN pg_namespace table_namespace ON table_namespace.oid = table_class.relnamespace
                        JOIN pg_class index_class ON index_class.oid = index_info.indexrelid
                        JOIN pg_namespace index_namespace ON index_namespace.oid = index_class.relnamespace
                        WHERE table_class.relname = 'Invoices'
                          AND table_namespace.nspname = current_schema()
                          AND index_info.indisunique = true
                          AND ARRAY(
                              SELECT attribute.attname
                              FROM unnest(index_info.indkey) WITH ORDINALITY AS indexed_column(attnum, ordinal_position)
                              JOIN pg_attribute attribute
                                ON attribute.attrelid = table_class.oid
                               AND attribute.attnum = indexed_column.attnum
                              ORDER BY indexed_column.ordinal_position
                          ) = ARRAY['InvoiceNo']::name[]
                        """;
                    await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        indexNames.Add(new PostgreSqlIndexName(
                            reader["schema_name"]?.ToString() ?? "public",
                            reader["index_name"]?.ToString() ?? string.Empty));
                    }
                }

                foreach (var indexName in indexNames.Where(item =>
                             !string.IsNullOrWhiteSpace(item.IndexName) &&
                             !string.Equals(item.IndexName, InvoiceNoTypeIndexName, StringComparison.Ordinal)))
                {
                    await DropPostgreSqlIndexAsync(connection, indexName).ConfigureAwait(false);
                }
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task DropPostgreSqlIndexAsync(DbConnection connection, PostgreSqlIndexName indexName)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"DROP INDEX IF EXISTS \"{EscapePostgreSqlIdentifier(indexName.SchemaName)}\".\"{EscapePostgreSqlIdentifier(indexName.IndexName)}\"";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        private static string QuoteSqliteString(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static string EscapeSqliteIdentifier(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\"\"");
        }

        private static string EscapePostgreSqlIdentifier(string value)
        {
            return (value ?? string.Empty).Replace("\"", "\"\"");
        }

        private sealed record PostgreSqlIndexName(string SchemaName, string IndexName);
    }
}
