namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiBackgroundJobRetentionOptions
    {
        public const string RetentionDaysEnvironmentVariable = "EXPORTDOCMANAGER_JOB_HISTORY_DAYS";
        public const string GlobalLimitEnvironmentVariable = "EXPORTDOCMANAGER_JOB_HISTORY_LIMIT";
        public const string PerUserLimitEnvironmentVariable = "EXPORTDOCMANAGER_JOB_USER_HISTORY_LIMIT";

        public int RetentionDays { get; init; } = 30;

        public int GlobalLimit { get; init; } = 2000;

        public int PerUserLimit { get; init; } = 200;

        public static ApiBackgroundJobRetentionOptions FromEnvironment() => new()
        {
            RetentionDays = ReadLimit(RetentionDaysEnvironmentVariable, 30, 1, 3650),
            GlobalLimit = ReadLimit(GlobalLimitEnvironmentVariable, 2000, 100, 100000),
            PerUserLimit = ReadLimit(PerUserLimitEnvironmentVariable, 200, 20, 10000)
        };

        internal ApiBackgroundJobRetentionOptions Normalize() => new()
        {
            RetentionDays = Math.Clamp(RetentionDays, 1, 3650),
            GlobalLimit = Math.Clamp(GlobalLimit, 100, 100000),
            PerUserLimit = Math.Clamp(PerUserLimit, 20, 10000)
        };

        private static int ReadLimit(string variableName, int defaultValue, int minimum, int maximum)
        {
            string value = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
            return int.TryParse(value.Trim(), out int parsed)
                ? Math.Clamp(parsed, minimum, maximum)
                : defaultValue;
        }
    }
}
