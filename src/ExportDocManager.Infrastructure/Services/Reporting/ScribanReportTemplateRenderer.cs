using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;
using Scriban;
using Scriban.Runtime;
using Serilog;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace ExportDocManager.Services.Reporting
{
    internal static class ScribanReportTemplateRenderer
    {
        internal const int MaximumCachedTemplates = 256;
        private static readonly ConcurrentDictionary<string, Template> TemplateCache = new();
        private static readonly ConcurrentQueue<string> TemplateCacheOrder = new();

        internal static int CachedTemplateCount => TemplateCache.Count;

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
                EnableRelaxedTargetAccess = true,
                LimitToString = ReportTemplateContentPolicy.MaximumRenderedHtmlCharacters,
                RegexTimeOut = TimeSpan.FromSeconds(1)
            };
            if (context.BuiltinObject["object"] is ScriptObject objectFunctions)
            {
                objectFunctions.Remove("eval");
                objectFunctions.Remove("eval_template");
            }
            context.PushGlobal(globals);

            var templateKey = ComputeTemplateHash(templateContent);
            var template = GetOrAddTemplate(templateKey, templateContent);

            if (template.HasErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, template.Messages));
            }

            string rendered = template.Render(context);
            ReportTemplateContentPolicy.ValidateRenderedHtml(rendered);
            return rendered;
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
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            int searchIndex = 0;
            StringBuilder decoded = null;
            while (searchIndex < html.Length)
            {
                int blockStart = html.IndexOf("{{", searchIndex, StringComparison.Ordinal);
                if (blockStart < 0)
                {
                    break;
                }

                int blockEnd = html.IndexOf("}}", blockStart + 2, StringComparison.Ordinal);
                if (blockEnd < 0)
                {
                    break;
                }

                decoded ??= new StringBuilder(html.Length);
                decoded.Append(html, searchIndex, blockStart - searchIndex);
                decoded.Append(WebUtility.HtmlDecode(html.Substring(blockStart, blockEnd + 2 - blockStart)));
                searchIndex = blockEnd + 2;
            }

            if (decoded == null)
            {
                return html;
            }

            decoded.Append(html, searchIndex, html.Length - searchIndex);
            return decoded.ToString();
        }

        private static string ComputeTemplateHash(string content)
        {
            var inputBytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            var hashBytes = SHA256.HashData(inputBytes);
            return Convert.ToHexString(hashBytes);
        }

        private static Template GetOrAddTemplate(string key, string content)
        {
            if (TemplateCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var parsed = Template.Parse(content ?? string.Empty);
            if (!TemplateCache.TryAdd(key, parsed))
            {
                return TemplateCache[key];
            }

            TemplateCacheOrder.Enqueue(key);
            while (TemplateCache.Count > MaximumCachedTemplates && TemplateCacheOrder.TryDequeue(out string oldestKey))
            {
                TemplateCache.TryRemove(oldestKey, out _);
            }

            return parsed;
        }

        internal static void ClearTemplateCacheForTests()
        {
            TemplateCache.Clear();
            while (TemplateCacheOrder.TryDequeue(out _))
            {
            }
        }
    }
}
