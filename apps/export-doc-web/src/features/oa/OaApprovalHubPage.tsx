import { Link, useSearchParams } from "react-router-dom";
import { ArrowRight, CheckCheck, ClipboardList, FilePlus2, Settings2 } from "lucide-react";
import { requestPersonLabel } from "../office/requestIdentity.ts";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { useOaApprovalHub } from "./useOaApprovalHub.ts";
import { oaAccess, oaKinds, oaModules } from "./oaModel.ts";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function OaApprovalHubPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const queues = useOaApprovalHub(client, user);
  const [params, setParams] = useSearchParams();
  const view = params.get("view") === "start" ? "start" : params.get("view") === "handling" ? "handling" : "pending";
  const selected = queues.filter(queue => view === "handling" ? queue.handling || queue.finance : !queue.handling && !queue.finance);
  const canManage = user.capabilities.canManageUsers || user.capabilities.unitManagement?.workflows;
  if (!queues.length) return <PageState tone="permission" title="没有申请模块权限，请联系管理员分配权限方案" />;
  return <section className="work-surface office-workspace oa-workspace oa-hub" aria-label="申请与审批中心">
    <header className="oa-hub-header">
      <div><p className="oa-hub-eyebrow">{user.businessDate} · {user.fullName}</p><h2>申请与审批中心</h2><p className="office-muted">发起申请、跟踪进度，或继续处理分配给你的工作。</p></div>
      {canManage && <Link className="command-button secondary" to="/office/approval-settings"><Settings2 size={16} aria-hidden="true" />流程管理</Link>}
    </header>
    <nav className="office-tabs oa-hub-tabs" aria-label="审批中心工作区">
      {([{ key: "pending", label: "审批与进度", icon: ClipboardList }, { key: "handling", label: "待我办理", icon: CheckCheck }, { key: "start", label: "发起申请", icon: FilePlus2 }] as const).map(({ key, label, icon: Icon }) =>
        <button key={key} type="button" aria-pressed={view === key} onClick={() => setParams({ view: key })}><Icon size={17} aria-hidden="true" />{label}</button>)}
    </nav>
    {view === "start" ? <div className="oa-hub-start-grid">
      {oaKinds.filter(kind => oaAccess(user, kind, "view")).map(kind => <article className="oa-hub-start-card" key={kind}>
        <ClipboardList size={22} aria-hidden="true" /><h3>{oaModules[kind].name}</h3><p className="office-muted">{oaModules[kind].description}</p>
        <div className="office-card-actions">{oaAccess(user, kind, "create") && <Link className="command-button" to={`/office/requests/${kind}?new=true`}>发起申请</Link>}<Link to={`/office/requests/${kind}?view=requests`}>申请记录<ArrowRight size={15} aria-hidden="true" /></Link></div>
      </article>)}
      {queues.filter(queue => ["rooms", "supplies"].includes(queue.kind) && !queue.handling).map(queue => <article className="oa-hub-start-card" key={queue.kind}>
        <ClipboardList size={22} aria-hidden="true" /><h3>{queue.title}</h3><p className="office-muted">{queue.description}</p><Link className="command-button secondary" to={queue.kind === "rooms" ? "/office/meeting-rooms" : "/office/supplies"}>选择资源<ArrowRight size={15} aria-hidden="true" /></Link>
      </article>)}
    </div> : <>
      <p className="office-muted">{view === "handling" ? "已批准的事项按职责交接、验收或归档。批准与实际办理分别记录。" : "审批人查看当前待签任务；其他员工查看自己申请的审批进度。每类显示最近五项，可进入列表查看全部。"}</p>
      {!selected.length && <PageState tone="empty" title="当前身份没有待办理职责" description="可以在“发起申请”中提交申请，或查看申请记录。" />}
      <div className="oa-hub-queue-grid">{selected.map(({ title, label, href, query }) => <article className="oa-hub-queue" key={href}>
        <header><div><p className="oa-hub-eyebrow">{label}</p><h3><Link to={href}>{title}</Link></h3></div><span className="oa-hub-count" aria-label={query.isPending ? "正在读取" : query.isError ? "读取失败" : `${label} ${query.data?.totalCount ?? 0} 项`}>{query.isPending || query.isError ? "—" : query.data?.totalCount ?? 0}</span></header>
        {query.isPending ? <p role="status">正在读取申请…</p> : query.isError ? <p role="alert">读取失败。<button className="command-button secondary" type="button" disabled={query.isFetching} onClick={() => void query.refetch()}>重试</button></p> : query.data?.items.length ?
          <ul>{query.data.items.map(row => <li key={row.id}><Link to={`${href}${href.includes("?") ? "&" : "?"}requestId=${row.id}`}><span>{row.title}</span><small>{requestPersonLabel(row.employeeName, row)}</small><ArrowRight size={16} aria-hidden="true" /></Link></li>)}</ul> : <p className="office-muted oa-hub-empty">暂无待处理事项</p>}
        <Link className="oa-hub-all" to={href}>进入列表<ArrowRight size={15} aria-hidden="true" /></Link>
      </article>)}</div>
    </>}
  </section>;
}
