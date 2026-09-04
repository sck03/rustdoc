using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Reporting;

internal static class ReportTemplateV3SchemaValidator
{
    private static readonly Regex Id = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Field = new("^[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Color = new("^#[0-9a-fA-F]{3,8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Hash = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] PageMargins = ["marginTopHundredthMm", "marginRightHundredthMm", "marginBottomHundredthMm", "marginLeftHundredthMm"];
    public static void Validate(ReportDocumentType reportType, JsonElement root)
    {
        Object(root, "V3 设计结构必须是对象。");
        if (RequiredInt(root, "version", "$.version") != ReportTemplateV3ContractCatalog.SchemaVersion) throw Error("$.version 必须为 3。");
        Marker(root, "astKind", ReportTemplateV3ContractCatalog.AstKind, "$.astKind"); Marker(root, "coordinateUnit", ReportTemplateV3ContractCatalog.CoordinateUnit, "$.coordinateUnit"); Marker(root, "contractVersion", ReportTemplateV3ContractCatalog.ContractVersion, "$.contractVersion");
        var embedded = RequiredString(root, "reportType", "$.reportType");
        if (!Enum.TryParse<ReportDocumentType>(embedded, true, out var parsed) || parsed != reportType) throw Error("V3 模板数据域与当前报表类型不一致。");
        var page = RequiredObject(root, "page", "$.page");
        if (!string.Equals(RequiredString(page, "size", "$.page.size"), ReportTemplateV3ContractCatalog.PageSize, StringComparison.OrdinalIgnoreCase)) throw Error("报表模板 V3 页面必须固定为 A4。");
        if (!root.TryGetProperty("layers", out var layers)) return;
        ValidatePage(page); var resourceIds = ValidateResources(root); ValidateRelease(root); if (root.TryGetProperty("grid", out var grid)) ValidateGrid(grid); ValidateLayers(reportType, layers, page, resourceIds);
    }
    private static void ValidatePage(JsonElement page)
    {
        var orientation = RequiredString(page, "orientation", "$.page.orientation"); if (!ReportTemplateV3ContractCatalog.Orientations.Contains(orientation, StringComparer.Ordinal)) throw Error("V3 页面方向无效。");
        var expected = ReportTemplateV3ContractCatalog.A4Dimensions(orientation); if (RequiredInt(page, "widthHundredthMm", "$.page.widthHundredthMm") != expected.WidthHundredthMm || RequiredInt(page, "heightHundredthMm", "$.page.heightHundredthMm") != expected.HeightHundredthMm) throw Error("V3 页面尺寸必须与 A4 方向一致，单位为 1/100 mm。");
        foreach (var name in PageMargins) if (page.TryGetProperty(name, out var value) && (!Integer(value) || value.GetInt32() is < 0 or > 6000)) throw Error($"$.page.{name}无效。");
    }
    private static HashSet<string>? ValidateResources(JsonElement root)
    {
        if (!root.TryGetProperty("resources", out var resources)) return null;
        Array(resources, "$.resources");
        if (resources.GetArrayLength() > ReportTemplateV3ContractCatalog.MaxResources) throw Error("V3 图片资源数量超过上限。");
        var ids = new HashSet<string>(StringComparer.Ordinal); int index = 0;
        foreach (var resource in resources.EnumerateArray())
        {
            var path = $"$.resources[{index++}]"; Object(resource, "图片资源必须是对象。"); var id = RequiredString(resource, "id", $"{path}.id");
            if (!Id.IsMatch(id) || !ids.Add(id)) throw Error("V3 图片资源 ID 缺失、格式无效或重复。");
            if (!ReportTemplateV3ContractCatalog.ImageMediaTypes.Contains(RequiredString(resource, "mediaType", $"{path}.mediaType"), StringComparer.Ordinal)) throw Error("V3 图片资源媒体类型不受支持。");
            if (resource.TryGetProperty("byteLength", out var bytes) && (!Integer(bytes) || bytes.GetInt64() is < 0 or > ReportTemplateV3ContractCatalog.MaxResourceBytes)) throw Error("V3 图片资源大小无效。");
            if (resource.TryGetProperty("sha256", out var hash) && (hash.ValueKind != JsonValueKind.String || !Hash.IsMatch(hash.GetString() ?? string.Empty))) throw Error("V3 图片资源 SHA-256 无效。");
        }
        return ids;
    }
    private static void ValidateRelease(JsonElement root)
    {
        if (!root.TryGetProperty("release", out var release)) return;
        Object(release, "发布信息必须是对象。"); var state = RequiredString(release, "state", "$.release.state");
        if (!ReportTemplateV3ContractCatalog.ReleaseStates.Contains(state, StringComparer.Ordinal)) throw Error("V3 发布状态无效。"); if (RequiredInt(release, "revision", "$.release.revision") < 0) throw Error("V3 发布修订号不能为负数。");
        if (release.TryGetProperty("publishedAt", out var published) && (published.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(published.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))) throw Error("V3 发布时间必须是有效的 ISO-8601 时间。");
        if (state == "Published" && (!release.TryGetProperty("publishedAt", out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))) throw Error("已发布 V3 模板必须记录 publishedAt。");
    }
    private static void ValidateGrid(JsonElement grid)
    {
        Object(grid, "网格设置必须是对象。"); BoolIfPresent(grid, "enabled", "$.grid.enabled"); BoolIfPresent(grid, "snap", "$.grid.snap");
        if (grid.TryGetProperty("sizeHundredthMm", out var size) && (!Integer(size) || size.GetInt32() is < 100 or > 5000)) throw Error("V3 网格间距无效。");
    }
    private static void ValidateLayers(ReportDocumentType reportType, JsonElement layers, JsonElement page, HashSet<string>? resourceIds)
    {
        Array(layers, "$.layers");
        if (layers.GetArrayLength() is 0 or > ReportTemplateV3ContractCatalog.MaxLayers) throw Error("V3 图层数量超出允许范围。");
        var ids = new HashSet<string>(StringComparer.Ordinal); var blockIds = new HashSet<string>(StringComparer.Ordinal); int total = 0; bool hasBody = false; int index = 0;
        foreach (var layer in layers.EnumerateArray())
        {
            var path = $"$.layers[{index++}]"; Object(layer, "图层必须是对象。"); var id = RequiredString(layer, "id", $"{path}.id");
            if (!Id.IsMatch(id) || !ids.Add(id)) throw Error("V3 图层 ID 缺失、格式无效或重复。");
            var role = RequiredString(layer, "role", $"{path}.role");
            if (!ReportTemplateV3ContractCatalog.LayerRoles.Contains(role, StringComparer.Ordinal)) throw Error("V3 图层角色无效。");
            hasBody |= role == "Body"; BoolIfPresent(layer, "visible", $"{path}.visible"); BoolIfPresent(layer, "locked", $"{path}.locked"); if (layer.TryGetProperty("designHeightHundredthMm", out var designHeight) && (!Integer(designHeight) || designHeight.GetInt32() is < 0 or > 60000)) throw Error("V3 图层设计高度无效。"); if (layer.TryGetProperty("print", out var print)) ValidatePrint(print, role, path);
            var elements = RequiredArray(layer, "elements", $"{path}.elements");
            if (elements.GetArrayLength() > ReportTemplateV3ContractCatalog.MaxElementsPerLayer || (total += elements.GetArrayLength()) > ReportTemplateV3ContractCatalog.MaxTotalElements) throw Error("V3 模板元素数量超过上限。");
            int elementIndex = 0;
            foreach (var element in elements.EnumerateArray()) ValidateElement(reportType, role, element, $"{path}.elements[{elementIndex++}]", page, ids, blockIds, resourceIds);
        }
        if (!hasBody) throw Error("V3 至少需要主体图层。");
    }
    private static void ValidatePrint(JsonElement print, string role, string path)
    {
        Object(print, "V3 图层打印设置必须是对象。"); BoolIfPresent(print, "repeatOnEveryPage", $"{path}.print.repeatOnEveryPage"); BoolIfPresent(print, "keepTogether", $"{path}.print.keepTogether"); BoolIfPresent(print, "pinToPageBottom", $"{path}.print.pinToPageBottom");
        if (role == "Body" && (Bool(print, "repeatOnEveryPage") || Bool(print, "pinToPageBottom"))) throw Error("V3 主体图层不能重复或贴底。");
        if (print.TryGetProperty("minHeightHundredthMm", out var height) && (!Integer(height) || height.GetInt32() is < 0 or > 26000)) throw Error("V3 图层最小高度无效。");
    }
    private static void ValidateElement(ReportDocumentType reportType, string role, JsonElement element, string path, JsonElement page, HashSet<string> ids, HashSet<string> blockIds, HashSet<string>? resourceIds)
    {
        Object(element, "元素必须是对象。"); var id = RequiredString(element, "id", $"{path}.id");
        if (!Id.IsMatch(id) || !ids.Add(id)) throw Error("V3 元素 ID 缺失、格式无效或重复。");
        int x = RequiredInt(element, "xHundredthMm", $"{path}.xHundredthMm"), y = RequiredInt(element, "yHundredthMm", $"{path}.yHundredthMm"), width = RequiredInt(element, "widthHundredthMm", $"{path}.widthHundredthMm"), height = RequiredInt(element, "heightHundredthMm", $"{path}.heightHundredthMm");
        int pageWidth = RequiredInt(page, "widthHundredthMm", "$.page.widthHundredthMm"), pageHeight = RequiredInt(page, "heightHundredthMm", "$.page.heightHundredthMm");
        if (x < 0 || y < 0 || width < ReportTemplateV3ContractCatalog.MinElementSize || height < ReportTemplateV3ContractCatalog.MinElementSize || width > pageWidth || height > pageHeight || x + width > pageWidth || y + height > pageHeight) throw Error($"{path} 的几何边界超出 A4 页面。");
        if (element.TryGetProperty("rotationDeg", out var rotation) && (!Number(rotation) || rotation.GetDouble() is < -360 or > 360)) throw Error($"{path}.rotationDeg 无效。"); if (element.TryGetProperty("zIndex", out var z) && (!Integer(z) || z.GetInt32() is < -100000 or > 100000)) throw Error($"{path}.zIndex 无效。"); if (element.TryGetProperty("style", out var style)) ValidateStyle(style, path);
        var type = RequiredString(element, "type", $"{path}.type");
        if (!ReportTemplateV3ContractCatalog.ElementTypes.Contains(type, StringComparer.Ordinal)) throw Error("V3 元素类型不受支持。");
        switch (type)
        {
            case "Text": TextLimit(element, "text", path); break;
            case "Field": ValidateField(reportType, RequiredString(element, "fieldPath", $"{path}.fieldPath"), $"{path}.fieldPath"); break;
            case "Image": ValidateImage(reportType, element, path, resourceIds); break;
            case "PageNumber": if (RequiredString(element, "format", $"{path}.format") is not ("Current" or "CurrentOfTotal")) throw Error("V3 页码格式无效。"); break;
            case "Line": if (RequiredString(element, "direction", $"{path}.direction") is not ("Horizontal" or "Vertical")) throw Error("V3 线方向无效。"); break;
            case "Flow": ValidateFlow(reportType, role, element, path, blockIds); break;
        }
    }
    private static void ValidateImage(ReportDocumentType reportType, JsonElement element, string path, HashSet<string>? resourceIds)
    {
        var source = RequiredString(element, "sourceKind", $"{path}.sourceKind");
        if (source is not ("Field" or "Resource")) throw Error("V3 图片来源无效。");
        var purpose = element.TryGetProperty("purpose", out var value) ? value.GetString() : null;
        if (purpose is not (null or "Image" or "Stamp")) throw Error("V3 图片用途无效。");
        if (source == "Field")
        {
            var field = RequiredString(element, "fieldPath", $"{path}.fieldPath"); ValidateControlledImage(reportType, field, $"{path}.fieldPath");
            if (purpose == "Stamp" && !ReportTemplateV3ContractCatalog.StampFieldPaths.Contains(field, StringComparer.Ordinal)) throw Error("V3 印章只能绑定受控单证章字段。");
        }
        else
        {
            var id = RequiredString(element, "resourceId", $"{path}.resourceId");
            if (!Id.IsMatch(id) || resourceIds is null || !resourceIds.Contains(id)) throw Error("V3 图片必须引用受控资源清单中的 resourceId。");
            if (purpose == "Stamp") throw Error("V3 印章不能直接绑定资源。");
        }
    }
    private static void ValidateFlow(ReportDocumentType reportType, string role, JsonElement element, string path, HashSet<string> blockIds)
    {
        var kind = RequiredString(element, "flowKind", $"{path}.flowKind"); if (!ReportTemplateV3ContractCatalog.FlowTypes.Contains(kind, StringComparer.Ordinal)) throw Error("V3 流组件类型无效。");
        if (role != "Body" && kind is ("DetailTable" or "PageBreak")) throw Error("明细表和分页符只能放在主体图层。"); if (reportType == ReportDocumentType.PaymentVoucher && kind == "DetailTable") throw Error("付款/报销模板不能使用出口单据明细表。");
        var block = RequiredObject(element, "block", $"{path}.block"); if (RequiredString(block, "type", $"{path}.block.type") != kind) throw Error("V3 流组件 block 类型必须与 flowKind 一致。");
        ValidateBlock(reportType, block, $"{path}.block", blockIds, 0);
    }
    private static void ValidateBlock(ReportDocumentType reportType, JsonElement block, string path, HashSet<string> ids, int depth)
    {
        if (depth > 12) throw Error("V3 结构嵌套层级过深。");
        if (block.TryGetProperty("id", out var idValue)) { var id = idValue.ValueKind == JsonValueKind.String ? idValue.GetString() ?? string.Empty : string.Empty; if (!Id.IsMatch(id) || !ids.Add(id)) throw Error("V3 结构组件 ID 无效或重复。"); }
        if (block.TryGetProperty("type", out var type) && (type.ValueKind != JsonValueKind.String || !ReportTemplateV3ContractCatalog.FlowTypes.Contains(type.GetString(), StringComparer.Ordinal) && !ReportTemplateV3ContractCatalog.InlineBlockTypes.Contains(type.GetString(), StringComparer.Ordinal))) throw Error("V3 结构组件类型不受支持。");
        if (block.TryGetProperty("expression", out _) || block.TryGetProperty("eval", out _)) throw Error("V3 不允许动态表达式，只支持结构化白名单条件。");
        if (block.TryGetProperty("operator", out var op) && (op.ValueKind != JsonValueKind.String || !ReportTemplateV3ContractCatalog.ConditionOperators.Contains(op.GetString(), StringComparer.Ordinal))) throw Error("V3 条件运算符不在白名单内。");
        if (block.TryGetProperty("fieldPath", out var field) && field.ValueKind == JsonValueKind.String) { var fieldPath = field.GetString() ?? string.Empty; if (block.TryGetProperty("contentKind", out var contentKind) && contentKind.GetString() == "Field" && string.IsNullOrWhiteSpace(fieldPath)) throw Error($"{path}.fieldPath 不能为空。"); if (!string.IsNullOrWhiteSpace(fieldPath)) ValidateField(reportType, fieldPath, $"{path}.fieldPath"); }
        if (block.TryGetProperty("sourcePath", out var source) && source.ValueKind == JsonValueKind.String && source.GetString() != "Invoice.Items") throw Error("V3 明细表数据源只能是 Invoice.Items。");
        foreach (var property in block.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.Object) ValidateBlock(reportType, property.Value, $"{path}.{property.Name}", ids, depth + 1); else if (property.Value.ValueKind == JsonValueKind.Array) foreach (var (item, index) in property.Value.EnumerateArray().Select((item, index) => (item, index))) if (item.ValueKind == JsonValueKind.Object) ValidateBlock(reportType, item, $"{path}.{property.Name}[{index}]", ids, depth + 1);
    }
    private static void ValidateField(ReportDocumentType type, string field, string path)
    {
        if (!Field.IsMatch(field)) throw Error("V3 字段路径只能使用点分隔标识符。");
        bool allowed = type == ReportDocumentType.PaymentVoucher ? field == "cny_amount_upper" || field.StartsWith("Payment.", StringComparison.Ordinal) : field.StartsWith("Invoice.", StringComparison.Ordinal) || field.StartsWith("Customer.", StringComparison.Ordinal) || field.StartsWith("Exporter.", StringComparison.Ordinal) || field.StartsWith("item.", StringComparison.Ordinal) || field is "ShowSeal" or "doc_seal_path" or "customs_seal_path" or "shipping_marks_image_data";
        if (!allowed) throw Error($"V3 字段 {field} 不属于当前报表数据域。");
    }
    private static void ValidateControlledImage(ReportDocumentType type, string field, string path)
    {
        if (type != ReportDocumentType.ExportDocument || !ReportTemplateV3ContractCatalog.ControlledImageFieldPaths.Contains(field, StringComparer.Ordinal)) throw Error("图片字段必须来自出口单据受控 data URI 字段。");
        ValidateField(type, field, path);
    }
    private static void ValidateStyle(JsonElement style, string path)
    {
        Object(style, "V3 样式必须是对象。"); foreach (var name in new[] { "color", "backgroundColor", "borderColor" })
            if (style.TryGetProperty(name, out var color) && (color.ValueKind != JsonValueKind.String || !Color.IsMatch(color.GetString() ?? string.Empty))) throw Error($"{path}.style.{name} 不是安全颜色值。");
        if (style.TryGetProperty("borderWidthPx", out var width) && (!Number(width) || width.GetDouble() is < 0 or > 8)) throw Error("V3 边框宽度无效。");
    }
    private static void TextLimit(JsonElement value, string name, string path)
    {
        if (!value.TryGetProperty(name, out var text) || text.ValueKind != JsonValueKind.String || (text.GetString() ?? string.Empty).Length > ReportTemplateV3ContractCatalog.MaxTextLength) throw Error($"{path}.{name} 缺失或过长。");
    }
    private static void Marker(JsonElement parent, string name, string expected, string path)
    {
        if (parent.TryGetProperty(name, out var value) && RequiredString(value, path) != expected) throw Error($"{path} 不受支持。");
    }
    private static JsonElement RequiredObject(JsonElement parent, string name, string path) { if (!parent.TryGetProperty(name, out var value)) throw Error($"{path} 缺失。"); Object(value, "V3 属性必须是对象。"); return value; }
    private static JsonElement RequiredArray(JsonElement parent, string name, string path) { if (!parent.TryGetProperty(name, out var value)) throw Error($"{path} 缺失。"); Array(value, path); return value; }
    private static string RequiredString(JsonElement parent, string name, string path) => parent.TryGetProperty(name, out var value) ? RequiredString(value, path) : throw Error($"{path} 缺失。");
    private static string RequiredString(JsonElement value, string path) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : throw Error($"{path} 必须是字符串。");
    private static int RequiredInt(JsonElement parent, string name, string path) => parent.TryGetProperty(name, out var value) && Integer(value) ? value.GetInt32() : throw Error($"{path} 必须是整数。");
    private static void Object(JsonElement value, string message) { if (value.ValueKind != JsonValueKind.Object) throw Error(message); }
    private static void Array(JsonElement value, string path) { if (value.ValueKind != JsonValueKind.Array) throw Error($"{path} 必须是数组。"); }
    private static void BoolIfPresent(JsonElement value, string name, string path) { if (value.TryGetProperty(name, out var item) && item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Error($"{path} 必须是布尔值。"); }
    private static bool Bool(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;
    private static bool Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);
    private static bool Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number);
    private static ArgumentException Error(string message) => new(message);
}
