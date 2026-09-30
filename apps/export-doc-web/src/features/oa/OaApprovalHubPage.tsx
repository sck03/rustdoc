import { Link } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { useOaApprovalHub } from "./useOaApprovalHub.ts";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function OaApprovalHubPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const queues = useOaApprovalHub(client, user);
  if (!queues.length) return <PageState tone="permission" title="没有申请模块权限，请联系管理员分配权限方案" />;
  return <section className="work-surface office-workspace oa-workspace" aria-label="申请与审批中心"><header className="oa-heading"><h2>申请与办理概况</h2><p className="office-muted">选择一类申请，查看进度或继续办理。</p>{user.capabilities.canManageUsers && <Link to="/office/approval-settings">审批规则与代理</Link>}</header>
    <div className="office-resource-grid">{queues.map(({ kind, finance, title, description, label, href, query }) => <article className="office-resource-card" key={`${kind}-${finance}`}>
      <h2><Link to={href}>{title}</Link></h2><p className="office-muted">{description}</p>
      <p>{query.isPending ? "正在读取申请…" : query.isError ? "读取失败，请进入模块重试" : `${label}：${query.data?.totalCount ?? 0}`}</p>
      <ul>{query.data?.items.map((row) => <li key={row.id}><Link to={`${href}${href.includes("?") ? "&" : "?"}requestId=${row.id}`}>{row.employeeName} · {row.title}</Link></li>)}</ul>
    </article>)}</div>
  </section>;
}
