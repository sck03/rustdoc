namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiBackgroundJobConcurrencyOptions
    {
        public const string GlobalEnvironmentVariable = "EXPORTDOCMANAGER_JOB_GLOBAL_CONCURRENCY";
        public const string PerUserEnvironmentVariable = "EXPORTDOCMANAGER_JOB_USER_CONCURRENCY";
        public const string BrowserEnvironmentVariable = "EXPORTDOCMANAGER_JOB_BROWSER_CONCURRENCY";
        public const string GlobalQueueEnvironmentVariable = "EXPORTDOCMANAGER_JOB_GLOBAL_QUEUE_LIMIT";
        public const string PerUserQueueEnvironmentVariable = "EXPORTDOCMANAGER_JOB_USER_QUEUE_LIMIT";

        public int GlobalLimit { get; init; } = 4;

        public int PerUserLimit { get; init; } = 2;

        public int BrowserLimit { get; init; } = 2;

        public int GlobalQueueLimit { get; init; } = 64;

        public int PerUserQueueLimit { get; init; } = 12;

        public static ApiBackgroundJobConcurrencyOptions FromEnvironment() => new()
        {
            GlobalLimit = ReadLimit(GlobalEnvironmentVariable, 4, 1, 32),
            PerUserLimit = ReadLimit(PerUserEnvironmentVariable, 2, 1, 16),
            BrowserLimit = ReadLimit(BrowserEnvironmentVariable, 2, 1, 16),
            GlobalQueueLimit = ReadLimit(GlobalQueueEnvironmentVariable, 64, 4, 1000),
            PerUserQueueLimit = ReadLimit(PerUserQueueEnvironmentVariable, 12, 2, 200)
        };

        internal ApiBackgroundJobConcurrencyOptions Normalize() => new()
        {
            GlobalLimit = Math.Clamp(GlobalLimit, 1, 32),
            PerUserLimit = Math.Clamp(PerUserLimit, 1, 16),
            BrowserLimit = Math.Clamp(BrowserLimit, 1, 16),
            GlobalQueueLimit = Math.Clamp(GlobalQueueLimit, 4, 1000),
            PerUserQueueLimit = Math.Clamp(PerUserQueueLimit, 2, 200)
        };

        private static int ReadLimit(string variableName, int defaultValue, int minimum, int maximum)
        {
            string value = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
            return int.TryParse(value.Trim(), out int parsed)
                ? Math.Clamp(parsed, minimum, maximum)
                : defaultValue;
        }
    }

    internal static class ApiBackgroundJobQueueStatusCatalog
    {
        public const string Rejected = "队列已满";
    }
}
