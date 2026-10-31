import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { AccessRestrictionSave, ApiPermissionResourceDefinitionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "../organization/useOrganizationDirectory.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
export function ScopeRestrictionsPanel({ client, principalKind, principalId, company, resources }: { client: ExportDocManagerApiClient; principalKind: "Account" | "Unit"; principalId: number; company: string; resources?: ApiPermissionResourceDefinitionDto[] }) {
  const [opened, setOpened] = useState(false);
  const [resourceKey, setResourceKey] = useState("");
  const [action, setAction] = useState("");
  const [mode, setMode] = useState<AccessRestrictionSave["mode"]>("Deny");
  const [members, setMembers] = useState<string[]>([]);
  const [reason, setReason] = useState("");
  const operation = useOrganizationOperation();
  const query = useQuery({ queryKey: ["access-restrictions", principalKind, principalId], enabled: opened, queryFn: ({ signal }) => client.getAccessRestrictions({ principalKind, principalId: String(principalId) }, { signal }) });
  const catalog = useQuery({ queryKey: queryKeys.permissionTemplates(), enabled: opened && !resources, queryFn: ({ signal }) => client.listPermissionTemplates({ signal }) });
  const directory = useQuery({ queryKey: queryKeys.organizationDirectory(), enabled: opened, queryFn: ({ signal }) => client.getOrganizationDirectory({ signal }) });
  const available = resources ?? catalog.data?.resources ?? [];
  const current = query.data?.items.find(row => row.resourceKey === resourceKey && row.action === action);
  const unit = directory.data?.companies.find(unit => unit.code === company);
  const projects = useQuery({ queryKey: ["personnel-foundation", unit?.id, "scope-projects"], enabled: opened && mode === "Projects" && Boolean(unit), queryFn: ({ signal }) => client.listProjects({ unitId: String(unit?.id), pageSize: 100 }, { signal }) });
  const choices = mode === "Units" ? directory.data?.companies.map(unit => ({ id: String(unit.id), name: unit.name })) ?? [] : mode === "Departments" ? directory.data?.departments.filter(department => principalKind === "Account" || department.companyCode === company).map(department => ({ id: String(department.id), name: `${department.companyCode} · ${department.name}` })) ?? [] : projects.data?.items.map(project => ({ id: project.id, name: project.name })) ?? [];
  return <details onToggle={event => setOpened(event.currentTarget.open)}><summary>明确的数据范围与限制</summary><p>限制与原有授权取交集。禁止动作优先；选择范围不会增加原本没有的权限。</p>{query.isError && <p role="alert">{readApiError(query.error)}</p>}<div className="office-form-grid"><OfficeField label="权限资源"><select value={resourceKey} onChange={event => { const resource = available.find(resource => resource.key === event.target.value); setResourceKey(event.target.value); setAction(resource?.actions[0]?.key ?? ""); setMembers([]); }}><option value="">选择资源</option>{available.map(resource => <option key={resource.key} value={resource.key}>{resource.name}</option>)}</select></OfficeField><OfficeField label="权限动作"><select value={action} onChange={event => setAction(event.target.value)}>{available.find(resource => resource.key === resourceKey)?.actions.map(action => <option key={action.key} value={action.key}>{action.name}</option>)}</select></OfficeField><OfficeField label="附加范围或限制"><select value={mode} onChange={event => { setMode(event.target.value as AccessRestrictionSave["mode"]); setMembers([]); }}><option value="Deny">禁止此动作</option><option value="Unrestricted">撤销附加限制，保留原授权</option><option value="Units">指定单位集合</option><option value="Departments">指定部门集合</option><option value="Projects">明确项目关系</option><option value="Participation">本人及明确参与事项</option></select></OfficeField></div>{["Units", "Departments", "Projects"].includes(mode) && <fieldset disabled={operation.busy}><legend>明确范围成员</legend>{choices.map(choice => <label className="checkbox-field" key={choice.id}><input type="checkbox" checked={members.includes(choice.id)} onChange={event => setMembers(current => event.target.checked ? [...current, choice.id] : current.filter(id => id !== choice.id))} />{choice.name}</label>)}</fieldset>}<OfficeField label="限制或调整原因"><input required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></OfficeField><button type="button" className="command-button secondary" disabled={operation.busy || !resourceKey || !action || !reason.trim()} onClick={() => void operation.run(signal => client.saveAccessRestriction({ body: { principalKind, principalId: String(principalId), resourceKey, action, mode, members, reason, expectedVersion: current?.versionNumber ?? 0 } }, { signal }), () => { void query.refetch(); })}>保存明确限制</button>{operation.error && <p role="alert">{operation.error}</p>}
    {query.data?.items.map(row => <p key={row.id}>{available.find(resource => resource.key === row.resourceKey)?.name ?? row.resourceKey} · {row.action} · {row.mode} · {row.reason}</p>)}
  </details>;
}
