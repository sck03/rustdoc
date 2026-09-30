import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { Announcement, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficeQueryState, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { announcementActive, announcementStatus, communicationAccess } from "./communicationModel.ts";
import { CommunicationPager } from "./CommunicationPager.tsx";

export function AnnouncementDetails({ client, user, row, onEdit }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: Announcement; onEdit: () => void }) {
  const operation = useOfficeOperation();
  const [action, setAction] = useState<"publish" | "withdraw" | "archive" | number | null>(null);
  const [receipts, setReceipts] = useState(false);
  const editable = communicationAccess(user, "announcements", "manage") && ["Draft", "Withdrawn"].includes(row.status);
  const active = announcementActive(row) && (!row.audienceDepartment || row.audienceDepartment === user.departmentId);
  return <article className="oa-detail">
    <header className="oa-detail-summary"><h2>{row.isPinned && "置顶 · "}{row.title}</h2><p>{announcementStatus[row.status]} · 发布版本 {row.publishVersion} · {row.authorName}</p>
      <p>可见范围：{row.audienceDepartment || "本公司全体"}</p><p>有效期：{formatBusinessDateTime(row.startsAt, user.businessTimeZone)} 至 {formatBusinessDateTime(row.expiresAt, user.businessTimeZone)}</p>
      <div className="office-card-actions">{editable && <><button type="button" className="command-button secondary" onClick={onEdit}>编辑草稿</button><button type="button" className="command-button" onClick={() => setAction("publish")}>发布公告</button></>}
        {communicationAccess(user, "announcements", "manage") && row.status !== "Archived" && <>{row.status === "Published" && <button type="button" className="command-button secondary" onClick={() => setAction("withdraw")}>撤回公告</button>}<button type="button" className="command-button secondary" onClick={() => setAction("archive")}>归档公告</button></>}
      </div>
    </header>
    <section className="oa-detail-content" aria-label="公告正文"><p className="communication-body">{row.body}</p></section>
    <section className="oa-attachments" aria-label="公告附件"><h3>公告附件</h3>{!row.attachments.length && <p className="office-muted">无附件</p>}<ul>{row.attachments.map(file => <li key={file.id} className="office-card-actions"><span>{file.fileName}</span><button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => void operation.run(signal => client.downloadAnnouncementAttachment({ id: row.id, attachmentId: file.id }, { signal }), blob => downloadBlob(blob, file.fileName))}>下载</button>{editable && <button type="button" className="command-button secondary" onClick={() => setAction(file.id)}>移除</button>}</li>)}</ul></section>
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
    {active && <section className="oa-detail-content" aria-label="阅读确认">{row.readAt ? <p role="status">已确认阅读 · {formatBusinessDateTime(row.readAt, user.businessTimeZone)}</p> : <button type="button" className="command-button" disabled={operation.busy} onClick={() => void operation.run(signal => client.confirmAnnouncementRead({ id: row.id, body: { publishVersion: row.publishVersion } }, { signal }), () => {})}>我已阅读并确认</button>}</section>}
    {communicationAccess(user, "announcements", "receipts") && <details className="oa-detail-history" onToggle={e => setReceipts(e.currentTarget.open)}><summary>阅读回执</summary>{receipts && <AnnouncementReceipts client={client} user={user} row={row} />}</details>}
    {action !== null && <AnnouncementAction client={client} row={row} action={action} onClose={() => setAction(null)} />}
  </article>;
}
function AnnouncementAction({ client, row, action, onClose }: { client: ExportDocManagerApiClient; row: Announcement; action: "publish" | "withdraw" | "archive" | number; onClose: () => void }) {
  const operation = useOfficeOperation();
  const [note, setNote] = useState("");
  const label = typeof action === "number" ? "移除附件" : { publish: "发布公告", withdraw: "撤回公告", archive: "归档公告" }[action];
  return <OfficeDialog title={label} onClose={onClose} {...operation} protectChanges><p>{row.title}</p>{action === "publish" && <p>发布后内容冻结，读者需要确认本次发布的内容。有效期未开始时将定时生效。</p>}
    <form onSubmit={e => { e.preventDefault(); const input = { id: row.id, body: { expectedVersion: row.versionNumber, note } }; void operation.run(signal => typeof action === "number" ? client.deleteAnnouncementAttachment({ ...input, attachmentId: action }, { signal }) : action === "publish" ? client.publishAnnouncement(input, { signal }) : action === "withdraw" ? client.withdrawAnnouncement(input, { signal }) : client.archiveAnnouncement(input, { signal }), onClose); }}>
      {action !== "publish" && <OfficeField label="操作原因"><textarea required rows={3} maxLength={500} value={note} disabled={operation.busy} onChange={e => setNote(e.target.value)} /></OfficeField>}<OfficeSubmit busy={operation.busy} label={label} />
    </form></OfficeDialog>;
}
function AnnouncementReceipts({ client, user, row }: { client: ExportDocManagerApiClient; user: ApiUserDto; row: Announcement }) {
  const [page, setPage] = useState(1);
  const query = useQuery({ queryKey: ["office", "receipts", user.id, row.id, page, row.versionNumber], queryFn: ({ signal }) => client.listAnnouncementReceipts({ id: row.id, pageNumber: page, pageSize: 20 }, { signal }) });
  return <><OfficeQueryState query={query} emptyTitle="尚无阅读回执" /><ol className="oa-history">{query.data?.items.map(r => <li key={r.id}>{r.readerName} · 发布版本 {r.publishVersion} · {formatBusinessDateTime(r.readAt, user.businessTimeZone)}</li>)}</ol><CommunicationPager page={page} total={query.data?.totalCount ?? 0} busy={query.isFetching} change={setPage} /></>;
}
