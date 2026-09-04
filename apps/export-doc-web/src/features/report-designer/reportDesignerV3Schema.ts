import type {
  ReportBlock,
  ReportDesignerReportType,
  ReportTextStyle,
} from "./reportDesignerSchema.ts";
import { reportDesignerV3ElementBounds } from "./reportDesignerGeometry.ts";

export { reportDesignerV3ElementBounds } from "./reportDesignerGeometry.ts";
export type { ReportDesignerV3ElementBounds } from "./reportDesignerGeometry.ts";
export const REPORT_DESIGNER_V3_VERSION = 3 as const;
/** Public, descriptive contract version; the schema version remains numeric for compact persisted HTML. */
export const REPORT_DESIGNER_V3_CONTRACT_VERSION = "3.0" as const;
export const REPORT_DESIGNER_V3_AST_KIND = "ReportDocument" as const;
export const REPORT_DESIGNER_V3_COORDINATE_UNIT = "hundredth-mm" as const;
export const REPORT_DESIGNER_V3_FLOW_TYPES = ["Row", "Grid", "Conditional", "DetailTable", "PageBreak"] as const;
export const REPORT_DESIGNER_V3_RELEASE_STATES = ["Draft", "Published", "Archived"] as const;
export const HUNDREDTH_MM_PER_MM = 100;
export const REPORT_DESIGNER_V3_MAX_LAYER_COUNT = 16;
export const REPORT_DESIGNER_V3_MAX_ELEMENTS_PER_LAYER = 1000;
export const REPORT_DESIGNER_V3_MAX_TOTAL_ELEMENTS = 4000;
export const REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM = 400;
export const REPORT_DESIGNER_V3_MAX_TEXT_LENGTH = 32768;
export const REPORT_DESIGNER_V3_MAX_FALLBACK_LENGTH = 2048;
export const REPORT_DESIGNER_V3_MAX_LABEL_LENGTH = 200;
export const REPORT_DESIGNER_V3_MAX_ALT_TEXT_LENGTH = 200;
export const REPORT_DESIGNER_V3_MAX_FIELD_PATH_LENGTH = 256;
export const REPORT_DESIGNER_V3_MAX_FONT_FAMILY_LENGTH = 256;
export const A4_PORTRAIT_SIZE_HUNDREDTH_MM = Object.freeze({ width: 21000, height: 29700 });
export const A4_LANDSCAPE_SIZE_HUNDREDTH_MM = Object.freeze({ width: 29700, height: 21000 });
export type ReportDesignerV3Schema = {
  version: typeof REPORT_DESIGNER_V3_VERSION;
  /** Explicit AST discriminator and coordinate unit keep persisted V3 self-describing. */
  astKind?: typeof REPORT_DESIGNER_V3_AST_KIND;
  coordinateUnit?: typeof REPORT_DESIGNER_V3_COORDINATE_UNIT;
  reportType: ReportDesignerReportType;
  page: ReportDesignerV3Page;
  layers: ReportDesignerV3Layer[];
  grid: ReportDesignerV3GridSettings;
  /** Optional on older V3 documents; normalized documents always emit it. */
  contractVersion?: typeof REPORT_DESIGNER_V3_CONTRACT_VERSION;
  resources?: ReportDesignerV3ImageResource[];
  release?: ReportDesignerV3Release;
  metadata?: {
    migratedFromVersion?: number;
    migratedAt?: string;
  };
};
export type ReportDesignerV3Page = {
  /** V3 deliberately has one physical page contract: A4. */
  size: "A4";
  orientation: "Portrait" | "Landscape";
  widthHundredthMm: number;
  heightHundredthMm: number;
  marginTopHundredthMm: number;
  marginRightHundredthMm: number;
  marginBottomHundredthMm: number;
  marginLeftHundredthMm: number;
  fontFamily: string;
  fontSizePt: number;
};
export type ReportDesignerV3LayerRole = "Header" | "Body" | "Footer" | "Overlay";
export type ReportDesignerV3Layer = {
  id: string;
  name: string;
  role: ReportDesignerV3LayerRole;
  designHeightHundredthMm?: number;
  print: ReportDesignerV3LayerPrintSettings;
  visible: boolean;
  locked: boolean;
  elements: ReportDesignerV3Element[];
};
export type ReportDesignerV3LayerPrintSettings = {
  /** Repeat the band in the browser print header/footer area on every page. */
  repeatOnEveryPage: boolean;
  /** Keep the band together when the browser lays out a page break. */
  keepTogether: boolean;
  /** Pin a footer band to the physical page bottom when it is not repeated. */
  pinToPageBottom: boolean;
  /** Reserved/visible band height in 1/100 mm. Zero means content-sized. */
  minHeightHundredthMm: number;
};
export type ReportDesignerV3GridSettings = {
  enabled: boolean;
  sizeHundredthMm: number;
  snap: boolean;
};
export type ReportDesignerV3ElementStyle = {
  fontFamily?: string;
  fontSizePt?: number;
  bold?: boolean;
  color?: string;
  backgroundColor?: string;
  align?: "Left" | "Center" | "Right";
  borderColor?: string;
  borderWidthPx?: number;
  borderStyle?: "Solid" | "Dashed" | "None";
  paddingHundredthMm?: number;
};
export type ReportDesignerV3ElementBase = {
  id: string;
  xHundredthMm: number;
  yHundredthMm: number;
  widthHundredthMm: number;
  heightHundredthMm: number;
  rotationDeg: number;
  zIndex: number;
  visible: boolean;
  locked: boolean;
  style: ReportDesignerV3ElementStyle;
  outputEnabled: boolean;
  label?: string;
};
export type ReportDesignerV3TextElement = ReportDesignerV3ElementBase & {
  type: "Text";
  text: string;
};
export type ReportDesignerV3FieldElement = ReportDesignerV3ElementBase & {
  type: "Field";
  fieldPath: string;
  fallbackText?: string;
};
export type ReportDesignerV3ImageElement = ReportDesignerV3ElementBase & {
  type: "Image";
  sourceKind: "Field" | "Resource";
  /** Stamp is still an image binding, not a second resource/runtime. */
  purpose?: "Image" | "Stamp";
  fieldPath?: string;
  resourceId?: string;
  altText?: string;
  hideWhenSourceEmpty: boolean;
};
export type ReportDesignerV3PageNumberElement = ReportDesignerV3ElementBase & {
  type: "PageNumber";
  format: "Current" | "CurrentOfTotal";
  prefix?: string;
  suffix?: string;
};
export type ReportDesignerV3RectangleElement = ReportDesignerV3ElementBase & {
  type: "Rectangle";
};
export type ReportDesignerV3LineElement = ReportDesignerV3ElementBase & {
  type: "Line";
  direction: "Horizontal" | "Vertical";
};

