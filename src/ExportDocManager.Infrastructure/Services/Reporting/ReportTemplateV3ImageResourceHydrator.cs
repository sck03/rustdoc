using System.Text.Json;
using System.Text.RegularExpressions;
using ExportDocManager.Services.Errors;
using HtmlAgilityPack;

namespace ExportDocManager.Services.Reporting;

internal sealed partial class ReportTemplateV3ImageResourceHydrator
{
    internal const string ResourceIdAttribute = "data-edm-v3-resource-id";
    private readonly IReportTemplateImageResourceService _resourceService;

    public ReportTemplateV3ImageResourceHydrator(IReportTemplateImageResourceService resourceService)
    {
        _resourceService = resourceService ?? throw new ArgumentNullException(nameof(resourceService));
    }

    public async Task<string> HydrateAsync(string renderedHtml, CancellationToken cancellationToken = default)
    {
        string source = renderedHtml ?? string.Empty;
        if (!source.Contains(ResourceIdAttribute, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        Dictionary<string, ResourceMetadata> resources = ReadResources(source);
        var document = new HtmlDocument
        {
            OptionOutputOriginalCase = true,
            OptionWriteEmptyNodes = true
        };
        document.LoadHtml(source);
        var resourceNodes = document.DocumentNode.Descendants()
            .Where(node => node.Attributes.Contains(ResourceIdAttribute))
            .ToArray();
        if (resourceNodes.Length == 0)
        {
            return source;
        }

        var dataUris = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (HtmlNode node in resourceNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(node.Name, "img", StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceValidationException("受控图片资源标记只能用于 img 元素。");
            }

            string resourceId = node.GetAttributeValue(ResourceIdAttribute, string.Empty).Trim();
            if (!resources.TryGetValue(resourceId, out var expected))
            {
                throw new ServiceValidationException("模板图片没有引用 resources 清单中的受控资源。");
            }

            if (!dataUris.TryGetValue(resourceId, out string? dataUri))
            {
                ReportTemplateImageResourceContent loaded;
                try
                {
                    loaded = await _resourceService.ReadAsync(resourceId, cancellationToken).ConfigureAwait(false);
                }
                catch (ResourceNotFoundException ex)
                {
                    throw new UserVisibleInfrastructureException("模板引用的受控图片资源不可用，请重新上传并保存模板。", ex);
                }

                if (!string.Equals(loaded.Resource.MediaType, expected.MediaType, StringComparison.Ordinal) ||
                    expected.ByteLength.HasValue && loaded.Resource.ByteLength != expected.ByteLength.Value ||
                    !string.IsNullOrWhiteSpace(expected.Sha256) &&
                    !string.Equals(loaded.Resource.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UserVisibleInfrastructureException("模板图片元数据与受控资源不一致，请重新上传并保存模板。");
                }

                dataUri = $"data:{loaded.Resource.MediaType};base64,{Convert.ToBase64String(loaded.Content)}";
                dataUris.Add(resourceId, dataUri);
            }

            node.SetAttributeValue("src", dataUri);
            node.Attributes.Remove(ResourceIdAttribute);
        }

        string hydrated = document.DocumentNode.OuterHtml;
        ReportTemplateContentPolicy.ValidateRenderedHtml(hydrated);
        return hydrated;
    }

    private static Dictionary<string, ResourceMetadata> ReadResources(string html)
    {
        Match match = SchemaCommentRegex().Match(html);
        if (!match.Success)
        {
            throw new ServiceValidationException("受控图片只能用于带有 V3 设计结构的模板。");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(match.Groups["json"].Value);
            if (!document.RootElement.TryGetProperty("resources", out JsonElement resources) ||
                resources.ValueKind != JsonValueKind.Array)
            {
                throw new ServiceValidationException("模板缺少受控图片 resources 清单。");
            }

            var result = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal);
            foreach (JsonElement resource in resources.EnumerateArray())
            {
                string id = resource.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
                    ? idValue.GetString() ?? string.Empty
                    : string.Empty;
                string mediaType = resource.TryGetProperty("mediaType", out var mediaTypeValue) && mediaTypeValue.ValueKind == JsonValueKind.String
                    ? mediaTypeValue.GetString() ?? string.Empty
                    : string.Empty;
                long? byteLength = resource.TryGetProperty("byteLength", out var byteLengthValue) && byteLengthValue.TryGetInt64(out long parsedLength)
                    ? parsedLength
                    : null;
                string? sha256 = resource.TryGetProperty("sha256", out var shaValue) && shaValue.ValueKind == JsonValueKind.String
                    ? shaValue.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result[id] = new ResourceMetadata(mediaType, byteLength, sha256);
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new ServiceValidationException("模板 V3 图片资源清单无效。", ex);
        }
    }

    [GeneratedRegex(
        "<!--\\s*EXPORTDOC_REPORT_DESIGNER_SCHEMA\\s*(?<json>[\\s\\S]*?)\\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SchemaCommentRegex();

    private sealed record ResourceMetadata(string MediaType, long? ByteLength, string? Sha256);
}
