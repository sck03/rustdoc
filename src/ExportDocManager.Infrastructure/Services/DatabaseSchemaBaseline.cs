using System.Data;
using System.Data.Common;
using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExportDocManager.Services.Infrastructure
{
    /// <summary>
    /// Owns the current production database baseline. Pre-release databases without an explicit
    /// schema marker are intentionally rejected instead of being upgraded through compatibility SQL.
    /// </summary>
    internal static partial class DatabaseSchemaBaseline
    {
        internal const int CurrentVersion = 9;
        internal const string MetadataTableName = "__ExportDocManagerSchema";
        internal const string PostgreSqlTrigramFeatureName = "postgresql.pg_trgm";
        internal const int PostgreSqlTrigramFeatureVersion = 2;

        public static async Task EnsureCurrentAsync(
            AppDbContext context,
            bool usesPostgreSql,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!context.Database.IsRelational())
            {
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            int tableCount = await CountApplicationTablesAsync(context, usesPostgreSql, cancellationToken)
                .ConfigureAwait(false);
            if (tableCount == 0)
            {
                await using var transaction = await context.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    string createScript = context.Database.GenerateCreateScript();
                    if (string.IsNullOrWhiteSpace(createScript))
                    {
                        throw new InvalidOperationException(
                            $"无法生成 ExportDocManager v{CurrentVersion} 数据库基线脚本。");
                    }

                    await context.Database.ExecuteSqlRawAsync(createScript, cancellationToken).ConfigureAwait(false);
                    await CreateCorePerformanceIndexesAsync(context, usesPostgreSql, cancellationToken).ConfigureAwait(false);
                    await WriteVersionAsync(context, usesPostgreSql, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    using var rollbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await transaction.RollbackAsync(rollbackTimeout.Token).ConfigureAwait(false);
                    throw;
                }

                if (usesPostgreSql)
                {
                    await CreatePostgreSqlTrigramIndexesAsync(context, logger, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            int? version = await ReadVersionAsync(context, usesPostgreSql, cancellationToken).ConfigureAwait(false);
            if (version != CurrentVersion)
            {
                if (version.HasValue)
                {
                    _ = DatabaseSchemaMigrationPlanner.BuildPlan(version.Value, CurrentVersion);
                }

                string detected = version.HasValue ? $"v{version.Value}" : "无版本标记";
                throw new InvalidOperationException(
                    $"当前数据库为{detected}，程序只接受正式 v{CurrentVersion} 基线。项目尚未投产，不执行旧结构兼容升级；请先备份需要保留的文件，再使用空数据库重新初始化。");
            }

            if (usesPostgreSql)
            {
                await CreatePostgreSqlTrigramIndexesAsync(context, logger, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task<bool> IsDatabaseEmptyAsync(
            AppDbContext context,
            bool usesPostgreSql,
            CancellationToken cancellationToken = default) =>
            await CountApplicationTablesAsync(context, usesPostgreSql, cancellationToken).ConfigureAwait(false) == 0;

        private static async Task<int> CountApplicationTablesAsync(
            AppDbContext context,
            bool usesPostgreSql,
            CancellationToken cancellationToken)
        {
            string sql = usesPostgreSql
                ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'"
                : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            object? value = await ExecuteScalarAsync(context, sql, cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(value ?? 0);
        }

        private static async Task<int?> ReadVersionAsync(
            AppDbContext context,
            bool usesPostgreSql,
            CancellationToken cancellationToken)
        {
            string tableExistsSql = usesPostgreSql
                ? $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '{MetadataTableName}'"
                : $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{MetadataTableName}'";
            int tableExists = Convert.ToInt32(
                await ExecuteScalarAsync(context, tableExistsSql, cancellationToken).ConfigureAwait(false) ?? 0);
            if (tableExists == 0)
            {
                return null;
            }

            object? version = await ExecuteScalarAsync(
                context,
                $"SELECT \"Version\" FROM \"{MetadataTableName}\" WHERE \"Id\" = 1",
                cancellationToken).ConfigureAwait(false);
            return version == null || version == DBNull.Value ? null : Convert.ToInt32(version);
        }

        private static async Task WriteVersionAsync(
            AppDbContext context,
            bool usesPostgreSql,
            CancellationToken cancellationToken)
        {
            string sql = usesPostgreSql
                ? $$"""
                    CREATE TABLE "{{MetadataTableName}}" (
                        "Id" integer PRIMARY KEY,
                        "Version" integer NOT NULL,
                        "AppliedAtUtc" timestamp with time zone NOT NULL
                    );
                    INSERT INTO "{{MetadataTableName}}" ("Id", "Version", "AppliedAtUtc")
                    VALUES (1, {{CurrentVersion}}, CURRENT_TIMESTAMP);
                    """
                : $$"""
                    CREATE TABLE "{{MetadataTableName}}" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_{{MetadataTableName}}" PRIMARY KEY,
                        "Version" INTEGER NOT NULL,
                        "AppliedAtUtc" TEXT NOT NULL
                    );
                    INSERT INTO "{{MetadataTableName}}" ("Id", "Version", "AppliedAtUtc")
                    VALUES (1, {{CurrentVersion}}, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
                    """;
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<object?> ExecuteScalarAsync(
            AppDbContext context,
            string commandText,
            CancellationToken cancellationToken)
        {
            DbConnection connection = context.Database.GetDbConnection();
            bool shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = commandText;
                command.CommandTimeout = 10;
                return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task CreateCorePerformanceIndexesAsync(
            AppDbContext context,
            bool usesPostgreSql,
            CancellationToken cancellationToken)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Invoices_OwnerUserId_InvoiceDate_Id"
                    ON "Invoices" ("OwnerUserId", "InvoiceDate", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Invoices_CompanyScope_DepartmentId_InvoiceDate_Id"
                    ON "Invoices" ("CompanyScope", "DepartmentId", "InvoiceDate", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Customers_OwnerUserId"
                    ON "Customers" ("OwnerUserId");
                CREATE INDEX IF NOT EXISTS "IX_Customers_CompanyScope_DepartmentId"
                    ON "Customers" ("CompanyScope", "DepartmentId");
                CREATE INDEX IF NOT EXISTS "IX_Exporters_OwnerUserId"
                    ON "Exporters" ("OwnerUserId");
                CREATE INDEX IF NOT EXISTS "IX_Exporters_CompanyScope_DepartmentId"
                    ON "Exporters" ("CompanyScope", "DepartmentId");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_StyleNo"
                    ON "Items" ("InvoiceId", "StyleNo");
                CREATE INDEX IF NOT EXISTS "IX_Items_HSCode"
                    ON "Items" ("HSCode");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_Id"
                    ON "Items" ("InvoiceId", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_StyleName"
                    ON "Items" ("InvoiceId", "StyleName");
                CREATE INDEX IF NOT EXISTS "IX_Items_InvoiceId_HSCode"
                    ON "Items" ("InvoiceId", "HSCode");
                CREATE INDEX IF NOT EXISTS "IX_Products_ProductCode_NameEN_UpdatedAt_Id"
                    ON "Products" ("ProductCode", "NameEN", "UpdatedAt", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Products_HSCode"
                    ON "Products" ("HSCode");
                CREATE INDEX IF NOT EXISTS "IX_Products_UpdatedAt_Id"
                    ON "Products" ("UpdatedAt", "Id");
                CREATE INDEX IF NOT EXISTS "IX_CustomsCooItems_HSCode"
                    ON "CustomsCooItems" ("HSCode");
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_IsManuallyVerified_UpdatedAt"
                    ON "HsCodeDeclarationExamples" ("IsManuallyVerified", "UpdatedAt");
                """,
                cancellationToken).ConfigureAwait(false);

            if (!usesPostgreSql)
            {
                await CreateSqliteSearchIndexesAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_HsCodes_Status_NormalizedCode_Prefix"
                    ON "HsCodes" ("Status", "NormalizedCode" varchar_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_RawCode_Prefix"
                    ON "HsCodeDeclarationExamples" ("RawReportedHsCode" varchar_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_CurrentCode_Prefix"
                    ON "HsCodeDeclarationExamples" ("ResolvedCurrentHsCode" varchar_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_Status_RawCode_Prefix"
                    ON "HsCodeRemoteCandidates" ("ReviewStatus", "RawReportedHsCode" varchar_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_Status_CurrentCode_Prefix"
                    ON "HsCodeRemoteCandidates" ("ReviewStatus", "SuggestedCurrentHsCode" varchar_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_Products_HSCode_Prefix"
                    ON "Products" ("HSCode" text_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_Items_HSCode_Prefix"
                    ON "Items" ("HSCode" text_pattern_ops);
                CREATE INDEX IF NOT EXISTS "IX_CustomsCooItems_HSCode_Prefix"
                    ON "CustomsCooItems" ("HSCode" text_pattern_ops);
                """,
                cancellationToken).ConfigureAwait(false);

        }

        private static async Task CreatePostgreSqlTrigramIndexesAsync(
            AppDbContext context,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                        "CREATE EXTENSION IF NOT EXISTS pg_trgm;",
                        cancellationToken)
                    .ConfigureAwait(false);
                await context.Database.ExecuteSqlRawAsync(
                    """
                    CREATE INDEX IF NOT EXISTS "IX_HsCodes_TextSearch_Trgm"
                        ON "HsCodes" USING gin (
                            "Name" gin_trgm_ops,
                            "Elements" gin_trgm_ops,
                            "Description" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_TextSearch_Trgm"
                        ON "HsCodeDeclarationExamples" USING gin (
                            "ProductName" gin_trgm_ops,
                            "Specification" gin_trgm_ops,
                            "SearchText" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_TextSearch_Trgm"
                        ON "HsCodeRemoteCandidates" USING gin (
                            "ProductName" gin_trgm_ops,
                            "Specification" gin_trgm_ops,
                            "QueryText" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Products_HistorySearch_Trgm"
                        ON "Products" USING gin (
                            "ProductCode" gin_trgm_ops,
                            "NameCN" gin_trgm_ops,
                            "NameEN" gin_trgm_ops,
                            "Material" gin_trgm_ops,
                            "Brand" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Items_HistorySearch_Trgm"
                        ON "Items" USING gin (
                            "StyleNo" gin_trgm_ops,
                            "StyleNameCN" gin_trgm_ops,
                            "StyleName" gin_trgm_ops,
                            "FabricComposition" gin_trgm_ops,
                            "Brand" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_CustomsCooItems_HistorySearch_Trgm"
                        ON "CustomsCooItems" USING gin (
                            "SourceStyleNo" gin_trgm_ops,
                            "GoodsName" gin_trgm_ops,
                            "GoodsNameE" gin_trgm_ops,
                            "GoodsDesc" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Invoices_TextSearch_Trgm"
                        ON "Invoices" USING gin (
                            "InvoiceNo" gin_trgm_ops,
                            "ContractNo" gin_trgm_ops,
                            "CustomerNameEN" gin_trgm_ops,
                            "NotifyPartyName" gin_trgm_ops,
                            "ExporterNameEN" gin_trgm_ops,
                            "ExporterNameCN" gin_trgm_ops,
                            "PortOfLoading" gin_trgm_ops,
                            "PortOfDestination" gin_trgm_ops,
                            "DestinationCountry" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Payments_TextSearch_Trgm"
                        ON "Payments" USING gin (
                            "InvoiceNo" gin_trgm_ops,
                            "PayerName" gin_trgm_ops,
                            "Project" gin_trgm_ops,
                            "Department" gin_trgm_ops,
                            "PayeeName" gin_trgm_ops,
                            "BankName" gin_trgm_ops,
                            "AccountNo" gin_trgm_ops,
                            "GoodsName" gin_trgm_ops,
                            "ShipmentCountry" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Customers_TextSearch_Trgm"
                        ON "Customers" USING gin (
                            "CustomerNameEN" gin_trgm_ops,
                            "NotifyPartyName" gin_trgm_ops,
                            "ContactPerson" gin_trgm_ops,
                            "Phone" gin_trgm_ops,
                            "Email" gin_trgm_ops,
                            "TaxId" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Exporters_TextSearch_Trgm"
                        ON "Exporters" USING gin (
                            "ExporterNameEN" gin_trgm_ops,
                            "ExporterNameCN" gin_trgm_ops,
                            "ContactPerson" gin_trgm_ops,
                            "CreditCode" gin_trgm_ops,
                            "CustomsCode" gin_trgm_ops,
                            "Phone" gin_trgm_ops,
                            "BankName" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Payees_TextSearch_Trgm"
                        ON "Payees" USING gin (
                            "Category" gin_trgm_ops,
                            "Name" gin_trgm_ops,
                            "BankName" gin_trgm_ops,
                            "RMBAccount" gin_trgm_ops,
                            "USDAccount" gin_trgm_ops,
                            "ContactPerson" gin_trgm_ops,
                            "Phone" gin_trgm_ops,
                            "Notes" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_CrmCustomers_TextSearch_Trgm"
                        ON "CrmCustomers" USING gin (
                            "Name" gin_trgm_ops,
                            "CountryRegion" gin_trgm_ops,
                            "Website" gin_trgm_ops,
                            "Source" gin_trgm_ops,
                            "Notes" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_SupplierCompanies_TextSearch_Trgm"
                        ON "SupplierCompanies" USING gin (
                            "Name" gin_trgm_ops,
                            "CountryRegion" gin_trgm_ops,
                            "Category" gin_trgm_ops,
                            "MainProducts" gin_trgm_ops,
                            "Notes" gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_CustomsCooProducerProfiles_TextSearch_Trgm"
                        ON "CustomsCooProducerProfiles" USING gin (
                            "CiqRegNo" gin_trgm_ops,
                            "PrdcEtpsName" gin_trgm_ops,
                            "PrdcEtpsConcEr" gin_trgm_ops,
                            "PrdcEtpsTel" gin_trgm_ops,
                            "Producer" gin_trgm_ops,
                            "ProducerTel" gin_trgm_ops,
                            "ProducerEmail" gin_trgm_ops,
                            "LastInvoiceNo" gin_trgm_ops,
                            "LastSourceStyleNo" gin_trgm_ops);
                    """,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState is "42501" or "0A000" or "58P01")
            {
                logger?.LogWarning(
                    ex,
                    "PostgreSQL pg_trgm indexes were not installed; optional search feature {FeatureName} v{FeatureVersion} remains unavailable. Exact and prefix indexes remain active; contains searches use the stable fallback.",
                    PostgreSqlTrigramFeatureName,
                    PostgreSqlTrigramFeatureVersion);
            }
        }
    }
}
