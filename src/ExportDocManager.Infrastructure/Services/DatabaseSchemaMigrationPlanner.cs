namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// Describes the only schema transition policy used before production: a database must already
/// be the current baseline. The registry is intentionally empty until a reviewed production
/// migration is added; no compatibility SQL is generated implicitly.
/// </summary>
internal static class DatabaseSchemaMigrationPlanner
{
    private static readonly IReadOnlyList<DatabaseSchemaMigrationStep> RegisteredSteps = [];

    internal static IReadOnlyList<DatabaseSchemaMigrationStep> BuildPlan(int actualVersion, int targetVersion)
    {
        if (actualVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualVersion));
        }

        if (targetVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVersion));
        }

        if (actualVersion == targetVersion)
        {
            return [];
        }

        if (actualVersion > targetVersion)
        {
            throw new InvalidOperationException(
                $"数据库架构 v{actualVersion} 高于当前程序 v{targetVersion}，不支持降级或回退旧程序。请先使用匹配版本完成导出，再重新初始化当前版本。");
        }

        var plan = new List<DatabaseSchemaMigrationStep>();
        int currentVersion = actualVersion;
        while (currentVersion < targetVersion)
        {
            int stepIndex = -1;
            for (int index = 0; index < RegisteredSteps.Count; index++)
            {
                if (RegisteredSteps[index].FromVersion == currentVersion)
                {
                    stepIndex = index;
                    break;
                }
            }

            if (stepIndex < 0)
            {
                throw new InvalidOperationException(
                    $"数据库架构 v{actualVersion} 没有注册到 v{targetVersion} 的 forward-only 迁移。项目尚未投产，不执行旧结构兼容升级；请先备份需要保留的数据，再使用空数据库重新初始化。");
            }

            DatabaseSchemaMigrationStep step = RegisteredSteps[stepIndex];
            if (step.ToVersion <= currentVersion || step.ToVersion > targetVersion)
            {
                throw new InvalidOperationException(
                    $"数据库迁移注册表包含无效步骤 v{step.FromVersion}->v{step.ToVersion}。");
            }

            plan.Add(step);
            currentVersion = step.ToVersion;
        }

        return plan;
    }
}

internal readonly record struct DatabaseSchemaMigrationStep(
    int FromVersion,
    int ToVersion,
    string Name);
