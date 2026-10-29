import { OaRequestContent } from "./OaRequestContent.tsx";
import { useState } from "react";
import { Link } from "react-router-dom";
import { requestPersonLabel } from "../office/requestIdentity.ts";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOaOperation } from "./useOaOperation.ts";
import { OaApprovalDocument } from "./OaApprovalDocument.tsx";
import { OaSubmissionHistory } from "./OaSubmissionHistory.tsx";
import { oaApi } from "./oaApi.ts";
import { oaAccess, oaActions, oaActionLabel, oaStatus, type OaActionName } from "./oaModel.ts";
import { canViewPersonnelDetails } from "../office/personnelModel.ts";
import { documentDescription } from "../../ui/documentAttachments.ts";
import { AttachmentUploadPanel } from "../../ui/AttachmentUploadPanel.tsx";
import { ApprovalReassignDialog } from "./ApprovalReassignDialog.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { OaApprovalSteps } from "./OaApprovalSteps.tsx";
import { OaApprovalPreview } from "./OaApprovalPreview.tsx";

export function OaRequestDetails({ client, user, row, onEdit }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest; onEdit: () => void }) {
  const operation = useOaOperation(row.kind);
  const [reassigning, setReassigning] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [historyOpened, setHistoryOpened] = useState(false);
  const [purpose, setPurpose] = useState("Voucher");
  const [action, setAction] = useState<OaActionName | number | null>(null);
  const editable = ["Draft", "Returned"].includes(row.status) && oaAccess(user, row.kind, "edit", row);
  const api = oaApi(client, row.kind);
  return <article className="oa-detail">
    <header className="oa-detail-summary">
    <div className="office-card-heading"><h2>{row.title}</h2><span className="office-badge" data-state={row.status}>{oaStatus[row.status]}</span></div>
    <dl className="oa-facts"><div><dt>申请编号</dt><dd>#{row.id}</dd></div><div><dt>{row.kind === "personnel" ? "变动人员" : "申请人"}</dt><dd>{requestPersonLabel(row.employeeName, row)}</dd></div><div><dt>所属部门</dt><dd>{row.departmentId}</dd></div><div><dt>更新时间</dt><dd>{formatBusinessDateTime(row.updatedAt, user.businessTimeZone)}</dd></div></dl>
    {row.kind === "personnel" && row.status === "Completed" && <InlineNotice tone="success" title="人员变动已生效">最终审批已更新人员档案和关联账号。
      {canViewPersonnelDetails(user) && <Link to={`/office/people/${row.employeeId}`}>查看人员档案</Link>}</InlineNotice>}
    {(editable || oaActions(row, user).length > 0) && <div className="office-card-actions">
      {editable && <button type="button" className="command-button secondary" disabled={operation.busy} onClick={onEdit}>编辑草稿</button>}
      {editable && <button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => setPreviewing(true)}>预览审批流程</button>}
      {oaActions(row, user).map((action) => <button type="button" className={`command-button${["cancel", "withdraw", "reject", "void"].includes(action) ? " secondary" : ""}`} key={action} disabled={operation.busy} onClick={() => setAction(action)}>{oaActionLabel(action, row.kind)}</button>)}
    </div>}
    {row.status === "HandedOff" && <InlineNotice tone="success" title="财务交接已登记">此状态仅记录资料交接，不代表已记账或已付款；办理人、时间和说明见审批与办理记录。</InlineNotice>}
    </header>
    <OaApprovalDocument key={row.submissionId} client={client} user={user} row={row} />
    {user.capabilities.canManageUsers && row.status === "Pending" && Boolean(row.approvalPlan?.steps.some(step => step.status === "Waiting")) && <button type="button" className="command-button secondary" onClick={() => setReassigning(true)}>移交未完成审批</button>}
    <OaApprovalSteps plan={row.approvalPlan} notes={row.approvalRoutingNotes} user={user} />
    <OaRequestContent row={row} user={user} />
    <section className="oa-attachments" aria-label="申请附件"><h3>{row.kind === "expense" ? "报销凭证" : "申请附件"}</h3>
      <p className="office-muted">{documentDescription}{row.kind === "expense" && "提交报销前至少上传一份凭证。"}</p>
      {editable && <><label>本批附件用途<select value={purpose} onChange={event=>setPurpose(event.target.value)}><option value="ReimbursementForm">报销单</option><option value="Voucher">发票／其他凭证</option><option value="Supporting">补充资料</option></select></label><AttachmentUploadPanel key={`${row.kind}-${row.id}`} record={row} operation={operation} upload={(current, body, signal) => { body.set("purpose",purpose); return api.upload(current.id, body, { signal }); }} /></>}
      {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
      {!row.attachments.length && <p className="office-muted">尚未上传附件。</p>}
      <ul>{row.attachments.map((file) => <li key={file.id} className="office-card-actions"><span>{file.fileName} · {Math.ceil(file.sizeBytes / 1024)} KiB</span>
        <span>{{ReimbursementForm:"报销单",Voucher:"发票／凭证",Supporting:"补充资料"}[file.purpose??"Voucher"]}</span><button className="command-button secondary" type="button" disabled={operation.busy} onClick={() => void operation.run((signal) => api.download(row.id, file.id, { signal }), (blob) => downloadBlob(blob, file.fileName), false)}>下载</button>
        {editable && <button className="command-button secondary" type="button" disabled={operation.busy} onClick={() => setAction(file.id)}>移除</button>}
      </li>)}</ul>
    </section>
    <details className="oa-detail-history" onToggle={event => setHistoryOpened(event.currentTarget.open)}><summary>审批与办理记录、提交版本</summary>{historyOpened && <><OaHistory client={client} user={user} row={row} /><OaSubmissionHistory client={client} user={user} row={row} /></>}</details>
    {action !== null && <OaActionDialog client={client} user={user} row={row} action={action} onClose={() => setAction(null)} />}
    {reassigning && <ApprovalReassignDialog client={client} row={row} onClose={() => setReassigning(false)} />}
    {previewing && <OaApprovalPreview client={client} user={user} row={row} onClose={() => setPreviewing(false)} />}
  </article>;
}

