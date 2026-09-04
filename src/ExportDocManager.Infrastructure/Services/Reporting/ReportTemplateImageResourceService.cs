using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Reporting;

public sealed partial class ReportTemplateImageResourceService : IReportTemplateImageResourceService
{
    private const string StoragePolicy =
        "受控图片按内容摘要写入运行数据根 Templates/Resources/V3，并随用户模板包导入导出；不保存外部 URL、客户端路径或服务器绝对路径。";
    private const string ResourcePrefix = "img-";
    private readonly string _resourceRoot;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    public ReportTemplateImageResourceService(IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _resourceRoot = Path.GetFullPath(Path.Combine(pathProvider.UserTemplateRoot, "Resources", "V3"));
    }

    public async Task<ReportTemplateImageResource> StoreAsync(
        Stream source,
        string? fileName = null,
        string? declaredMediaType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await using var buffer = new MemoryStream(capacity: 1024 * 1024);
        long byteLength = await BoundedStreamHelper.CopyToAsync(
                source,
                buffer,
                ReportTemplateV3ContractCatalog.MaxResourceBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (byteLength == 0)
        {
            throw new ServiceValidationException("图片文件不能为空。");
        }

        byte[] content = buffer.ToArray();
        var format = DetectFormat(content)
                     ?? throw new ServiceValidationException("图片内容无效；只支持 PNG、JPEG、GIF 或 WebP。");
        string normalizedDeclaredType = (declaredMediaType ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalizedDeclaredType) &&
            !string.Equals(normalizedDeclaredType, "application/octet-stream", StringComparison.Ordinal) &&
            !string.Equals(normalizedDeclaredType, format.MediaType, StringComparison.Ordinal))
        {
            throw new ServiceValidationException("图片声明类型与实际内容不一致，请重新选择原始图片文件。");
        }

        string sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        string resourceId = $"{ResourcePrefix}{sha256}.{format.Extension}";

        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureResourceRoot();
            string targetPath = ResolveResourcePath(resourceId, requireExisting: false);

            if (File.Exists(targetPath))
            {
                await VerifyExistingFileAsync(targetPath, sha256, format.MediaType, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                int resourceCount = ControlledFileSystemEnumerator
                    .EnumerateFiles(
                        _resourceRoot,
                        cancellationToken,
                        "受控图片资源目录不能包含符号链接、目录联接或其他重解析点。")
                    .Count;
                if (resourceCount >= ReportTemplateV3ContractCatalog.MaxResources)
                {
                    throw new ServiceValidationException(
                        $"受控图片资源数量已达到上限 {ReportTemplateV3ContractCatalog.MaxResources} 个，请先清理未使用资源。");
                }

                await AtomicFileHelper.WriteFileAtomicAsync(
                        targetPath,
                        (temporaryPath, ct) => File.WriteAllBytesAsync(temporaryPath, content, ct),
                        cancellationToken)
                    .ConfigureAwait(false);
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    targetPath,
                    _resourceRoot,
                    "受控图片资源不能经过符号链接、目录联接或其他重解析点。");
            }
        }
        finally
        {
            _writeSemaphore.Release();
        }

