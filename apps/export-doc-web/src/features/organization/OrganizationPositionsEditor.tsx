import type { OrganizationPosition } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";

export function OrganizationPositionsEditor({ value, onChange }: { value: OrganizationPosition[]; onChange: (value: OrganizationPosition[]) => void }) {
  return <details className="office-field-wide"><summary>公司职位目录（{value.length} 项）</summary>
    <p className="office-muted">维护本公司的常用职位。人员档案仍可直接填写自定义职位；停用或移除建议项保留已登记的任职历史，职位不自动授予账号权限。</p>
    {value.map((position, index) => <div className="office-card-actions" key={index}>
      <OfficeField label={`职位 ${index + 1}`}><input required maxLength={120} value={position.name} onChange={event => onChange(value.map((old, i) => i === index ? { ...old, name: event.target.value } : old))} /></OfficeField>
      <label className="checkbox-field"><input type="checkbox" checked={position.isActive} onChange={event => onChange(value.map((old, i) => i === index ? { ...old, isActive: event.target.checked } : old))} />启用建议</label>
      <button className="command-button secondary" type="button" onClick={() => onChange(value.filter((_, i) => i !== index))}>移除职位建议</button>
    </div>)}
    <button className="command-button secondary" type="button" disabled={value.length >= 200} onClick={() => onChange([...value, { name: "", isActive: true }])}>添加公司职位</button>
  </details>;
}
