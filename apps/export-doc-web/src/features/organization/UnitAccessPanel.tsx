import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, UnitAccessDirectory, UnitAccessRecord, UnitAccessSave } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "./useOrganizationDirectory.ts";
import { PermissionModuleGrid } from "../settings/PermissionModuleGrid.tsx";
import { grantKey } from "../settings/permissionSchemeModel.ts";
import { unitPermissionEditor } from "../settings/unitPermissionModel.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { UnitPermissionSchemes } from "./UnitPermissionSchemes.tsx";
import { PermissionVersionPicker } from "../settings/PermissionVersionPicker.tsx";
import { ScopeRestrictionsPanel } from "../settings/ScopeRestrictionsPanel.tsx";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput, formatBusinessDateTime } from "../../ui/businessTime.ts";

export function UnitAccessPanel({ client, user, company }: { client: ExportDocManagerApiClient; user: ApiUserDto; company: string }) {
  const [editing, setEditing] = useState<{ record?: UnitAccessRecord } | null>(null);
  const query = useQuery({ queryKey: ["unit-access", company], enabled: Boolean(company), queryFn: ({ signal }) => client.getUnitAccessDirectory({ companyCode: company }, { signal }) });
  const group = query.data?.companies.find(item => item.code === company)?.unitKind === "Group";
  return <section className="organization-section" aria-label="单位身份与授权"><div className="organization-heading"><h3>单位身份与授权</h3>{(!group || user.capabilities.canManageUsers) && <button type="button" className="command-button" disabled={!query.data} onClick={() => setEditing({})}>新增单位身份</button>}</div>
    <p className="office-muted">正式任职、临时协作和单位管理分别授权。每个身份独立生效；职位与上下级关系不自动授予业务权限。</p>
    {query.data && <UnitPermissionSchemes client={client} company={company} directory={query.data} />}
    {query.isError && <PageState tone="error" title="单位授权加载失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} />}
    {query.data?.items.map(item => <div className="office-resource-card" key={item.id}><strong>{item.userName} · {item.name}</strong><p>{item.mode === "Administration" ? "单位管理" : item.mode === "Collaboration" ? "临时审批协作" : "业务任职"} · {item.isActive ? "启用" : "停用"}{item.endsAt ? ` · 到期 ${formatBusinessDateTime(item.endsAt, user.businessTimeZone)}` : ""}</p><button type="button" className="text-button" disabled={item.mode === "Administration" && !user.capabilities.canManageUsers || item.userId === user.id && !user.capabilities.canManageUsers} onClick={() => setEditing({ record: item })}>编辑授权</button></div>)}
    {editing && query.data && <UnitAccessEditor client={client} user={user} company={company} directory={query.data} record={editing.record} onClose={() => setEditing(null)} onSaved={() => setEditing(null)} />}
  </section>;
}

function UnitAccessEditor({ client, user, company, directory, record, onClose, onSaved }: { client: ExportDocManagerApiClient; user: ApiUserDto; company: string; directory: UnitAccessDirectory; record?: UnitAccessRecord; onClose: () => void; onSaved: () => void }) {
  const group = directory.companies.find(item => item.code === company)?.unitKind === "Group";
  const initial: UnitAccessSave = record ? { ...record, expectedVersion: record.versionNumber } : {
    userId: 0, companyCode: company, name: "", mode: group ? "Administration" : "Business", appointmentId: "", permissionGrants: [], disabledModules: [], oaAssignedOnly: false,
    management: { organization: false, appointments: false, workflows: false, roles: false }, includeDescendants: false, startsAt: "", endsAt: "", isActive: true, expectedVersion: 0,
  };
  const [draft, setDraft] = useState(initial);
  const operation = useOrganizationOperation();
  const patch = (value: Partial<UnitAccessSave>) => setDraft(old => ({ ...old, ...value }));
  const scheme = directory.permissionTemplates.find(t => t.id === draft.permissionTemplateId);
  const inherited = draft.permissionGrants === null;
  const versionDefinition = useQuery({queryKey:["permission-version-definition",draft.permissionTemplateId,draft.permissionSchemeVersion],enabled:inherited&&Boolean(draft.permissionTemplateId&&draft.permissionSchemeVersion),queryFn:({signal})=>client.getPermissionSchemeVersion({schemeId:String(draft.permissionTemplateId),version:draft.permissionSchemeVersion??0},{signal})});
  const directGrants = draft.permissionGrants ?? versionDefinition.data?.grants ?? [];
  const inheritedModules = inherited ? versionDefinition.data?.disabledModules ?? [] : [];
  const effectiveModules = [...new Set([...draft.disabledModules, ...inheritedModules])];
  const grants = Object.fromEntries(directGrants.map(g => [grantKey(g.resourceKey,g.action),g.dataScope]));
  const model = unitPermissionEditor(directory.resources,directory.grantCeiling,draft.mode==="Collaboration");
  const changeGrants = (value: Record<string, string>) => patch({ permissionGrants: model.grants(value), permissionTemplateId: null,permissionSchemeVersion:null });
  const templates = directory.permissionTemplates.filter(template=>draft.mode!=="Collaboration"||model.collaborationScheme(template.grants));
  const appointments = directory.users.find(u => u.id === draft.userId)?.appointments ?? [];
  return <OfficeDialog title={record ? "编辑单位身份" : "新增单位身份"} onClose={onClose} {...operation} protectChanges hasChanges={JSON.stringify(draft) !== JSON.stringify(initial)}>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.saveUnitAccess({ body: draft }, { signal }), onSaved); }} onInvalidCapture={event => { const details = (event.target as HTMLElement).closest("details"); if (details) details.open = true; }}>
      <fieldset className="office-form-grid" disabled={operation.busy}>
        <OfficeField label="人员账号"><select required value={draft.userId || ""} disabled={Boolean(record)} onChange={e => patch({ userId: Number(e.target.value), appointmentId: "" })}><option value="">选择账号</option>{record && !directory.users.some(u => u.id === record.userId) && <option value={record.userId}>{record.userName}（当前不可启用）</option>}{directory.users.map(option => <option key={option.id} value={option.id}>{option.name} · {option.companyCode}</option>)}</select></OfficeField>
        <OfficeField label="身份名称"><input required maxLength={120} value={draft.name} onChange={e => patch({ name: e.target.value })} /></OfficeField>
        <OfficeField label="身份类型"><select value={draft.mode} disabled={Boolean(record)} onChange={e => patch({ mode: e.target.value as UnitAccessSave["mode"], permissionGrants: [], permissionTemplateId: null, appointmentId: "", oaAssignedOnly: e.target.value === "Collaboration" })}>{!group && <><option value="Business">正式任职</option><option value="Collaboration">临时审批协作</option></>}{user.capabilities.canManageUsers && <option value="Administration">单位管理</option>}</select></OfficeField>
        {draft.mode === "Business" && <OfficeField label="任职部门"><select required value={draft.appointmentId} onChange={e => patch({ appointmentId: e.target.value })}><option value="">选择有效任职</option>{draft.appointmentId && !appointments.some(a => a.id === draft.appointmentId) && <option value={draft.appointmentId}>已失效任职 · {draft.appointmentId}</option>}{appointments.map(a => <option key={a.id} value={a.id}>{a.departmentName} · {a.jobTitle}</option>)}</select>{draft.userId > 0 && !appointments.length && <p className="field-help">此人员尚无本单位的有效任职，请先在人事管理中登记任职。</p>}</OfficeField>}
        <label className="checkbox-field"><input type="checkbox" checked={draft.isActive} onChange={e => patch({ isActive: e.target.checked })} />启用身份</label>
        <details className="office-field-wide"><summary>授权有效期</summary><p>按业务时区 {user.businessTimeZone} 填写。</p>{(["startsAt", "endsAt"] as const).map(field => <OfficeField key={field} label={field === "startsAt" ? "生效时间" : "到期时间"}><input type="datetime-local" required={field === "endsAt" && draft.mode === "Collaboration"} value={toBusinessDateTimeLocalInput(draft[field], user.businessTimeZone)} onChange={e => { try { e.target.setCustomValidity(""); patch({ [field]: businessDateTimeLocalInputToIso(e.target.value, user.businessTimeZone) ?? "" }); } catch (error) { e.target.setCustomValidity(readApiError(error)); e.target.reportValidity(); } }} /></OfficeField>)}</details>
        {draft.mode === "Administration" && <div className="office-field-wide"><h4>管理范围与职责</h4>{([ ["organization","组织维护"],["appointments","任职维护"],["workflows","流程维护"],["roles","业务授权"],["analytics","集团审批统计"] ] as const).map(([key,label]) => <label className="checkbox-field" key={key}><input type="checkbox" checked={draft.management[key] ?? false} onChange={e => patch({ management: { ...draft.management, [key]: e.target.checked } })} />{label}</label>)}<label className="checkbox-field"><input type="checkbox" checked={draft.includeDescendants} onChange={e => patch({ includeDescendants: e.target.checked })} />包含下级单位（以后加入的下级也在范围内）</label><p>下方设置可下放的业务权限上限；管理身份本身不获得这些业务权限。</p></div>}
        <div className="office-field-wide">
          {inherited&&<PermissionVersionPicker key={draft.permissionTemplateId} client={client} schemeId={draft.permissionTemplateId} value={draft.permissionSchemeVersion} disabled={operation.busy} onChange={permissionSchemeVersion=>patch({permissionSchemeVersion})} />}
          {draft.mode !== "Administration" && <OfficeField label="权限来源"><select value={inherited ? String(draft.permissionTemplateId ?? "") : "custom"} onChange={e => e.target.value === "custom" ? patch({ permissionTemplateId: null, permissionGrants: directGrants, disabledModules: effectiveModules, oaAssignedOnly: draft.oaAssignedOnly || Boolean(scheme?.oaAssignedOnly) }) : patch({ permissionTemplateId: Number(e.target.value), permissionSchemeVersion: templates.find(t=>t.id===Number(e.target.value))?.publishedVersion??null, permissionGrants: null })}><option value="custom">独立配置</option>{inherited && !scheme && <option value={draft.permissionTemplateId ?? ""}>当前方案已停用或超出授权范围</option>}{templates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></OfficeField>}
          <p>账号级关闭模块约束全部身份。此身份使用明确的发布版本；草稿编辑不改变已分配权限。</p>
          <label className="checkbox-field"><input type="checkbox" checked={draft.oaAssignedOnly || inherited && Boolean(scheme?.oaAssignedOnly)} disabled={draft.mode === "Collaboration" || inherited && Boolean(scheme?.oaAssignedOnly)} onChange={e => patch({ oaAssignedOnly: e.target.checked })} />OA 仅限本人及明确参与事项</label>
          {directory.grantCeiling && <p>只显示上级允许下放的操作与数据范围。</p>}
          <PermissionModuleGrid resources={model.resources} grants={grants} dataScopes={model.recordScopes} actionDataScopes={model.actionDataScopes} disabled={operation.busy} onChange={changeGrants} disabledModules={effectiveModules} inheritedModules={inheritedModules} readOnlyActions={inherited} onModulesChange={disabledModules => patch({ disabledModules: disabledModules.filter(key => !inheritedModules.includes(key)) })} />
        </div>
      </fieldset><OfficeSubmit busy={operation.busy||(inherited&&!versionDefinition.data)} label="保存单位授权" />
    </form>
    {record&&<ScopeRestrictionsPanel client={client} principalKind="Unit" principalId={record.id} company={company} resources={directory.resources} />}
  </OfficeDialog>;
}
