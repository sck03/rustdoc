import { portableReportSansFontFamily } from "../../app/typographyPolicy.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { normalizeReportDesignerV3Schema } from "./reportDesignerV3Validation.ts";
import {
  REPORT_DESIGNER_V3_AST_KIND,
  REPORT_DESIGNER_V3_CONTRACT_VERSION,
  REPORT_DESIGNER_V3_COORDINATE_UNIT,
  type ReportDesignerV3Schema,
} from "./reportDesignerV3Schema.ts";

export type ReportDesignerV3ParseResult = {
  schema: ReportDesignerV3Schema;
  migrated: boolean;
  hadSchema: boolean;
  sourceVersion: 3 | null;
  issues: Array<{ severity: "warning" | "error"; path: string; message: string }>;
};

export function hasReportDesignerV3Schema(content: string) {
  return parseReportDesignerV3Json(content) !== null;
}

export function hasValidReportDesignerV3Schema(content: string) {
  const source = parseReportDesignerV3Json(content);
  if (!source) return false;
  try {
    const parsed = JSON.parse(source) as unknown;
    if (!isRecordWithVersion(parsed, 3)) return false;
    const normalized = normalizeReportDesignerV3Schema(parsed);
    return normalized.schema !== null &&
      !normalized.issues.some((issue) => issue.severity === "error");
  } catch {
    return false;
  }
}

/**
 * `.dtpl` exposes the V3 JSON document directly. There is no HTML or V2
 * compatibility parser in the visual designer.
 */
export function parseReportDesignerV3Source(
  content: string,
  reportType: ReportDesignerReportType,
): ReportDesignerV3ParseResult {
  if (!content.trim()) {
    return { schema: createEmptyReportDesignerV3Schema(reportType), migrated: false, hadSchema: false, sourceVersion: 3, issues: [] };
  }
  const source = parseReportDesignerV3Json(content);
  if (!source) {
    return createReplacementDraft(reportType, false, [
      { severity: "error", path: "$", message: "模板必须是统一 .dtpl V3 JSON；旧 HTML、V2 或损坏内容不受支持。" },
    ]);
  }

  try {
    const parsed = JSON.parse(source) as unknown;
    if (isRecordWithVersion(parsed, 3)) {
      const normalized = normalizeReportDesignerV3Schema(parsed, reportType);
      if (normalized.schema) {
        return {
          schema: normalized.schema,
          migrated: normalized.issues.length > 0,
          hadSchema: true,
          sourceVersion: 3,
          issues: normalized.issues,
        };
      }

      return createReplacementDraft(reportType, true, [
        ...normalized.issues,
        { severity: "error", path: "$", message: "V3 设计结构无法读取，已生成隔离的安全草稿，原模板不会被静默覆盖。" },
      ]);
    }

    return createReplacementDraft(reportType, true, [
      { severity: "error", path: "$.version", message: "模板必须使用 version: 3 的 .dtpl V3 结构；V2 和 HTML 不受支持。" },
    ]);
  } catch {
    const replacement = createReplacementDraft(reportType, true, [
      { severity: "error", path: "$", message: "设计结构 JSON 损坏，已生成隔离的 V3 草稿，原模板不会被静默覆盖。" },
    ]);
    replacement.sourceVersion = 3;
    return replacement;
  }
}

function parseReportDesignerV3Json(content: string) {
  const trimmed = content.trim();
  if (!trimmed.startsWith("{")) return null;
  try {
    const parsed = JSON.parse(trimmed) as unknown;
    return isRecordWithVersion(parsed, 3) ? trimmed : null;
  } catch {
    return null;
  }
}

function createReplacementDraft(
  reportType: ReportDesignerReportType,
  hadSchema: boolean,
  issues: ReportDesignerV3ParseResult["issues"],
): ReportDesignerV3ParseResult {
  return {
    schema: createEmptyReportDesignerV3Schema(reportType),
    migrated: false,
    hadSchema,
    sourceVersion: null,
    issues,
  };
}

function createEmptyReportDesignerV3Schema(reportType: ReportDesignerReportType): ReportDesignerV3Schema {
  return {
    version: 3,
    astKind: REPORT_DESIGNER_V3_AST_KIND,
    coordinateUnit: REPORT_DESIGNER_V3_COORDINATE_UNIT,
    contractVersion: REPORT_DESIGNER_V3_CONTRACT_VERSION,
    reportType,
    page: {
      size: "A4",
      orientation: "Portrait",
      widthHundredthMm: 21000,
      heightHundredthMm: 29700,
      marginTopHundredthMm: 800,
      marginRightHundredthMm: 1000,
      marginBottomHundredthMm: 800,
      marginLeftHundredthMm: 1000,
      fontFamily: portableReportSansFontFamily,
      fontSizePt: 9,
    },
    grid: { enabled: true, sizeHundredthMm: 500, snap: true },
    layers: [
      { id: "header", name: "页眉", role: "Header", print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: false, minHeightHundredthMm: 1800 }, visible: true, locked: false, elements: [] },
      { id: "body", name: "主体", role: "Body", print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false, minHeightHundredthMm: 0 }, visible: true, locked: false, elements: [] },
      { id: "footer", name: "页脚", role: "Footer", print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: true, minHeightHundredthMm: 800 }, visible: true, locked: false, elements: [] },
      { id: "overlay", name: "覆盖层", role: "Overlay", print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false, minHeightHundredthMm: 0 }, visible: true, locked: false, elements: [] },
    ],
  };
}

function isRecordWithVersion(value: unknown, version: number): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value) && (value as Record<string, unknown>).version === version;
}
