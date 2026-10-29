import { useRef, useState, type FormEvent } from "react";
import type { ApiOrganizationCompanyDto, ApiOrganizationDepartmentDto, ExportDocManagerApiClient, OrganizationChangeImpact } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "./useOrganizationDirectory.ts";
import { departmentManagerLabel, parentDepartmentOptions } from "./organizationModel.ts";
import { OrganizationManagerPicker } from "./OrganizationManagerPicker.tsx";
import { OrganizationPositionsEditor } from "./OrganizationPositionsEditor.tsx";
import { OrganizationLeadersEditor } from "./OrganizationLeadersEditor.tsx";

export function OrganizationCompanyForm({ client, record, companies = [], canStructure = false, onClose, onSaved }: {
  companies?: ApiOrganizationCompanyDto[]; canStructure?: boolean;
  client: ExportDocManagerApiClient; record?: ApiOrganizationCompanyDto; onClose: () => void; onSaved: (company: ApiOrganizationCompanyDto) => void;
}) {
  const operation = useOrganizationOperation();
  const [executive, setExecutive] = useState<{ id: number; name: string; appointmentId?: string | null } | null>(record?.executiveEmployeeId ? { id: record.executiveEmployeeId, appointmentId: record.executiveAppointmentId, name: record.executiveName || `人员 #${record.executiveEmployeeId}` } : null);
  const [unitKind, setUnitKind] = useState(record?.unitKind ?? "Company");
  const [choosingExecutive, setChoosingExecutive] = useState(false);
  const [positions, setPositions] = useState(record?.positions ?? []);
  const [additionalLeaders, setAdditionalLeaders] = useState(record?.additionalLeaders ?? []);
  const formRef = useRef<HTMLFormElement>(null);
  const [impact, setImpact] = useState<OrganizationChangeImpact | null>(null);
  function request(formElement: HTMLFormElement) {
    const form = new FormData(formElement);
    return { code: record?.code ?? String(form.get("code") ?? "").trim(), name: String(form.get("name") ?? "").trim(),
      parentCompanyCode: canStructure ? String(form.get("parentCompanyCode") ?? "") || null : record?.parentCompanyCode ?? null,
      unitKind,
      isActive: form.has("isActive"), expectedVersion: record?.versionNumber ?? 0, executiveEmployeeId: executive?.id ?? null, executiveAppointmentId: executive?.appointmentId ?? null, positions, additionalLeaders };
  }
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const body = request(event.currentTarget);
    void operation.run((signal) => record ? client.updateOrganizationCompany({ id: record.id, body }, { signal })
      : client.createOrganizationCompany({ body }, { signal }), onSaved);
  }
  return <><OfficeDialog title={record ? "编辑公司" : "新增公司"} onClose={onClose} {...operation} protectChanges hasChanges={(executive?.id ?? null) !== (record?.executiveEmployeeId ?? null) || JSON.stringify(positions) !== JSON.stringify(record?.positions ?? []) || JSON.stringify(additionalLeaders) !== JSON.stringify(record?.additionalLeaders ?? [])}>
    <form ref={formRef} onSubmit={submit} onChange={() => setImpact(null)} onInvalidCapture={event => { const section = (event.target as HTMLElement).closest("details"); if (section) section.open = true; }}><fieldset className="office-form-grid" disabled={operation.busy}>
      <OfficeField label="公司代码"><input name="code" required maxLength={50} defaultValue={record?.code ?? ""} readOnly={Boolean(record)} autoFocus={!record} /></OfficeField>
      <OfficeField label="公司名称"><input name="name" required maxLength={120} defaultValue={record?.name ?? ""} autoFocus={Boolean(record)} /></OfficeField>
      <label className="checkbox-field"><input type="checkbox" name="isActive" defaultChecked={record?.isActive ?? true} />启用公司</label>
      {canStructure && <details className="office-field-wide"><summary>集团隶属</summary><OfficeField label="上级单位"><select name="parentCompanyCode" defaultValue={record?.parentCompanyCode ?? ""}><option value="">独立根单位</option>{companies.filter(c => c.isActive && c.code !== record?.code).map(c => <option key={c.code} value={c.code}>{c.name}</option>)}</select></OfficeField><OfficeField label="单位类型"><select name="unitKind" value={unitKind} onChange={e => setUnitKind(e.target.value as "Company" | "Group")}><option value="Company">经营单位（公司 / 分支）</option><option value="Group">集团目录（无业务任职）</option></select></OfficeField></details>}
      {unitKind === "Company" && <><div className="office-field-wide"><h3>公司领导审批负责人</h3><p>{executive?.name || "未设置"}</p>
        <p className="office-muted">须关联启用账号并取得对应业务的查看和审批权限。普通资料范围独立设置；在途步骤通过申请详情移交。</p>
        <button type="button" className="command-button secondary" disabled={!record?.isActive} onClick={() => setChoosingExecutive(true)}>选择公司领导审批负责人</button>
        {executive && <button type="button" className="command-button secondary" onClick={() => setExecutive(null)}>清除公司领导审批负责人</button>}
      </div>
      <OrganizationPositionsEditor value={positions} onChange={setPositions} />
      <OrganizationLeadersEditor client={client} company={record?.code ?? ""} primary={executive?.id} value={additionalLeaders} initialNames={record?.additionalLeaderNames} onChange={setAdditionalLeaders} /></>}
      {unitKind === "Group" && <p className="office-field-wide office-muted">集团目录用于单位隶属和分级管理。人员任职、部门及业务职责请设置在经营单位。</p>}
      <p className="office-field-wide office-muted">代码创建后固定，名称可随时调整。停用前须处理启用部门、账号和在职人员。</p>
    </fieldset>
    {record && canStructure && <button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => {
      if (formRef.current?.reportValidity()) { const body = request(formRef.current); void operation.run(signal => client.previewOrganizationCompanyChange({ id: record.id, body }, { signal }), setImpact); }
    }}>检查组织变更影响</button>}
    {impact && <section aria-label="组织变更影响" aria-live="polite"><p>{impact.requiresHandover ? "以下授权的单位范围将发生变化，请先交接并停用，调整后重新授权。" : "此次调整没有改变已有授权的单位范围。保存时仍会检查组织和业务引用。"}</p>{impact.impacts.map(item => <div className="office-resource-card" key={item.roleId}><strong>{item.userName} · {item.roleName}</strong><p>新增单位：{item.addedUnits.map(unit => `${unit.name}（${unit.code}）`).join("、") || "无"}</p><p>移出单位：{item.removedUnits.map(unit => `${unit.name}（${unit.code}）`).join("、") || "无"}</p></div>)}</section>}
    <OfficeSubmit busy={operation.busy} label="保存公司" /></form>
  </OfficeDialog>{choosingExecutive && record && <OrganizationManagerPicker client={client} companyCode={record.code} onClose={() => setChoosingExecutive(false)} onSelect={person => { setExecutive({ id: person.id, appointmentId: person.appointmentId, name: `${person.fullName} · ${person.employeeNumber}` }); setChoosingExecutive(false); }} />}</>;
}

