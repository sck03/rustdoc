import { useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OfficeHandlingCommand, OfficeHandlingService } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { OfficeDialog, OfficeField } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { generalCategories } from "./oaCategories.ts";

const categories = { ...generalCategories, Supply: "物品组", Room: "会议室" } as const;
export function HandlingServicesEditor({ client, user, accounts }: { client: ExportDocManagerApiClient; user: ApiUserDto; accounts: ApprovalAccount[] }) {
  const [keyword, setKeyword] = useState("");
  const [category, setCategory] = useState("");
  const [state, setState] = useState<"" | "active" | "inactive">("");
  const [cursors, setCursors] = useState([""]);
  const [editing, setEditing] = useState<OfficeHandlingService>();
  const query = useQuery({ queryKey: ["office", "handling-services", user.id, user.companyScope, "manage", keyword, category, state, cursors.at(-1)],
    queryFn: ({ signal }) => client.listManagedHandlingServices({ keyword, category, state, after: cursors.at(-1), pageSize: 25 }, { signal }) });
  const filter = (change: () => void) => { change(); setCursors([""]); };
  return <section aria-label="办理分工">
    <div className="office-form-grid">
      <OfficeField label="搜索事项"><input type="search" value={keyword} maxLength={100} onChange={e => filter(() => setKeyword(e.target.value))} onKeyDown={e => { if (e.key === "Enter") e.preventDefault(); }}/></OfficeField>
      <OfficeField label="类别"><select value={category} onChange={e => filter(() => setCategory(e.target.value))}><option value="">全部类别</option>{Object.entries(categories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></OfficeField>
      <OfficeField label="状态"><select value={state} onChange={e => filter(() => setState(e.target.value as typeof state))}><option value="">全部状态</option><option value="active">启用</option><option value="inactive">停用</option></select></OfficeField>
    </div>
    <div className="office-card-actions"><Button type="button" onClick={() => setEditing({ key: createRequestKey(), name: "", category: "Seal", revision: 0, handlerUserIds: [0], isActive: true })}>添加办理事项</Button><Button type="button" busy={query.isFetching} onClick={() => void query.refetch()}>刷新</Button></div>
    {query.isError && <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>}
    <ResponsiveTableFrame mobileLayout="cards"><table aria-label="办理事项目录"><thead><tr><th>事项</th><th>类别</th><th>办理人员</th><th>状态</th><th>操作</th></tr></thead><tbody>
      {query.data?.items.map(row => <tr key={row.key}><td data-label="事项">{row.name}</td><td data-label="类别">{categories[row.category]}</td><td data-label="办理人员">{row.handlerNames}</td><td data-label="状态">{row.isActive ? "启用" : "停用"}</td><td data-label="操作"><Button type="button" onClick={() => setEditing(row)}>编辑</Button></td></tr>)}
    </tbody></table></ResponsiveTableFrame>
    {!query.isPending && !query.data?.items.length && <p className="office-muted">没有匹配的办理事项。</p>}
    <div className="office-card-actions"><Button type="button" disabled={cursors.length === 1 || query.isFetching} onClick={() => setCursors(values => values.slice(0,-1))}>上一页</Button><span>第 {cursors.length} 页</span><Button type="button" disabled={!query.data?.nextCursor || query.isFetching} onClick={() => setCursors(values => [...values, query.data!.nextCursor])}>下一页</Button></div>
    {editing && <ServiceDialog client={client} initial={editing} accounts={accounts} onClose={() => setEditing(undefined)}/>}
  </section>;
}

function ServiceDialog({ client, initial, accounts, onClose }: { client: ExportDocManagerApiClient; initial: OfficeHandlingService; accounts: ApprovalAccount[]; onClose: () => void }) {
  const [draft, setDraft] = useState(initial);
  const [removing, setRemoving] = useState(false);
  const operation = useOfficeOperation([["office", "handling-services"], ["office", "approval-settings"]], false);
  const attempt = useRef<{ remove: boolean; body: OfficeHandlingCommand } | undefined>(undefined);
  const update = (change: Partial<OfficeHandlingService>) => { setDraft(value => ({ ...value, ...change })); attempt.current = undefined; setRemoving(false); };
  const save = (remove: boolean) => {
    if (!attempt.current || attempt.current.remove !== remove) attempt.current = { remove, body: { service: draft, commandKey: createRequestKey() } };
    void operation.run(signal => remove ? client.removeHandlingService({ body: attempt.current!.body }, { signal }) : client.saveHandlingService({ body: attempt.current!.body }, { signal }), onClose);
  };
  return <OfficeDialog title={initial.revision ? "编辑办理事项" : "添加办理事项"} busy={operation.busy} error={operation.error} onClose={onClose} protectChanges hasChanges={JSON.stringify(draft)!==JSON.stringify(initial)}>
    <fieldset disabled={operation.busy} className="office-form-grid"><legend>事项与人员</legend>
      <OfficeField label="类别"><select value={draft.category} onChange={e => update({ category: e.target.value as OfficeHandlingService["category"] })}>{Object.entries(categories).map(([key,label]) => <option key={key} value={key}>{label}</option>)}</select></OfficeField>
      <OfficeField label="事项名称"><input required maxLength={100} value={draft.name} onChange={e => update({ name: e.target.value })}/></OfficeField>
      <div className="office-field-wide">{draft.handlerUserIds.map((id,index) => <div className="office-card-actions" key={index}>
        <OfficeField label={`办理人员 ${index+1}`}><select required value={id || ""} onChange={e => update({ handlerUserIds: draft.handlerUserIds.map((value,i) => i === index ? Number(e.target.value) : value) })}><option value="">请选择账号</option>{id > 0 && !accounts.some(a => a.id === id) && <option value={id}>原人员不可用（{id}）</option>}{accounts.map(account => <option key={account.id} value={account.id} disabled={account.id !== id && draft.handlerUserIds.includes(account.id)}>{account.fullName} · {account.username}</option>)}</select></OfficeField>
        <Button type="button" disabled={draft.handlerUserIds.length === 1} onClick={() => update({ handlerUserIds: draft.handlerUserIds.filter((_,i) => i !== index) })}>移除人员</Button>
      </div>)}<Button type="button" disabled={draft.handlerUserIds.length >= 10} onClick={() => update({ handlerUserIds: [...draft.handlerUserIds,0] })}>添加办理人员</Button></div>
      <label className="checkbox-field"><input type="checkbox" checked={draft.isActive} onChange={e => update({ isActive: e.target.checked })}/>允许新申请选用</label>
    </fieldset>
    <p className="office-muted">停用后原申请继续办理。更换人员将交接该事项的未结业务。</p>
    <div className="office-card-actions"><Button type="button" variant="primary" busy={operation.busy} disabled={!draft.name.trim() || draft.handlerUserIds.some(id => !id)} onClick={() => save(false)}>保存事项</Button>{!!initial.revision && <Button type="button" disabled={operation.busy} onClick={() => setRemoving(true)}>删除未使用事项</Button>}</div>
    {removing && <InlineNotice tone="warning" action={<Button type="button" disabled={operation.busy} onClick={() => save(true)}>确认删除</Button>}>确认删除“{initial.name}”？已有业务引用的事项只能停用。</InlineNotice>}
  </OfficeDialog>;
}
