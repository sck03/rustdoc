using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Scriban;
using Scriban.Runtime;
using Serilog;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace ExportDocManager.Services.Reporting
{
    internal static class ScribanReportTemplateRenderer
    {
        private static readonly ConcurrentDictionary<string, Template> TemplateCache = new();

        public static string PreprocessHtmlTemplate(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            bool hasDesignerAttributes =
                html.Contains("data-repeat", StringComparison.Ordinal) ||
                html.Contains("data-show-if", StringComparison.Ordinal) ||
                html.Contains("data-field-name", StringComparison.Ordinal);

            if (!hasDesignerAttributes)
            {
                return html.Contains("{{", StringComparison.Ordinal) ? DecodeScribanBlocks(html) : html;
            }

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                bool modified = false;

                modified |= RewriteBlockNodes(doc, "data-repeat", expression => $"{{{{ for {expression} }}}}");
                modified |= RewriteBlockNodes(doc, "data-show-if", expression => $"{{{{ if {expression} }}}}");
                modified |= RewriteFieldNodes(doc);

                if (modified)
                {
                    return DecodeScribanBlocks(doc.DocumentNode.OuterHtml);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to preprocess HTML template");
            }

            return DecodeScribanBlocks(html);
        }

        public static string Render(string templateContent, ScriptObject globals)
        {
            var context = new TemplateContext
            {
                MemberRenamer = member => member.Name,
                StrictVariables = false,
                EnableRelaxedMemberAccess = true,
                EnableRelaxedTargetAccess = true
            };
            context.PushGlobal(globals);

            var templateKey = ComputeTemplateHash(templateContent);
            var template = TemplateCache.GetOrAdd(templateKey, _ => Template.Parse(templateContent));

            if (template.HasErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, template.Messages));
            }

            return template.Render(context);
        }

        private static bool RewriteBlockNodes(HtmlDocument doc, string attributeName, Func<string, string> startBuilder)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//*[@{attributeName}]");
            if (nodes == null)
            {
                return false;
            }

            bool modified = false;
            foreach (var node in nodes)
            {
                var expression = node.GetAttributeValue(attributeName, string.Empty);
                node.Attributes.Remove(attributeName);

                if (string.IsNullOrWhiteSpace(expression))
                {
                    continue;
                }

                node.ParentNode.InsertBefore(doc.CreateTextNode(startBuilder(expression)), node);
                node.ParentNode.InsertAfter(doc.CreateTextNode("{{ end }}"), node);
                modified = true;
            }

            return modified;
        }

        private static bool RewriteFieldNodes(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes("//*[@data-field-name]");
            if (nodes == null)
            {
                return false;
            }

            bool modified = false;
            foreach (var node in nodes)
            {
                var fieldName = node.GetAttributeValue("data-field-name", string.Empty);
                var fieldFormat = node.GetAttributeValue("data-field-format", string.Empty);

                node.Attributes.Remove("data-field-name");
                node.Attributes.Remove("data-field-format");
                node.Attributes.Remove("data-field-label");

                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                node.InnerHtml = string.IsNullOrWhiteSpace(fieldFormat)
                    ? $"{{{{ {fieldName} }}}}"
                    : $"{{{{ {fieldName} | {fieldFormat} }}}}";
                modified = true;
            }

            return modified;
        }

        private static string DecodeScribanBlocks(string html)
        {
            return string.IsNullOrWhiteSpace(html)
                ? html
                : Regex.Replace(html, @"\{\{[\s\S]*?\}\}", match => WebUtility.HtmlDecode(match.Value));
        }

        private static string ComputeTemplateHash(string content)
        {
            var inputBytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            var hashBytes = MD5.HashData(inputBytes);
            return Convert.ToHexString(hashBytes);
        }
    }
}
