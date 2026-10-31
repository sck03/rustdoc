import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useOrganizationOperation } from "./useOrganizationDirectory.ts";
import { foundationDraft, foundationLabels, listFoundation, saveFoundation, type FoundationDraft, type FoundationKind, type FoundationRecord } from "./foundationModel.ts";
import { PositionFields, ProjectFields, ResponsibilityFields } from "./FoundationFields.tsx";

export function PersonnelFoundationPanel({ client, user, unitId }: { client: ExportDocManagerApiClient; user: ApiUserDto; unitId: string }) {
  const [opened, setOpened] = useState(false);
  const [kind, setKind] = useState<FoundationKind>("positions");
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<{ item?: FoundationRecord } | null>(null);
  const canReadEmployment = user.capabilities.canManageUsers || user.capabilities.permissions?.some(grant => grant.resourceKey === "office.people" && grant.action === "view-details");
  const query = useQuery({ queryKey: ["personnel-foundation", unitId, kind, page], enabled: opened, queryFn: ({ signal }) => listFoundation(client, kind, unitId, page, signal) });
  const kinds = (Object.keys(foundationLabels) as FoundationKind[]).filter(kind => kind !== "employments" || canReadEmployment);
  return <details onToggle={event => setOpened(event.currentTarget.open)}><summary>岗位、雇佣与审批职责</summary><div className="office-card-actions" role="group" aria-label="人员基础资料类别">{kinds.map(value => <button type="button" key={value} className="command-button secondary" aria-pressed={kind === value} onClick={() => { setKind(value); setPage(1); }}>{foundationLabels[value]}</button>)}{kind !== "employments" && <button type="button" className="command-button" onClick={() => setEditing({})}>新增{foundationLabels[kind]}</button>}</div>
    {opened && (query.isPending ? <PageState tone="loading" title="正在加载人员基础资料" /> : query.isError ? <PageState tone="error" title="人员基础资料加载失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} /> : <>{!query.data.items.length && <p>尚无{foundationLabels[kind]}记录。</p>}{query.data.items.map(item => <article key={item.id} className="office-resource-card"><strong>{"name" in item ? item.name : `工号 ${item.employeeNumber}`}</strong><p>{"code" in item ? item.code : `${item.period.startsOn} — ${item.period.endsOn ?? "持续有效"}`}</p>{kind !== "employments" && <button type="button" className="text-button" onClick={() => setEditing({ item })}>编辑{foundationLabels[kind]}</button>}</article>)}{query.data.totalCount > 20 && <div className="office-card-actions"><button type="button" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>上一页</button><span>第 {page} 页 · 共 {query.data.totalCount} 条</span><button type="button" disabled={page * 20 >= query.data.totalCount || query.isFetching} onClick={() => setPage(page + 1)}>下一页</button></div>}</>)}
    {editing && kind !== "employments" && <FoundationEditor client={client} kind={kind} unitId={unitId} item={editing.item} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void query.refetch(); }} />}
  </details>;
}
function FoundationEditor({ client, kind, unitId, item, onClose, onSaved }: { client: ExportDocManagerApiClient; kind: Exclude<FoundationKind, "employments">; unitId: string; item?: FoundationRecord; onClose: () => void; onSaved: () => void }) {
  const [initial] = useState(() => foundationDraft(kind, unitId, item));
  const [draft, setDraft] = useState(initial);
  const [keyword, setKeyword] = useState("");
  const options = useQuery({ queryKey: ["personnel-foundation", unitId, "candidates", keyword], queryFn: ({ signal }) => client.getIdentityCandidates({ unitId, keyword, pageSize: 100 }, { signal }) });
  const operation = useOrganizationOperation();
  const patch = (update: Partial<FoundationDraft["body"]>) => setDraft(value => ({ ...value, body: { ...value.body, ...update } } as FoundationDraft));
  return <OfficeDialog title={`${item ? "编辑" : "新增"}${foundationLabels[kind]}`} onClose={onClose} {...operation} protectChanges hasChanges={JSON.stringify(draft) !== JSON.stringify(initial)}>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => saveFoundation(client, draft, signal), onSaved); }} onInvalidCapture={event => { const details = (event.target as HTMLElement).closest("details"); if (details) details.open = true; }}>
      <fieldset disabled={operation.busy} className="office-form-grid">{"name" in draft.body && <><OfficeField label="名称"><input required maxLength={120} value={draft.body.name} onChange={event => patch({ name: event.target.value })} /></OfficeField><OfficeField label="代码"><input required maxLength={120} value={draft.body.code} onChange={event => patch({ code: event.target.value })} /></OfficeField></>}
        <OfficeField label="查找人员或工号"><input type="search" maxLength={100} value={keyword} onChange={event => setKeyword(event.target.value)} /></OfficeField>
        {options.isPending ? <PageState tone="loading" title="正在加载任职候选" /> : options.isError ? <PageState tone="error" title="任职候选加载失败" description={readApiError(options.error)} action={<button type="button" onClick={() => void options.refetch()}>重试</button>} /> : <>{draft.kind === "positions" && <PositionFields value={draft.body} options={options.data} patch={patch} />}{draft.kind === "responsibilities" && <ResponsibilityFields value={draft.body} options={options.data} patch={patch} />}{draft.kind === "projects" && <ProjectFields value={draft.body} options={options.data} patch={patch} />}{options.data.totalCount > options.data.items.length && <p role="status">请按姓名或工号检索所需人员。</p>}</>}
        {"isActive" in draft.body && <label className="checkbox-field"><input type="checkbox" checked={draft.body.isActive} onChange={event => patch({ isActive: event.target.checked })} />启用</label>}<OfficeField label="维护原因"><textarea required maxLength={500} value={draft.body.note} onChange={event => patch({ note: event.target.value })} /></OfficeField>
      </fieldset><OfficeSubmit busy={operation.busy || !options.data} label={`保存${foundationLabels[kind]}`} />
    </form>
  </OfficeDialog>;
}
