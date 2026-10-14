import type { ReportDetailTableCellContent } from "./reportDesignerSchema.ts";
import { createIssue, isRecord, normalizeId, readEnum, readString, readOptionalString, readRequiredFieldPath, readOptionalFieldPath, type ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";
import { normalizeCondition } from "./reportDesignerConditions.ts";

export const MAX_DETAIL_PARTS = 256;

export function moveDetailCellPart(parts: ReportDetailTableCellContent[], id: string, direction: -1 | 1) {
  const index = parts.findIndex(part => part.id === id), target = index + direction;
  if (index < 0 || target < 0 || target >= parts.length) return parts;
  const next = [...parts];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

export function normalizeDetailTableCellContentList(
  value: unknown,
  path: string,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableCellContent[] {
  if (value === undefined || value === null) {
    return [];
  }

  if (!Array.isArray(value)) {
    issues.push(createIssue("warning", path, "明细单元格组合内容必须是数组，已使用单字段列。"));
    return [];
  }

  const partIds = new Set<string>();
  if (value.length > MAX_DETAIL_PARTS) issues.push(createIssue("error", path, `每列最多 ${MAX_DETAIL_PARTS} 个组合片段。`));
  const parts = value.slice(0, MAX_DETAIL_PARTS)
    .map((part, index) => normalizeDetailTableCellContent(part, `${path}[${index}]`, partIds, issues))
    .filter((part): part is ReportDetailTableCellContent => Boolean(part));
  validateCompositePositions(parts, path, issues);
  return parts;
}

function normalizeDetailTableCellContent(
  value: unknown,
  path: string,
  partIds: Set<string>,
  issues: ReportDesignerSchemaIssue[],
): ReportDetailTableCellContent | null {
  if (!isRecord(value)) {
    issues.push(createIssue("warning", path, "明细单元格组合片段无效，已忽略。"));
    return null;
  }

  const kind = readEnum(value.kind, ["Text", "Field", "LineBreak", "ColumnBreak", "Spacer"] as const, "Text", `${path}.kind`, issues);
  const condition = value.condition === undefined ? undefined : normalizeCondition(value.condition, `${path}.condition`, issues, true);
  if (condition && (kind === "LineBreak" || kind === "ColumnBreak")) issues.push(createIssue("error", path, "换行和分栏不支持显示条件，请在内容片段上设置。"));
  const heightMm = kind === "Spacer" && typeof value.heightMm === "number" ? value.heightMm : undefined;
  if (kind === "Spacer" && (heightMm === undefined || !Number.isFinite(heightMm) || heightMm < 0.5 || heightMm > 40)) issues.push(createIssue("error", `${path}.heightMm`, "明细占位行高度必须在 0.5–40 mm 之间。"));
  if (typeof value.text === "string" && [...value.text].length > 32768) issues.push(createIssue("error", `${path}.text`, "组合文本不能超过 32768 个字符。"));
  return {
    id: normalizeId(value.id, "detail-cell-part", partIds, `${path}.id`, issues),
    kind,
    visible: value.visible !== false,
    condition,
    heightMm,
    positionPercent: kind === "ColumnBreak" && typeof value.positionPercent === "number" ? value.positionPercent : undefined,
    text: kind === "Text" ? readString(value.text, "", `${path}.text`, issues) : readOptionalString(value.text, `${path}.text`, issues) ?? "",
    fieldPath: kind === "Field"
      ? readRequiredFieldPath(value.fieldPath, `${path}.fieldPath`, issues)
      : readOptionalFieldPath(value.fieldPath, `${path}.fieldPath`, issues),
  };
}

export function validateCompositePositions(parts: ReportDetailTableCellContent[], path: string, issues: ReportDesignerSchemaIssue[]) {
  let position = 0;
  parts.forEach((part, index) => {
    if (part.kind === "LineBreak" || part.kind === "Spacer") position = 0;
    if (part.kind === "ColumnBreak") {
      const next = part.positionPercent;
      if (next === undefined || !Number.isFinite(next) || next <= position || next >= 100) issues.push(createIssue("error", `${path}[${index}].positionPercent`, "分栏位置必须在 0—100% 之间，且同一行从左向右递增。"));
      position = next ?? position;
    }
  });
}

/** Each logical line owns independent, proportional alignment slots. */
export function renderDetailComposite(parts: ReportDetailTableCellContent[], render: (part: ReportDetailTableCellContent) => string, verticalAlign: "Top" | "Middle" | "Bottom" = "Top") {
  type Slot = { position: number; html: string };
  const rows: (Slot[] | number)[] = [];
  let current: Slot[] = [{ position: 0, html: "" }];
  for (const part of parts) {
    if (part.visible === false) continue;
    if (part.kind === "LineBreak") { rows.push(current); current = [{ position: 0, html: "" }]; }
    else if (part.kind === "Spacer") {
      if (current.some(slot => slot.html)) rows.push(current);
      rows.push(part.heightMm ?? 3);
      current = [{ position: 0, html: "" }];
    } else if (part.kind === "ColumnBreak") current.push({position:part.positionPercent ?? 50,html:""});
    else current.at(-1)!.html += render(part);
  }
  if (typeof rows.at(-1) !== "number" || current.some(slot => slot.html)) rows.push(current);
  return rows.map(row => {
    if (typeof row === "number") return `<div class="edm-detail-spacer" style="height:${row}mm"></div>`;
    const widths = row.map((slot,index)=>(row[index+1]?.position ?? 100)-slot.position);
    const align = verticalAlign === "Bottom" ? "end" : verticalAlign === "Middle" ? "center" : "start";
    return `<div class="edm-detail-composite-line" style="align-items:${align};grid-template-columns:${widths.map(w=>`${w}%`).join(" ")};min-height:1.3em">${row.map(slot=>`<span style="min-width:0;padding-right:1mm;overflow-wrap:anywhere">${slot.html}</span>`).join("")}</div>`;
  }).join("");
}
