import type {
  ReportBlock,
  ReportDesignerReportType,
  ReportDesignerSchema,
  ReportImageBlock,
  ReportBorderStyle,
  ReportSection,
} from "./reportDesignerSchema.ts";
import {
  A4_LANDSCAPE_SIZE_HUNDREDTH_MM,
  A4_PORTRAIT_SIZE_HUNDREDTH_MM,
  HUNDREDTH_MM_PER_MM,
  REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER,
  REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS,
  REPORT_DESIGNER_V3_MAX_LAYER_COUNT,
  clampReportDesignerV3ElementToPage,
  mmToHundredthMm,
  styleFromLegacyTextStyle,
  type ReportDesignerV3Element,
  type ReportDesignerV3ElementBase,
  type ReportDesignerV3ElementStyle,
  type ReportDesignerV3Layer,
  type ReportDesignerV3Page,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";
import { isControlledReportImageFieldPath } from "./reportDesignerSchemaDomains.ts";

export type ReportDesignerV3MigrationIssue = {
  severity: "warning" | "error";
  path: string;
  message: string;
};

export type ReportDesignerV3MigrationResult = {
  schema: ReportDesignerV3Schema;
  issues: ReportDesignerV3MigrationIssue[];
};

export function migrateReportDesignerSchemaV2ToV3(
  source: ReportDesignerSchema,
  targetReportType: ReportDesignerReportType = source.reportType,
): ReportDesignerV3MigrationResult {
  const issues: ReportDesignerV3MigrationIssue[] = [];
  if (source.reportType !== targetReportType) {
    issues.push({
      severity: "error",
      path: "$.reportType",
      message: `旧模板数据域为 ${source.reportType}，当前路由要求 ${targetReportType}。已按当前路由建立安全草稿，但跨域字段不会自动转换，请修正后再保存。`,
    });
  }
  const page = createV3Page(source);
  const sourceDimensions = readLegacyPageDimensions(source);
  const targetDimensions = source.page.orientation === "Landscape"
    ? A4_LANDSCAPE_SIZE_HUNDREDTH_MM
    : A4_PORTRAIT_SIZE_HUNDREDTH_MM;
  const sourceHeight = Math.max(1, sourceDimensions.heightMm * HUNDREDTH_MM_PER_MM);
  const heightScale = targetDimensions.height / sourceHeight;
  if (source.page.size !== "A4") {
    issues.push({
      severity: "warning",
      path: "$.page.size",
      message: "v3 画布固定为 A4，原模板页面已按方向迁移到 A4；请复核边距和分页。",
    });
  }
  const contentWidth = Math.max(
    mmToHundredthMm(20),
    page.widthHundredthMm - page.marginLeftHundredthMm - page.marginRightHundredthMm,
  );
  const pageHeight = page.heightHundredthMm;
  const headerSections = source.sections.filter((section) => section.type === "Header");
  const bodySections = source.sections.filter((section) => section.type === "Body");
  const footerSections = source.sections.filter((section) => section.type === "Footer");
  const headerHeight = headerSections.reduce((sum, section) => sum + estimateSectionHeight(section, heightScale), 0);
  const footerHeight = footerSections.reduce((sum, section) => sum + estimateSectionHeight(section, heightScale), 0);
  const bodyStart = Math.min(
    Math.max(page.marginTopHundredthMm, page.marginTopHundredthMm + headerHeight + mmToHundredthMm(4)),
    Math.max(page.marginTopHundredthMm, pageHeight - page.marginBottomHundredthMm - mmToHundredthMm(20)),
  );
  const footerStart = Math.max(
    bodyStart,
    pageHeight - page.marginBottomHundredthMm - Math.max(footerHeight, mmToHundredthMm(18)),
  );

  const usedLayerIds = new Set<string>();
  const usedElementIds = new Set<string>();
  if (source.sections.length > REPORT_DESIGNER_V3_MAX_LAYER_COUNT) {
    issues.push({
      severity: "warning",
      path: "$.sections",
      message: `原模板包含 ${source.sections.length} 个区段，v3 最多保留 ${REPORT_DESIGNER_V3_MAX_LAYER_COUNT} 个图层；超出部分不会自动迁移，请先合并区段后再确认。`,
    });
  }
  const sourceSections = source.sections.slice(0, REPORT_DESIGNER_V3_MAX_LAYER_COUNT);
  let migratedElementCount = 0;
  const layers = sourceSections.map((section) => {
    const originY = section.type === "Header"
      ? page.marginTopHundredthMm
      : section.type === "Footer"
        ? footerStart
        : bodyStart;
    const remainingTotal = Math.max(0, REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS - migratedElementCount);
    const elementLimit = Math.min(REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER, remainingTotal);
    if (section.blocks.length > elementLimit) {
      issues.push({
        severity: "warning",
        path: `$.sections.${section.id}.blocks`,
        message: elementLimit === 0
          ? `V3 元素总数已达到 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS}，该区段不会自动迁移组件。`
          : section.blocks.length > REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER
            ? `该区段包含 ${section.blocks.length} 个组件，V3 单图层最多迁移 ${REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER} 个；超出部分不会自动迁移。`
            : `V3 元素总数最多 ${REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS} 个，该区段超出总预算的组件不会自动迁移。`,
      });
    }
    const layer = migrateSection(section, originY, contentWidth, page, issues, heightScale, usedLayerIds, usedElementIds, targetReportType, elementLimit);
    migratedElementCount += layer.elements.length;
    return layer;
  });

  // Overlay is a first-class layer even when the legacy schema did not need
  // one.  It gives the V3 editor a stable place for seals, watermarks and
  // user-added free-canvas objects without changing the migrated section order.
  if (layers.length < REPORT_DESIGNER_V3_MAX_LAYER_COUNT) {
    layers.push({
      id: createUniqueId("layer-overlay", usedLayerIds),
      name: "覆盖层",
      role: "Overlay",
      designHeightHundredthMm: 0,
      print: {
        repeatOnEveryPage: false,
        keepTogether: false,
        pinToPageBottom: false,
        minHeightHundredthMm: 0,
      },
      visible: true,
      locked: false,
      elements: [],
    });
  } else {
    issues.push({
      severity: "warning",
      path: "$.sections",
      message: `v3 图层已达到 ${REPORT_DESIGNER_V3_MAX_LAYER_COUNT} 层上限，未追加空覆盖层；请在确认迁移前安排图层合并。`,
    });
  }

  if (!layers.some((layer) => layer.elements.length > 0)) {
    issues.push({
      severity: "warning",
      path: "$.sections",
      message: "原模板没有可迁移组件，已保留空的 v3 画布。",
    });
  }

  return {
    schema: {
      version: 3,
      reportType: targetReportType,
      page,
      layers,
      grid: {
        enabled: true,
        sizeHundredthMm: mmToHundredthMm(5),
        snap: true,
      },
      metadata: {
        migratedFromVersion: source.version,
        migratedAt: new Date().toISOString(),
      },
    },
    issues,
  };
}

function createV3Page(source: ReportDesignerSchema): ReportDesignerV3Page {
  return {
    size: "A4",
    orientation: source.page.orientation,
    widthHundredthMm: source.page.orientation === "Landscape"
      ? A4_LANDSCAPE_SIZE_HUNDREDTH_MM.width
      : A4_PORTRAIT_SIZE_HUNDREDTH_MM.width,
    heightHundredthMm: source.page.orientation === "Landscape"
      ? A4_LANDSCAPE_SIZE_HUNDREDTH_MM.height
      : A4_PORTRAIT_SIZE_HUNDREDTH_MM.height,
    marginTopHundredthMm: mmToHundredthMm(source.page.marginTopMm),
    marginRightHundredthMm: mmToHundredthMm(source.page.marginRightMm),
    marginBottomHundredthMm: mmToHundredthMm(source.page.marginBottomMm),
    marginLeftHundredthMm: mmToHundredthMm(source.page.marginLeftMm),
    fontFamily: source.page.fontFamily,
    fontSizePt: source.page.fontSizePt,
  };
}

function migrateSection(
  section: ReportSection,
  originY: number,
  contentWidth: number,
  page: ReportDesignerV3Page,
  issues: ReportDesignerV3MigrationIssue[],
  heightScale: number,
  usedLayerIds: Set<string>,
  usedElementIds: Set<string>,
  reportType: ReportDesignerSchema["reportType"],
  elementLimit: number,
): ReportDesignerV3Layer {
  let cursorY = originY;
  const elements: ReportDesignerV3Element[] = [];
  for (const [index, block] of section.blocks.slice(0, Math.max(0, elementLimit)).entries()) {
    const height = Math.max(mmToHundredthMm(4), Math.round(estimateBlockHeight(block) * heightScale));
    const element = migrateBlock(block, page.marginLeftHundredthMm, cursorY, contentWidth, height, index, usedElementIds, reportType, issues, `$.sections.${section.id}.blocks.${block.id}`);
    const clampedElement = clampReportDesignerV3ElementToPage(element, page);
    if (
      clampedElement.xHundredthMm !== element.xHundredthMm ||
      clampedElement.yHundredthMm !== element.yHundredthMm ||
      clampedElement.widthHundredthMm !== element.widthHundredthMm ||
      clampedElement.heightHundredthMm !== element.heightHundredthMm
    ) {
      issues.push({
        severity: "warning",
        path: `$.sections.${section.id}.blocks.${block.id}`,
        message: "组件超出 A4 页面边界，已限制在画布内；长明细仍由打印分页处理。",
      });
    }
    elements.push(clampedElement);
    if (block.type === "Image" && block.sourceKind === "StaticUrl" && block.url.trim()) {
      issues.push({
        severity: "warning",
        path: `$.sections.${section.id}.blocks.${block.id}`,
        message: "静态图片不会直接写入 v3；请上传到受控资源库后绑定 resourceId。原 HTML 在确认转换前保持不变。",
      });
    }
    cursorY += height + mmToHundredthMm(2);
  }

  return {
    id: createUniqueId(`layer-${section.id}`, usedLayerIds),
    name: section.type === "Header" ? "页眉" : section.type === "Footer" ? "页脚" : "主体",
    role: section.type,
    designHeightHundredthMm: section.type === "Header" ? Math.max(1800, cursorY - originY) : section.type === "Footer" ? Math.max(1400, cursorY - originY) : 0,
    print: {
      repeatOnEveryPage: section.type === "Body" ? false : section.print.repeatOnEveryPage,
      keepTogether: section.print.keepTogether,
      pinToPageBottom: section.type === "Footer" && section.print.pinToPageBottom === true,
      minHeightHundredthMm: Math.max(0, Math.round((section.print.minHeightMm ?? 0) * HUNDREDTH_MM_PER_MM)),
    },
    visible: true,
    locked: false,
    elements,
  };
}

function migrateBlock(
  block: ReportBlock,
  x: number,
  y: number,
  width: number,
  height: number,
  index: number,
  usedElementIds: Set<string>,
  reportType: ReportDesignerSchema["reportType"],
  issues: ReportDesignerV3MigrationIssue[],
  blockPath: string,
): ReportDesignerV3Element {
  const base = {
    id: createUniqueId(block.id || `v3-element-${index + 1}`, usedElementIds),
    xHundredthMm: x,
    yHundredthMm: y,
    widthHundredthMm: width,
    heightHundredthMm: height,
    rotationDeg: 0,
    zIndex: index,
    visible: block.output?.enabled !== false,
    locked: false,
    style: styleFromLegacyBlock(block),
    outputEnabled: block.output?.enabled !== false,
  };

  switch (block.type) {
    case "Text":
      return { ...base, type: "Text", text: block.text };
    case "Field":
      return {
        ...base,
        type: "Field",
        fieldPath: block.fieldPath,
        label: block.label,
        fallbackText: block.fallbackText,
      };
    case "Image":
      return migrateImageBlock(block, base, reportType, issues, blockPath);
    case "Row":
    case "Grid":
    case "Conditional":
    case "DetailTable":
    case "PageBreak":
      return {
        ...base,
        type: "Flow",
        flowKind: block.type,
        block,
      };
  }
}

function styleFromLegacyBlock(block: ReportBlock): ReportDesignerV3ElementStyle {
  const style = styleFromLegacyTextStyle("style" in block ? block.style : undefined);
  if (!("border" in block) || !block.border) return style;
  return {
    ...style,
    borderColor: block.border.color,
    borderWidthPx: Math.max(0, block.border.widthPx),
    borderStyle: block.border.style === "None" ? "None" : block.border.style === "Dashed" ? "Dashed" : "Solid",
    paddingHundredthMm: hasLegacyBorder(block.border) ? mmToHundredthMm(2) : undefined,
  };
}

function hasLegacyBorder(border: ReportBorderStyle) {
  return border.widthPx > 0 && border.style !== "None" && Boolean(border.top || border.right || border.bottom || border.left);
}

function migrateImageBlock(
  block: ReportImageBlock,
  base: ReportDesignerV3ElementBase,
  reportType: ReportDesignerSchema["reportType"],
  issues: ReportDesignerV3MigrationIssue[],
  blockPath: string,
): ReportDesignerV3Element {
  if (block.sourceKind === "Field" && isControlledReportImageFieldPath(block.fieldPath) && reportType === "ExportDocument") {
    return {
      ...base,
      type: "Image",
      sourceKind: "Field",
      fieldPath: block.fieldPath,
      altText: block.altText,
      hideWhenSourceEmpty: block.hideWhenSourceEmpty,
    };
  }

  if (block.sourceKind === "Field" && block.fieldPath.trim()) {
    issues.push({
      severity: "warning",
      path: blockPath,
      message: "原图片字段不是受控 data URI 字段，已转换为待上传资源占位；请绑定 doc_seal_path、customs_seal_path 或 shipping_marks_image_data。",
    });
  }

  // Static legacy URLs are intentionally not copied into executable V3
  // markup.  Keep a safe, editable placeholder instead of introducing a
  // forbidden cross-domain field (for example ShowSeal in a payment template).
  return {
    ...base,
    type: "Image",
    sourceKind: "Resource",
    resourceId: undefined,
    altText: block.altText || block.title || "请上传受控图片资源",
    hideWhenSourceEmpty: false,
  };
}

function createUniqueId(preferred: string, used: Set<string>) {
  const normalized = preferred.trim() || "v3-item";
  let candidate = normalized;
  let suffix = 2;
  while (used.has(candidate)) candidate = `${normalized}-${suffix++}`;
  used.add(candidate);
  return candidate;
}

function estimateSectionHeight(section: ReportSection, heightScale = 1) {
  return section.blocks.reduce((sum, block) => sum + Math.max(mmToHundredthMm(4), Math.round(estimateBlockHeight(block) * heightScale)) + mmToHundredthMm(2), 0);
}

function estimateBlockHeight(block: ReportBlock) {
  switch (block.type) {
    case "Image":
      return mmToHundredthMm(Math.max(8, block.heightMm ?? 24));
    case "Grid":
      return mmToHundredthMm(Math.max(10, block.rows.reduce((sum, row) => sum + (row.heightMm ?? 9), 0)));
    case "DetailTable":
      return mmToHundredthMm(30);
    case "PageBreak":
      return mmToHundredthMmSafe(4);
    case "Row":
      return mmToHundredthMmSafe(12);
    case "Conditional":
      return mmToHundredthMmSafe(10);
    case "Text":
    case "Field":
      return mmToHundredthMmSafe(9);
  }
}

function mmToHundredthMmSafe(value: number) {
  return Math.max(HUNDREDTH_MM_PER_MM, mmToHundredthMm(value));
}

function readLegacyPageDimensions(source: ReportDesignerSchema) {
  const base = source.page.size === "A5"
    ? { widthMm: 148, heightMm: 210 }
    : source.page.size === "Letter"
      ? { widthMm: 216, heightMm: 279 }
      : source.page.size === "Custom"
        ? { widthMm: source.page.widthMm ?? 210, heightMm: source.page.heightMm ?? 297 }
        : { widthMm: 210, heightMm: 297 };
  return source.page.orientation === "Landscape"
    ? { widthMm: base.heightMm, heightMm: base.widthMm }
    : base;
}
