using System.Collections.Frozen;
using HtmlAgilityPack;

namespace ExportDocManager.Services.EmailTemplates
{
    internal static class EmailTemplateHtmlPolicy
    {
        private static readonly FrozenSet<string> AllowedElements = new[]
        {
            "a", "b", "blockquote", "br", "div", "em", "h1", "h2", "h3", "h4", "hr", "i",
            "li", "ol", "p", "s", "span", "strong", "table", "tbody", "td", "tfoot", "th",
            "thead", "tr", "u", "ul"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> BlockedElements = new[]
        {
            "audio", "base", "button", "canvas", "embed", "form", "frame", "frameset", "iframe",
            "input", "link", "math", "meta", "object", "option", "script", "select", "source",
            "style", "svg", "textarea", "video"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var document = new HtmlDocument
            {
                OptionFixNestedTags = true
            };
            document.LoadHtml(value);
            SanitizeChildren(document.DocumentNode);
            return document.DocumentNode.InnerHtml.Trim();
        }

        private static void SanitizeChildren(HtmlNode parent)
        {
            foreach (var node in parent.ChildNodes.ToArray())
            {
                if (node.NodeType == HtmlNodeType.Comment)
                {
                    node.Remove();
                    continue;
                }

                if (node.NodeType == HtmlNodeType.Text)
                {
                    continue;
                }

                if (node.NodeType != HtmlNodeType.Element)
                {
                    node.Remove();
                    continue;
                }

                string elementName = node.Name.ToLowerInvariant();
                if (BlockedElements.Contains(elementName))
                {
                    node.Remove();
                    continue;
                }

                if (!AllowedElements.Contains(elementName))
                {
                    SanitizeChildren(node);
                    Unwrap(node);
                    continue;
                }

                SanitizeAttributes(node, elementName);
                SanitizeChildren(node);
            }
        }

        private static void SanitizeAttributes(HtmlNode node, string elementName)
        {
            foreach (var attribute in node.Attributes.ToArray())
            {
                string attributeName = attribute.Name.ToLowerInvariant();
                bool keep = elementName == "a" && attributeName is "href" or "target" or "title" or "rel"
                    || elementName is "td" or "th" && attributeName is "colspan" or "rowspan" or "scope";
                if (!keep) node.Attributes.Remove(attribute);
            }

            if (elementName == "a")
            {
                string href = node.GetAttributeValue("href", string.Empty).Trim();
                if (!IsAllowedHref(href)) node.Attributes.Remove("href");

                string target = node.GetAttributeValue("target", string.Empty).Trim();
                if (!target.Equals("_blank", StringComparison.OrdinalIgnoreCase))
                {
                    node.Attributes.Remove("target");
                    node.Attributes.Remove("rel");
                }
                else
                {
                    node.SetAttributeValue("target", "_blank");
                    node.SetAttributeValue("rel", "noopener noreferrer");
                }
            }

            if (elementName is "td" or "th")
            {
                NormalizePositiveIntegerAttribute(node, "colspan");
                NormalizePositiveIntegerAttribute(node, "rowspan");
                string scope = node.GetAttributeValue("scope", string.Empty).Trim().ToLowerInvariant();
                if (scope is not ("row" or "col" or "rowgroup" or "colgroup")) node.Attributes.Remove("scope");
            }
        }

        private static bool IsAllowedHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return false;
            if (href.Any(char.IsControl)) return false;
            if (href.StartsWith('#')) return true;
            return href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
        }

        private static void NormalizePositiveIntegerAttribute(HtmlNode node, string attributeName)
        {
            string value = node.GetAttributeValue(attributeName, string.Empty).Trim();
            if (!int.TryParse(value, out int parsed) || parsed < 1 || parsed > 100)
            {
                node.Attributes.Remove(attributeName);
                return;
            }

            node.SetAttributeValue(attributeName, parsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void Unwrap(HtmlNode node)
        {
            var parent = node.ParentNode;
            if (parent == null) return;
            foreach (var child in node.ChildNodes.ToArray()) parent.InsertBefore(child, node);
            node.Remove();
        }
    }
}
