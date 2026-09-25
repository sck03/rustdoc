import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { oaApi } from "./oaApi.ts";
import { oaAccess, oaActions, oaActionLabel, oaStatus, type OaActionName } from "./oaModel.ts";
import { expenseCategories } from "./OaLineEditor.tsx";
import { generalCategories, leaveCategories } from "./OaTemporalFields.tsx";

export function OaRequestDetails({ client, user, row, onEdit }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest; onEdit: () => void }) {
  const operation = useOfficeOperation();
  const [action, setAction] = useState<OaActionName | number | null>(null);
  const editable = ["Draft", "Rejected"].includes(row.status) && oaAccess(user, row.kind, "edit", row);
  const api = oaApi(client, row.kind);
  return <article className="oa-detail">
    <div className="office-card-heading"><h2>{row.title}</h2><span className="office-badge" data-state={row.status}>{oaStatus[row.status]}</span></div>
    <p className="office-muted">#{row.id} · {row.employeeName} · {row.departmentId} · 更新于 {formatBusinessDateTime(row.updatedAt, user.businessTimeZone)}</p>
    {(editable || oaActions(row, user).length > 0) && <div className="office-card-actions">
      {editable && <button type="button" className="command-button secondary" onClick={onEdit}>编辑草稿</button>}
      {oaActions(row, user).map((action) => <button type="button" className="command-button" key={action} disabled={operation.busy} onClick={() => setAction(action)}>{oaActionLabel(action, row.kind, user.capabilities.usesOfficeRegister)}</button>)}
    </div>}
    {row.status === "HandedOff" && <InlineNotice tone="success" title="已移交独立财务软件">此状态仅记录资料移交，不代表已记账或已付款。</InlineNotice>}
    <p className="oa-reason">{row.reason}</p>
    {row.leave && <p>{leaveCategories[row.leave.category]} · {row.leave.startsOn} {row.leave.startPeriod === "AM" ? "上午" : "下午"} 至 {row.leave.endsOn} {row.leave.endPeriod === "AM" ? "上午" : "下午"} · {row.durationDays} 个自然日</p>}
    {row.travel && <p>{row.travel.destination} · {row.travel.startsOn} 至 {row.travel.endsOn} · {row.durationDays} 天</p>}
    {row.overtime && <p>{row.overtime.location} · {formatBusinessDateTime(row.overtime.startsAt, user.businessTimeZone)} 至 {formatBusinessDateTime(row.overtime.endsAt, user.businessTimeZone)} · {row.durationHours} 小时</p>}
    {row.category && <p>申请类别：{generalCategories[row.category]}</p>}
    {row.totalAmount && <p><strong>{row.kind === "purchase" ? "预算合计" : "报销合计"}：{row.currency} {row.totalAmount}</strong></p>}
    {row.lines && <ol className="oa-lines">{row.lines.map((line, index) => <li key={`${row.id}-${index}`} className="oa-line">{expenseCategories[line.category]} · {line.spentOn} · {line.description} · {line.amount} {row.currency}</li>)}</ol>}
    {row.purchaseLines && <ol className="oa-lines">{row.purchaseLines.map((line, index) => <li key={`${row.id}-${index}`} className="oa-line">{line.name} {line.specification} · {line.quantity} {line.unit} × {line.unitPrice} {row.currency}</li>)}</ol>}
    <section className="oa-attachments" aria-label="申请附件"><h3>{row.kind === "expense" ? "报销凭证" : "申请附件"}</h3>
      <p className="office-muted">支持 PDF、PNG、JPEG；每个 10 MiB，最多 20 个、合计 50 MiB。{row.kind === "expense" && "提交报销前至少上传一份凭证。"}</p>
      {editable && <OfficeField label="上传附件"><input type="file" accept=".pdf,.png,.jpg,.jpeg" disabled={operation.busy} onChange={(event) => {
        const file = event.target.files?.[0]; event.target.value = "";
        if (file) void operation.run((signal) => {
          if (file.size > 10 * 1024 * 1024) throw new Error("文件不能超过 10 MiB。");
          const form = new FormData(); form.set("file", file); form.set("expectedVersion", String(row.versionNumber));
          return api.upload(row.id, form, { signal });
        }, () => {});
      }} /></OfficeField>}
      {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
      {!row.attachments.length && <p className="office-muted">尚未上传附件。</p>}
      <ul>{row.attachments.map((file) => <li key={file.id} className="office-card-actions"><span>{file.fileName} · {Math.ceil(file.sizeBytes / 1024)} KiB</span>
        <button className="command-button secondary" type="button" disabled={operation.busy} onClick={() => void operation.run((signal) => api.download(row.id, file.id, { signal }), (blob) => downloadBlob(blob, file.fileName))}>下载</button>
        {editable && <button className="command-button secondary" type="button" disabled={operation.busy} onClick={() => setAction(file.id)}>移除</button>}
      </li>)}</ul>
    </section>
    <details><summary>审批与办理记录</summary><OaHistory client={client} user={user} row={row} /></details>
    {action !== null && <OaActionDialog client={client} user={user} row={row} action={action} onClose={() => setAction(null)} />}
  </article>;
}

function OaActionDialog({ client, user, row, action, onClose }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest; action: OaActionName | number; onClose: () => void }) {
  const operation = useOfficeOperation();
  const [note, setNote] = useState("");
  const label = typeof action === "number" ? "移除附件" : oaActionLabel(action, row.kind, user.capabilities.usesOfficeRegister);
  return <OfficeDialog title={label} onClose={onClose} {...operation} protectChanges>
    <p>{row.title} · {row.employeeName}</p>
    {action === "complete" && <p className="office-muted">{row.kind === "expense" ? "请记录移交对象、日期或财务软件的接收编号。此处不执行付款。" : "请如实记录办理结果；采购请注明验收情况，通用申请请注明交付内容。"}</p>}
    <form onSubmit={(event) => { event.preventDefault(); const api = oaApi(client, row.kind); const body = { expectedVersion: row.versionNumber, note };
      void operation.run((signal) => typeof action === "number" ? api.remove(row.id, action, body, { signal }) : api.action(action, row.id, body, { signal }), onClose);
    }}><OfficeField label="处理说明"><textarea required maxLength={500} rows={4} value={note} disabled={operation.busy} onChange={(event) => setNote(event.target.value)} /></OfficeField><OfficeSubmit busy={operation.busy} label={label} /></form>
  </OfficeDialog>;
}