        return CreateResource(resourceId, format.MediaType, byteLength, sha256, fileName);
    }

    public async Task<ReportTemplateImageResourceContent> ReadAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        string normalizedResourceId = (resourceId ?? string.Empty).Trim();
        string path = ResolveResourcePath(normalizedResourceId, requireExisting: true);
        var match = ResourceIdRegex().Match(normalizedResourceId);
        string expectedHash = match.Groups["hash"].Value;
        string expectedMediaType = ExtensionToMediaType(match.Groups["extension"].Value);
        byte[] content = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        var actualFormat = DetectFormat(content);
        string actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (actualFormat == null ||
            !string.Equals(actualFormat.MediaType, expectedMediaType, StringComparison.Ordinal) ||
            !string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new UserVisibleInfrastructureException("受控图片资源校验失败，请重新上传该图片。");
        }

        return new ReportTemplateImageResourceContent
        {
            Resource = CreateResource(normalizedResourceId, actualFormat.MediaType, content.LongLength, actualHash, null),
            Content = content
        };
    }

    private void EnsureResourceRoot()
    {
        PathBoundaryHelper.EnsureNoLinkLikeComponents(
            _resourceRoot,
            "受控图片资源目录不能经过符号链接、目录联接或其他重解析点。");
        Directory.CreateDirectory(_resourceRoot);
        PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
            _resourceRoot,
            _resourceRoot,
            "受控图片资源目录不能包含符号链接、目录联接或其他重解析点。");
    }

    private string ResolveResourcePath(string resourceId, bool requireExisting)
    {
        string normalized = (resourceId ?? string.Empty).Trim();
        if (!ResourceIdRegex().IsMatch(normalized))
        {
            throw new ServiceValidationException("受控图片资源 ID 无效。");
        }

        string path = Path.GetFullPath(Path.Combine(_resourceRoot, normalized));
        if (!PathBoundaryHelper.IsWithinRoot(path, _resourceRoot))
        {
            throw new PermissionDeniedException("受控图片资源路径不能离开运行数据根。");
        }

        PathBoundaryHelper.EnsureNoLinkLikeComponents(
            path,
            "受控图片资源不能经过符号链接、目录联接或其他重解析点。");
        if (requireExisting && !File.Exists(path))
        {
            throw new ResourceNotFoundException("受控图片资源不存在，请重新上传并绑定。");
        }

        return path;
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new ResourceNotFoundException("受控图片资源不存在，请重新上传并绑定。");
        }
        if (info.Length is <= 0 or > ReportTemplateV3ContractCatalog.MaxResourceBytes)
        {
            throw new UserVisibleInfrastructureException("受控图片资源大小校验失败，请重新上传该图片。");
        }

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new MemoryStream(checked((int)info.Length));
        await BoundedStreamHelper.CopyToAsync(
                input,
                output,
                ReportTemplateV3ContractCatalog.MaxResourceBytes,
                cancellationToken)
            .ConfigureAwait(false);
        return output.ToArray();
    }

    private static async Task VerifyExistingFileAsync(
        string path,
        string expectedHash,
        string expectedMediaType,
        CancellationToken cancellationToken)
    {
        byte[] content = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        var format = DetectFormat(content);
        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (format == null ||
            !string.Equals(format.MediaType, expectedMediaType, StringComparison.Ordinal) ||
            !string.Equals(hash, expectedHash, StringComparison.Ordinal))
        {
            throw new UserVisibleInfrastructureException("同名受控图片资源已损坏，系统已停止覆盖，请检查数据目录。");
        }
    }

    private static ReportTemplateImageResource CreateResource(
        string id,
        string mediaType,
        long byteLength,
        string sha256,
        string? fileName) => new()
        {
            Id = id,
            MediaType = mediaType,
            ByteLength = byteLength,
            Sha256 = sha256,
            AltText = NormalizeAltText(fileName),
            StoragePolicy = StoragePolicy
        };

    private static string NormalizeAltText(string? fileName)
    {
        string value = Path.GetFileNameWithoutExtension(fileName ?? string.Empty)
            .Normalize(NormalizationForm.FormC)
            .Trim();
        return value.Length <= 200 ? value : value[..200];
    }

    private static ImageFormat? DetectFormat(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return new("image/png", "png");
        if (content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF && content[^2] == 0xFF && content[^1] == 0xD9)
            return new("image/jpeg", "jpg");
        if (content.Length >= 6 && (content[..6].SequenceEqual("GIF87a"u8) || content[..6].SequenceEqual("GIF89a"u8)))
            return new("image/gif", "gif");
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8))
            return new("image/webp", "webp");
        return null;
    }

    private static string ExtensionToMediaType(string extension) => extension switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        _ => throw new ServiceValidationException("受控图片资源扩展名无效。")
    };

    [GeneratedRegex("^img-(?<hash>[0-9a-f]{64})\\.(?<extension>png|jpg|gif|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceIdRegex();

    private sealed record ImageFormat(string MediaType, string Extension);
}
