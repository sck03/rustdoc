import { Link } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeQueryState } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { communicationAccess, notificationActions, notificationLink } from "./communicationModel.ts";
import { useNotifications, useNotificationCount } from "./useCommunication.ts";
import { CommunicationPager } from "./CommunicationPager.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function NotificationsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const m = useNotifications(client, user);
  const count = useNotificationCount(client, user);
  const operation = useOfficeOperation();
  if (!communicationAccess(user, "notifications")) return <PageState tone="permission" title="没有站内通知权限" />;
  return <section className="work-surface office-workspace oa-workspace" aria-label="站内通知"><header className="oa-heading"><h2>站内通知{count.data && ` · ${count.data.unreadCount} 条未读`}</h2></header>
    <p className="office-muted">查看申请与审批动态，进入关联申请继续办理。标记已读仅更新通知状态。</p>
    <div className="office-toolbar oa-toolbar"><label className="checkbox-field"><input type="checkbox" checked={m.unread} onChange={e => m.changeUnread(e.target.checked)} />仅未读</label><button type="button" className="command-button secondary" disabled={operation.busy || !count.data?.unreadCount} onClick={() => void operation.run(signal => client.readAllNotifications({ signal }), () => {})}>全部标记已读</button><button type="button" className="command-button secondary" onClick={() => { void m.query.refetch(); void count.refetch(); }}>刷新</button></div>
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}{count.isError && <InlineNotice tone="error">{readApiError(count.error)}</InlineNotice>}
    <OfficeQueryState query={m.query} emptyTitle="当前没有通知" /><div className="office-resource-grid">{m.query.data?.items.map(row => { const link = notificationLink(row); return <article className="office-resource-card" key={row.id}><h3>{notificationActions[row.action] ?? row.action} · {row.title}</h3><p>{formatBusinessDateTime(row.createdAt, user.businessTimeZone)} · {row.status === "Read" ? "已读" : "未读"}</p><div className="office-card-actions">{link && <Link className="command-button secondary" to={link}>查看关联申请</Link>}{row.status === "Unread" && <button type="button" className="command-button" disabled={operation.busy} onClick={() => void operation.run(signal => client.readNotification({ id: row.id }, { signal }), () => {})}>标记已读</button>}</div></article>; })}</div>
    <CommunicationPager page={m.page} total={m.query.data?.totalCount ?? 0} busy={m.query.isFetching} change={m.setPage} />
  </section>;
}
