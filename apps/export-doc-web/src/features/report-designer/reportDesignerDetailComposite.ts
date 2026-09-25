import type { ReportDetailTableCellContent } from "./reportDesignerSchema.ts";
import { createIssue, isRecord, normalizeId, readEnum, readString, readOptionalString, readRequiredFieldPath, readOptionalFieldPath, type ReportDesignerSchemaIssue } from "./reportDesignerSchemaValues.ts";

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
  const parts = value
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

  const kind = readEnum(value.kind, ["Text", "Field", "LineBreak", "ColumnBreak"] as const, "Text", `${path}.kind`, issues);
  return {
    id: normalizeId(value.id, "detail-cell-part", partIds, `${path}.id`, issues),
    kind,
    visible: value.visible !== false,
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
    if (part.kind === "LineBreak") position = 0;
    if (part.kind === "ColumnBreak") {
      const next = part.positionPercent;
      if (next === undefined || !Number.isFinite(next) || next <= position || next >= 100) issues.push(createIssue("error", `${path}[${index}].positionPercent`, "分栏位置必须在 0—100% 之间，且同一行从左向右递增。"));
      position = next ?? position;
    }
  });
}

/** Each logical line owns independent, proportional alignment slots. */
export function renderDetailComposite(parts: ReportDetailTableCellContent[], render: (part: ReportDetailTableCellContent) => string, verticalAlign: "Top" | "Middle" | "Bottom" = "Top") {
  const rows: { position: number; html: string }[][] = [[{position:0,html:""}]];
  for (const part of parts) {
    if (part.visible === false) continue;
    if (part.kind === "LineBreak") rows.push([{position:0,html:""}]);
    else if (part.kind === "ColumnBreak") rows.at(-1)!.push({position:part.positionPercent ?? 50,html:""});
    else rows.at(-1)!.at(-1)!.html += render(part);
  }
  return rows.map(row => {
    const widths = row.map((slot,index)=>(row[index+1]?.position ?? 100)-slot.position);
    const align = verticalAlign === "Bottom" ? "end" : verticalAlign === "Middle" ? "center" : "start";
    return `<div class="edm-detail-composite-line" style="align-items:${align};grid-template-columns:${widths.map(w=>`${w}%`).join(" ")};min-height:1.3em">${row.map(slot=>`<span style="min-width:0;padding-right:1mm;overflow-wrap:anywhere">${slot.html}</span>`).join("")}</div>`;
  }).join("");
}
