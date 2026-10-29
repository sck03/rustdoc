import { useState, type FormEvent } from "react";
import { RefreshCw } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { isMeetingBooking, officeActionLabels, officeRequestActions, officeStatus, officeStatusLabels,
  type OfficeAction, type OfficeKind, type OfficeRequestRow } from "./officeModel.ts";
import { applyOfficeAction, useOfficeOperation, useOfficeRequests } from "./useOfficeData.ts";
import { OfficeDialog, OfficeField, OfficePager, OfficeQueryState, OfficeSubmit } from "./OfficeUi.tsx";
import { OfficeHistoryDialog } from "./OfficeHistoryDialog.tsx";
import { OaApprovalSteps } from "../oa/OaApprovalSteps.tsx";
import { requestPersonLabel } from "./requestIdentity.ts";

export function OfficeRequestsPanel({ client, user, kind }: { client: ExportDocManagerApiClient; user: ApiUserDto; kind: OfficeKind }) {
  const model = useOfficeRequests(client, user, kind);
  const [selected, setSelected] = useState<{ row: OfficeRequestRow; action: OfficeAction; label: string } | null>(null);
  const [history, setHistory] = useState<OfficeRequestRow | null>(null);
  const statuses = kind === "rooms" ? ["Pending", "Approved", "InUse", "Completed", "Rejected", "Cancelled"] : ["Pending", "Approved", "Issued", "Returned", "Rejected", "Cancelled"];
  return <>
    {model.focused && <div className="office-toolbar"><p>正在查看{model.focus.requestId ? `申请 #${model.focus.requestId}` : "所选人员的申请记录"}</p>
      <button type="button" className="command-button secondary" onClick={model.clearFocus}>显示全部记录</button></div>}
    <div className="office-toolbar">
      {model.access.allows("issue") && <label className="checkbox-field"><input type="checkbox" checked={model.handlingOnly} onChange={event => model.changeHandling(event.target.checked)} />我负责的交接与归还</label>}
      <label className="office-filter">申请状态<select value={model.status} onChange={(event) => model.changeStatus(event.target.value)}><option value="">全部状态</option>
        {statuses.map((status) => <option key={status} value={status}>{status === "Approved" && kind === "rooms" ? "待使用／领钥匙" : officeStatusLabels[status]}</option>)}</select></label>
      {model.access.canSeeOthers && <label className="checkbox-field"><input type="checkbox" checked={model.mineOnly} onChange={(event) => model.changeMineOnly(event.target.checked)} />仅我的申请</label>}
      <button type="button" className="icon-button" aria-label="刷新申请记录" disabled={model.query.isFetching} onClick={() => void model.query.refetch()}><RefreshCw size={17} aria-hidden="true" /></button>
    </div>
    <p className="office-muted">提交内容已冻结。需要修改请取消后重新申请；取消后释放时段或预留库存，处理记录继续保留。</p>
    <OfficeQueryState query={model.query} emptyTitle="当前条件下没有申请记录" />
    {!model.query.isError && <div className="office-request-list">{model.query.data?.items.map((row) => <article key={row.id} className="office-request-card">
      <div className="office-card-heading"><h2>{isMeetingBooking(row) ? row.title : row.supplyName}</h2><span className="office-badge" data-state={row.status}>{officeStatus(row, user.businessDate)}</span></div>
      {isMeetingBooking(row) ? <>
        <p>{row.roomName} · {row.attendeeCount} 人</p>
        {row.handlingName && <p>办理分工：{row.handlingName} · 办理人员：{row.handlerNames}</p>}
        <p className="office-period"><time dateTime={row.startsAt}>{formatBusinessDateTime(row.startsAt, user.businessTimeZone)}</time><span>至</span><time dateTime={row.endsAt}>{formatBusinessDateTime(row.endsAt, user.businessTimeZone)}</time></p>
        {row.issuedAt && <p className="office-muted">交接：{formatBusinessDateTime(row.issuedAt, user.businessTimeZone)}{row.returnedAt ? ` · 归还：${formatBusinessDateTime(row.returnedAt, user.businessTimeZone)}` : ""}</p>}
      </> : <>
        <p>申请 {row.quantity} {row.unit}{row.isReturnable ? ` · 已归还 ${row.returnedQuantity} ${row.unit}` : " · 消耗领用，发放即完成，无需归还"}</p>
        <p>{row.purpose}</p>{row.returnDueDate && <p className="office-muted">预计归还：{row.returnDueDate}</p>}
        {row.handlingName && <p>物品组：{row.handlingName} · 保管人员：{row.handlerNames}</p>}
      </>}
      <p className="office-muted">申请人：{requestPersonLabel(row.applicantName, row)} · 提交于 {formatBusinessDateTime(row.createdAt, user.businessTimeZone)}</p>
      <details><summary>审批流程</summary><OaApprovalSteps plan={row.approvalPlan} user={user}/></details>
      <footer className="office-card-actions">{officeRequestActions(row, user, kind).map((entry) =>
        <button key={entry.action} type="button" className={entry.action === "approve" ? "command-button" : "command-button secondary"}
          onClick={() => setSelected({ row, ...entry })}>{entry.label}</button>)}
        <button className="command-button secondary" type="button" onClick={() => setHistory(row)}>处理记录</button>
      </footer>
    </article>)}</div>}
    <OfficePager page={model.query.data} paging={model.paging} busy={model.query.isFetching} />
    {selected && <OfficeActionDialog client={client} kind={kind} {...selected} onClose={() => setSelected(null)} />}
    {history && <OfficeHistoryDialog client={client} user={user} kind={kind} id={history.id} title="申请处理记录" onClose={() => setHistory(null)} />}
  </>;
}

