import { useState, type FormEvent } from "react";
import type { ApiOrganizationCompanyDto, ApiOrganizationDepartmentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "./useOrganizationDirectory.ts";
import { parentDepartmentOptions } from "./organizationModel.ts";
import { OrganizationManagerPicker } from "./OrganizationManagerPicker.tsx";

export function OrganizationCompanyForm({ client, record, onClose, onSaved }: {
  client: ExportDocManagerApiClient; record?: ApiOrganizationCompanyDto; onClose: () => void; onSaved: (company: ApiOrganizationCompanyDto) => void;
}) {
  const operation = useOrganizationOperation();
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = { code: record?.code ?? String(form.get("code") ?? "").trim(), name: String(form.get("name") ?? "").trim(),
      isActive: form.has("isActive"), expectedVersion: record?.versionNumber ?? 0 };
    void operation.run((signal) => record ? client.updateOrganizationCompany({ code: record.code, body }, { signal })
      : client.createOrganizationCompany({ body }, { signal }), onSaved);
  }
  return <OfficeDialog title={record ? "维护公司" : "新增公司"} onClose={onClose} {...operation} protectChanges>
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      <OfficeField label="公司代码"><input name="code" required maxLength={50} defaultValue={record?.code ?? ""} readOnly={Boolean(record)} autoFocus={!record} /></OfficeField>
      <OfficeField label="公司名称"><input name="name" required maxLength={120} defaultValue={record?.name ?? ""} autoFocus={Boolean(record)} /></OfficeField>
      <label className="checkbox-field"><input type="checkbox" name="isActive" defaultChecked={record?.isActive ?? true} />启用公司</label>
      <p className="office-field-wide office-muted">代码创建后固定，名称可随时调整。停用前须处理启用部门、账号和在职人员。</p>
    </fieldset><OfficeSubmit busy={operation.busy} label="保存公司" /></form>
  </OfficeDialog>;
}

export function OrganizationDepartmentForm({ client, company, departments, record, parentCode, onClose, onSaved }: {
  client: ExportDocManagerApiClient; company: ApiOrganizationCompanyDto; departments: ApiOrganizationDepartmentDto[];
  record?: ApiOrganizationDepartmentDto; parentCode?: string; onClose: () => void; onSaved: () => void;
}) {
  const operation = useOrganizationOperation();
  const [manager, setManager] = useState<{ id: number; name: string } | null>(record?.managerEmployeeId ? { id: record.managerEmployeeId, name: record.managerName } : null);
  const [choosingManager, setChoosingManager] = useState(false);
  const [active, setActive] = useState(record?.isActive ?? true);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = { code: record?.code ?? String(form.get("code") ?? "").trim(), companyCode: company.code,
      name: String(form.get("name") ?? "").trim(), parentCode: String(form.get("parentCode") ?? "") || null,
      managerEmployeeId: manager?.id ?? null, isActive: active, expectedVersion: record?.versionNumber ?? 0 };
    void operation.run((signal) => record ? client.updateOrganizationDepartment({ code: record.code, body }, { signal })
      : client.createOrganizationDepartment({ body }, { signal }), onSaved);
  }
  return <>
    <OfficeDialog title={record ? "维护部门" : "新增部门"} onClose={onClose} {...operation} protectChanges hasChanges={(manager?.id ?? null) !== (record?.managerEmployeeId ?? null)}>
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
        <p className="office-field-wide office-muted">调整上级部门会改变组织展示层级，现有人员归属和授权范围按原部门保留。停用前须调整子部门、负责人及在职人员。</p>
      </fieldset><OfficeSubmit busy={operation.busy} label="保存部门" /></form>
    </OfficeDialog>
    {choosingManager && <OrganizationManagerPicker client={client} companyCode={company.code} onClose={() => setChoosingManager(false)}
      onSelect={(person) => { setManager({ id: person.id, name: person.fullName }); setChoosingManager(false); }} />}
  </>;
}
