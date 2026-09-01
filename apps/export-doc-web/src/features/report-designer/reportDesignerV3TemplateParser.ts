import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { normalizeReportDesignerV3Schema } from "./reportDesignerV3Validation.ts";
import type { ReportDesignerV3Schema } from "./reportDesignerV3Schema.ts";

const schemaCommentPattern = /<!--\s*EXPORTDOC_REPORT_DESIGNER_SCHEMA\s*([\s\S]*?)\s*-->/i;

export type ReportDesignerV3ParseResult = {
  schema: ReportDesignerV3Schema;
  migrated: boolean;
  hadSchema: boolean;
  sourceVersion: 3 | null;
  issues: Array<{ severity: "warning" | "error"; path: string; message: string }>;
};

export function hasReportDesignerV3Schema(content: string) {
  return schemaCommentPattern.test(content);
}

export function hasValidReportDesignerV3Schema(content: string) {
  const match = content.match(schemaCommentPattern);
  if (!match) return false;
  try {
    const parsed = JSON.parse(match[1]) as unknown;
    return isRecordWithVersion(parsed, 3);
  } catch {
    return false;
  }
}

/**
 * V3 is the only structured design format. Older templates stay on the
 * advanced HTML runtime until the user explicitly opts into a V3 replacement.
 * V2 comments are never interpreted or migrated automatically.
 */
export function parseReportDesignerV3FromHtml(
  content: string,
  reportType: ReportDesignerReportType,
): ReportDesignerV3ParseResult {
  const match = content.match(schemaCommentPattern);
  if (!match) {
    const inferredOrientation = inferTemplateOrientation(content);
    const replacement = createReplacementDraft(reportType, false, [
      { severity: "warning", path: "$", message: "当前模板使用高级 HTML 运行时，适合复杂表格、合并单元格和精确分页；确认后才会创建新的 V3 A4 编辑草稿。" },
    ]);
    replacement.schema.page.orientation = inferredOrientation ?? "Portrait";
    replacement.schema.page.widthHundredthMm = inferredOrientation === "Landscape" ? 29700 : 21000;
    replacement.schema.page.heightHundredthMm = inferredOrientation === "Landscape" ? 21000 : 29700;
    return replacement;
  }

  try {
    const parsed = JSON.parse(match[1]) as unknown;
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
      { severity: "error", path: "$.version", message: "模板包含已移除的 V2 或未知设计结构；请继续使用高级 HTML，或确认创建新的 V3 A4 模板。" },
    ]);
  } catch {
    const replacement = createReplacementDraft(reportType, true, [
      { severity: "error", path: "$", message: "设计结构 JSON 损坏，已生成隔离的 V3 草稿，原模板不会被静默覆盖。" },
    ]);
    replacement.sourceVersion = 3;
    return replacement;
  }
}

function inferTemplateOrientation(content: string): "Portrait" | "Landscape" | null {
  const pageRule = content.match(/@page\s*\{[^}]*\bsize\s*:\s*A4\s+(portrait|landscape)\b/i);
  return pageRule?.[1]?.toLowerCase() === "landscape" ? "Landscape" : pageRule ? "Portrait" : null;
}

function createReplacementDraft(
  reportType: ReportDesignerReportType,
  hadSchema: boolean,
  issues: ReportDesignerV3ParseResult["issues"],
): ReportDesignerV3ParseResult {
  return {
    schema: createEmptyReportDesignerV3Schema(reportType),
    migrated: true,
    hadSchema,
    sourceVersion: null,
    issues,
  };
}

function createEmptyReportDesignerV3Schema(reportType: ReportDesignerReportType): ReportDesignerV3Schema {
  return {
    version: 3,
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
      fontFamily: "Arial, Noto Sans CJK SC, Microsoft YaHei",
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
