import { FieldShell } from "./FormFields.tsx";
import { documentSpareKeys, type DocumentSpareFields } from "./documentSpareFields.ts";
import "../styles/document-spare-fields.css";
import { documentFieldLabel, useDocumentFieldLabels } from "./DocumentFieldLabelsContext.tsx";

export function DocumentSpareFieldsPanel({ value, onChange, group, disabled = false, label = "备用字段" }: {
  group: "invoice" | "payment";
  value: Partial<DocumentSpareFields>; onChange: (patch: Partial<DocumentSpareFields>) => void; disabled?: boolean; label?: string;
}) {
  const labels = useDocumentFieldLabels(group);
  const count = documentSpareKeys.filter((key) => value[key]?.trim()).length;
  return <details className="document-spare-fields">
    <summary>{label}<small>已填写 {count} / {documentSpareKeys.length}</small></summary>
    <p className="form-field-description">每项最多 500 字，可用于模板报表。管理员可在“系统设置 → 运行与数据库 → 单据字段名称”统一修改名称。</p>
    <div className="field-grid">{documentSpareKeys.map((key) => <FieldShell key={key} label={documentFieldLabel(labels, key)} disabled={disabled}>
      {() => <input value={value[key] ?? ""} disabled={disabled} maxLength={500} onChange={(event) => onChange({ [key]: event.target.value })} />}
    </FieldShell>)}</div>
  </details>;
}
