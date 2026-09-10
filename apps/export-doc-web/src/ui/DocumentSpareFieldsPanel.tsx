import { FieldShell } from "./FormFields.tsx";
import { documentSpareKeys, type DocumentSpareFields } from "./documentSpareFields.ts";
import "../styles/document-spare-fields.css";

export function DocumentSpareFieldsPanel({ value, onChange, disabled = false, label = "备用字段" }: {
  value: Partial<DocumentSpareFields>; onChange: (patch: Partial<DocumentSpareFields>) => void; disabled?: boolean; label?: string;
}) {
  const count = documentSpareKeys.filter((key) => value[key]?.trim()).length;
  return <details className="document-spare-fields">
    <summary>{label}<small>已填写 {count} / {documentSpareKeys.length}</small></summary>
    <p className="form-field-description">用于本单据的自定义内容，每项最多 500 字。可在模板设计器中选择对应的备用字段，并为它设置实际用途的标签。</p>
    <div className="field-grid">{documentSpareKeys.map((key, index) => <FieldShell key={key} label={`备用 ${index + 1}`} disabled={disabled}>
      {() => <input value={value[key] ?? ""} disabled={disabled} maxLength={500} onChange={(event) => onChange({ [key]: event.target.value })} />}
    </FieldShell>)}</div>
  </details>;
}
