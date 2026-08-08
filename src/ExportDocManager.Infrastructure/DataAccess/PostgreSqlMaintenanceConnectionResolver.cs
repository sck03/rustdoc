#nullable enable

using System.Text;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.DataAccess;

internal sealed record PostgreSqlMaintenanceConnectionProfile(
    DatabaseConnectionSettings ConnectionSettings,
    string OwnerRole,
    bool UsesDedicatedCredentials);

internal static class PostgreSqlMaintenanceConnectionResolver
{
    public const string UsernameEnvironmentVariable =
        "EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_USERNAME";
    public const string PasswordEnvironmentVariable =
        "EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD";
    public const string PasswordFileEnvironmentVariable =
        "EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_PASSWORD_FILE";
    public const string OwnerRoleEnvironmentVariable =
        "EXPORTDOCMANAGER_POSTGRES_OWNER_ROLE";

    public static PostgreSqlMaintenanceConnectionProfile Resolve(
        DatabaseConnectionSettings applicationSettings,
        IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(applicationSettings);
        ArgumentNullException.ThrowIfNull(pathProvider);

        string username = Environment.GetEnvironmentVariable(UsernameEnvironmentVariable)?.Trim()
            ?? string.Empty;
        string? password = PostgreSqlPasswordResolver.ResolveEnvironmentSecret(
            PasswordEnvironmentVariable,
            PasswordFileEnvironmentVariable,
            pathProvider);
        string ownerRole = Environment.GetEnvironmentVariable(OwnerRoleEnvironmentVariable)?.Trim()
            ?? string.Empty;
        bool hasDedicatedConfiguration =
            username.Length > 0 || password != null || ownerRole.Length > 0;
        if (!hasDedicatedConfiguration)
        {
            string applicationRole = ValidateIdentifier(
                applicationSettings.PostgreSqlUsername,
                "PostgreSQL 应用账号");
            return new PostgreSqlMaintenanceConnectionProfile(
                Clone(applicationSettings, applicationRole, applicationSettings.PostgreSqlPassword),
                applicationRole,
                UsesDedicatedCredentials: false);
        }

        if (username.Length == 0 || password == null || password.Length == 0 || ownerRole.Length == 0)
        {
            throw new ServiceValidationException(
                $"PostgreSQL 权限分离配置不完整。必须同时设置 {UsernameEnvironmentVariable}、" +
                $"{PasswordEnvironmentVariable}（或 {PasswordFileEnvironmentVariable}）和 " +
                $"{OwnerRoleEnvironmentVariable}。");
        }

        username = ValidateIdentifier(username, "PostgreSQL 维护账号");
        ownerRole = ValidateIdentifier(ownerRole, "PostgreSQL 所有者角色");
        return new PostgreSqlMaintenanceConnectionProfile(
            Clone(applicationSettings, username, password),
            ownerRole,
            UsesDedicatedCredentials: true);
    }

    public static string ResolveOwnerRole(string fallbackRole)
    {
        string configured = Environment.GetEnvironmentVariable(OwnerRoleEnvironmentVariable)?.Trim()
            ?? string.Empty;
        return ValidateIdentifier(
            configured.Length > 0 ? configured : fallbackRole,
            "PostgreSQL 所有者角色");
    }

    private static DatabaseConnectionSettings Clone(
        DatabaseConnectionSettings source,
        string username,
        string password) =>
        new()
        {
            Provider = source.Provider,
            SqliteDatabaseFileName = source.SqliteDatabaseFileName,
            PostgreSqlHost = source.PostgreSqlHost,
            PostgreSqlPort = source.PostgreSqlPort,
            PostgreSqlDatabase = source.PostgreSqlDatabase,
            PostgreSqlUsername = username,
            PostgreSqlPassword = password,
            PostgreSqlAdditionalOptions = source.PostgreSqlAdditionalOptions
        };

    private static string ValidateIdentifier(string? value, string label)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 ||
            normalized.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(normalized) > 63)
        {
            throw new ServiceValidationException(
                $"{label}不能为空、不能包含控制字符，且 UTF-8 长度不能超过 63 字节。");
        }

        return normalized;
    }
}
