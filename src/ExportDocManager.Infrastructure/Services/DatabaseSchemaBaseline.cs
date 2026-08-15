using System.Data;
using System.Data.Common;
using ExportDocManager.DataAccess;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;

namespace ExportDocManager.Services.Infrastructure
{
    /// <summary>
    /// Owns the current production database baseline. Pre-release databases without an explicit
    /// schema marker are intentionally rejected instead of being upgraded through compatibility SQL.
    /// </summary>
    internal static class DatabaseSchemaBaseline
    {
        internal const int CurrentVersion = 8;
        internal const string MetadataTableName = "__ExportDocManagerSchema";

        public static async Task EnsureCurrentAsync(
            AppDbContext context,
            bool usesPostgreSql,
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
                    await CreatePostgreSqlTrigramIndexesAsync(context, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            int? version = await ReadVersionAsync(context, usesPostgreSql, cancellationToken).ConfigureAwait(false);
            if (version != CurrentVersion)
            {
                string detected = version.HasValue ? $"v{version.Value}" : "无版本标记";
                throw new InvalidOperationException(
                    $"当前数据库为{detected}，程序只接受正式 v{CurrentVersion} 基线。项目尚未投产，不执行旧结构兼容升级；请先备份需要保留的文件，再使用空数据库重新初始化。");
            }

            if (usesPostgreSql)
            {
                await CreatePostgreSqlTrigramIndexesAsync(context, cancellationToken).ConfigureAwait(false);
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
                    DROP INDEX IF EXISTS "IX_HsCodes_TextSearch_Trgm";
                    DROP INDEX IF EXISTS "IX_HsCodeDeclarationExamples_TextSearch_Trgm";
                    DROP INDEX IF EXISTS "IX_HsCodeRemoteCandidates_TextSearch_Trgm";
                    DROP INDEX IF EXISTS "IX_Products_HistorySearch_Trgm";
                    DROP INDEX IF EXISTS "IX_Items_HistorySearch_Trgm";
                    DROP INDEX IF EXISTS "IX_CustomsCooItems_HistorySearch_Trgm";

                    CREATE INDEX IF NOT EXISTS "IX_HsCodes_TextSearch_Upper_Trgm"
                        ON "HsCodes" USING gin (
                            upper("Name") gin_trgm_ops,
                            upper("Elements") gin_trgm_ops,
                            upper("Description") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeDeclarationExamples_TextSearch_Upper_Trgm"
                        ON "HsCodeDeclarationExamples" USING gin (
                            upper("ProductName") gin_trgm_ops,
                            upper("Specification") gin_trgm_ops,
                            upper("SearchText") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_HsCodeRemoteCandidates_TextSearch_Upper_Trgm"
                        ON "HsCodeRemoteCandidates" USING gin (
                            upper("ProductName") gin_trgm_ops,
                            upper("Specification") gin_trgm_ops,
                            upper("QueryText") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Products_HistorySearch_Upper_Trgm"
                        ON "Products" USING gin (
                            upper("ProductCode") gin_trgm_ops,
                            upper("NameCN") gin_trgm_ops,
                            upper("NameEN") gin_trgm_ops,
                            upper("Material") gin_trgm_ops,
                            upper("Brand") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Items_HistorySearch_Upper_Trgm"
                        ON "Items" USING gin (
                            upper("StyleNo") gin_trgm_ops,
                            upper("StyleNameCN") gin_trgm_ops,
                            upper("StyleName") gin_trgm_ops,
                            upper("FabricComposition") gin_trgm_ops,
                            upper("Brand") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_CustomsCooItems_HistorySearch_Upper_Trgm"
                        ON "CustomsCooItems" USING gin (
                            upper("SourceStyleNo") gin_trgm_ops,
                            upper("GoodsName") gin_trgm_ops,
                            upper("GoodsNameE") gin_trgm_ops,
                            upper("GoodsDesc") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Invoices_TextSearch_Upper_Trgm"
                        ON "Invoices" USING gin (
                            upper("InvoiceNo") gin_trgm_ops,
                            upper("ContractNo") gin_trgm_ops,
                            upper("CustomerNameEN") gin_trgm_ops,
                            upper("NotifyPartyName") gin_trgm_ops,
                            upper("ExporterNameEN") gin_trgm_ops,
                            upper("ExporterNameCN") gin_trgm_ops,
                            upper("PortOfLoading") gin_trgm_ops,
                            upper("PortOfDestination") gin_trgm_ops,
                            upper("DestinationCountry") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Payments_TextSearch_Upper_Trgm"
                        ON "Payments" USING gin (
                            upper("InvoiceNo") gin_trgm_ops,
                            upper("PayerName") gin_trgm_ops,
                            upper("Project") gin_trgm_ops,
                            upper("Department") gin_trgm_ops,
                            upper("PayeeName") gin_trgm_ops,
                            upper("BankName") gin_trgm_ops,
                            upper("AccountNo") gin_trgm_ops,
                            upper("GoodsName") gin_trgm_ops,
                            upper("ShipmentCountry") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Customers_TextSearch_Upper_Trgm"
                        ON "Customers" USING gin (
                            upper("CustomerNameEN") gin_trgm_ops,
                            upper("NotifyPartyName") gin_trgm_ops,
                            upper("ContactPerson") gin_trgm_ops,
                            upper("Phone") gin_trgm_ops,
                            upper("Email") gin_trgm_ops,
                            upper("TaxId") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Exporters_TextSearch_Upper_Trgm"
                        ON "Exporters" USING gin (
                            upper("ExporterNameEN") gin_trgm_ops,
                            upper("ExporterNameCN") gin_trgm_ops,
                            upper("ContactPerson") gin_trgm_ops,
                            upper("CreditCode") gin_trgm_ops,
                            upper("CustomsCode") gin_trgm_ops,
                            upper("Phone") gin_trgm_ops,
                            upper("BankName") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_Payees_TextSearch_Upper_Trgm"
                        ON "Payees" USING gin (
                            upper("Category") gin_trgm_ops,
                            upper("Name") gin_trgm_ops,
                            upper("BankName") gin_trgm_ops,
                            upper("RMBAccount") gin_trgm_ops,
                            upper("USDAccount") gin_trgm_ops,
                            upper("ContactPerson") gin_trgm_ops,
                            upper("Phone") gin_trgm_ops,
                            upper("Notes") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_CrmCustomers_TextSearch_Upper_Trgm"
                        ON "CrmCustomers" USING gin (
                            upper("Name") gin_trgm_ops,
                            upper("CountryRegion") gin_trgm_ops,
                            upper("Website") gin_trgm_ops,
                            upper("Source") gin_trgm_ops,
                            upper("Notes") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_SupplierCompanies_TextSearch_Upper_Trgm"
                        ON "SupplierCompanies" USING gin (
                            upper("Name") gin_trgm_ops,
                            upper("CountryRegion") gin_trgm_ops,
                            upper("Category") gin_trgm_ops,
                            upper("MainProducts") gin_trgm_ops,
                            upper("Notes") gin_trgm_ops);
                    CREATE INDEX IF NOT EXISTS "IX_CustomsCooProducerProfiles_TextSearch_Upper_Trgm"
                        ON "CustomsCooProducerProfiles" USING gin (
                            upper("CiqRegNo") gin_trgm_ops,
                            upper("PrdcEtpsName") gin_trgm_ops,
                            upper("PrdcEtpsConcEr") gin_trgm_ops,
                            upper("PrdcEtpsTel") gin_trgm_ops,
                            upper("Producer") gin_trgm_ops,
                            upper("ProducerTel") gin_trgm_ops,
                            upper("ProducerEmail") gin_trgm_ops,
                            upper("LastInvoiceNo") gin_trgm_ops,
                            upper("LastSourceStyleNo") gin_trgm_ops);
                    """,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState is "42501" or "0A000" or "58P01")
            {
                Log.Warning(
                    ex,
                    "PostgreSQL pg_trgm indexes were not installed. Exact and prefix indexes remain active; contains searches use the stable fallback.");
            }
        }
    }
}
