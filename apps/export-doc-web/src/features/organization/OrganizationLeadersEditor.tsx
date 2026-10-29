import { useState } from "react";
import type { ExportDocManagerApiClient, OrganizationLeader } from "../../api/index.ts";
import { OrganizationManagerPicker } from "./OrganizationManagerPicker.tsx";

export const leaderWorkflows: Record<string, string> = { leave: "请假", overtime: "加班", expense: "报销", travel: "出差", purchase: "采购", general: "通用申请", personnel: "人员变动", room: "会议室", supply: "物品" };

export function OrganizationLeadersEditor({ client, company, primary, value, initialNames, onChange }: {
  client: ExportDocManagerApiClient; company: string; primary?: number; value: OrganizationLeader[]; initialNames?: Record<string, string>; onChange: (value: OrganizationLeader[]) => void;
}) {
  const [choosing, setChoosing] = useState(false);
  const [names, setNames] = useState<Record<string, string>>(initialNames ?? {});
  const [message, setMessage] = useState("");
  return <><details className="office-field-wide"><summary>分管负责人（{value.length}）</summary>
    <p className="office-muted">按业务选择分管人员。流程选择“主负责人与分管负责人”时，匹配人员参与会签或或签；临时代办请使用审批代理。</p>
    {value.map((leader, index) => <fieldset key={leader.employeeId} className="office-resource-card"><legend>{names[leader.employeeId] || `人员 #${leader.employeeId}`}</legend>
      {Object.entries(leaderWorkflows).map(([kind, label]) => <label className="checkbox-field" key={kind}><input type="checkbox" checked={leader.workflowKinds.includes(kind as OrganizationLeader["workflowKinds"][number])}
        onChange={event => onChange(value.map((old, i) => i === index ? { ...old, workflowKinds: event.target.checked ? [...old.workflowKinds, kind as OrganizationLeader["workflowKinds"][number]] : old.workflowKinds.filter(k => k !== kind) } : old))} />{label}</label>)}
      <button type="button" className="command-button secondary" onClick={() => onChange(value.filter((_, i) => i !== index))}>移除分管负责人</button>
    </fieldset>)}
    {message && <p role="alert">{message}</p>}
    <button type="button" className="command-button secondary" disabled={!company || value.length >= 10} onClick={() => { setMessage(""); setChoosing(true); }}>添加分管负责人</button>
  </details>{choosing && <OrganizationManagerPicker client={client} companyCode={company} onClose={() => setChoosing(false)} onSelect={person => {
    setChoosing(false);
    if (person.id === primary || value.some(v => v.employeeId === person.id)) { setMessage("此人员已是主负责人或分管负责人，请选择其他人员。"); return; }
    setNames(current => ({ ...current, [person.id]: `${person.fullName} · ${person.employeeNumber}` }));
    onChange([...value, { employeeId: person.id, appointmentId: person.appointmentId, workflowKinds: ["general"] }]);
  }} />}</>;
}
