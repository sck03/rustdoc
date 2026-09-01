import type { ReportDesignerFieldGroup } from "./reportDesignerFields.ts";
import type { ReportDesignerV3Layer } from "./reportDesignerV3Schema.ts";

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
