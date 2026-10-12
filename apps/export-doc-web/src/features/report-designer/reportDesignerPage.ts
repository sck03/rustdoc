import { ApiReportTemplateV3PageContractDefaults as contract } from "../../api/generated/exportDocManagerApi.ts";
import type { ReportDesignerV3Page } from "./reportDesignerV3Schema.ts";

export const REPORT_PAPER_PRESETS = contract.presets;
export const REPORT_PAPER_MIN_DIMENSION = contract.minDimensionHundredthMm;
export const REPORT_PAPER_MAX_DIMENSION = contract.maxDimensionHundredthMm;
export type ReportPaperSize = typeof REPORT_PAPER_PRESETS[number]["size"] | "Custom";
export const reportPaperOptions = [
  ...REPORT_PAPER_PRESETS.map(preset => ({ value: preset.size, label: preset.size })),
  { value: "Custom", label: "自定义" },
];

export function isReportPaperSize(value: unknown): value is ReportPaperSize {
  return value === "Custom" || REPORT_PAPER_PRESETS.some(preset => preset.size === value);
}

export function reportDesignerV3PageDimensions(orientation: ReportDesignerV3Page["orientation"], size: Exclude<ReportPaperSize, "Custom"> = "A4") {
  const preset = REPORT_PAPER_PRESETS.find(preset => preset.size === size)!;
  return orientation === "Landscape"
    ? { width: preset.heightHundredthMm, height: preset.widthHundredthMm }
    : { width: preset.widthHundredthMm, height: preset.heightHundredthMm };
}

export function reportPageGeometryIssue(page: Record<string, unknown>): string | null {
  if (!isReportPaperSize(page.size)) return "纸张类型无效。";
  if (page.orientation !== "Portrait" && page.orientation !== "Landscape") return "纸张方向无效。";
  const width = page.widthHundredthMm, height = page.heightHundredthMm;
  if (typeof width !== "number" || typeof height !== "number" || !Number.isInteger(width) || !Number.isInteger(height) ||
      width < REPORT_PAPER_MIN_DIMENSION || height < REPORT_PAPER_MIN_DIMENSION || width > REPORT_PAPER_MAX_DIMENSION || height > REPORT_PAPER_MAX_DIMENSION) {
    return "纸张宽高须在 40–600 mm 之间，精确到 0.01 mm。";
  }
  if (page.size !== "Custom") {
    const expected = reportDesignerV3PageDimensions(page.orientation, page.size);
    if (width !== expected.width || height !== expected.height) return "纸张名称、方向与标准尺寸不一致。";
  } else if (page.orientation === "Landscape" ? width < height : width > height) return "纸张方向与宽高不一致。";
  return null;
}

/** Editing dimensions sets the custom orientation; changing orientation swaps them. */
export function updateReportPageGeometry(page: ReportDesignerV3Page, update: Partial<ReportDesignerV3Page>) {
  const size = isReportPaperSize(update.size) ? update.size : page.size;
  let orientation = update.orientation === "Portrait" || update.orientation === "Landscape" ? update.orientation : page.orientation;
  let width = page.widthHundredthMm, height = page.heightHundredthMm;
  if (size === "Custom") {
    const dimension = (value: number | undefined, current: number) => Number.isFinite(value)
      ? Math.min(REPORT_PAPER_MAX_DIMENSION, Math.max(REPORT_PAPER_MIN_DIMENSION, Math.round(value!))) : current;
    width = dimension(update.widthHundredthMm, width);
    height = dimension(update.heightHundredthMm, height);
    if (update.widthHundredthMm !== undefined || update.heightHundredthMm !== undefined) {
      if (width !== height) orientation = width > height ? "Landscape" : "Portrait";
    } else if (orientation !== page.orientation) [width, height] = [height, width];
  } else ({ width, height } = reportDesignerV3PageDimensions(orientation, size));
  return { size, orientation, widthHundredthMm: width, heightHundredthMm: height };
}
