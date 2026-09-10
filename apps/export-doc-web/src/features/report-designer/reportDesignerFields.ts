import type { ApiReportTemplateFieldCatalogResponse } from "../../api/index.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { normalizeDesignerFieldPath } from "./reportDesignerMutationUtils.ts";

export type ReportDesignerFieldGroup = {
  category: string;
  fields: Array<{ label: string; value: string; originalValue?: string }>;
};
export type ReportDesignerField = ReportDesignerFieldGroup["fields"][number] & { category: string };

const exportTemplateSystemFields: ReportDesignerField[] = [
  { category: "模板系统字段", label: "是否带章 (ShowSeal)", value: "ShowSeal" },
  { category: "模板系统字段", label: "单证章图片 (doc_seal_path)", value: "doc_seal_path" },
  { category: "模板系统字段", label: "报关章图片 (customs_seal_path)", value: "customs_seal_path" },
  { category: "模板系统字段", label: "唛头图片 (shipping_marks_image_data)", value: "shipping_marks_image_data" },
];

export function buildReportDesignerFieldGroups(
  fieldCatalog?: ApiReportTemplateFieldCatalogResponse | null,
  fallbackReportType: ReportDesignerReportType = "ExportDocument",
): ReportDesignerFieldGroup[] {
  const reportType = fieldCatalog?.reportType ?? fallbackReportType;
  const fields: ReportDesignerField[] = (fieldCatalog?.fields ?? []).map((field) => ({
    category: field.category || "其它字段",
    label: field.label,
    value: normalizeDesignerFieldPath(field.value),
    originalValue: field.value,
  }));
  if (reportType === "ExportDocument") fields.push(...exportTemplateSystemFields);

  // The API owns both field definitions and grouping. New business fields need
  // no parallel browser classification table to become selectable.
  const groups = new Map<string, ReportDesignerFieldGroup["fields"]>();
  const seen = new Set<string>();
  for (const field of fields) {
    const key = field.value.toLowerCase();
    if (!key || seen.has(key)) continue;
    seen.add(key);
    const group = groups.get(field.category) ?? [];
    group.push({ label: field.label, value: field.value, originalValue: field.originalValue });
    groups.set(field.category, group);
  }
  const categories = [...new Set([...(fieldCatalog?.categoryOrder ?? []), ...groups.keys()])];
  return categories.flatMap((category) => {
    const grouped = groups.get(category);
    return grouped?.length ? [{ category, fields: grouped }] : [];
  });
}