/**
 * Flow elements deliberately hold the existing structured block AST.  This keeps
 * detail tables and fixed ticket grids in their tested renderer while the page
 * itself moves to a single v3 coordinate model.
 */
export type ReportDesignerV3FlowElement = ReportDesignerV3ElementBase & {
  type: "Flow";
  flowKind: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak";
  block: Extract<ReportBlock, {
    type: "Row" | "Grid" | "Conditional" | "DetailTable" | "PageBreak";
  }>;
};
export type ReportDesignerV3Element =
  | ReportDesignerV3TextElement
  | ReportDesignerV3FieldElement
  | ReportDesignerV3ImageElement
  | ReportDesignerV3PageNumberElement
  | ReportDesignerV3RectangleElement
  | ReportDesignerV3LineElement
  | ReportDesignerV3FlowElement;

export type ReportDesignerV3ElementType = ReportDesignerV3Element["type"];
export type ReportDesignerV3ImageResource = {
  id: string;
  mediaType: "image/png" | "image/jpeg" | "image/gif" | "image/webp";
  byteLength?: number;
  sha256?: string;
  altText?: string;
};
export type ReportDesignerV3Release = {
  state: (typeof REPORT_DESIGNER_V3_RELEASE_STATES)[number];
  revision: number;
  publishedAt?: string;
};
export function hundredthMmToMm(value: number) {
  return value / HUNDREDTH_MM_PER_MM;
}
export function mmToHundredthMm(value: number) {
  return Math.round(value * HUNDREDTH_MM_PER_MM);
}
export function reportDesignerV3ElementText(element: ReportDesignerV3Element) {
  switch (element.type) {
    case "Text":
      return element.text;
    case "Field":
      return element.label ? `${element.label}: {{ ${element.fieldPath} }}` : `{{ ${element.fieldPath} }}`;
    case "Image":
      return element.sourceKind === "Field" ? `图片字段 ${element.fieldPath ?? ""}` : `资源 ${element.resourceId ?? "未上传"}`;
    case "PageNumber":
      return element.format === "CurrentOfTotal" ? "页码/总页数" : "当前页码";
    case "Rectangle":
      return "矩形";
    case "Line":
      return element.direction === "Horizontal" ? "水平线" : "垂直线";
    case "Flow":
      return element.flowKind === "DetailTable" ? "明细表（自动重复）" : element.flowKind === "Grid" ? "普通表格" : `${element.flowKind} 流式组件`;
  }
}
export function reportDesignerV3PageSize(page: ReportDesignerV3Page) {
  const size = page.orientation === "Landscape"
    ? A4_LANDSCAPE_SIZE_HUNDREDTH_MM
    : A4_PORTRAIT_SIZE_HUNDREDTH_MM;
  return {
    widthMm: hundredthMmToMm(size.width),
    heightMm: hundredthMmToMm(size.height),
  };
}
export function reportDesignerV3PageDimensions(orientation: ReportDesignerV3Page["orientation"]) {
  return orientation === "Landscape"
    ? A4_LANDSCAPE_SIZE_HUNDREDTH_MM
    : A4_PORTRAIT_SIZE_HUNDREDTH_MM;
}
/**
 * Keep every free-canvas object inside the physical A4 page.  This helper is
 * shared by migration and mutations so a malformed or legacy object cannot
 * escape the page depending on which path produced it.
 */
