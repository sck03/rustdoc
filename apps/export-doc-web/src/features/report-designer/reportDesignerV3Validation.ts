import {
  isRecord,
  isReportDesignerCssColor,
  isReportDesignerFieldPath,
  isSafeReportDesignerCssFontFamily,
  readEnum,
  readNumber,
  type ReportDesignerSchemaIssue,
} from "./reportDesignerSchemaValues.ts";
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
  REPORT_DESIGNER_V3_MAX_RESOURCES,
  REPORT_DESIGNER_V3_MAX_RESOURCE_BYTES,
  REPORT_DESIGNER_V3_MAX_TEXT_LENGTH,
  REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS,
  REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM,
  REPORT_DESIGNER_V3_CONTRACT_VERSION,
  REPORT_DESIGNER_V3_AST_KIND,
  REPORT_DESIGNER_V3_COORDINATE_UNIT,
  REPORT_DESIGNER_V3_FLOW_TYPES,
  REPORT_DESIGNER_V3_RELEASE_STATES,
  REPORT_DESIGNER_V3_VERSION,
  clampReportDesignerV3ElementToPage,
  reportDesignerV3ElementBounds,
  reportDesignerV3PageDimensions,
  type ReportDesignerV3Element,
  type ReportDesignerV3ElementStyle,
  type ReportDesignerV3ImageResource,
  type ReportDesignerV3Layer,
  type ReportDesignerV3Page,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

