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
        internal const int CurrentVersion = 5;
        internal const string MetadataTableName = "__ExportDocManagerSchema";

        public static async Task EnsureCurrentAsync(AppDbContext context, bool usesPostgreSql)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!context.Database.IsRelational())
            {
                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
                return;
            }

            int tableCount = await CountApplicationTablesAsync(context, usesPostgreSql).ConfigureAwait(false);
            if (tableCount == 0)
            {
                bool created = await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
                if (!created)
                {
                    throw new InvalidOperationException($"数据库不是空库，无法建立 ExportDocManager v{CurrentVersion} 基线。请使用新数据库重新初始化。");
                }

                await CreatePerformanceIndexesAsync(context, usesPostgreSql).ConfigureAwait(false);
                await WriteVersionAsync(context, usesPostgreSql).ConfigureAwait(false);
                return;
            }

            int? version = await ReadVersionAsync(context, usesPostgreSql).ConfigureAwait(false);
            if (version != CurrentVersion)
            {
                string detected = version.HasValue ? $"v{version.Value}" : "无版本标记";
                throw new InvalidOperationException(
                    $"当前数据库为{detected}，程序只接受正式 v{CurrentVersion} 基线。项目尚未投产，不执行旧结构兼容升级；请先备份需要保留的文件，再使用空数据库重新初始化。");
            }
        }

        private static async Task<int> CountApplicationTablesAsync(AppDbContext context, bool usesPostgreSql)
        {
            string sql = usesPostgreSql
                ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_type = 'BASE TABLE'"
                : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            object value = await ExecuteScalarAsync(context, sql).ConfigureAwait(false);
            return Convert.ToInt32(value ?? 0);
        }

        private static async Task<int?> ReadVersionAsync(AppDbContext context, bool usesPostgreSql)
        {
            string tableExistsSql = usesPostgreSql
                ? $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '{MetadataTableName}'"
                : $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{MetadataTableName}'";
            int tableExists = Convert.ToInt32(
                await ExecuteScalarAsync(context, tableExistsSql).ConfigureAwait(false) ?? 0);
            if (tableExists == 0)
            {
                return null;
            }

            object version = await ExecuteScalarAsync(
                context,
                $"SELECT \"Version\" FROM \"{MetadataTableName}\" WHERE \"Id\" = 1").ConfigureAwait(false);
            return version == null || version == DBNull.Value ? null : Convert.ToInt32(version);
        }

        private static async Task WriteVersionAsync(AppDbContext context, bool usesPostgreSql)
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
            await context.Database.ExecuteSqlRawAsync(sql).ConfigureAwait(false);
        }

        private static async Task<object> ExecuteScalarAsync(AppDbContext context, string commandText)
        {
            DbConnection connection = context.Database.GetDbConnection();
            bool shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync().ConfigureAwait(false);
            }

            try
            {
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = commandText;
                return await command.ExecuteScalarAsync().ConfigureAwait(false);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task CreatePerformanceIndexesAsync(AppDbContext context, bool usesPostgreSql)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Invoices_OwnerUserId_InvoiceDate_Id"
                    ON "Invoices" ("OwnerUserId", "InvoiceDate", "Id");
                CREATE INDEX IF NOT EXISTS "IX_Invoices_CompanyScope_DepartmentId_InvoiceDate_Id"
                    ON "Invoices" ("CompanyScope", "DepartmentId", "InvoiceDate", "Id");
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
                """).ConfigureAwait(false);

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
                """).ConfigureAwait(false);

            await CreatePostgreSqlTrigramIndexesAsync(context).ConfigureAwait(false);
        }

        private static async Task CreatePostgreSqlTrigramIndexesAsync(AppDbContext context)
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;")
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
                    """).ConfigureAwait(false);
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
