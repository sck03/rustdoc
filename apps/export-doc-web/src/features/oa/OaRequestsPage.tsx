import { useState } from "react";
import { Link } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeQueryState } from "../office/OfficeUi.tsx";
import { OaRequestDialog } from "./OaRequestDialog.tsx";
import { OaRequestDetails } from "./OaRequestDetails.tsx";
import { oaAccess, oaModules, oaStatus, type OaKind } from "./oaModel.ts";
import { useOaRequests } from "./useOaRequests.ts";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function OaRequestsPage({ client, user, kind }: { client: ExportDocManagerApiClient; user: ApiUserDto; kind: OaKind }) {
  const model = useOaRequests(client, user, kind);
  const payments = useModulePermission("document.payments");
  const [editing, setEditing] = useState<OaRequest | "new" | null>(null);
  if (!oaAccess(user, kind, "view")) return <PageState tone="permission" title="没有此申请模块的查看权限" />;
  const module = oaModules[kind];
  return <section className="work-surface office-workspace oa-workspace" aria-label={module.name}>
    <header className="oa-heading"><h2>{model.selected ? "申请详情" : "申请列表"}</h2><Link to="/office/approvals">申请与审批中心</Link></header>
    {kind === "expense" && <InlineNotice tone="info" action={payments.canView ? <Link to="/payments">填写付款或报销打印单</Link> : undefined}>
      此处办理报销申请、审批和财务接收；付款报销打印单在工作台单独填写，打印不改变申请状态。
    </InlineNotice>}
    {model.selected ? <>
      <button type="button" className="command-button secondary" onClick={() => model.select(0)}>返回申请列表</button>
      {model.detail.isPending ? <PageState tone="loading" title="正在读取申请" /> : model.detail.isError ? <PageState tone="error" title="申请读取失败" description={readApiError(model.detail.error)} action={<button className="command-button" type="button" onClick={() => void model.detail.refetch()}>重新读取</button>} />
        : model.detail.data && <OaRequestDetails key={model.detail.data.id} client={client} user={user} row={model.detail.data} onEdit={() => setEditing(model.detail.data!)} />}
    </> : <>
      {(model.canReceive || model.canApprove) && <nav className="office-tabs" aria-label="申请工作区"><button type="button" aria-pressed={!model.financeOnly && !model.approvalsOnly} onClick={() => model.changeView("requests")}>申请记录</button>{model.canApprove && <button type="button" aria-pressed={model.approvalsOnly} onClick={() => model.changeView("approvals")}>待我审批</button>}{model.canReceive && <button type="button" aria-pressed={model.financeOnly} onClick={() => model.changeView("finance")}>财务接收</button>}</nav>}
      {model.financeOnly && <p className="office-muted">核对已批准报销单的明细、凭证和审批记录，确认后登记接收；付款由独立财务软件办理。</p>}
      <div className="office-toolbar oa-toolbar">
        {!model.financeOnly && !model.approvalsOnly && <label className="checkbox-field"><input type="checkbox" checked={model.mineOnly} onChange={(event) => model.changeMine(event.target.checked)} />仅本人提交</label>}
        <label className="office-field"><span>状态</span><select value={model.status} onChange={(event) => model.changeStatus(event.target.value)}>{!model.financeOnly && !model.approvalsOnly && <option value="">全部状态</option>}{Object.entries(oaStatus).filter(([key]) => model.approvalsOnly ? key === "Pending" : model.financeOnly ? ["Approved", "HandedOff"].includes(key) : kind === "expense" ? key !== "Completed" : key !== "HandedOff").map(([key, label]) => <option key={key} value={key}>{model.financeOnly && key === "Approved" ? "待财务接收" : label}</option>)}</select></label>
        <button type="button" className="command-button secondary" disabled={model.query.isFetching} onClick={() => void model.query.refetch()}>刷新</button>
        {!model.financeOnly && oaAccess(user, kind, "create") && <button type="button" className="command-button" onClick={() => setEditing("new")}>新建{module.name}</button>}
      </div>
      <OfficeQueryState query={model.query} emptyTitle="当前筛选下没有申请" />
      <div className="office-resource-grid">{model.query.data?.items.map((row) => <article key={row.id} className="office-resource-card"><div className="office-card-heading"><h2><button className="oa-title-button" type="button" onClick={() => model.select(row.id)}>{row.title}</button></h2><span className="office-badge" data-state={row.status}>{oaStatus[row.status]}</span></div><p>{row.employeeName} · {row.departmentId}</p><p className="office-muted">#{row.id} {row.totalAmount && ` · ${row.currency} ${row.totalAmount}`}{row.durationDays && ` · ${row.durationDays} 天`}{row.durationHours && ` · ${row.durationHours} 小时`}</p></article>)}</div>
      <div className="office-card-actions"><button className="command-button secondary" type="button" disabled={model.page <= 1 || model.query.isFetching} onClick={() => model.setPage(model.page - 1)}>上一页</button><span>第 {model.page} 页 · 共 {model.query.data?.totalCount ?? 0} 条</span><button className="command-button secondary" type="button" disabled={model.query.isFetching || model.page * 20 >= (model.query.data?.totalCount ?? 0)} onClick={() => model.setPage(model.page + 1)}>下一页</button></div>
    </>}
    {editing && <OaRequestDialog client={client} user={user} kind={kind} record={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} onSaved={(row) => { setEditing(null); model.select(row.id); }} />}
  </section>;
}