function OfficeActionDialog({ client, kind, row, action, label, onClose }: {
  client: ExportDocManagerApiClient; kind: OfficeKind; row: OfficeRequestRow; action: OfficeAction; label: string; onClose: () => void;
}) {
  const operation = useOfficeOperation();
  const partialReturn = action === "return" && !isMeetingBooking(row);
  const needsReason = action === "reject" || action === "cancel";
  const description = action === "approve" ? (kind === "rooms" ? "全部审批通过后，申请人可按预约时间到场办理交接。" : "全部审批通过后将预留相应库存，实际发放时扣减在库数量。")
    : action === "issue" ? "请核对使用人身份，确认已当面完成实物交接。"
      : action === "return" ? (isMeetingBooking(row) && !row.requiresKey ? "请确认会议已结束、场地已交接，再登记结束使用。" : "请先确认已经收到归还的钥匙或物品，再登记归还。")
        : "请填写原因，申请人可在处理记录中查看。";
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    void operation.run((signal) => applyOfficeAction(client, kind, row, action, String(form.get("note") ?? ""),
      partialReturn ? Number(form.get("quantity")) : 0, signal, String(form.get("taskKey")??"")), onClose);
  }
  return <OfficeDialog title={label || officeActionLabels[action]} onClose={onClose} {...operation} protectChanges>
    <p><strong>{requestPersonLabel(row.applicantName, row)}</strong> · {isMeetingBooking(row) ? `${row.roomName} · ${row.title}` : `${row.supplyName} · ${row.quantity} ${row.unit}`}</p>
    <p className="office-muted">{description}</p>
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      {["approve","reject"].includes(action)&&<OfficeField label="当前审批任务"><select name="taskKey" required>{row.reviewTaskKeys?.map(key=>{const task=row.approvalPlan?.steps.find(s=>s.taskKey===key);return <option key={key} value={key}>{task?row.approvalPlan?.nodes[task.stage].name:key} · {task?.approverName}</option>;})}</select></OfficeField>}
      {partialReturn && !isMeetingBooking(row) && <OfficeField label={`本次归还数量（尚欠 ${row.quantity - row.returnedQuantity} ${row.unit}）`} wide>
        <input type="number" name="quantity" required min={1} max={row.quantity - row.returnedQuantity} defaultValue={row.quantity - row.returnedQuantity} /></OfficeField>}
      <OfficeField label={needsReason ? "处理原因（必填）" : "交接／处理备注"} wide><textarea name="note" rows={3} maxLength={500} required={needsReason} /></OfficeField>
    </fieldset><OfficeSubmit busy={operation.busy} label={label} /></form>
  </OfficeDialog>;
}
