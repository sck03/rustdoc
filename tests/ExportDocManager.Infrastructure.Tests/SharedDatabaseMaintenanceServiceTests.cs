using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SharedDatabaseMaintenanceServiceTests
    {
        [Theory]
        [InlineData("pg_dump (PostgreSQL) 16.4", 16)]
        [InlineData("PostgreSQL 15.8 (Debian 15.8-1)", 15)]
        [InlineData("psql (PostgreSQL) 9.6.24", 9)]
        public void ParsePostgreSqlMajorVersion_ShouldReadToolAndServerFormats(string value, int expected)
        {
            Assert.Equal(expected, SharedDatabaseMaintenanceService.ParsePostgreSqlMajorVersion(value, "test"));
        }

        [Fact]
        public void EnsurePgDumpVersionSupported_ShouldRejectOlderClientMajorVersion()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SharedDatabaseMaintenanceService.EnsurePgDumpVersionSupported(15, 16));

            Assert.Contains("低于", exception.Message, StringComparison.Ordinal);
            SharedDatabaseMaintenanceService.EnsurePgDumpVersionSupported(16, 16);
            SharedDatabaseMaintenanceService.EnsurePgDumpVersionSupported(17, 16);
        }

        [Fact]
        public void RestorePlanQuoting_ShouldPreserveArgumentsWithoutShellExpansion()
        {
            Assert.Equal("'C:\\Tools\\O''Brien\\pg_restore.exe'",
                SharedDatabaseMaintenanceService.QuotePowerShellLiteral("C:\\Tools\\O'Brien\\pg_restore.exe"));
            Assert.Equal("'/opt/o'\"'\"'brien/pg_restore'",
                SharedDatabaseMaintenanceService.QuotePosixShellArgument("/opt/o'brien/pg_restore"));
        }

        [Fact]
        public void PostRestoreOwnershipSql_ShouldHandleAllRoutineKindsAndRoleScopedDefaults()
        {
            string sql = SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
                "team_db",
                "app\"role",
                ["legacy_owner"]);

            Assert.Contains("p.prokind AS routine_kind", sql, StringComparison.Ordinal);
            Assert.Contains("ALTER PROCEDURE", sql, StringComparison.Ordinal);
            Assert.Contains("ALTER AGGREGATE", sql, StringComparison.Ordinal);
            Assert.Contains("ALTER FUNCTION", sql, StringComparison.Ordinal);
            Assert.Contains("GRANT EXECUTE ON ALL ROUTINES", sql, StringComparison.Ordinal);
            Assert.Contains("ALTER DEFAULT PRIVILEGES FOR ROLE \"app\"\"role\"", sql, StringComparison.Ordinal);
            Assert.Contains("REASSIGN OWNED BY \"legacy_owner\" TO \"app\"\"role\"", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void PostRestoreOwnershipSql_ShouldKeepCommentMetadataOnSingleCommentLines()
        {
            string sql = SharedDatabaseMaintenanceService.BuildPostRestoreOwnershipSql(
                "team_db\r\nGRANT ALL ON DATABASE postgres TO public;",
                "app_role\nDROP ROLE important_role;",
                []);

            Assert.Contains(
                "-- Target database: team_db GRANT ALL ON DATABASE postgres TO public;",
                sql,
                StringComparison.Ordinal);
            Assert.Contains(
                "-- Application role: app_role DROP ROLE important_role;",
                sql,
                StringComparison.Ordinal);
            Assert.DoesNotContain("\nGRANT ALL ON DATABASE postgres TO public;\n", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("\nDROP ROLE important_role;\n", sql, StringComparison.Ordinal);
        }
    }
}
