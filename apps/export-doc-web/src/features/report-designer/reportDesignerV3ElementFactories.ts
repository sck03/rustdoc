import type { ReportBlock } from "./reportDesignerSchema.ts";
import {
  type ReportDesignerV3Element,
  type ReportDesignerV3ElementBase,
} from "./reportDesignerV3Schema.ts";

/**
 * Constructors for the small set of free-canvas primitives.  Keeping these
 * defaults separate from mutation orchestration makes both the palette and
 * tests use one canonical element shape.
 */
export function createV3TextElement(x = 1500, y = 1500): ReportDesignerV3Element {
  return createBase("Text", x, y, 9000, 1200, { fontSizePt: 10, align: "Left" }, { text: "选中后在右侧编辑文本" });
}

export function createV3FieldElement(fieldPath: string, x = 1500, y = 1500): ReportDesignerV3Element {
  return createBase("Field", x, y, 9000, 1200, { fontSizePt: 10, align: "Left" }, { fieldPath, fallbackText: fieldPath });
}

export function createV3RectangleElement(x = 1500, y = 1500): ReportDesignerV3Element {
  return createBase("Rectangle", x, y, 9000, 5000, { borderColor: "#334155", borderWidthPx: 1, borderStyle: "Solid" }, {});
}

export function createV3LineElement(x = 1500, y = 1500): ReportDesignerV3Element {
  return createBase("Line", x, y, 9000, 300, { borderColor: "#334155", borderWidthPx: 1, borderStyle: "Solid" }, { direction: "Horizontal" });
}

export function createV3ImageElement(x = 1500, y = 1500): ReportDesignerV3Element {
  return createBase("Image", x, y, 5000, 3500, { borderColor: "#94a3b8", borderWidthPx: 1, borderStyle: "Dashed" }, {
    sourceKind: "Resource",
    resourceId: undefined,
    altText: "未上传图片",
    hideWhenSourceEmpty: true,
  });
}

export function createV3FlowElement(
  block: Extract<ReportBlock, { type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak" }>,
  x = 1500,
  y = 1500,
): ReportDesignerV3Element {
  const height = block.type === "DetailTable" ? 6000 : block.type === "Grid" ? 4500 : block.type === "PageBreak" ? 500 : 1800;
  return createBase("Flow", x, y, 18000, height, { borderColor: "#94a3b8", borderWidthPx: 1, borderStyle: "Dashed" }, {
    flowKind: block.type,
    block,
  });
}

export function createV3ElementId(prefix: string) {
  const uuid = typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID().replace(/-/g, "")
    : `${Date.now()}${Math.random().toString(16).slice(2)}`;
  return `v3-${prefix}-${uuid.slice(0, 20)}`;
}

function createBase<T extends ReportDesignerV3Element["type"]>(
  type: T,
  x: number,
  y: number,
  width: number,
  height: number,
  style: ReportDesignerV3ElementBase["style"],
  extra: Record<string, unknown>,
) {
  return {
    id: createV3ElementId(type.toLowerCase()),
    type,
    xHundredthMm: x,
    yHundredthMm: y,
    widthHundredthMm: width,
    heightHundredthMm: height,
    rotationDeg: 0,
    zIndex: 0,
    visible: true,
    locked: false,
    style,
    outputEnabled: true,
    ...extra,
  } as ReportDesignerV3Element;
}
