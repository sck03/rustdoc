import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDesignerV3Layer } from "./reportDesignerV3Schema.ts";

export const REPORT_DESIGNER_V3_ZOOM_PRESETS = [50, 75, 100, 125, 150] as const;
export const REPORT_DESIGNER_V3_MIN_ZOOM = 0.45;
export const REPORT_DESIGNER_V3_MAX_ZOOM = 1.5;

export function clampReportDesignerV3Zoom(value: number) {
  const safe = Number.isFinite(value) ? value : 0.72;
  return Math.min(REPORT_DESIGNER_V3_MAX_ZOOM, Math.max(REPORT_DESIGNER_V3_MIN_ZOOM, Math.round(safe * 100) / 100));
}

export function fitReportDesignerV3Zoom(viewportWidth: number, viewportHeight: number, pageWidth: number, pageHeight: number, padding = 48) {
  if (![viewportWidth, viewportHeight, pageWidth, pageHeight].every((value) => Number.isFinite(value) && value > 0)) return 0.72;
  return clampReportDesignerV3Zoom(Math.min((viewportWidth - padding) / pageWidth, (viewportHeight - padding) / pageHeight));
}

export function flattenFields(groups: ReportDesignerFieldGroup[]) {
  return groups.flatMap((group) => group.fields)
    .filter((field, index, fields) => fields.findIndex((candidate) => candidate.value === field.value) === index);
}

export function filterFieldGroups(groups: ReportDesignerFieldGroup[], query: string) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return groups;
  return groups
    .map((group) => ({ ...group, fields: group.fields.filter((field) => `${field.label} ${field.value}`.toLowerCase().includes(normalized)) }))
    .filter((group) => group.fields.length > 0);
}

export function countElements(schema: { layers: ReportDesignerV3Layer[] }) {
  return schema.layers.reduce((count, layer) => count + layer.elements.length, 0);
}

export function formatNumber(value: number) {
  return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}

export function isEditableTarget(target: EventTarget | null) {
  return target instanceof HTMLElement && (["input", "textarea", "select"].includes(target.tagName.toLowerCase()) || target.isContentEditable);
}

export function migrationNoticeTitle(sourceVersion: 3 | null, hasBlockingIssues: boolean) {
  return sourceVersion === 3
    ? (hasBlockingIssues ? "V3 结构需要修复确认" : "V3 结构已规范化，请确认")
    : "高级 HTML 模板可选择转换为 V3";
}

export function migrationNoticeDescription(sourceVersion: 3 | null) {
  return sourceVersion === 3
    ? "当前 V3 结构包含无法直接使用的字段或组件；确认后只会生成经过校验的 V3 草稿。"
    : "当前内容使用高级 HTML 运行时，原版式会保持不变。确认后只在内存中创建新的 V3 A4 草稿，保存前可继续返回高级 HTML。";
}
