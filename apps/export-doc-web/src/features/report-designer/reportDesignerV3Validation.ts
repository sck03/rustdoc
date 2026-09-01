import { isRecord, type ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";
import { normalizeEmbeddedReportDesignerBlock } from "./reportDesignerSchemaValidation.ts";
import {
  validateControlledReportImageFieldPath,
  validateReportTypeFieldPath,
} from "./reportDesignerSchemaDomains.ts";
import {
  REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER,
  REPORT_DESIGNER_V3_MAX_ALT_TEXT_LENGTH,
  REPORT_DESIGNER_V3_MAX_FALLBACK_LENGTH,
  REPORT_DESIGNER_V3_MAX_FIELD_PATH_LENGTH,
  REPORT_DESIGNER_V3_MAX_FONT_FAMILY_LENGTH,
  REPORT_DESIGNER_V3_MAX_LABEL_LENGTH,
  REPORT_DESIGNER_V3_MAX_LAYER_COUNT,
  REPORT_DESIGNER_V3_MAX_TEXT_LENGTH,
  REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS,
  REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM,
  REPORT_DESIGNER_V3_VERSION,
  clampReportDesignerV3ElementToPage,
  reportDesignerV3ElementBounds,
  reportDesignerV3PageDimensions,
  type ReportDesignerV3Element,
  type ReportDesignerV3ElementStyle,
  type ReportDesignerV3Layer,
  type ReportDesignerV3Page,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

const identifierPattern = /^[A-Za-z0-9][A-Za-z0-9_.:-]{0,119}$/;
const colorPattern = /^#[0-9a-fA-F]{3,8}$/;
const fontFamilyPattern = /^[A-Za-z0-9 \t"',._-]+$/;
const fieldPathPattern = /^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$/;
const resourceIdPattern = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$/;
export type ReportDesignerV3ValidationResult = {
  schema: ReportDesignerV3Schema | null;
  issues: ReportDesignerSchemaIssue[];
};

export function normalizeReportDesignerV3Schema(
  input: unknown,
  expectedReportType?: ReportDesignerReportType,
): ReportDesignerV3ValidationResult {
  const issues: ReportDesignerSchemaIssue[] = [];
  if (!isRecord(input) || input.version !== REPORT_DESIGNER_V3_VERSION) {
    issues.push({ severity: "error", path: "$.version", message: "仅支持报表设计器 schema version 3。" });
    return { schema: null, issues };
  }

  const suppliedReportType = input.reportType === "PaymentVoucher" ? "PaymentVoucher" : input.reportType === "ExportDocument" ? "ExportDocument" : null;
  if (!suppliedReportType) {
    issues.push({ severity: "error", path: "$.reportType", message: "报表类型无效。" });
  }
  if (expectedReportType && suppliedReportType && suppliedReportType !== expectedReportType) {
    issues.push({
      severity: "error",
      path: "$.reportType",
      message: `模板数据域与当前报表类型不一致（当前为 ${expectedReportType}，结构声明为 ${suppliedReportType}）。请修正后再保存。`,
    });
  }
  // The route/template selection is authoritative.  A mismatched embedded
  // schema is still returned as a safe draft so the editor can show and repair
  // it, but all field-domain validation and subsequent export use this type.
  const reportType = expectedReportType ?? suppliedReportType;
  const page = normalizePage(input.page, issues);
  const layers = normalizeLayers(input.layers, page, reportType ?? "ExportDocument", issues);
  const grid = normalizeGrid(input.grid, issues);
  if (!reportType || !page || !layers) {
    return { schema: null, issues };
  }
  validateBodyFlowOverlaps(layers, issues);

  return {
    schema: {
      version: 3,
      reportType,
      page,
      layers,
      grid,
      metadata: normalizeMetadata(input.metadata),
    },
    issues,
  };
}

export function validateReportDesignerV3Schema(schema: ReportDesignerV3Schema) {
  return normalizeReportDesignerV3Schema(schema).issues;
}

export function hasBlockingReportDesignerV3SchemaIssues(issues: ReportDesignerSchemaIssue[]) {
  return issues.some((issue) => issue.severity === "error");
}

function normalizePage(value: unknown, issues: ReportDesignerSchemaIssue[]): ReportDesignerV3Page | null {
  if (!isRecord(value)) {
    issues.push({ severity: "error", path: "$.page", message: "v3 页面设置缺失。" });
    return null;
  }
  const orientation = readEnum(value.orientation, ["Portrait", "Landscape"] as const, "Portrait", "$.page.orientation", issues);
  const expectedDimensions = reportDesignerV3PageDimensions(orientation);
  const suppliedWidth = readInteger(value.widthHundredthMm, expectedDimensions.width, 4000, 60000, "$.page.widthHundredthMm", issues);
  const suppliedHeight = readInteger(value.heightHundredthMm, expectedDimensions.height, 4000, 60000, "$.page.heightHundredthMm", issues);
  if (suppliedWidth !== expectedDimensions.width || suppliedHeight !== expectedDimensions.height) {
    issues.push({ severity: "warning", path: "$.page", message: "v3 画布固定为 A4，页面尺寸已按方向恢复为标准 A4。" });
  }
  const width = expectedDimensions.width;
  const height = expectedDimensions.height;
  const marginTop = readInteger(value.marginTopHundredthMm, 800, 0, Math.max(0, height - 200), "$.page.marginTopHundredthMm", issues);
  const marginRight = readInteger(value.marginRightHundredthMm, 800, 0, Math.max(0, width - 200), "$.page.marginRightHundredthMm", issues);
  const marginBottom = readInteger(value.marginBottomHundredthMm, 800, 0, Math.max(0, height - marginTop - 100), "$.page.marginBottomHundredthMm", issues);
  const marginLeft = readInteger(value.marginLeftHundredthMm, 800, 0, Math.max(0, width - marginRight - 100), "$.page.marginLeftHundredthMm", issues);
  if (value.size !== "A4") {
    issues.push({ severity: "warning", path: "$.page.size", message: "v3 仅支持 A4，已统一为 A4。" });
  }
  const fontFamily = normalizeFontFamily(value.fontFamily, "$.page.fontFamily", issues);
  const fontSizePt = readNumber(value.fontSizePt, 10, 6, 48, "$.page.fontSizePt", issues);

  return {
    size: "A4",
    orientation,
    widthHundredthMm: width,
    heightHundredthMm: height,
    marginTopHundredthMm: marginTop,
    marginRightHundredthMm: marginRight,
    marginBottomHundredthMm: marginBottom,
    marginLeftHundredthMm: marginLeft,
    fontFamily,
    fontSizePt,
  };
}

function normalizeLayers(
  value: unknown,
  page: ReportDesignerV3Page | null,
  reportType: ReportDesignerReportType,
  issues: ReportDesignerSchemaIssue[],
): ReportDesignerV3Layer[] | null {
  if (!Array.isArray(value) || value.length === 0) {
    issues.push({ severity: "error", path: "$.layers", message: "v3 至少需要一个图层。" });
    return null;
  }
  const ids = new Set<string>();
  const blockIds = new Set<string>();
  const layers: ReportDesignerV3Layer[] = [];
  const sourceLayers = value.slice(0, REPORT_DESIGNER_V3_MAX_LAYER_COUNT);
  if (value.length > REPORT_DESIGNER_V3_MAX_LAYER_COUNT) {
    issues.push({ severity: "warning", path: "$.layers", message: `图层数量超过 ${REPORT_DESIGNER_V3_MAX_LAYER_COUNT}，多余图层已忽略。` });
  }
  let totalElements = 0;
  sourceLayers.forEach((rawLayer, layerIndex) => {
    const path = `$.layers[${layerIndex}]`;
    if (!isRecord(rawLayer)) {
      issues.push({ severity: "error", path, message: "图层必须是对象。" });
      return;
    }
    const id = normalizeId(rawLayer.id, `layer-${layerIndex + 1}`, ids, `${path}.id`, issues);
    const role = readEnum(rawLayer.role, ["Header", "Body", "Footer", "Overlay"] as const, "Body", `${path}.role`, issues);
    const remaining = Math.max(0, REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS - totalElements);
    const elements = normalizeElements(rawLayer.elements, page, reportType, role, `${path}.elements`, ids, blockIds, remaining, issues);
    totalElements += elements.length;
    layers.push({
      id,
      name: typeof rawLayer.name === "string" && rawLayer.name.trim() ? rawLayer.name.trim().slice(0, 120) : role === "Header" ? "页眉" : role === "Footer" ? "页脚" : role === "Body" ? "主体" : "覆盖层",
      role,
      print: normalizeLayerPrint(rawLayer.print, role, `${path}.print`, issues),
      visible: typeof rawLayer.visible === "boolean" ? rawLayer.visible : true,
      locked: typeof rawLayer.locked === "boolean" ? rawLayer.locked : false,
      elements,
    });
  });
  if (totalElements >= REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS && value.length > 0) {
    const suppliedCount = sourceLayers.reduce((sum, layer) => sum + (isRecord(layer) && Array.isArray(layer.elements) ? layer.elements.length : 0), 0);
    if (suppliedCount > totalElements) {
      issues.push({ severity: "warning", path: "$.layers", message: `元素总数超过 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS}，多余元素已忽略。` });
    }
  }
  if (!layers.some((layer) => layer.role === "Overlay") && layers.length < REPORT_DESIGNER_V3_MAX_LAYER_COUNT) {
    layers.push({
      id: normalizeId("layer-overlay", "layer-overlay", ids, "$.layers.overlay.id", issues),
      name: "覆盖层",
      role: "Overlay",
      print: createLegacyV3LayerPrintDefaults(),
      visible: true,
      locked: false,
      elements: [],
    });
    issues.push({ severity: "warning", path: "$.layers", message: "缺少覆盖层，已自动补齐。" });
  } else if (!layers.some((layer) => layer.role === "Overlay")) {
    // Do not exceed the hard layer cap merely to add an empty helper layer;
    // preserving all supplied layers is safer than silently dropping one.
    issues.push({ severity: "warning", path: "$.layers", message: `图层数量已达到 ${REPORT_DESIGNER_V3_MAX_LAYER_COUNT}，无法自动补齐覆盖层。` });
  }
  return layers.length > 0 ? layers : null;
}

function normalizeElements(
  value: unknown,
  page: ReportDesignerV3Page | null,
  reportType: ReportDesignerReportType,
  layerRole: ReportDesignerV3Layer["role"],
  path: string,
  ids: Set<string>,
  blockIds: Set<string>,
  remainingTotal: number,
  issues: ReportDesignerSchemaIssue[],
): ReportDesignerV3Element[] {
  if (!Array.isArray(value)) {
    issues.push({ severity: "error", path, message: "图层元素必须是数组。" });
    return [];
  }
  const layerLimit = Math.min(REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, remainingTotal);
  const exceedsLayerLimit = value.length > REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER;
  const exceedsTotalLimit = value.length > remainingTotal;
  if (exceedsLayerLimit || exceedsTotalLimit) {
    issues.push({ severity: "warning", path, message: exceedsTotalLimit
      ? `模板元素总数达到 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS}，该图层多余元素已忽略。`
      : `单个图层元素超过 ${REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER}，多余元素已忽略。` });
  }
  return value.slice(0, layerLimit)
    .map((rawElement, index) => normalizeElement(rawElement, page, reportType, layerRole, `${path}[${index}]`, ids, blockIds, issues))
    .filter((element): element is ReportDesignerV3Element => Boolean(element));
}

function normalizeElement(
  value: unknown,
  page: ReportDesignerV3Page | null,
  reportType: ReportDesignerReportType,
  layerRole: ReportDesignerV3Layer["role"],
  path: string,
  ids: Set<string>,
  blockIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDesignerV3Element | null {
  if (!isRecord(value) || typeof value.type !== "string") {
    issues.push({ severity: "error", path, message: "元素必须包含 type。" });
    return null;
  }
  const id = normalizeId(value.id, "element", ids, `${path}.id`, issues);
  const pageWidth = page?.widthHundredthMm ?? 21000;
  const pageHeight = page?.heightHundredthMm ?? 29700;
  const width = readInteger(value.widthHundredthMm, 8000, REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM, pageWidth, `${path}.widthHundredthMm`, issues);
  const height = readInteger(value.heightHundredthMm, 900, REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM, pageHeight, `${path}.heightHundredthMm`, issues);
  const x = readInteger(value.xHundredthMm, 0, 0, Math.max(0, pageWidth - width), `${path}.xHundredthMm`, issues);
  const y = readInteger(value.yHundredthMm, 0, 0, Math.max(0, pageHeight - height), `${path}.yHundredthMm`, issues);
  const base = {
    id,
    xHundredthMm: x,
    yHundredthMm: y,
    widthHundredthMm: width,
    heightHundredthMm: height,
    rotationDeg: readNumber(value.rotationDeg, 0, -360, 360, `${path}.rotationDeg`, issues),
    zIndex: readInteger(value.zIndex, 0, -100000, 100000, `${path}.zIndex`, issues),
    visible: typeof value.visible === "boolean" ? value.visible : true,
    locked: typeof value.locked === "boolean" ? value.locked : false,
    style: normalizeStyle(value.style, `${path}.style`, issues),
    outputEnabled: typeof value.outputEnabled === "boolean" ? value.outputEnabled : true,
    label: normalizeOptionalString(value.label, REPORT_DESIGNER_V3_MAX_LABEL_LENGTH, `${path}.label`, issues),
  };

  const normalizedElement: ReportDesignerV3Element | null = (() => {
    switch (value.type) {
      case "Text":
        return { ...base, type: "Text", text: normalizeText(value.text, `${path}.text`, issues) };
      case "Field": {
        const fieldPath = normalizeFieldPath(value.fieldPath, `${path}.fieldPath`, issues);
        validateReportTypeFieldPath(reportType, fieldPath, `${path}.fieldPath`, issues);
        return {
          ...base,
          type: "Field",
          fieldPath,
          fallbackText: normalizeOptionalString(value.fallbackText, REPORT_DESIGNER_V3_MAX_FALLBACK_LENGTH, `${path}.fallbackText`, issues),
        };
      }
      case "Image": {
        const sourceKind = readEnum(value.sourceKind, ["Field", "Resource"] as const, "Field", `${path}.sourceKind`, issues);
        const rawFieldPath = typeof value.fieldPath === "string" && value.fieldPath.trim() ? value.fieldPath : undefined;
        const fieldPath = sourceKind === "Field" && rawFieldPath
          ? normalizeFieldPath(rawFieldPath, `${path}.fieldPath`, issues)
          : undefined;
        const rawResourceId = typeof value.resourceId === "string" && value.resourceId.trim() ? value.resourceId : undefined;
        const resourceId = sourceKind === "Resource" && rawResourceId && resourceIdPattern.test(rawResourceId.trim())
          ? rawResourceId.trim()
          : undefined;
        if (sourceKind === "Resource" && rawResourceId && !resourceId) {
          issues.push({ severity: "warning", path: `${path}.resourceId`, message: "图片资源 ID 格式无效，已清除。" });
        }
        if (sourceKind === "Field" && !fieldPath) {
          issues.push({ severity: "error", path: `${path}.fieldPath`, message: "字段图片必须绑定字段。" });
        }
        if (sourceKind === "Field" && fieldPath) {
          validateControlledReportImageFieldPath(reportType, fieldPath, `${path}.fieldPath`, issues);
        }
        if (sourceKind === "Resource" && !resourceId) {
          issues.push({ severity: "warning", path: `${path}.resourceId`, message: "图片资源尚未上传，导出时会显示占位提示。" });
        }
        return {
          ...base,
          type: "Image",
          sourceKind,
          fieldPath,
          resourceId,
          altText: normalizeOptionalString(value.altText, REPORT_DESIGNER_V3_MAX_ALT_TEXT_LENGTH, `${path}.altText`, issues),
          hideWhenSourceEmpty: typeof value.hideWhenSourceEmpty === "boolean" ? value.hideWhenSourceEmpty : true,
        };
      }
      case "Rectangle":
        return { ...base, type: "Rectangle" };
      case "Line":
        return { ...base, type: "Line", direction: readEnum(value.direction, ["Horizontal", "Vertical"] as const, "Horizontal", `${path}.direction`, issues) };
      case "Flow": {
        const flowKind = readEnum(value.flowKind, ["Row", "Grid", "Conditional", "DetailTable", "PageBreak"] as const, "Row", `${path}.flowKind`, issues);
        if (layerRole !== "Body" && (flowKind === "DetailTable" || flowKind === "PageBreak")) {
          issues.push({
            severity: "error",
            path: `${path}.flowKind`,
            message: `${flowKind === "DetailTable" ? "明细表" : "分页符"}只能放在主体图层；页眉、页脚和覆盖层仅支持固定内容流组件。`,
          });
        }
        if (!isRecord(value.block) || value.block.type !== flowKind) {
          issues.push({ severity: "error", path: `${path}.block`, message: "流式元素的 block 类型与 flowKind 不一致。" });
          return null;
        }
        const normalizedBlock = normalizeEmbeddedReportDesignerBlock(value.block, {
          reportType,
          sectionType: layerRole === "Header" || layerRole === "Footer" ? layerRole : "Body",
          path: `${path}.block`,
          blockIds,
        });
        issues.push(...normalizedBlock.issues);
        if (!normalizedBlock.block || normalizedBlock.block.type !== flowKind) {
          if (normalizedBlock.block) {
            issues.push({ severity: "error", path: `${path}.block.type`, message: "流式元素的 block 类型与 flowKind 不一致。" });
          }
          return null;
        }
        return { ...base, type: "Flow", flowKind, block: normalizedBlock.block as never };
      }
      default:
        issues.push({ severity: "error", path: `${path}.type`, message: `不支持的 v3 元素类型 ${value.type}。` });
        return null;
    }
  })();

  if (!normalizedElement || !page) return normalizedElement;
  const clampedElement = clampReportDesignerV3ElementToPage(normalizedElement, page);
  if (
    clampedElement.xHundredthMm !== normalizedElement.xHundredthMm ||
    clampedElement.yHundredthMm !== normalizedElement.yHundredthMm ||
    clampedElement.widthHundredthMm !== normalizedElement.widthHundredthMm ||
    clampedElement.heightHundredthMm !== normalizedElement.heightHundredthMm
  ) {
    issues.push({ severity: "warning", path, message: "元素的视觉边界超出 A4 页面，已限制在画布内。" });
  }
  return clampedElement;
}

function normalizeLayerPrint(
  value: unknown,
  role: ReportDesignerV3Layer["role"],
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  // V3 documents written before layer print metadata existed must not gain
  // silent repeat/pin behavior when reopened.  Migrations from V2 provide
  // explicit settings, while old V3 records get a conservative no-repeat
  // default.
  const fallback = createLegacyV3LayerPrintDefaults();
  if (!isRecord(value)) return fallback;
  const repeat = typeof value.repeatOnEveryPage === "boolean" ? value.repeatOnEveryPage : fallback.repeatOnEveryPage;
  const keepTogether = typeof value.keepTogether === "boolean" ? value.keepTogether : fallback.keepTogether;
  const requestedPin = typeof value.pinToPageBottom === "boolean" ? value.pinToPageBottom : fallback.pinToPageBottom;
  const pinToPageBottom = role === "Footer" ? requestedPin : false;
  if (requestedPin && role !== "Footer") {
    issues.push({ severity: "warning", path: `${path}.pinToPageBottom`, message: "只有页脚图层支持贴底，已关闭该设置。" });
  }
  const minHeightHundredthMm = readInteger(value.minHeightHundredthMm, 0, 0, 26000, `${path}.minHeightHundredthMm`, issues);
  return { repeatOnEveryPage: role === "Body" ? false : repeat, keepTogether, pinToPageBottom, minHeightHundredthMm };
}

function createLegacyV3LayerPrintDefaults() {
  return {
    repeatOnEveryPage: false,
    keepTogether: false,
    pinToPageBottom: false,
    minHeightHundredthMm: 0,
  } as const;
}

function normalizeStyle(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]): ReportDesignerV3ElementStyle {
  if (!isRecord(value)) {
    return {};
  }
  const style: ReportDesignerV3ElementStyle = {};
  if (value.fontFamily !== undefined) {
    const fontFamily = normalizeFontFamily(value.fontFamily, `${path}.fontFamily`, issues);
    if (fontFamily) style.fontFamily = fontFamily;
  }
  if (value.fontSizePt !== undefined) style.fontSizePt = readNumber(value.fontSizePt, 10, 6, 96, `${path}.fontSizePt`, issues);
  if (typeof value.bold === "boolean") style.bold = value.bold;
  if (typeof value.color === "string" && colorPattern.test(value.color.trim())) style.color = value.color.trim();
  if (typeof value.backgroundColor === "string" && colorPattern.test(value.backgroundColor.trim())) style.backgroundColor = value.backgroundColor.trim();
  if (value.align === "Left" || value.align === "Center" || value.align === "Right") style.align = value.align;
  if (typeof value.borderColor === "string" && colorPattern.test(value.borderColor.trim())) style.borderColor = value.borderColor.trim();
  if (value.borderWidthPx !== undefined) style.borderWidthPx = readNumber(value.borderWidthPx, 0, 0, 8, `${path}.borderWidthPx`, issues);
  if (value.borderStyle === "Solid" || value.borderStyle === "Dashed" || value.borderStyle === "None") style.borderStyle = value.borderStyle;
  if (value.paddingHundredthMm !== undefined) style.paddingHundredthMm = readInteger(value.paddingHundredthMm, 0, 0, 1000, `${path}.paddingHundredthMm`, issues);
  return style;
}

function normalizeGrid(value: unknown, issues: ReportDesignerSchemaIssue[]): ReportDesignerV3Schema["grid"] {
  if (!isRecord(value)) return { enabled: true, sizeHundredthMm: 500, snap: true };
  return {
    enabled: typeof value.enabled === "boolean" ? value.enabled : true,
    sizeHundredthMm: readInteger(value.sizeHundredthMm, 500, 100, 5000, "$.grid.sizeHundredthMm", issues),
    snap: typeof value.snap === "boolean" ? value.snap : true,
  };
}

function normalizeMetadata(value: unknown) {
  if (!isRecord(value)) return undefined;
  return {
    migratedFromVersion: typeof value.migratedFromVersion === "number" ? value.migratedFromVersion : undefined,
    migratedAt: typeof value.migratedAt === "string" ? value.migratedAt.slice(0, 64) : undefined,
  };
}

function validateBodyFlowOverlaps(
  layers: ReportDesignerV3Layer[],
  issues: ReportDesignerSchemaIssue[],
) {
  const bodyLayers = layers
    .map((layer, layerIndex) => ({ layer, layerIndex }))
    .filter(({ layer }) => layer.role === "Body");
  const flowElements = bodyLayers.flatMap(({ layer, layerIndex }) => layer.elements
    .filter((element) => element.type === "Flow" && element.visible && element.outputEnabled)
    .map((element, elementIndex) => ({ element, layerIndex, elementIndex })));
  const staticElements = bodyLayers.flatMap(({ layer, layerIndex }) => layer.elements
    .filter((element) => element.type !== "Flow" && element.visible && element.outputEnabled)
    .map((element, elementIndex) => ({ element, layerIndex, elementIndex })));
  let emitted = 0;
  for (const flow of flowElements) {
    const flowBounds = reportDesignerV3ElementBounds(flow.element);
    for (const staticElement of staticElements) {
      const bounds = reportDesignerV3ElementBounds(staticElement.element);
      if (bounds.left >= flowBounds.right || bounds.right <= flowBounds.left ||
          bounds.top >= flowBounds.bottom || bounds.bottom <= flowBounds.top) {
        continue;
      }
      issues.push({
        severity: "warning",
        path: `$.layers[${staticElement.layerIndex}].elements[${staticElement.elementIndex}]`,
        message: `主体静态元素与 Flow 组件 ${flow.element.id} 的视觉区域重叠；打印时 Flow 流内容优先，请移动元素或改用覆盖层以避免动态明细遮挡。`,
      });
      if (++emitted >= 32) {
        issues.push({ severity: "warning", path: "$.layers", message: "主体静态元素与 Flow 的重叠提示超过 32 项，其他项请在画布中逐项检查。" });
        return;
      }
    }
  }
}

function normalizeFieldPath(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  const fieldPath = typeof value === "string" ? value.trim() : "";
  if (fieldPath.length > REPORT_DESIGNER_V3_MAX_FIELD_PATH_LENGTH) {
    issues.push({ severity: "error", path, message: `字段路径长度不能超过 ${REPORT_DESIGNER_V3_MAX_FIELD_PATH_LENGTH} 个字符。` });
    return "";
  }
  if (fieldPathPattern.test(fieldPath)) return fieldPath;
  issues.push({ severity: "error", path, message: "字段名只能使用点分隔标识符。" });
  return "";
}

function normalizeText(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value !== "string") return "";
  return truncateString(value, REPORT_DESIGNER_V3_MAX_TEXT_LENGTH, path, issues);
}

function normalizeOptionalString(
  value: unknown,
  maximumLength: number,
  path: string,
  issues: ReportDesignerSchemaIssue[],
) {
  if (value === undefined || value === null) return undefined;
  if (typeof value !== "string") return undefined;
  return truncateString(value, maximumLength, path, issues);
}

function truncateString(value: string, maximumLength: number, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (value.length <= maximumLength) return value;
  issues.push({ severity: "warning", path, message: `文本长度超过 ${maximumLength} 个字符，已截断。` });
  return value.slice(0, maximumLength);
}

function normalizeFontFamily(value: unknown, path: string, issues: ReportDesignerSchemaIssue[]) {
  if (typeof value !== "string") {
    if (value !== undefined) issues.push({ severity: "warning", path, message: "字体名称无效，已使用安全默认字体。" });
    return "Arial, 'Microsoft YaHei', sans-serif";
  }
  const normalized = value.trim();
  if (normalized.length > REPORT_DESIGNER_V3_MAX_FONT_FAMILY_LENGTH || !fontFamilyPattern.test(normalized)) {
    issues.push({ severity: "warning", path, message: "字体名称包含不受支持的字符或过长，已使用安全默认字体。" });
    return "Arial, 'Microsoft YaHei', sans-serif";
  }
  return normalized || "Arial, 'Microsoft YaHei', sans-serif";
}

function normalizeId(value: unknown, fallback: string, ids: Set<string>, path: string, issues: ReportDesignerSchemaIssue[]) {
  const requested = typeof value === "string" && identifierPattern.test(value.trim()) ? value.trim() : fallback;
  let id = requested;
  let suffix = 2;
  while (ids.has(id)) id = `${requested}-${suffix++}`;
  if (id !== value) issues.push({ severity: "warning", path, message: "ID 缺失或重复，已自动修正。" });
  ids.add(id);
  return id;
}

function readInteger(value: unknown, fallback: number, min: number, max: number, path: string, issues: ReportDesignerSchemaIssue[]) {
  const parsed = typeof value === "number" ? value : typeof value === "string" ? Number(value) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    issues.push({ severity: "warning", path, message: "数字无效，已使用默认值。" });
    return Math.round(fallback);
  }
  const normalized = Math.round(Math.min(max, Math.max(min, parsed)));
  if (normalized !== parsed) issues.push({ severity: "warning", path, message: `数字已限制在 ${min}-${max}。` });
  return normalized;
}

function readNumber(value: unknown, fallback: number, min: number, max: number, path: string, issues: ReportDesignerSchemaIssue[]) {
  const parsed = typeof value === "number" ? value : typeof value === "string" ? Number(value) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    issues.push({ severity: "warning", path, message: "数字无效，已使用默认值。" });
    return fallback;
  }
  const normalized = Math.min(max, Math.max(min, parsed));
  if (normalized !== parsed) issues.push({ severity: "warning", path, message: `数字已限制在 ${min}-${max}。` });
  return normalized;
}

function readEnum<T extends string>(value: unknown, allowed: readonly T[], fallback: T, path: string, issues: ReportDesignerSchemaIssue[]): T {
  if (typeof value === "string" && allowed.includes(value as T)) return value as T;
  issues.push({ severity: "warning", path, message: "枚举值无效，已使用默认值。" });
  return fallback;
}
