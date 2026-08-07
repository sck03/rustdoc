using System.Collections.Frozen;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ExportDocManager.Services.Errors;
using Scriban;
using Scriban.Syntax;

namespace ExportDocManager.Services.Reporting;

/// <summary>
/// The single validation boundary for user supplied report HTML.  Report
/// templates are executable Scriban plus HTML which is later opened by a
/// server-side browser, so business-domain and browser-safety checks must be
/// applied before a file/database row is accepted and again before rendering.
/// </summary>
internal static class ReportTemplateContentPolicy
{
    private const int MaximumTemplateCharacters = 2_000_000;
    internal const int MaximumRenderedHtmlCharacters = 32_000_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly FrozenSet<string> PaymentForbiddenRoots = new[]
    {
        "Invoice", "Customer", "Exporter", "items", "item", "Invoice.Items",
        "ShowSeal", "withSeal", "doc_seal_path", "customs_seal_path", "shipping_marks_image_data"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ExportForbiddenRoots = new[]
    {
        "Payment", "Payee", "cny_amount_upper", "payer_seal_path"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ForbiddenElements = new[]
    {
        "script", "iframe", "frame", "object", "embed", "applet", "portal", "fencedframe", "link", "base"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CssPresentationAttributes = new[]
    {
        "fill", "stroke", "filter", "clip-path", "mask", "cursor",
        "marker", "marker-start", "marker-mid", "marker-end"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SafeImageDataPrefixes =
    [
        "data:image/png;base64,",
        "data:image/jpeg;base64,",
        "data:image/gif;base64,",
        "data:image/webp;base64,"
    ];

    private static readonly Regex CssUrl = new(
        @"url\s*\(\s*(?<quote>['""]?)(?<url>.*?)\k<quote>\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        RegexTimeout);

    private static readonly Regex CssImport = new(
        @"@import\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex DangerousCssConstruct = new(
        @"(?:expression\s*\(|behavior\s*:|-moz-binding\s*:)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public static void Validate(ReportDocumentType reportType, string content)
    {
        string source = content ?? string.Empty;
        if (source.Length > MaximumTemplateCharacters)
        {
            throw new ArgumentException("报表模板内容超过允许长度。", nameof(content));
        }

        ValidateHtml(source, allowTemplateExpressions: true);

        // Designer attributes are converted to Scriban control blocks by the
        // renderer. Validate the converted form as well so a forbidden field
        // cannot hide in data-field-name/data-repeat attributes.
        string processed = ScribanReportTemplateRenderer.PreprocessHtmlTemplate(source);
        if (!string.Equals(processed, source, StringComparison.Ordinal))
        {
            ValidateHtml(processed, allowTemplateExpressions: true);
        }

        var template = Template.Parse(processed);
        if (template.HasErrors)
        {
            throw new ArgumentException(
                $"报表模板 Scriban 语法无效：{string.Join(" ", template.Messages.Select(message => message.Message))}",
                nameof(content));
        }

        var visitor = new GlobalVariableVisitor();
        template.Page.Accept(visitor);
        if (visitor.UsesThis)
        {
            throw new ArgumentException(
                "报表模板不能通过 this 动态枚举或索引全局数据，请使用当前业务域的明确字段。",
                nameof(content));
        }

        if (visitor.UsesDynamicEvaluation)
        {
            throw new ArgumentException("报表模板不允许使用 object.eval 或 object.eval_template 动态执行内容。", nameof(content));
        }

        if (reportType == ReportDocumentType.PaymentVoucher)
        {
            var sealReferences = visitor.ReferencedNames
                .Where(IsSealReferenceName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sealReferences.Length > 0)
            {
                throw new ArgumentException(
                    $"付款报销模板不提供印章数据，不能使用印章字段：{string.Join("、", sealReferences)}。",
                    nameof(content));
            }
        }

        var forbidden = visitor.Roots
            .Where(root => reportType == ReportDocumentType.PaymentVoucher
                ? PaymentForbiddenRoots.Contains(root)
                : ExportForbiddenRoots.Contains(root))
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (forbidden.Length > 0)
        {
            string domain = reportType == ReportDocumentType.PaymentVoucher ? "付款报销" : "报关单证";
            throw new ArgumentException(
                $"{domain}模板不能使用另一业务域字段：{string.Join("、", forbidden)}。",
                nameof(content));
        }
    }

    public static void ValidateRenderedHtml(string html)
    {
        string rendered = html ?? string.Empty;
        if (rendered.Length > MaximumRenderedHtmlCharacters)
        {
            throw new ServiceValidationException("报表渲染结果超过允许长度，请减少明细行、图片或模板重复内容。");
        }

        try
        {
            ValidateHtml(rendered, allowTemplateExpressions: false);
        }
        catch (ArgumentException ex)
        {
            throw new ServiceValidationException($"报表渲染结果包含不安全内容：{ex.Message}", ex);
        }
    }

    private static void ValidateHtml(string html, bool allowTemplateExpressions)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.NodeType != HtmlNodeType.Element)
            {
                continue;
            }

            if (ForbiddenElements.Contains(node.Name))
            {
                throw new ArgumentException($"报表模板不允许使用 <{node.Name}> 元素。", nameof(html));
            }

            foreach (var attribute in node.Attributes)
            {
                string name = attribute.Name ?? string.Empty;
                string value = attribute.Value ?? string.Empty;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("报表模板不允许使用 HTML 事件脚本属性。", nameof(html));
                }

                if (string.Equals(name, "http-equiv", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(value.Trim(), "refresh", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("报表模板不允许使用自动跳转或刷新。", nameof(html));
                }

                if (IsUrlAttribute(name) && IsUnsafeUrlAttribute(name, value, allowTemplateExpressions))
                {
                    throw new ArgumentException("报表模板不允许访问外部、相对、文件或脚本 URL。", nameof(html));
                }

                if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase) &&
                    ContainsDangerousCss(value, allowTemplateExpressions))
                {
                    throw new ArgumentException("报表模板样式不允许加载外部资源。", nameof(html));
                }

                if (CssPresentationAttributes.Contains(name) &&
                    ContainsDangerousCss(value, allowTemplateExpressions))
                {
                    throw new ArgumentException("报表模板图形样式不允许加载外部资源。", nameof(html));
                }
            }
        }

        foreach (var styleNode in document.DocumentNode.Descendants("style"))
        {
            string css = styleNode.InnerText ?? string.Empty;
            if (ContainsDangerousCss(css, allowTemplateExpressions))
            {
                throw new ArgumentException("报表模板样式不允许加载外部资源。", nameof(html));
            }
        }
    }

    private static bool IsUrlAttribute(string name) =>
        name.Equals("src", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("srcset", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("href", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(":href", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("action", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("formaction", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("poster", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("background", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsafeUrlAttribute(
        string attributeName,
        string value,
        bool allowTemplateExpressions)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (allowTemplateExpressions && IsTemplateExpression(normalized))
        {
            return false;
        }

        if (attributeName.Equals("href", StringComparison.OrdinalIgnoreCase) &&
            normalized.StartsWith('#'))
        {
            return false;
        }

        if (attributeName.Equals("srcset", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !IsSafeImageDataUrl(normalized);
    }

    private static bool ContainsDangerousCss(string value, bool allowTemplateExpressions)
    {
        try
        {
            string css = value ?? string.Empty;
            if (CssImport.IsMatch(css) || DangerousCssConstruct.IsMatch(css))
            {
                return true;
            }

            foreach (Match match in CssUrl.Matches(css))
            {
                string url = match.Groups["url"].Value.Trim();
                if (allowTemplateExpressions && IsTemplateExpression(url))
                {
                    continue;
                }

                if (!IsSafeImageDataUrl(url))
                {
                    return true;
                }
            }

            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static bool IsTemplateExpression(string value) =>
        value.StartsWith("{{", StringComparison.Ordinal) ||
        value.StartsWith("{%", StringComparison.Ordinal);

    private static bool IsSafeImageDataUrl(string value) =>
        SafeImageDataPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsSealReferenceName(string name)
    {
        string value = name ?? string.Empty;
        return value.Contains("seal", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("stamp", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("印章", StringComparison.Ordinal) ||
               value.Contains("公章", StringComparison.Ordinal) ||
               value.Contains("盖章", StringComparison.Ordinal);
    }

    private sealed class GlobalVariableVisitor : ScriptVisitor
    {
        public HashSet<string> Roots { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ReferencedNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool UsesThis { get; private set; }

        public bool UsesDynamicEvaluation { get; private set; }

        public override void Visit(ScriptVariableGlobal node)
        {
            if (!string.IsNullOrWhiteSpace(node?.BaseName))
            {
                Roots.Add(node.BaseName);
                ReferencedNames.Add(node.BaseName);
            }

            base.Visit(node);
        }

        public override void Visit(ScriptThisExpression node)
        {
            UsesThis = true;
            base.Visit(node);
        }

        public override void Visit(ScriptMemberExpression node)
        {
            if (!string.IsNullOrWhiteSpace(node?.Member?.BaseName))
            {
                ReferencedNames.Add(node.Member.BaseName);
            }

            if (node?.Target is ScriptVariableGlobal target &&
                string.Equals(target.BaseName, "object", StringComparison.OrdinalIgnoreCase) &&
                IsDynamicEvaluationMember(node.Member?.BaseName))
            {
                UsesDynamicEvaluation = true;
            }

            base.Visit(node);
        }

        public override void Visit(ScriptIndexerExpression node)
        {
            if (node?.Index is ScriptLiteral referencedLiteral && referencedLiteral.Value is string referencedName)
            {
                ReferencedNames.Add(referencedName);
            }

            if (node?.Target is ScriptVariableGlobal target &&
                string.Equals(target.BaseName, "object", StringComparison.OrdinalIgnoreCase) &&
                node.Index is ScriptLiteral literal &&
                IsDynamicEvaluationMember(literal.Value?.ToString()))
            {
                UsesDynamicEvaluation = true;
            }

            base.Visit(node);
        }

        private static bool IsDynamicEvaluationMember(string memberName) =>
            string.Equals(memberName, "eval", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(memberName, "eval_template", StringComparison.OrdinalIgnoreCase);
    }
}
