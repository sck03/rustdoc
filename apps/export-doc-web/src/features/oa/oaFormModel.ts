import type { OaFormDefinition, OaFormField, OaFormValues } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";

export const newFormField = (): OaFormField => ({
  key: `f_${createRequestKey()
    .replace(/[^a-zA-Z0-9_]/g, "")
    .slice(0, 30)}`,
  label: "新字段",
  kind: "Text",
  required: false,
});

/** Snapshot labels follow stable reference IDs, including after table rows move. */
export function formReferenceLabels(form: OaFormDefinition, values: OaFormValues, display: OaFormValues) {
  const labels = new Map<string, string>();
  function collect(field: OaFormField, value: unknown, label: unknown) {
    if (
      (field.kind === "Person" || field.kind === "Department") &&
      typeof value === "string" &&
      typeof label === "string"
    ) {
      labels.set(`${field.kind}:${value}`, label);
    }
  }
  for (const field of form.fields) {
    const value = values[field.key],
      label = display[field.key];
    collect(field, value, label);
    if (field.kind === "Table" && Array.isArray(value) && Array.isArray(label)) {
      value.forEach((row, index) =>
        field.columns?.forEach((column) => collect(column, row[column.key], label[index]?.[column.key])),
      );
    }
  }
  return labels;
}
