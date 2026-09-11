import { FieldShell } from "./FormFields.tsx";
import { documentSpareKeys, type DocumentSpareFields } from "./documentSpareFields.ts";
import "../styles/document-spare-fields.css";
import { documentFieldLabel, useDocumentFieldLabels } from "./DocumentFieldLabelsContext.tsx";
import { DocumentSettingsShortcut } from "./DocumentSettingsShortcut.tsx";

export function DocumentSpareFieldsPanel({ value, onChange, group, disabled = false, label = "备用字段" }: {
  group: "invoice" | "payment";
  value: Partial<DocumentSpareFields>; onChange: (patch: Partial<DocumentSpareFields>) => void; disabled?: boolean; label?: string;
}) {
  const labels = useDocumentFieldLabels(group);
  const count = documentSpareKeys.filter((key) => value[key]?.trim()).length;
  return <details className="document-spare-fields">
    <summary>{label}<small>已填写 {count} / {documentSpareKeys.length}</small></summary>
    <p className="form-field-description">每项最多 500 字，可用于模板报表。名称由管理员在“系统设置 → 单据设置 → 字段名称”中统一维护。 <DocumentSettingsShortcut group={group} /></p>
    <div className="field-grid">{documentSpareKeys.map((key) => <FieldShell key={key} label={documentFieldLabel(labels, key)} disabled={disabled}>
      {() => <input value={value[key] ?? ""} disabled={disabled} maxLength={500} onChange={(event) => onChange({ [key]: event.target.value })} />}
    </FieldShell>)}</div>
  </details>;
}
