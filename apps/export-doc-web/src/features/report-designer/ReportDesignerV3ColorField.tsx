import { useEffect, useState } from "react";
import { isReportDesignerCssColor } from "./reportDesignerSchemaValues.ts";
const palette = ["#1f2933", "#334155", "#2563eb", "#0f766e", "#15803d", "#ca8a04", "#dc2626", "#7c3aed", "#ffffff"];
export function ReportDesignerV3ColorField({
  label,
  value,
  allowEmpty = false,
  disabled = false,
  onCommit,
}: {
  label: string;
  value: string;
  allowEmpty?: boolean;
  disabled?: boolean;
  onCommit: (value: string) => void;
}) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  const invalid = Boolean(draft) && !isReportDesignerCssColor(draft);
  function commit(next = draft) {
    const normalized = next.trim();
    if ((allowEmpty && !normalized) || isReportDesignerCssColor(normalized)) {
      setDraft(normalized);
      if (normalized !== value) onCommit(normalized);
    }
  }
  return <div className="report-designer-v3-color-field">
    <span>{label}</span>
    <div className="report-designer-v3-color-palette" aria-label="常用颜色">
      {palette.map((color) => <button key={color} type="button" aria-label={color} title={color} disabled={disabled} style={{ backgroundColor: color }} onClick={() => { setDraft(color); onCommit(color); }} />)}
    </div>
    <div className="report-designer-v3-color-advanced">
      <input type="color" value={isReportDesignerCssColor(draft) ? draft : "#000000"} disabled={disabled} aria-label={`${label}原生选择器`} onChange={(event) => commit(event.target.value)} />
      <input type="text" value={draft} disabled={disabled} placeholder="请输入有效的颜色值" aria-label={`${label}高级色值`} aria-invalid={invalid} onChange={(event) => setDraft(event.target.value)} onBlur={() => commit()} onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); commit(); } }} />
      {allowEmpty ? <button className="report-designer-v3-color-clear" type="button" disabled={disabled || !draft} onClick={() => { setDraft(""); onCommit(""); }}>清空</button> : null}
    </div>
    {invalid ? <small role="alert">请输入有效的颜色值</small> : null}
  </div>;
}
