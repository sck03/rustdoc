import { useLayoutEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { AccessRestrictionList, AccessRestrictionRecord, AccessRestrictionSave, ApiOrganizationDirectoryResponse, ApiPermissionResourceDefinitionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { identityQueryRoots, queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";

const modes: Record<AccessRestrictionSave["mode"], string> = {
  Deny: "禁止此动作", Unrestricted: "撤销附加限制，保留原授权", Units: "指定单位集合",
  Departments: "指定部门集合", Projects: "明确项目关系", Participation: "本人及明确参与事项",
};
type Props = { client: ExportDocManagerApiClient; principalKind: "Account" | "Unit"; principalId: number; company: string; resources?: ApiPermissionResourceDefinitionDto[] };

export function ScopeRestrictionsPanel(props: Props) {
  const { client, principalKind, principalId, resources } = props;
  const [opened, setOpened] = useState(false);
  const [busy, setBusy] = useState(false);
  const [resourceKey, setResourceKey] = useState("");
  const [action, setAction] = useState("");
  const query = useQuery({ queryKey: ["access-restrictions", principalKind, principalId], enabled: opened,
    queryFn: ({ signal }) => client.getAccessRestrictions({ principalKind, principalId: String(principalId) }, { signal }) });
  const catalog = useQuery({ queryKey: queryKeys.permissionTemplates(), enabled: opened && !resources,
    queryFn: ({ signal }) => client.listPermissionTemplates({ signal }) });
  const directory = useQuery({ queryKey: queryKeys.organizationDirectory(), enabled: opened,
    queryFn: ({ signal }) => client.getOrganizationDirectory({ signal }) });
  const available = resources ?? catalog.data?.resources ?? [];
  const current = query.data?.items.find(row => row.resourceKey === resourceKey && row.action === action);
  const failure = query.error ?? directory.error ?? catalog.error;
  return <details onToggle={event => setOpened(event.currentTarget.open)}>
    <summary>明确的数据范围与限制</summary>
    <p>限制与原有授权取交集；选择范围不会增加原本没有的权限。查看、操作和输出须分别设置。</p>
    {failure && <p role="alert">{readApiError(failure)}</p>}
    <div className="office-form-grid">
      <OfficeField label="权限资源"><select disabled={busy} value={resourceKey} onChange={event => {
        const resource = available.find(item => item.key === event.target.value);
        setResourceKey(event.target.value); setAction(resource?.actions[0]?.key ?? "");
      }}><option value="">选择资源</option>{available.map(resource => <option key={resource.key} value={resource.key}>{resource.name}</option>)}</select></OfficeField>
      <OfficeField label="权限动作"><select disabled={busy} value={action} onChange={event => setAction(event.target.value)}>
        {available.find(resource => resource.key === resourceKey)?.actions.map(item => <option key={item.key} value={item.key}>{item.name}</option>)}
      </select></OfficeField>
    </div>
    {opened && query.data && directory.data && resourceKey && action && <ScopeRestrictionEditor
      key={`${principalKind}:${principalId}:${resourceKey}:${action}:${current?.versionNumber ?? 0}`}
      {...props} onBusyChange={setBusy} resourceKey={resourceKey} action={action} current={current} directory={directory.data} />}
    {query.data?.items.map(row => {
      const resource = available.find(item => item.key === row.resourceKey);
      return <p key={row.id}>{resource?.name ?? row.resourceKey} · {resource?.actions.find(item => item.key === row.action)?.name ?? row.action} · {modes[row.mode]} · {row.reason}</p>;
    })}
  </details>;
}

function ScopeRestrictionEditor({ client, principalKind, principalId, company, resourceKey, action, current, directory, onBusyChange }:
  Props & { resourceKey: string; action: string; current?: AccessRestrictionRecord; directory: ApiOrganizationDirectoryResponse; onBusyChange(busy: boolean): void }) {
  const [mode, setMode] = useState<AccessRestrictionSave["mode"]>(current?.mode ?? "Deny");
  const [members, setMembers] = useState<string[]>(current?.members.map(member => member.id) ?? []);
  const [reason, setReason] = useState(current?.reason ?? "");
  const [error, setError] = useState("");
  const cache = useQueryClient();
  const unit = directory.companies.find(item => item.code === company);
  const projects = useQuery({ queryKey: ["personnel-foundation", unit?.id, "scope-projects"], enabled: mode === "Projects" && Boolean(unit),
    queryFn: ({ signal }) => client.listProjects({ unitId: String(unit?.id), pageSize: 100 }, { signal }) });
  const choices = mode === "Units" ? directory.companies.map(item => ({ id: String(item.id), name: item.name }))
    : mode === "Departments" ? directory.departments.filter(item => principalKind === "Account" || item.companyCode === company)
      .map(item => ({ id: String(item.id), name: `${item.companyCode} · ${item.name}` }))
    : projects.data?.items.map(item => ({ id: item.id, name: item.name })) ?? [];
  const key = ["access-restrictions", principalKind, principalId] as const;
  const operation = useScopedMutation(`${principalKind}:${principalId}:${resourceKey}:${action}`, client, {
    mutationFn: (_: void, signal) => client.saveAccessRestriction({ body: { principalKind, principalId: String(principalId), resourceKey, action,
      mode, members, reason, expectedVersion: current?.versionNumber ?? 0 } }, { signal }),
    onSuccess: row => cache.setQueryData<AccessRestrictionList>(key, data => data ? {
      ...data, items: [...data.items.filter(item => item.resourceKey !== resourceKey || item.action !== action), row],
    } : data),
    refreshKeys: () => [key, ...identityQueryRoots],
    onError: cause => setError(readApiError(cause)),
  });
  useLayoutEffect(() => { onBusyChange(operation.isPending); return () => onBusyChange(false); }, [onBusyChange, operation.isPending]);
  const hasMembers = ["Units", "Departments", "Projects"].includes(mode);
  return <>
    <fieldset className="permission-fieldset" disabled={operation.isPending}>
      <OfficeField label="附加范围或限制"><select value={mode} onChange={event => { setMode(event.target.value as AccessRestrictionSave["mode"]); setMembers([]); }}>
        {Object.entries(modes).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select></OfficeField>
      {hasMembers && <fieldset><legend>明确范围成员</legend>{choices.map(choice => <label className="checkbox-field" key={choice.id}>
        <input type="checkbox" checked={members.includes(choice.id)} onChange={event => setMembers(value => event.target.checked ? [...value, choice.id] : value.filter(id => id !== choice.id))} />{choice.name}
      </label>)}</fieldset>}
      {projects.isError && mode === "Projects" && <p role="alert">{readApiError(projects.error)}</p>}
      <OfficeField label="限制或调整原因"><input required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></OfficeField>
      <button type="button" className="command-button secondary" disabled={!reason.trim() || hasMembers && members.length === 0}
        onClick={() => { setError(""); operation.mutate(); }}>保存明确限制</button>
    </fieldset>
    {error && <p role="alert">{error}</p>}
  </>;
}
