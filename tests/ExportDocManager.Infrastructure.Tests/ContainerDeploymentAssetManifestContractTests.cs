using System.Security.Cryptography;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ContainerDeploymentAssetManifestContractTests
{
    [Fact]
    public void DeploymentAssetManifest_ShouldMatchEveryPublishedInstallerAsset()
    {
        string root = FindRepositoryRoot();
        string containerRoot = Path.Combine(root, "deploy", "container");
        string manifestPath = Path.Combine(containerRoot, "deployment-assets.sha256");
        string[] expectedAssets =
        [
            "docker-compose.ghcr.yml",
            "docker-compose.acme.yml",
            "nginx.acme.conf",
            "postgres-init-roles.sh",
            "install-container.sh"
        ];

        Dictionary<string, string> manifest = File.ReadAllLines(manifestPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseManifestLine)
            .ToDictionary(entry => entry.FileName, entry => entry.Hash, StringComparer.Ordinal);

        Assert.Equal(expectedAssets.Order(StringComparer.Ordinal), manifest.Keys.Order(StringComparer.Ordinal));
        foreach (string asset in expectedAssets)
        {
            string actualHash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(containerRoot, asset))))
                .ToLowerInvariant();
            Assert.Equal(actualHash, manifest[asset]);
        }
    }

    private static (string FileName, string Hash) ParseManifestLine(string line)
    {
        int separator = line.IndexOf("  ", StringComparison.Ordinal);
        Assert.True(separator == 64, $"Invalid deployment asset manifest line: {line}");
        string hash = line[..separator];
        string fileName = line[(separator + 2)..];
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain('/', fileName);
        Assert.DoesNotContain('\\', fileName);
        return (fileName, hash);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExportDocManager.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