function OaHistory({ client, user, row }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: OaRequest }) {
  const [page, setPage] = useState(1);
  const query = useQuery({ queryKey: ["office", "oa", row.kind, user.id, user.companyScope, row.id, "history", page, row.versionNumber],
    queryFn: ({ signal }) => oaApi(client, row.kind).history(row.id, page, { signal }) });
  const labels: Record<string, string> = { create: "创建草稿", update: "修改草稿", upload: "上传附件", "delete-attachment": "移除附件", submit: "提交审批", withdraw: "撤回修改", approve: "批准", reject: "驳回", cancel: "取消", void: "作废批准", complete: "完成登记" };
  if (query.isError) return <InlineNotice tone="error">{readApiError(query.error)}</InlineNotice>;
  return <><ol className="oa-history">{query.data?.items.map((item) => <li key={item.id}><strong>{labels[item.action] ?? item.action}</strong> · {item.actorName} · {formatBusinessDateTime(item.occurredAt, user.businessTimeZone)}<p>{item.note || "无附加说明"}</p></li>)}</ol>
    <div className="office-card-actions"><button type="button" className="command-button secondary" disabled={page === 1 || query.isFetching} onClick={() => setPage(page - 1)}>上一页记录</button><span>第 {page} 页 · {query.data?.totalCount ?? 0} 条</span><button type="button" className="command-button secondary" disabled={query.isFetching || page * 20 >= (query.data?.totalCount ?? 0)} onClick={() => setPage(page + 1)}>下一页记录</button></div></>;
}
