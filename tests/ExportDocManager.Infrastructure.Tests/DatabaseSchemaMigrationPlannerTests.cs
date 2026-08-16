using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class DatabaseSchemaMigrationPlannerTests
{
    [Fact]
    public void CurrentBaseline_ShouldProduceNoMigrationSteps()
    {
        IReadOnlyList<DatabaseSchemaMigrationStep> plan =
            DatabaseSchemaMigrationPlanner.BuildPlan(
                DatabaseSchemaBaseline.CurrentVersion,
                DatabaseSchemaBaseline.CurrentVersion);

        Assert.Empty(plan);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(1)]
    public void PreReleaseVersions_ShouldBeRejectedWithoutCompatibilitySteps(int actualVersion)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseSchemaMigrationPlanner.BuildPlan(actualVersion, DatabaseSchemaBaseline.CurrentVersion));

        Assert.Contains("forward-only", exception.Message, StringComparison.Ordinal);
        Assert.Contains("空数据库重新初始化", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FutureDatabaseVersions_ShouldNeverBeDowngraded()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseSchemaMigrationPlanner.BuildPlan(
                DatabaseSchemaBaseline.CurrentVersion + 1,
                DatabaseSchemaBaseline.CurrentVersion));

        Assert.Contains("不支持降级", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSqlTrigramFeature_ShouldHaveExplicitOptionalVersion()
    {
        Assert.Equal("postgresql.pg_trgm", DatabaseSchemaBaseline.PostgreSqlTrigramFeatureName);
        Assert.Equal(1, DatabaseSchemaBaseline.PostgreSqlTrigramFeatureVersion);
    }
}
