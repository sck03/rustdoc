using System.Globalization;
using System.Net;
using System.Text;

namespace ExportDocManager.Api.Hosting;

internal static class ApiContainerPackingPdfHtmlBuilder
{
    public static string Build(ApiContainerPackingPdfRequest request, DateTimeOffset generatedAt)
    {
        ApiContainerPackingAnalysisDto analysis = request.Analysis;
        var html = new StringBuilder("""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
            @page{size:A4 portrait;margin:10mm}*{box-sizing:border-box}body{margin:0;color:#172033;font:12px/1.5 Arial,"Microsoft YaHei",sans-serif}header{display:flex;justify-content:space-between;gap:20px;border-bottom:2px solid #1f5b94;padding-bottom:10px}h1{font-size:24px;margin:2px 0;color:#123b63}.eyebrow{color:#52708d;font-size:11px;letter-spacing:.18em}.meta{text-align:right;color:#40566d}.cards{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:12px 0}.card{border:1px solid #d7e1ea;border-radius:7px;padding:8px;background:#f8fbfd}.card strong{display:block;font-size:18px;color:#174f82}.scene{border:1px solid #b9c9d8;border-radius:8px;padding:10px;page-break-inside:avoid}.scene h2,.list h2{font-size:15px;margin:0 0 8px}.scene svg{display:block;width:100%;height:auto;background:#f3f7fa}.note{margin-top:6px;color:#60758a}table{width:100%;border-collapse:collapse}.list{margin-top:12px}th,td{padding:6px;border:1px solid #d9e2ea;text-align:left;vertical-align:top}th{background:#eaf2f8;color:#24445f}tbody tr:nth-child(even){background:#f8fafc}.ok{color:#137345}.warn{color:#b54708}@media print{.list{break-before:auto}}
            </style></head><body>
            """);
        string projectName = Text(request.ProjectName, "未命名方案");
        html.Append("<header><div><div class=\"eyebrow\">现场装柜作业单</div><h1>")
            .Append(projectName)
            .Append("</h1></div><div class=\"meta\"><strong>")
            .Append(Text(request.ContainerType, "自定义柜型"))
            .Append("</strong><br>")
            .Append(Text($"{request.Container.Length} × {request.Container.Width} × {request.Container.Height} cm", string.Empty))
            .Append("<br>生成时间：")
            .Append(Text(generatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), string.Empty))
            .Append("</div></header><section class=\"cards\">");
        Card(html, "体积利用率", $"{analysis.VolumeUtilizationPercent:0.##}%");
        Card(html, "重量利用率", $"{analysis.WeightUtilizationPercent:0.##}%");
        Card(html, "已装 / 总箱数", $"{analysis.PackedPackages} / {analysis.TotalPackages}");
        Card(html, "预计柜数", analysis.EstimatedContainerCount.ToString(CultureInfo.InvariantCulture));
        html.Append("</section><section class=\"scene\"><h2>俯视装载示意</h2><svg role=\"img\" viewBox=\"0 0 ")
            .Append(Number(request.Container.Length)).Append(' ').Append(Number(request.Container.Width))
            .Append("\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"100%\" height=\"100%\" fill=\"#eef4f8\" stroke=\"#24445f\" stroke-width=\"3\"/>");
        foreach (ApiPackedCargoItemDto item in analysis.PackedItems.Take(800))
        {
            html.Append("<rect x=\"").Append(Number(item.X)).Append("\" y=\"").Append(Number(item.Y))
                .Append("\" width=\"").Append(Number(item.Width)).Append("\" height=\"").Append(Number(item.Height))
                .Append("\" rx=\"2\" fill=\"").Append(Color(item.ColorArgb))
                .Append("\" fill-opacity=\".78\" stroke=\"#172033\" stroke-width=\"1\"><title>")
                .Append(Text(item.DetailText, item.Name)).Append("</title></rect>");
        }
        html.Append("</svg><div class=\"note\">颜色代表货物类别；示意图按柜体实际长宽比例绘制。")
            .Append(analysis.PackedItems.Count > 800 ? "为保证文档流畅，仅展示前 800 个装载块。" : string.Empty)
            .Append("</div></section><section class=\"list\"><h2>货物汇总</h2><table><thead><tr><th>货物</th><th>装载说明</th><th>装载块</th><th>代表箱数</th><th>重量</th><th>区域</th></tr></thead><tbody>");
        foreach (var group in analysis.PackedItems
                     .GroupBy(item => new { item.Name, item.DetailText, item.PreferredZone, item.ColorArgb })
                     .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.DetailText, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.PreferredZone, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.ColorArgb))
        {
            html.Append("<tr><td><span style=\"display:inline-block;width:9px;height:9px;border-radius:2px;background:")
                .Append(Color(group.Key.ColorArgb)).Append(";margin-right:5px\"></span>")
                .Append(Text(group.Key.Name, "未命名货物")).Append("</td><td>")
                .Append(Text(group.Key.DetailText, "-")).Append("</td><td>")
                .Append(group.Count()).Append("</td><td>")
                .Append(group.Sum(item => (long)item.UnitsRepresented)).Append("</td><td>")
                .Append(group.Sum(item => item.TotalWeight).ToString("0.##", CultureInfo.InvariantCulture)).Append(" kg</td><td>")
                .Append(Text(group.Key.PreferredZone, "Auto")).Append("</td></tr>");
        }
        html.Append("</tbody></table><p class=\"")
            .Append(analysis.UnpackedPackages == 0 ? "ok" : "warn")
            .Append("\">未装箱数：").Append(analysis.UnpackedPackages)
            .Append("；重心状态：").Append(analysis.IsCenterOfGravityWithinTolerance ? "在容差范围内" : "超出容差，请复核")
            .Append("。</p></section></body></html>");
        return html.ToString();
    }

    private static void Card(StringBuilder html, string label, string value) =>
        html.Append("<div class=\"card\"><span>").Append(Text(label, string.Empty)).Append("</span><strong>")
            .Append(Text(value, "-")).Append("</strong></div>");

    private static string Text(string? value, string fallback) =>
        WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());

    private static string Number(float value) =>
        float.IsFinite(value) ? Math.Max(0, value).ToString("0.###", CultureInfo.InvariantCulture) : "0";

    private static string Number(int value) => Math.Max(1, value).ToString(CultureInfo.InvariantCulture);

    private static string Color(int argb) => $"#{unchecked((uint)argb) & 0x00FFFFFF:X6}";
}
