using System.Text;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Reporting
{
    internal static class ReportFontPolicy
    {
        public const string FontDirectoryRelativePath = "Fonts/OpenSource";
        public const string SansRegularFileName = "NotoSansCJKsc-Regular.otf";
        public const string SansBoldFileName = "NotoSansCJKsc-Bold.otf";
        public const string SerifRegularFileName = "NotoSerifCJKsc-Regular.otf";

        public const string SansCssFamilyList =
            "\"Noto Sans CJK SC\", \"Noto Sans SC\", \"Source Han Sans SC\", \"PingFang SC\", \"Microsoft YaHei UI\", \"Microsoft YaHei\", \"Segoe UI\", Arial, sans-serif";

        public const string SerifCssFamilyList =
            "\"Noto Serif CJK SC\", \"Noto Serif SC\", \"Source Han Serif SC\", \"Songti SC\", SimSun, \"Times New Roman\", serif";

        public static string BuildHtmlStyle(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            string fontRoot = Path.Combine(pathProvider.ResourceRoot, "Fonts", "OpenSource");
            var css = new StringBuilder();
            css.AppendLine("<style id=\"edm-report-font-policy\">");
            AppendFontFace(css, fontRoot, SansRegularFileName, "Noto Sans CJK SC", 400);
            AppendFontFace(css, fontRoot, SansBoldFileName, "Noto Sans CJK SC", 700);
            AppendFontFace(css, fontRoot, SerifRegularFileName, "Noto Serif CJK SC", 400);
            css.AppendLine(":root {");
            css.Append("  --edm-report-font-sans: ").Append(SansCssFamilyList).AppendLine(";");
            css.Append("  --edm-report-font-serif: ").Append(SerifCssFamilyList).AppendLine(";");
            css.AppendLine("}");
            css.AppendLine("body { font-family: var(--edm-report-font-sans) !important; }");
            css.AppendLine("body[data-edm-report-font=\"serif\"] { font-family: var(--edm-report-font-serif) !important; }");
            css.AppendLine("code, pre, kbd, samp { font-family: \"JetBrains Mono\", \"Cascadia Mono\", \"Liberation Mono\", monospace; }");
            css.AppendLine("</style>");
            return css.ToString();
        }

        public static ReportFontAvailability Inspect(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            string fontRoot = Path.Combine(pathProvider.ResourceRoot, "Fonts", "OpenSource");
            return new ReportFontAvailability(
                FontRoot: fontRoot,
                SansRegularAvailable: File.Exists(Path.Combine(fontRoot, SansRegularFileName)),
                SansBoldAvailable: File.Exists(Path.Combine(fontRoot, SansBoldFileName)),
                SerifRegularAvailable: File.Exists(Path.Combine(fontRoot, SerifRegularFileName)));
        }

        private static void AppendFontFace(
            StringBuilder css,
            string fontRoot,
            string fileName,
            string family,
            int weight)
        {
            string fontPath = Path.Combine(fontRoot, fileName);
            if (!File.Exists(fontPath))
            {
                return;
            }

            string uri = new Uri(Path.GetFullPath(fontPath)).AbsoluteUri;
            css.AppendLine("@font-face {");
            css.Append("  font-family: ").Append(CssQuote(family)).AppendLine(";");
            css.Append("  src: url(").Append(CssQuote(uri)).AppendLine(") format(\"opentype\");");
            css.Append("  font-style: normal; font-weight: ").Append(weight).AppendLine("; font-display: block;");
            css.AppendLine("}");
        }

        private static string CssQuote(string value) =>
            $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", "\\A ", StringComparison.Ordinal)}\"";
    }

    internal sealed record ReportFontAvailability(
        string FontRoot,
        bool SansRegularAvailable,
        bool SansBoldAvailable,
        bool SerifRegularAvailable)
    {
        public bool Complete => SansRegularAvailable && SansBoldAvailable && SerifRegularAvailable;
    }
}
