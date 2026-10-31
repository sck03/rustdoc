import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiPermissionTemplateDto, ExportDocManagerApiClient, UnitAccessDirectory, UnitPermissionTemplateSave } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "./useOrganizationDirectory.ts";
import { PermissionModuleGrid } from "../settings/PermissionModuleGrid.tsx";
import { grantKey } from "../settings/permissionSchemeModel.ts";
import { unitPermissionEditor } from "../settings/unitPermissionModel.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { PermissionPublicationActions } from "../settings/PermissionPublicationActions.tsx";

export function UnitPermissionSchemes({ client, company, directory }: { client: ExportDocManagerApiClient; company: string; directory: UnitAccessDirectory }) {
  const [editing, setEditing] = useState<{ item?: ApiPermissionTemplateDto } | null>(null);
  const [opened, setOpened] = useState(false);
  const query = useQuery({ queryKey: ["unit-schemes", company], enabled: opened, queryFn: ({ signal }) => client.getUnitPermissionTemplates({ companyCode: company }, { signal }) });
  return <details onToggle={event => setOpened(event.currentTarget.open)}><summary>本单位权限方案</summary><p>本单位身份可共用方案；平台方案在账号与权限中统一维护。保存时检查授权上限及未结职责。</p>
    {query.isError && <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>}
    <button type="button" className="command-button" disabled={!query.data} onClick={() => setEditing({})}>新增单位方案</button>
    {query.data?.items.map(item => <div className="office-resource-card" key={item.id}><strong>{item.name}</strong><p>{item.isActive ? "启用" : "停用"} · {item.unitRoleReferenceCount ?? 0} 个单位身份引用</p><button type="button" className="text-button" onClick={() => setEditing({ item })}>编辑方案</button><PermissionPublicationActions client={client} scheme={item} disabled={Boolean(editing)} unitTargets={directory.items.filter(role=>role.mode!=="Administration"&&role.permissionTemplateId===item.id&&role.permissionGrants==null).map(role=>({kind:"Unit",id:String(role.id),expectedVersion:role.versionNumber,label:role.name}))} onPublished={()=>void query.refetch()} /></div>)}
    {editing && query.data && <SchemeEditor client={client} unitId={query.data.unitId} directory={directory} item={editing.item} onClose={() => setEditing(null)} onSaved={() => setEditing(null)} />}
  </details>;
}

function SchemeEditor({ client, unitId, directory, item, onClose, onSaved }: { client: ExportDocManagerApiClient; unitId: number; directory: UnitAccessDirectory; item?: ApiPermissionTemplateDto; onClose: () => void; onSaved: () => void }) {
  const [initial] = useState<UnitPermissionTemplateSave>(() => ({ unitId, id: item?.id ?? 0, code: item?.code ?? `unit-${createRequestKey()}`, name: item?.name ?? "", description: item?.description ?? "", isActive: item?.isActive ?? true, grants: item?.grants ?? [], disabledModules: item?.disabledModules ?? [], oaAssignedOnly: item?.oaAssignedOnly ?? false, expectedVersion: item?.versionNumber ?? 0 }));
  const [draft, setDraft] = useState(initial);
  const operation = useOrganizationOperation();
  const patch = (value: Partial<UnitPermissionTemplateSave>) => setDraft(current => ({ ...current, ...value }));
  const model = unitPermissionEditor(directory.resources,directory.grantCeiling);
  return <OfficeDialog title={item ? "编辑单位方案" : "新增单位方案"} onClose={onClose} {...operation} protectChanges hasChanges={JSON.stringify(draft) !== JSON.stringify(initial)}>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.saveUnitPermissionTemplate({ body: draft }, { signal }), onSaved); }}>
      <fieldset disabled={operation.busy} className="office-form-grid">
        <OfficeField label="方案名称"><input required maxLength={120} value={draft.name} onChange={e => patch({ name: e.target.value })} /></OfficeField>
        <OfficeField label="适用职责说明"><input maxLength={1000} value={draft.description ?? ""} onChange={e => patch({ description: e.target.value })} /></OfficeField>
        <label className="checkbox-field"><input type="checkbox" checked={draft.isActive} onChange={e => patch({ isActive: e.target.checked })} />启用方案</label>
        <label className="checkbox-field"><input type="checkbox" checked={draft.oaAssignedOnly ?? false} onChange={e => patch({ oaAssignedOnly: e.target.checked })} />仅查看本人及明确参与的申请</label>
        <div className="office-field-wide"><PermissionModuleGrid resources={model.resources} grants={Object.fromEntries(draft.grants.map(g => [grantKey(g.resourceKey,g.action),g.dataScope]))} dataScopes={model.recordScopes} actionDataScopes={model.actionDataScopes} disabled={operation.busy} onChange={value => patch({ grants: model.grants(value) })} disabledModules={draft.disabledModules ?? []} onModulesChange={disabledModules => patch({ disabledModules })} /></div>
      </fieldset><p>修改会同时影响引用此方案的身份，并使相关账号的现有会话失效。无法满足未结职责时会拒绝保存并提示交接。</p><OfficeSubmit busy={operation.busy} label="保存单位方案" />
    </form>
  </OfficeDialog>;
}
