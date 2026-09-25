import { useQueries } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { oaApi } from "./oaApi.ts";
import { oaAccess, oaKinds, oaModules } from "./oaModel.ts";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function OaApprovalHubPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const kinds = oaKinds.filter((kind) => oaAccess(user, kind, "view"));
  const queries = useQueries({ queries: kinds.map((kind) => ({ queryKey: ["office", "oa", "hub", kind, user.id, user.companyScope],
    queryFn: ({ signal }: { signal: AbortSignal }) => oaApi(client, kind).list({ mineOnly: !oaAccess(user, kind, "approve"), status: "Pending", pageNumber: 1, pageSize: 5 }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false })) });
  if (!kinds.length) return <PageState tone="permission" title="没有申请模块权限，请联系管理员分配权限方案" />;
  return <section className="work-surface office-workspace oa-workspace" aria-label="申请与审批中心"><header className="oa-heading"><h2>待审批概况</h2><p className="office-muted">选择一类申请，查看进度或继续办理。</p></header>
    <div className="office-resource-grid">{kinds.map((kind, index) => { const query = queries[index]; const module = oaModules[kind]; return <article className="office-resource-card" key={kind}>
      <h2><Link to={`/office/requests/${kind}`}>{module.name}</Link></h2><p className="office-muted">{module.description}</p>
      <p>{query.isPending ? "正在读取待审批申请…" : query.isError ? "读取失败，请进入模块重试" : `${oaAccess(user, kind, "approve") ? "权限范围内待审批" : "我的待审批申请"}：${query.data?.totalCount ?? 0}`}</p>
      <ul>{query.data?.items.map((row) => <li key={row.id}><Link to={`/office/requests/${kind}?requestId=${row.id}`}>{row.employeeName} · {row.title}</Link></li>)}</ul>
    </article>; })}</div>
  </section>;
}
