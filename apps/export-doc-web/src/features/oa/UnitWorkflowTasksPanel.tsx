import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, UnitWorkflowTask, UnitTaskReassignment } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { PageState } from "../../ui/PageState.tsx";
import type { ApprovalAccount } from "./useApprovalSettings.ts";

export function UnitWorkflowTasksPanel({ client, user, accounts }: { client: ExportDocManagerApiClient; user: ApiUserDto; accounts: ApprovalAccount[] }) {
  const [page, setPage] = useState(1);
  const [assignee, setAssignee] = useState(0);
  const [editing, setEditing] = useState<UnitWorkflowTask | null>(null);
  const query = useQuery({ queryKey: ["unit-workflow-tasks", user.contextId, page, assignee], queryFn: ({ signal }) => client.getUnitWorkflowTasks({ pageNumber: page, pageSize: 20, assigneeId: assignee || undefined }, { signal }) });
  return <details><summary>在途任务与职责交接</summary><p>包含待激活的后续任务。交接只改变未签署任务，保留原流程版本、申请资料与历史签署。</p>
    <OfficeField label="按责任人查看"><select value={assignee} onChange={e => { setAssignee(Number(e.target.value)); setPage(1); }}><option value={0}>全部责任人</option>{accounts.map(a => <option key={a.id} value={a.id}>{a.fullName}</option>)}</select></OfficeField>
    {query.isError ? <PageState tone="error" title="在途任务读取失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} /> : query.isPending ? <PageState tone="loading" title="正在读取在途任务" /> : <>
      {!query.data.items.length && <p>当前没有等待交接的在途任务。</p>}
      {query.data.items.map(task => <div className="office-resource-card" key={`${task.requestId}:${task.taskKey}`}><strong>事项 #{task.requestId} · {task.nodeKey}</strong><p>{task.assigneeName} · {task.active ? "当前待办" : "等待前序节点"}{task.dueAt && ` · 到期 ${formatBusinessDateTime(task.dueAt, user.businessTimeZone)}`}</p>{task.identityAvailable === false && <p role="status">签署身份已失效，请安排移交或重新审核授权。</p>}{task.authorizationEndsAt && <p>身份到期：{formatBusinessDateTime(task.authorizationEndsAt, user.businessTimeZone)}</p>}<button type="button" className="command-button secondary" onClick={() => setEditing(task)}>移交任务</button></div>)}
      <div className="office-card-actions"><button type="button" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>上一页</button><span>第 {page} 页 · 共 {query.data.totalCount} 项</span><button type="button" disabled={page * 20 >= query.data.totalCount || query.isFetching} onClick={() => setPage(page + 1)}>下一页</button></div>
    </>}
    {editing && <Handover client={client} task={editing} accounts={accounts} onClose={() => setEditing(null)} onSaved={() => setEditing(null)} />}
  </details>;
}

function Handover({ client, task, accounts, onClose, onSaved }: { client: ExportDocManagerApiClient; task: UnitWorkflowTask; accounts: ApprovalAccount[]; onClose: () => void; onSaved: () => void }) {
  const [toUserId, setToUserId] = useState(0);
  const [toContextId, setToContextId] = useState("");
  const contexts = accounts.find(a => a.id === toUserId)?.contexts ?? [];
  const [note, setNote] = useState("");
  const operation = useOfficeOperation([["unit-workflow-tasks"], ["unit-workflow-summary"]]);
  return <OfficeDialog title={`移交事项 #${task.requestId} 的任务`} onClose={onClose} {...operation} protectChanges hasChanges={toUserId > 0 || note !== ""}>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.reassignUnitWorkflowTask({ body: { requestId: task.requestId, requestKind: task.requestKind as UnitTaskReassignment["requestKind"], expectedVersion: task.versionNumber, taskKey: task.taskKey, fromUserId: task.assigneeId, toUserId, toContextId, note } }, { signal }), onSaved); }}>
      <fieldset disabled={operation.busy}><p>原责任人：{task.assigneeName}</p><OfficeField label="接任人"><select required value={toUserId || ""} onChange={e => { const id = Number(e.target.value); setToUserId(id); setToContextId(accounts.find(a => a.id === id)?.contexts?.[0]?.id ?? ""); }}><option value="">选择接任人</option>{accounts.map(a => <option key={a.id} value={a.id}>{a.fullName} · {a.username}</option>)}</select></OfficeField><OfficeField label="接任身份"><select value={toContextId} onChange={e => setToContextId(e.target.value)}>{contexts.map(c => <option key={c.id} value={c.id}>{c.name} · {c.departmentId}</option>)}</select></OfficeField><OfficeField label="移交原因"><textarea required maxLength={500} value={note} onChange={e => setNote(e.target.value)} /></OfficeField></fieldset><OfficeSubmit busy={operation.busy} label="确认移交" />
    </form>
  </OfficeDialog>;
}
