using System.IO.Compression;
using System.Text;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class SecurityAndResourceBoundaryTests
{
    [Fact]
    public void LocalSecretProtector_ShouldUseRandomNonceAndRejectTampering()
    {
        string root = CreateTempDirectory("secret-protector");
        try
        {
            var first = new LocalSecretProtector(root);
            var second = new LocalSecretProtector(root);
            string encryptedOne = first.Protect("数据库密码");
            string encryptedTwo = first.Protect("数据库密码");

            Assert.NotEqual(encryptedOne, encryptedTwo);
            Assert.Equal("数据库密码", second.Unprotect(encryptedOne));
            Assert.Null(second.Unprotect(encryptedOne[..^1] + (encryptedOne[^1] == 'A' ? 'B' : 'A')));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BoundedStreamHelper_ShouldEnforceLimitForChunkedInput()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("123456"));
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<PayloadLimitExceededException>(() =>
            BoundedStreamHelper.CopyToAsync(source, destination, maximumBytes: 5));
    }

    [Fact]
    public async Task BoundedStreamHelper_ShouldAllowAnEmptyStreamWithZeroBudget()
    {
        await using var source = new MemoryStream();
        await using var destination = new MemoryStream();

        Assert.Equal(0, await BoundedStreamHelper.CopyToAsync(source, destination, maximumBytes: 0));
    }

    [Fact]
    public async Task ZipArchiveHelper_ShouldRejectTraversalEntries()
    {
        string root = CreateTempDirectory("zip-boundary");
        string packagePath = Path.Combine(root, "unsafe.zip");
        string targetPath = Path.Combine(root, "target");
        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                await using var writer = new StreamWriter(
                    archive.CreateEntry("../outside.txt").Open(),
                    Encoding.UTF8);
                await writer.WriteAsync("unsafe");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, targetPath));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ZipArchiveHelper_ShouldRejectPortablePathAmbiguity()
    {
        string root = CreateTempDirectory("zip-portable-path");
        string packagePath = Path.Combine(root, "unsafe.zip");
        string targetPath = Path.Combine(root, "target");
        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                await using var writer = new StreamWriter(
                    archive.CreateEntry("report.txt:secret").Open(),
                    Encoding.UTF8);
                await writer.WriteAsync("unsafe");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ZipArchiveHelper.ExtractToDirectorySafeAsync(packagePath, targetPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"edm-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
