import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeQueryState } from "../office/OfficeUi.tsx";
import { communicationAccess } from "./communicationModel.ts";
import { NotificationCard } from "./NotificationCard.tsx";
import { useCommunicationOperation, useNotifications, useNotificationCount } from "./useCommunication.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function NotificationsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const m = useNotifications(client, user);
  const count = useNotificationCount(client, user);
  const operation = useCommunicationOperation();
  if (!communicationAccess(user, "notifications")) return <PageState tone="permission" title="没有站内通知权限" />;
  return <ListWorkspace className="office-workspace oa-workspace" title={`站内通知${count.data ? ` · ${count.data.unreadCount} 条未读` : ""}`} aria-label="站内通知"
    description="查看申请与审批动态，进入关联申请继续办理。标记已读仅更新通知状态。">
    <div className="office-toolbar oa-toolbar"><label className="checkbox-field"><input type="checkbox" checked={m.unread} onChange={e => m.changeUnread(e.target.checked)} />仅未读</label><button type="button" className="command-button secondary" disabled={operation.busy || !count.data?.unreadCount} onClick={() => void operation.run(signal => client.readAllNotifications({ signal }), () => {})}>全部标记已读</button><button type="button" className="command-button secondary" onClick={() => { void m.query.refetch(); void count.refetch(); }}>刷新</button></div>
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}{count.isError && <InlineNotice tone="error">{readApiError(count.error)}</InlineNotice>}
    <OfficeQueryState query={m.query} emptyTitle="当前没有通知" />
    <div className="office-resource-grid notification-grid">{m.query.data?.items.map(row => <NotificationCard key={row.id} row={row} timeZone={user.businessTimeZone} busy={operation.busy}
      onRead={() => void operation.run(signal => client.readNotification({ id: row.id }, { signal }), () => {})} />)}</div>
    <Pagination pageNumber={m.page} totalCount={m.query.data?.totalCount ?? 0} pageSize={20} isBusy={m.query.isFetching} onPageChange={m.setPage} />
  </ListWorkspace>;
}
