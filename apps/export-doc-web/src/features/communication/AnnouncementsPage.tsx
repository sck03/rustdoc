import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { useState } from "react";
import type { Announcement, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeQueryState } from "../office/OfficeUi.tsx";
import { announcementStatus, communicationAccess } from "./communicationModel.ts";
import { useAnnouncements } from "./useCommunication.ts";
import { AnnouncementDialog } from "./AnnouncementDialog.tsx";
import { AnnouncementDetails } from "./AnnouncementDetails.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { Button } from "../../ui/Button.tsx";
import { FilterBar } from "../../ui/primitives/Field.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";
import "../../styles/routes/communication.css";

export function AnnouncementsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const m = useAnnouncements(client, user);
  const [editing, setEditing] = useState<Announcement | "new" | null>(null);
  const opening = useOfficeOperation();
  if (!communicationAccess(user, "announcements")) return <PageState tone="permission" title="没有公告查看权限" />;
  return <ListWorkspace className="office-workspace oa-workspace" title="公司公告" actions={!m.selected && <>
    <Button disabled={m.query.isFetching} onClick={() => void m.query.refetch()}>刷新</Button>
    {communicationAccess(user, "announcements", "manage") && <Button variant="primary" onClick={() => setEditing("new")}>新建公告</Button>}
  </>}>
    {m.selected ? <><button type="button" className="command-button secondary" onClick={() => m.select(0)}>返回公告列表</button>{m.detail.isPending ? <PageState tone="loading" title="正在读取公告" /> : m.detail.isError ? <PageState tone="error" title="公告读取失败" description={readApiError(m.detail.error)} /> : m.detail.data && <AnnouncementDetails client={client} user={user} row={m.detail.data} onEdit={() => setEditing(m.detail.data!)} onDeleted={() => m.select(0)} />}</> : <>
      <FilterBar label="公告筛选">{communicationAccess(user, "announcements", "manage") && <label className="checkbox-field"><input type="checkbox" checked={m.manage} onChange={e => m.changeManage(e.target.checked)} />发布管理（含草稿和归档）</label>}
        {!m.manage && <label className="checkbox-field"><input type="checkbox" checked={m.unread} onChange={e => m.changeUnread(e.target.checked)} />仅未确认</label>}</FilterBar>
      {opening.error && <InlineNotice tone="error">{opening.error}</InlineNotice>}
      <OfficeQueryState query={m.query} emptyTitle="当前没有公告" /><div className="office-resource-grid">{m.query.data?.items.map(row => <article className="office-resource-card" key={row.id}>
        <h3><button type="button" className="oa-title-button" onClick={() => m.select(row.id)}>{row.isPinned && "置顶 · "}{row.title}</button></h3>
        <p><span className="office-badge" data-state={row.status}>{announcementStatus[row.status]}</span> · {row.authorName}</p>
        <p className="office-muted">{row.publishVersion === 0 ? "尚未发布，仅发布管理员可见" : `发布版本 ${row.publishVersion} · ${row.readAt ? "已确认阅读" : "尚未确认"}`}</p>
        <div className="office-card-actions">
          {communicationAccess(user, "announcements", "manage") && ["Draft", "Withdrawn"].includes(row.status) && <Button disabled={opening.busy}
            onClick={() => void opening.run(signal => client.getAnnouncement({ id: row.id }, { signal }), setEditing)}>继续编辑</Button>}
          <button type="button" className="command-button secondary" onClick={() => m.select(row.id)}>{m.manage ? "查看与管理" : "查看公告"}</button>
        </div>
      </article>)}</div>
      <Pagination pageNumber={m.page} totalCount={m.query.data?.totalCount ?? 0} pageSize={20} isBusy={m.query.isFetching} onPageChange={m.setPage} /></>}
    {editing && <AnnouncementDialog client={client} user={user} record={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} onSaved={row => { setEditing(null); m.select(row.id); }} />}
  </ListWorkspace>;
}
