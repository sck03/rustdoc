import { useState } from "react";
import type { Announcement, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeQueryState } from "../office/OfficeUi.tsx";
import { announcementStatus, communicationAccess } from "./communicationModel.ts";
import { useAnnouncements } from "./useCommunication.ts";
import { AnnouncementDialog } from "./AnnouncementDialog.tsx";
import { AnnouncementDetails } from "./AnnouncementDetails.tsx";
import { CommunicationPager } from "./CommunicationPager.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";
import "../../styles/routes/communication.css";

export function AnnouncementsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const m = useAnnouncements(client, user);
  const [editing, setEditing] = useState<Announcement | "new" | null>(null);
  if (!communicationAccess(user, "announcements")) return <PageState tone="permission" title="没有公告查看权限" />;
  return <section className="work-surface office-workspace oa-workspace" aria-label="公司公告"><header className="oa-heading"><h2>公司公告</h2></header>
    {m.selected ? <><button type="button" className="command-button secondary" onClick={() => m.select(0)}>返回公告列表</button>{m.detail.isPending ? <PageState tone="loading" title="正在读取公告" /> : m.detail.isError ? <PageState tone="error" title="公告读取失败" description={readApiError(m.detail.error)} /> : m.detail.data && <AnnouncementDetails client={client} user={user} row={m.detail.data} onEdit={() => setEditing(m.detail.data!)} />}</> : <>
      <div className="office-toolbar oa-toolbar">{communicationAccess(user, "announcements", "manage") && <><label className="checkbox-field"><input type="checkbox" checked={m.manage} onChange={e => m.changeManage(e.target.checked)} />发布管理（含草稿和归档）</label><button type="button" className="command-button" onClick={() => setEditing("new")}>新建公告</button></>}
        {!m.manage && <label className="checkbox-field"><input type="checkbox" checked={m.unread} onChange={e => m.changeUnread(e.target.checked)} />仅未确认</label>}<button type="button" className="command-button secondary" disabled={m.query.isFetching} onClick={() => void m.query.refetch()}>刷新</button></div>
      <OfficeQueryState query={m.query} emptyTitle="当前没有公告" /><div className="office-resource-grid">{m.query.data?.items.map(row => { return <article className="office-resource-card" key={row.id}><h3><button type="button" className="oa-title-button" onClick={() => m.select(row.id)}>{row.isPinned && "置顶 · "}{row.title}</button></h3><p>{announcementStatus[row.status]} · {row.authorName}</p><p className="office-muted">{row.readAt ? "已确认阅读" : "尚未确认"} · 发布版本 {row.publishVersion}</p></article>; })}</div>
      <CommunicationPager page={m.page} total={m.query.data?.totalCount ?? 0} busy={m.query.isFetching} change={m.setPage} /></>}
    {editing && <AnnouncementDialog client={client} user={user} record={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} onSaved={row => { setEditing(null); m.select(row.id); }} />}
  </section>;
}
