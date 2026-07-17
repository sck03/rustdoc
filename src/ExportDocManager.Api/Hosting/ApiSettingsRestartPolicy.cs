using ExportDocManager.DataAccess;
using ExportDocManager.Models;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSettingsDtoFactory
    {
        public static bool RequiresRestartForSystemSettingsChange(
            SystemSettings current,
            SystemSettings requested)
        {
            current ??= new SystemSettings();
            requested ??= new SystemSettings();

            return !string.Equals(
                       DatabaseModeHelper.NormalizeProvider(current.DatabaseProvider),
                       DatabaseModeHelper.NormalizeProvider(requested.DatabaseProvider),
                       StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(
                       DbHelper.NormalizeSqliteDatabaseFileName(current.SqliteDatabaseFileName),
                       DbHelper.NormalizeSqliteDatabaseFileName(requested.SqliteDatabaseFileName),
                       StringComparison.Ordinal) ||
                   !string.Equals(
                       DbHelper.NormalizePostgreSqlText(current.PostgreSqlHost),
                       DbHelper.NormalizePostgreSqlText(requested.PostgreSqlHost),
                       StringComparison.Ordinal) ||
                   DbHelper.NormalizePostgreSqlPort(current.PostgreSqlPort) !=
                   DbHelper.NormalizePostgreSqlPort(requested.PostgreSqlPort) ||
                   !string.Equals(
                       DbHelper.NormalizePostgreSqlText(current.PostgreSqlDatabase),
                       DbHelper.NormalizePostgreSqlText(requested.PostgreSqlDatabase),
                       StringComparison.Ordinal) ||
                   !string.Equals(
                       DbHelper.NormalizePostgreSqlText(current.PostgreSqlUsername),
                       DbHelper.NormalizePostgreSqlText(requested.PostgreSqlUsername),
                       StringComparison.Ordinal) ||
                   !string.Equals(
                       current.PostgreSqlPassword ?? string.Empty,
                       requested.PostgreSqlPassword ?? string.Empty,
                       StringComparison.Ordinal) ||
                   !string.Equals(
                       DbHelper.NormalizePostgreSqlAdditionalOptions(current.PostgreSqlAdditionalOptions),
                       DbHelper.NormalizePostgreSqlAdditionalOptions(requested.PostgreSqlAdditionalOptions),
                       StringComparison.Ordinal);
        }
    }
}