const identifierPattern = /^[A-Za-z0-9][A-Za-z0-9_.:-]{0,119}$/;
const resourceIdPattern = /^img-([0-9a-f]{64})\.(png|jpg|gif|webp)$/;
const sha256Pattern = /^[0-9a-fA-F]{64}$/;
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

  const resources = normalizeResources(input.resources, issues);
  if (resources === null) return { schema: null, issues };
  validateResourceReferences(layers, resources ?? [], issues);
  return {
    schema: {
      version: 3,
      astKind: normalizeMarker(input.astKind, REPORT_DESIGNER_V3_AST_KIND, "$.astKind", issues),
      coordinateUnit: normalizeMarker(input.coordinateUnit, REPORT_DESIGNER_V3_COORDINATE_UNIT, "$.coordinateUnit", issues),
      reportType,
      page,
      layers,
      grid,
      contractVersion: normalizeMarker(input.contractVersion, REPORT_DESIGNER_V3_CONTRACT_VERSION, "$.contractVersion", issues),
      resources,
      release: normalizeRelease(input.release, issues),
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
  if (value.length > REPORT_DESIGNER_V3_MAX_LAYER_COUNT) {
    issues.push({ severity: "error", path: "$.layers", message: `图层数量不能超过 ${REPORT_DESIGNER_V3_MAX_LAYER_COUNT}。` });
    return null;
  }
  let totalElements = 0;
  for (const [layerIndex, layer] of value.entries()) {
    const count = isRecord(layer) && Array.isArray(layer.elements) ? layer.elements.length : 0;
    if (count > REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER) {
      issues.push({ severity: "error", path: `$.layers[${layerIndex}].elements`, message: `单个图层元素不能超过 ${REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER}。` });
      return null;
    }
    totalElements += count;
  }
  if (totalElements > REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS) {
    issues.push({ severity: "error", path: "$.layers", message: `元素总数不能超过 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS}。` });
    return null;
  }
  const ids = new Set<string>();
  const blockIds = new Set<string>();
  const layers: ReportDesignerV3Layer[] = [];
  value.forEach((rawLayer, layerIndex) => {
    const path = `$.layers[${layerIndex}]`;
    if (!isRecord(rawLayer)) {
      issues.push({ severity: "error", path, message: "图层必须是对象。" });
      return;
    }
    const id = normalizeId(rawLayer.id, `layer-${layerIndex + 1}`, ids, `${path}.id`, issues);
    const role = readEnum(rawLayer.role, ["Header", "Body", "Footer", "Overlay"] as const, "Body", `${path}.role`, issues);
    const elements = normalizeElements(rawLayer.elements, page, reportType, role, `${path}.elements`, ids, blockIds, issues);
    const print = normalizeLayerPrint(rawLayer.print, role, `${path}.print`, issues);
    const defaultDesignHeight = role === "Header" ? Math.max(1800, print.minHeightHundredthMm) : role === "Footer" ? Math.max(1400, print.minHeightHundredthMm) : 0;
    layers.push({
      id,
      name: typeof rawLayer.name === "string" && rawLayer.name.trim() ? rawLayer.name.trim().slice(0, 120) : role === "Header" ? "页眉" : role === "Footer" ? "页脚" : role === "Body" ? "主体" : "覆盖层",
      role,
      designHeightHundredthMm: rawLayer.designHeightHundredthMm === undefined ? defaultDesignHeight : readInteger(rawLayer.designHeightHundredthMm, defaultDesignHeight, 0, page?.heightHundredthMm ?? 29700, `${path}.designHeightHundredthMm`, issues),
      print,
      visible: typeof rawLayer.visible === "boolean" ? rawLayer.visible : true,
      locked: typeof rawLayer.locked === "boolean" ? rawLayer.locked : false,
      elements,
    });
  });
  if (!layers.some((layer) => layer.role === "Body")) {
    issues.push({ severity: "error", path: "$.layers", message: "v3 至少需要一个主体图层。" });
  }
  if (!layers.some((layer) => layer.role === "Overlay") && layers.length < REPORT_DESIGNER_V3_MAX_LAYER_COUNT) {
    layers.push({
      id: normalizeId("layer-overlay", "layer-overlay", ids, "$.layers.overlay.id", issues),
      name: "覆盖层",
      role: "Overlay",
      designHeightHundredthMm: 0,
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
  issues: ReportDesignerSchemaIssue[],
): ReportDesignerV3Element[] {
  if (!Array.isArray(value)) {
    issues.push({ severity: "error", path, message: "图层元素必须是数组。" });
    return [];
  }
  return value
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
        const purpose = value.purpose === "Image" || value.purpose === "Stamp" ? value.purpose : "Image";
        if (value.purpose !== "Image" && value.purpose !== "Stamp") {
          issues.push({ severity: "error", path: `${path}.purpose`, message: "图片用途必须明确为 Image 或 Stamp。" });
        }
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
        if (purpose === "Stamp" && sourceKind !== "Field") {
          issues.push({ severity: "error", path: `${path}.purpose`, message: "印章必须绑定受控图片字段，不能直接读取资源或外部地址。" });
        }
        if (purpose === "Stamp" && fieldPath && !["doc_seal_path", "customs_seal_path"].includes(fieldPath)) {
          issues.push({ severity: "error", path: `${path}.fieldPath`, message: "印章只能绑定单证章或报关章受控字段。" });
        }
        return {
          ...base,
          type: "Image",
          sourceKind,
          purpose,
          fieldPath,
          resourceId,
          altText: normalizeOptionalString(value.altText, REPORT_DESIGNER_V3_MAX_ALT_TEXT_LENGTH, `${path}.altText`, issues),
          hideWhenSourceEmpty: typeof value.hideWhenSourceEmpty === "boolean" ? value.hideWhenSourceEmpty : true,
        };
      }
      case "PageNumber":
        return {
          ...base,
          type: "PageNumber",
          format: readEnum(value.format, ["Current", "CurrentOfTotal"] as const, "CurrentOfTotal", `${path}.format`, issues),
          prefix: normalizeOptionalString(value.prefix, 80, `${path}.prefix`, issues),
          suffix: normalizeOptionalString(value.suffix, 80, `${path}.suffix`, issues),
        };
      case "Rectangle":
        return { ...base, type: "Rectangle" };
      case "Line":
        return { ...base, type: "Line", direction: readEnum(value.direction, ["Horizontal", "Vertical"] as const, "Horizontal", `${path}.direction`, issues) };
      case "Flow": {
        const flowKind = readEnum(value.flowKind, REPORT_DESIGNER_V3_FLOW_TYPES, "Row", `${path}.flowKind`, issues);
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
  if (typeof value.color === "string" && isReportDesignerCssColor(value.color)) style.color = value.color.trim();
  if (typeof value.backgroundColor === "string" && isReportDesignerCssColor(value.backgroundColor)) style.backgroundColor = value.backgroundColor.trim();
  if (value.align === "Left" || value.align === "Center" || value.align === "Right") style.align = value.align;
  if (typeof value.borderColor === "string" && isReportDesignerCssColor(value.borderColor)) style.borderColor = value.borderColor.trim();
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

function normalizeMarker<T extends string>(value: unknown, expected: T, path: string, issues: ReportDesignerSchemaIssue[]): T {
  if (value === expected) return expected;
  issues.push({ severity: "error", path, message: `V3 契约标记无效，必须为 ${expected}。` });
  return expected;
}

function validateResourceReferences(
  layers: ReportDesignerV3Layer[],
  resources: ReportDesignerV3ImageResource[],
  issues: ReportDesignerSchemaIssue[],
) {
  const ids = new Set(resources.map((resource) => resource.id));
  layers.forEach((layer, layerIndex) => layer.elements.forEach((element, elementIndex) => {
    if (element.type !== "Image" || element.sourceKind !== "Resource" || !element.resourceId || ids.has(element.resourceId)) return;
    issues.push({ severity: "error", path: `$.layers[${layerIndex}].elements[${elementIndex}].resourceId`, message: "图片必须引用 resources 清单中的受控资源 ID。" });
  }));
}

function normalizeResources(value: unknown, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined) return undefined;
  if (!Array.isArray(value)) {
    issues.push({ severity: "error", path: "$.resources", message: "图片资源清单必须是数组。" });
    return undefined;
  }
  if (value.length > REPORT_DESIGNER_V3_MAX_RESOURCES) {
    issues.push({ severity: "error", path: "$.resources", message: `图片资源数量不能超过 ${REPORT_DESIGNER_V3_MAX_RESOURCES} 个。` });
    return null;
  }
  const ids = new Set<string>();
  const invalid = (path: string, message: string) => {
    issues.push({ severity: "error", path, message });
    return [];
  };
  return value.flatMap((item, index) => {
    const path = `$.resources[${index}]`;
    if (!isRecord(item)) return invalid(path, "图片资源必须是对象。");
    const id = typeof item.id === "string" && resourceIdPattern.test(item.id.trim()) ? item.id.trim() : "";
    if (!id || ids.has(id)) return invalid(`${path}.id`, "图片资源 ID 缺失或重复。");
    ids.add(id);
    const mediaType = ["image/png", "image/jpeg", "image/gif", "image/webp"].includes(String(item.mediaType))
      ? item.mediaType as ReportDesignerV3ImageResource["mediaType"] : undefined;
    if (!mediaType) return invalid(`${path}.mediaType`, "图片资源只支持 PNG、JPEG、GIF 或 WebP。");
    const byteLength = typeof item.byteLength === "number" && Number.isInteger(item.byteLength) &&
      item.byteLength > 0 && item.byteLength <= REPORT_DESIGNER_V3_MAX_RESOURCE_BYTES
      ? item.byteLength
      : null;
    if (byteLength === null) return invalid(`${path}.byteLength`, "图片资源大小必须是有效的正整数。");
    const sha256 = typeof item.sha256 === "string" && sha256Pattern.test(item.sha256.trim())
      ? item.sha256.trim().toLowerCase()
      : "";
    if (!sha256) return invalid(`${path}.sha256`, "SHA-256 必须是 64 位十六进制字符串。");
    const idMatch = resourceIdPattern.exec(id);
    const expectedExtension = mediaType === "image/jpeg" ? "jpg" : mediaType.slice("image/".length);
    if (!idMatch || idMatch[1] !== sha256 || idMatch[2] !== expectedExtension) {
      return invalid(`${path}.id`, "图片资源 ID 必须与媒体类型和 SHA-256 内容摘要一致。");
    }
    return [{ id, mediaType, byteLength, sha256, altText: normalizeOptionalString(item.altText, 200, `${path}.altText`, issues) }];
  });
}

function normalizeRelease(value: unknown, issues: ReportDesignerSchemaIssue[]) {
  if (value === undefined) return { state: "Draft" as const, revision: 0 };
  if (!isRecord(value)) {
    issues.push({ severity: "error", path: "$.release", message: "发布信息必须是对象。" });
    return { state: "Draft" as const, revision: 0 };
  }
  const state = readEnum(value.state, REPORT_DESIGNER_V3_RELEASE_STATES, "Draft", "$.release.state", issues);
  const revision = readInteger(value.revision, 0, 0, 2_000_000_000, "$.release.revision", issues);
  const publishedAt = typeof value.publishedAt === "string" && value.publishedAt.trim() ? value.publishedAt.trim().slice(0, 64) : undefined;
  if (state === "Published" && !publishedAt) issues.push({ severity: "error", path: "$.release.publishedAt", message: "已发布模板必须记录发布时间。" });
  return { state, revision, publishedAt };
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
  if (isReportDesignerFieldPath(fieldPath)) return fieldPath;
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
  if (normalized.length > REPORT_DESIGNER_V3_MAX_FONT_FAMILY_LENGTH || !isSafeReportDesignerCssFontFamily(normalized)) {
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
  return Math.round(readNumber(value, fallback, min, max, path, issues));
}