export function OrganizationDepartmentForm({ client, company, departments, record, parentCode, onClose, onSaved }: {
  client: ExportDocManagerApiClient; company: ApiOrganizationCompanyDto; departments: ApiOrganizationDepartmentDto[];
  record?: ApiOrganizationDepartmentDto; parentCode?: string; onClose: () => void; onSaved: (department: ApiOrganizationDepartmentDto) => void;
}) {
  const operation = useOrganizationOperation();
  const [manager, setManager] = useState<{ id: number; name: string; appointmentId?: string | null } | null>(record?.managerEmployeeId ? { id: record.managerEmployeeId, appointmentId: record.managerAppointmentId, name: departmentManagerLabel(record) } : null);
  const [choosingManager, setChoosingManager] = useState(false);
  const [active, setActive] = useState(record?.isActive ?? true);
  const [additionalLeaders, setAdditionalLeaders] = useState(record?.additionalLeaders ?? []);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = { code: record?.code ?? String(form.get("code") ?? "").trim(), companyCode: company.code,
      name: String(form.get("name") ?? "").trim(), parentCode: String(form.get("parentCode") ?? "") || null,
      managerEmployeeId: manager?.id ?? null, managerAppointmentId: manager?.appointmentId ?? null, additionalLeaders, isActive: active, expectedVersion: record?.versionNumber ?? 0 };
    void operation.run((signal) => record ? client.updateOrganizationDepartment({ id: record.id, body }, { signal })
      : client.createOrganizationDepartment({ body }, { signal }), onSaved);
  }
  return <>
    <OfficeDialog title={record ? "编辑部门" : "新增部门"} onClose={onClose} {...operation} protectChanges hasChanges={(manager?.id ?? null) !== (record?.managerEmployeeId ?? null) || JSON.stringify(additionalLeaders) !== JSON.stringify(record?.additionalLeaders ?? [])}>
      <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
        <p className="office-field-wide office-muted">所属公司：{company.name}。部门代码和所属公司创建后固定。</p>
        <OfficeField label="部门代码"><input name="code" required maxLength={50} defaultValue={record?.code ?? ""} readOnly={Boolean(record)} autoFocus={!record} /></OfficeField>
        <OfficeField label="部门名称"><input name="name" required maxLength={120} defaultValue={record?.name ?? ""} autoFocus={Boolean(record)} /></OfficeField>
        <OfficeField label="上级部门" wide><select name="parentCode" defaultValue={record?.parentCode ?? parentCode ?? ""}>
          <option value="">直接隶属公司</option>{parentDepartmentOptions(departments, record?.code).filter((item) => !active || item.isActive).map((item) =>
            <option key={item.code} value={item.code}>{item.label}{item.isActive ? "" : "（停用）"}</option>)}
        </select></OfficeField>
        <div className="office-field office-field-wide"><span>部门负责人</span><div className="office-card-actions"><span>{manager?.name || "未设置"}</span>
          <button type="button" className="command-button secondary" disabled={!active} onClick={() => setChoosingManager(true)}>选择负责人</button>
          {manager && <button type="button" className="command-button secondary" onClick={() => setManager(null)}>取消负责人</button>}</div></div>
        <label className="checkbox-field"><input type="checkbox" name="isActive" checked={active} onChange={(event) => setActive(event.target.checked)} />启用部门</label>
        <OrganizationLeadersEditor client={client} company={company.code} primary={manager?.id} value={additionalLeaders} initialNames={record?.additionalLeaderNames} onChange={setAdditionalLeaders} />
        <p className="office-field-wide office-muted">调整上级部门会改变组织展示层级，现有人员归属和授权范围按原部门保留。停用前须调整子部门、负责人、在职人员及启用账号。</p>
      </fieldset><OfficeSubmit busy={operation.busy} label="保存部门" /></form>
    </OfficeDialog>
    {choosingManager && <OrganizationManagerPicker client={client} companyCode={company.code} onClose={() => setChoosingManager(false)}
      onSelect={(person) => { setManager({ id: person.id, appointmentId: person.appointmentId, name: departmentManagerLabel({ managerName: person.fullName, managerEmployeeNumber: person.employeeNumber }) }); setChoosingManager(false); }} />}
  </>;
}
