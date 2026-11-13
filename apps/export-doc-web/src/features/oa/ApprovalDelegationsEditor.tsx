import type { OaApprovalSettings } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import type { ApprovalAccount } from "./useApprovalSettings.ts";

type Delegations = OaApprovalSettings["delegations"];
export function ApprovalDelegationsEditor({ value, accounts, timeZone, onChange }: {
  value: Delegations; accounts: ApprovalAccount[]; timeZone: string;
  onChange: (value: Delegations) => void;
}) {
  const change = (index: number, patch: Partial<Delegations[number]>) =>
    onChange(value.map((item, i) => i === index ? { ...item, ...patch } : item));
  const accountOptions = <><option value={0}>请选择账号</option>{accounts.map(account =>
    <option key={account.id} value={account.id}>{account.fullName} · {account.username} · {account.departmentId}</option>)}</>;
  return <section className="oa-settings-section">
    <p className="office-muted">代理仅在有效期内生效；每个节点分别签署并记录实际办理人。</p>
    <div className="office-resource-grid">
      {value.map((item, index) => <section className="office-resource-card" key={item.key} aria-label={`代理 ${index + 1}`}>
        <OfficeField label="原审批人"><select value={item.principalUserId} onChange={event => change(index, { principalUserId: Number(event.target.value) })}>{accountOptions}</select></OfficeField>
        <OfficeField label="代理人"><select value={item.delegateUserId} onChange={event => change(index, { delegateUserId: Number(event.target.value) })}>{accountOptions}</select></OfficeField>
        {(["startsAt", "endsAt"] as const).map(key => <OfficeField key={key} label={key === "startsAt" ? "代理开始时间" : "代理结束时间"}>
          <input type="datetime-local" required value={toBusinessDateTimeLocalInput(item[key], timeZone)} onChange={event => {
            try { change(index, { [key]: businessDateTimeLocalInputToIso(event.target.value, timeZone) ?? "" }); }
            catch (error) { event.target.setCustomValidity(readApiError(error)); event.target.reportValidity(); }
          }} onInput={event => event.currentTarget.setCustomValidity("")} />
        </OfficeField>)}
        <label className="checkbox-field"><input type="checkbox" checked={item.isActive} onChange={event => change(index, { isActive: event.target.checked })} />启用代理</label>
        <div className="office-card-actions"><Button type="button" onClick={() => onChange(value.filter((_, i) => i !== index))}>移除代理设置</Button></div>
      </section>)}
    </div>
    <div className="office-card-actions"><Button type="button" onClick={() => onChange([...value, {
      key: createRequestKey(), principalUserId: 0, delegateUserId: 0, startsAt: new Date().toISOString(),
      endsAt: new Date(Date.now() + 86400000).toISOString(), isActive: true,
    }])}>添加代理</Button></div>
  </section>;
}
