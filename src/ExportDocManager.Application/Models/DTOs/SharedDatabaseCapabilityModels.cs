namespace ExportDocManager.Models.DTOs
{
    public sealed class SharedDatabaseCapabilityProfile
    {
        public bool SharedDatabaseEnabled { get; init; }

        public bool SharedDatabasePendingConfiguration { get; init; }

        public string CurrentModeText { get; init; } = "当前是单机模式（SQLite）";

        public IReadOnlyList<string> PlannedModules { get; init; } = [];

        public string SummaryText => PlannedModules.Count == 0
            ? CurrentModeText
            : $"{CurrentModeText}；未来计划逐步支持共享数据库的模块：{string.Join("、", PlannedModules)}";
    }
}
