namespace ExportDocManager.Services.Infrastructure
{
    public sealed class RuntimeDependencyDiagnosticsService : IRuntimeDependencyDiagnosticsService
    {
        private readonly IAppPathProvider _pathProvider;
        private readonly IReadOnlyList<IRuntimeDependencyDiagnosticContributor> _contributors;

        public RuntimeDependencyDiagnosticsService(IAppPathProvider pathProvider)
            : this(pathProvider, [])
        {
        }

        public RuntimeDependencyDiagnosticsService(
            IAppPathProvider pathProvider,
            IEnumerable<IRuntimeDependencyDiagnosticContributor> contributors)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _contributors = (contributors ?? [])
                .Where(contributor => contributor != null)
                .ToList();
        }

        public IReadOnlyList<RuntimeDependencyDiagnostic> Inspect()
        {
            var diagnostics = new List<RuntimeDependencyDiagnostic>();
            foreach (var contributor in _contributors)
            {
                diagnostics.AddRange(contributor.Inspect());
            }
            diagnostics.Add(InspectPostgreSqlTools());
            return diagnostics;
        }

        private RuntimeDependencyDiagnostic InspectPostgreSqlTools()
        {
            PostgreSqlToolPaths tools = PostgreSqlToolLocator.Resolve(_pathProvider);
            string resolvedPath = string.IsNullOrWhiteSpace(tools.BinRoot)
                ? Path.Combine(_pathProvider.ToolRoot, "PostgreSQL", "bin")
                : tools.BinRoot;

            if (tools.ToolsReady)
            {
                return new RuntimeDependencyDiagnostic(
                    "postgresql-tools",
                    "PostgreSQL 维护工具",
                    "optional",
                    "ready",
                    true,
                    Path.GetFullPath(resolvedPath),
                    "pg_dump、pg_restore 和 psql 已就绪。");
            }

            if (tools.AvailableToolCount > 0)
            {
                bool completeButIncompatible = tools.AvailableToolCount == 3;
                return new RuntimeDependencyDiagnostic(
                    "postgresql-tools",
                    "PostgreSQL 维护工具",
                    "optional",
                    completeButIncompatible ? "incompatible" : "incomplete",
                    false,
                    Path.GetFullPath(resolvedPath),
                    completeButIncompatible
                        ? $"PostgreSQL 客户端工具版本不兼容；要求三个工具均为 PostgreSQL 18 或更高且主版本一致。{tools.Version}"
                        : $"PostgreSQL 客户端工具不完整，仅找到 {tools.AvailableToolCount}/3 个文件。");
            }

            return new RuntimeDependencyDiagnostic(
                "postgresql-tools",
                "PostgreSQL 维护工具",
                "optional",
                "missing",
                false,
                Path.GetFullPath(resolvedPath),
                "未安装 PostgreSQL 维护工具；SQLite 单机版和普通业务功能不受影响。");
        }
    }
}
