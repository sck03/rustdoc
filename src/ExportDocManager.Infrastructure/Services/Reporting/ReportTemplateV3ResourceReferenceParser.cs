using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Reporting;

internal sealed record ReportTemplateV3ResourceReference(
    string Id,
    string Sha256,
    string MediaType,
    long ByteLength);

/// <summary>
/// Reads the authoritative V3 manifest after the normal template validation
/// boundary has accepted it. Only resources referenced by an Image element
/// are returned; merely placing an ID in the manifest never grants access.
/// </summary>
internal static partial class ReportTemplateV3ResourceReferenceParser
{
    public static IReadOnlyList<ReportTemplateV3ResourceReference> Parse(
        ReportDocumentType reportType,
        string? content)
    {
        string source = content ?? string.Empty;
        Match match;
        try
        {
            match = SchemaCommentRegex().Match(source);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new ArgumentException("报表模板 V3 图片资源清单读取超时。", nameof(content), exception);
        }

        if (!match.Success)
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(match.Groups["json"].Value);
            JsonElement root = document.RootElement;
            ReportTemplateV3SchemaValidator.Validate(reportType, root);

            var manifest = root.TryGetProperty("resources", out JsonElement resources)
                ? resources.EnumerateArray().ToDictionary(
                    item => item.GetProperty("id").GetString() ?? string.Empty,
                    item => new ReportTemplateV3ResourceReference(
                        item.GetProperty("id").GetString() ?? string.Empty,
                        item.GetProperty("sha256").GetString() ?? string.Empty,
                        item.GetProperty("mediaType").GetString() ?? string.Empty,
                        item.GetProperty("byteLength").GetInt64()),
                    StringComparer.Ordinal)
                : new Dictionary<string, ReportTemplateV3ResourceReference>(StringComparer.Ordinal);

            var referencedIds = new HashSet<string>(StringComparer.Ordinal);
            CollectImageResourceIds(root, referencedIds, depth: 0);
            return referencedIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => manifest[id])
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("报表模板 V3 图片资源清单 JSON 无效。", nameof(content), exception);
        }
    }

    private static void CollectImageResourceIds(
        JsonElement value,
        ISet<string> referencedIds,
        int depth)
    {
        if (depth > 32)
        {
            throw new ArgumentException("报表模板 V3 图片资源结构嵌套层级过深。", nameof(value));
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            bool isResourceImage =
                value.TryGetProperty("type", out JsonElement type) &&
                type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), "Image", StringComparison.Ordinal) &&
                value.TryGetProperty("sourceKind", out JsonElement sourceKind) &&
                sourceKind.ValueKind == JsonValueKind.String &&
                string.Equals(sourceKind.GetString(), "Resource", StringComparison.Ordinal);
            if (isResourceImage &&
                value.TryGetProperty("resourceId", out JsonElement resourceId) &&
                resourceId.ValueKind == JsonValueKind.String)
            {
                referencedIds.Add(resourceId.GetString() ?? string.Empty);
            }

            foreach (JsonProperty property in value.EnumerateObject())
            {
                CollectImageResourceIds(property.Value, referencedIds, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                CollectImageResourceIds(item, referencedIds, depth + 1);
            }
        }
    }

    [GeneratedRegex(
        "<!--\\s*EXPORTDOC_REPORT_DESIGNER_SCHEMA\\s*(?<json>[\\s\\S]*?)\\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SchemaCommentRegex();
}
