using ExportDocManager.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class RelationalExceptionClassifierTests
{
    [Theory]
    [InlineData(1555, true)]
    [InlineData(2067, true)]
    [InlineData(787, false)]
    [InlineData(1299, false)]
    [InlineData(275, false)]
    [InlineData(19, false)]
    public void UniqueConstraint_ShouldNotMisclassifyOtherSqliteConstraints(int extendedCode, bool expected)
    {
        var exception = new DbUpdateException("write failed", new SqliteException("constraint", 19, extendedCode));
        Assert.Equal(expected, RelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, true)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, false)]
    [InlineData(PostgresErrorCodes.NotNullViolation, false)]
    public void UniqueConstraint_ShouldClassifyPostgreSqlByItsExactSqlState(string code, bool expected)
    {
        var exception = new DbUpdateException("write failed", new PostgresException("constraint", "ERROR", "ERROR", code));
        Assert.Equal(expected, RelationalExceptionClassifier.IsUniqueConstraintViolation(exception));
    }
}
