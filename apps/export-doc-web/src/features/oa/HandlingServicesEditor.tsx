import type { OfficeHandlingService } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import type { ApprovalAccount } from "./useApprovalSettings.ts";

const handlingCategories = { Seal: "用印", Certificate: "证明开具", IT: "IT 支持", Repair: "维修", Other: "其他事项", Supply: "物品组", Room: "会议室" } as const;
export function HandlingServicesEditor({ services, accounts, disabled, onChange }: { services: OfficeHandlingService[]; accounts: ApprovalAccount[]; disabled: boolean; onChange: (services: OfficeHandlingService[]) => void }) {
  const update = (index: number, change: Partial<OfficeHandlingService>) => onChange(services.map((row, i) => i === index ? { ...row, ...change } : row));
  return <details open><summary>办理分工：事项、会议室与物品组</summary>
    <p className="office-muted">例如公章、合同章、在职证明、会议室、日常用品（卷纸、抽纸、垃圾袋）、服装辅料（吊牌、色卡、布料卡）。每项指定 1–10 位办理人，任一位办理即可，记录实际操作人。领导审批独立执行，全部通过后交办理人执行。</p>
    <p className="office-muted">办理人员须先授予对应模块的查看和完成登记／发放权限，本人范围即可按分工办理。接收归还、库存入库与盘点分别授权。</p>
    <p className="office-muted">人员变更会交接该组未结业务；历史事项名称和实际操作记录保留。停用仅停止新选用，原申请仍需办结。</p>
    {services.map((service, index) => <fieldset key={service.key} disabled={disabled} className="office-form-grid office-resource-card"><legend>办理分工 {index + 1}</legend>
      <OfficeField label="分工类别"><select value={service.category} onChange={event => update(index, { category: event.target.value as OfficeHandlingService["category"] })}>{Object.entries(handlingCategories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></OfficeField>
      <OfficeField label="事项或资源分工名称"><input required maxLength={100} value={service.name} placeholder="如公章、在职证明、三楼会议室、服装辅料" onChange={event => update(index, { name: event.target.value })} /></OfficeField>
      <div className="office-field-wide">{service.handlerUserIds.map((id, person) => <div className="office-card-actions" key={person}>
        <OfficeField label={`办理人员 ${person + 1}`}><select required value={id || ""} onChange={event => update(index, { handlerUserIds: service.handlerUserIds.map((value, i) => i === person ? Number(event.target.value) : value) })}><option value="">请选择账号</option>{accounts.map(account => <option value={account.id} key={account.id} disabled={account.id !== id && service.handlerUserIds.includes(account.id)}>{account.fullName} · {account.username}</option>)}</select></OfficeField>
        <button type="button" className="command-button secondary" disabled={service.handlerUserIds.length === 1} onClick={() => update(index, { handlerUserIds: service.handlerUserIds.filter((_, i) => i !== person) })}>移除人员</button>
      </div>)}<button type="button" className="command-button secondary" disabled={service.handlerUserIds.length >= 10} onClick={() => update(index, { handlerUserIds: [...service.handlerUserIds, 0] })}>添加办理人员</button></div>
      <label className="checkbox-field"><input type="checkbox" checked={service.isActive} onChange={event => update(index, { isActive: event.target.checked })} />允许新申请选用</label>
      <button type="button" className="command-button secondary" onClick={() => onChange(services.filter((_, i) => i !== index))}>移除未使用分工</button>
    </fieldset>)}
    <button type="button" className="command-button secondary" disabled={disabled || services.length >= 100} onClick={() => onChange([...services, { key: createRequestKey(), category: "Seal", name: "", handlerUserIds: [0], isActive: true }])}>添加办理分工</button>
  </details>;
}