export function clampReportDesignerV3ElementToPage(
  element: ReportDesignerV3Element,
  page: Pick<ReportDesignerV3Page, "widthHundredthMm" | "heightHundredthMm">,
): ReportDesignerV3Element {
  const pageWidth = Number.isFinite(page.widthHundredthMm) && page.widthHundredthMm > 0
    ? Math.round(page.widthHundredthMm)
    : A4_PORTRAIT_SIZE_HUNDREDTH_MM.width;
  const pageHeight = Number.isFinite(page.heightHundredthMm) && page.heightHundredthMm > 0
    ? Math.round(page.heightHundredthMm)
    : A4_PORTRAIT_SIZE_HUNDREDTH_MM.height;
  const safeWidth = Number.isFinite(element.widthHundredthMm)
    ? Math.round(element.widthHundredthMm)
    : REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM;
  const safeHeight = Number.isFinite(element.heightHundredthMm)
    ? Math.round(element.heightHundredthMm)
    : REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM;
  const minimumSize = Math.min(
    REPORT_DESIGNER_V3_MIN_ELEMENT_SIZE_HUNDREDTH_MM,
    pageWidth,
    pageHeight,
  );
  let width = Math.min(pageWidth, Math.max(minimumSize, safeWidth));
  let height = Math.min(pageHeight, Math.max(minimumSize, safeHeight));
  const safeX = Number.isFinite(element.xHundredthMm) ? Math.round(element.xHundredthMm) : 0;
  const safeY = Number.isFinite(element.yHundredthMm) ? Math.round(element.yHundredthMm) : 0;
  const rotation = Number.isFinite(element.rotationDeg) ? element.rotationDeg : 0;
  const originalCenterX = safeX + width / 2;
  const originalCenterY = safeY + height / 2;
  const initialBounds = reportDesignerV3ElementBounds({
    xHundredthMm: 0,
    yHundredthMm: 0,
    widthHundredthMm: width,
    heightHundredthMm: height,
    rotationDeg: rotation,
  });
  const rotatedWidth = initialBounds.right - initialBounds.left;
  const rotatedHeight = initialBounds.bottom - initialBounds.top;
  // A rotated rectangle can have a visual footprint larger than either of
  // its persisted axes.  Uniformly scale it before clamping the center so a
  // 45° oversized object cannot remain partly outside the physical page.
  const fitScale = Math.min(
    1,
    rotatedWidth > 0 ? pageWidth / rotatedWidth : 1,
    rotatedHeight > 0 ? pageHeight / rotatedHeight : 1,
  );
  if (fitScale < 1) {
    width = Math.max(minimumSize, Math.floor(width * fitScale));
    height = Math.max(minimumSize, Math.floor(height * fitScale));
  }
  const fittedBounds = reportDesignerV3ElementBounds({
    xHundredthMm: 0,
    yHundredthMm: 0,
    widthHundredthMm: width,
    heightHundredthMm: height,
    rotationDeg: rotation,
  });
  const fittedRotatedWidth = fittedBounds.right - fittedBounds.left;
  const fittedRotatedHeight = fittedBounds.bottom - fittedBounds.top;
  const centerX = clampElementCenter(
    originalCenterX,
    pageWidth,
    fittedRotatedWidth / 2,
  );
  const centerY = clampElementCenter(
    originalCenterY,
    pageHeight,
    fittedRotatedHeight / 2,
  );
  return {
    ...element,
    xHundredthMm: Math.round(Math.min(Math.max(0, pageWidth - width), Math.max(0, centerX - width / 2))),
    yHundredthMm: Math.round(Math.min(Math.max(0, pageHeight - height), Math.max(0, centerY - height / 2))),
    widthHundredthMm: width,
    heightHundredthMm: height,
    rotationDeg: rotation,
  };
}

function clampElementCenter(center: number, pageSize: number, halfExtent: number) {
  const minimum = Math.min(halfExtent, pageSize / 2);
  return Math.min(pageSize - minimum, Math.max(minimum, center));
}

export function isReportDesignerV3FlowElement(element: ReportDesignerV3Element): element is ReportDesignerV3FlowElement {
  return element.type === "Flow";
}

export function styleFromLegacyTextStyle(style: ReportTextStyle | undefined): ReportDesignerV3ElementStyle {
  if (!style) {
    return {};
  }

  return {
    fontSizePt: style.fontSizePt,
    bold: style.bold,
    align: style.align,
  };
}
