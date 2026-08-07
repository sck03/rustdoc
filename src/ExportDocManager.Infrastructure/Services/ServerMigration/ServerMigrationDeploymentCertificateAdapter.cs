namespace ExportDocManager.Services.Infrastructure;

/// <summary>
/// 部署层证书适配边界。完整迁移只迁移应用数据，TLS/Certbot 证书必须由目标部署重新签发。
/// </summary>
internal static class ServerMigrationDeploymentCertificateAdapter
{
    public const string StoragePolicy = "完整迁移不包含许可证、机器绑定状态、日志缓存和 TLS/Certbot 证书；证书由目标部署重新签发。";

    public static bool IsCertificateEntry(string normalizedPath) =>
        normalizedPath.Equals("Deployment/Certificates", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.StartsWith("Deployment/Certificates/", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.Equals("Deployment/Certbot", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.StartsWith("Deployment/Certbot/", StringComparison.OrdinalIgnoreCase);

    public static void EnsureNotIncluded(string normalizedPath)
    {
        if (IsCertificateEntry(normalizedPath))
        {
            throw new InvalidDataException("服务器迁移包不能包含 TLS/Certbot 证书；请在部署层重新签发证书。");
        }
    }
}