function OaActionDialog({ client, row, action, onClose }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest; action: OaActionName | number; onClose: () => void }) {
  const operation = useOaOperation(row.kind);
  const [note, setNote] = useState("");
  const [taskKey,setTaskKey]=useState(row.reviewTaskKeys?.[0]??"");
  const label = typeof action === "number" ? "移除附件" : oaActionLabel(action, row.kind);
  return <OfficeDialog title={label} onClose={onClose} {...operation} protectChanges>
    <p>{row.title} · {requestPersonLabel(row.employeeName, row)}</p>
    {action === "complete" && <p className="office-muted">{row.kind === "expense" ? "请核对已批准的明细和凭证，填写接收说明或财务软件接收编号。系统保留办理人和时间；付款在独立财务软件中完成。" : "请如实记录办理结果；采购请注明验收情况，通用申请请注明交付内容。"}</p>}
    <form onSubmit={(event) => { event.preventDefault(); const api = oaApi(client, row.kind); const body = { expectedVersion: row.versionNumber, note, taskKey };
      void operation.run((signal) => typeof action === "number" ? api.remove(row.id, action, body, { signal }) : api.action(action, row.id, body, { signal }), onClose);
    }}>
      {typeof action==="string"&&["approve","reject","return"].includes(action)&&<OfficeField label="当前审批任务"><select required value={taskKey} onChange={e=>setTaskKey(e.target.value)}>{row.reviewTaskKeys?.map(key=>{const task=row.approvalPlan?.steps.find(s=>s.taskKey===key);return <option key={key} value={key}>{task?row.approvalPlan?.nodes[task.stage].name:key} · {task?.approverName}</option>;})}</select></OfficeField>}
      <OfficeField label="处理说明"><textarea required maxLength={500} rows={4} value={note} disabled={operation.busy} onChange={(event) => setNote(event.target.value)} /></OfficeField><OfficeSubmit busy={operation.busy} label={label} /></form>
  </OfficeDialog>;
}

function OaHistory({ client, user, row }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest }) {
  const [page, setPage] = useState(1);
  const query = useQuery({ queryKey: ["office", "oa", row.kind, user.id, user.companyScope, row.id, "history", page, row.versionNumber],
    queryFn: ({ signal }) => oaApi(client, row.kind).history(row.id, page, { signal }) });
  useServerPageNumber(query, page, setPage);
  const labels: Record<string, string> = { create: "创建草稿", update: "修改草稿", upload: "上传附件", "delete-attachment": "移除附件", submit: "提交审批", withdraw: "撤回修改", approve: "批准", "approve-step": "步骤批准", remind: "申请人催办", reject: "最终驳回", return: "退回修改", cancel: "取消", void: "作废批准", complete: "完成登记", reassign: "办理分工交接", "approval-reassign": "审批职责移交" };
  if (query.isError) return <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>;
  return <><ol className="oa-history">{query.data?.items.map((item) => <li key={item.id}><strong>{labels[item.action] ?? item.action}</strong> · {item.actorName} · {formatBusinessDateTime(item.occurredAt, user.businessTimeZone)}<p>{item.note || "无附加说明"}</p></li>)}</ol>
    <div className="office-card-actions"><button type="button" className="command-button secondary" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>上一页记录</button><span>第 {page} 页 · {query.data?.totalCount ?? 0} 条</span><button type="button" className="command-button secondary" disabled={query.isFetching || page * 20 >= (query.data?.totalCount ?? 0)} onClick={() => setPage(page + 1)}>下一页记录</button></div></>;
}
