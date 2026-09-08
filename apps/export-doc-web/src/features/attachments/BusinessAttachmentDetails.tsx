import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import type { BusinessAttachmentDetails as AttachmentDetails, BusinessAttachmentRevisionRecord, BusinessAttachmentUpdate } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { attachmentVersionLabel, canPreviewAttachment, fileSizeLabel } from "./attachmentModel.ts";

export function BusinessAttachmentDetails({ details, busy, timeZone, onClose, onReplace, onUpdate, onRead }: {
  details: AttachmentDetails; busy: boolean; timeZone: string; onClose: () => void; onReplace: () => void;
  onUpdate: (request: BusinessAttachmentUpdate) => Promise<boolean>;
  onRead: (revision: BusinessAttachmentRevisionRecord, preview: boolean) => void;
}) {
  const item = details.attachment;
  const region = useRef<HTMLElement>(null);
  useEffect(() => { region.current?.focus(); }, [item.id]);
  const [note, setNote] = useState("");
  const [error, setError] = useState("");
  const confirmAction = useConfirmation();
  const date = (value: string) => new Intl.DateTimeFormat("zh-CN", { timeZone, dateStyle: "short", timeStyle: "short" }).format(new Date(value));
  async function update(currentRevision: number | null | undefined, archived: boolean) {
    if (!note.trim()) { setError("请先填写本次确认、停用或恢复的说明。"); return; }
    const title = item.isArchived !== archived ? archived ? "停用资料" : "恢复资料" : `确认 v${currentRevision} 为有效版本`;
    if (!await confirmAction({ title, description: `${item.title}：${note.trim()}`, details: ["原文件和历史版本继续保留。"], confirmLabel: "确认" })) return;
    setError("");
    if (await onUpdate({ expectedVersion: item.versionNumber, currentRevision: currentRevision ?? null, isArchived: archived, note })) setNote("");
  }
  return <section className="attachment-detail" aria-label="业务资料版本" ref={region} tabIndex={-1}>
    <header className="business-records-heading"><div><h2>{item.title}</h2><p>{attachmentVersionLabel(item)}</p></div>
      <div className="business-records-actions"><Link to={`/invoices/${item.invoiceId}`} className="command-button secondary">打开单据 {item.invoiceNo}</Link>
        <button className="command-button secondary" type="button" disabled={busy} onClick={onClose}>关闭版本列表</button></div></header>
    {item.canEdit && <div className="business-records-form">
      <label>操作说明<input value={note} onChange={(event) => setNote(event.target.value)} disabled={busy} maxLength={500} placeholder="填写确认依据或停用、恢复原因" /></label>
      {error && <InlineNotice tone="error">{error}</InlineNotice>}
      <div className="business-records-actions">{!item.isArchived && <button type="button" className="command-button" disabled={busy} onClick={onReplace}>上传新版本</button>}
        <button type="button" className="command-button secondary" disabled={busy} onClick={() => void update(item.currentRevision, !item.isArchived)}>{item.isArchived ? "恢复资料" : "停用资料"}</button></div>
    </div>}
    <ol className="business-records-list">{details.revisions.map((version) => <li key={version.revision} className="attachment-version" data-current={version.revision === item.currentRevision}>
      <div className="business-records-heading"><h3>v{version.revision} · {version.fileName}{version.revision === item.currentRevision ? " · 当前有效" : ""}</h3>
        <span className="business-records-muted">{fileSizeLabel(version.length)} · {version.uploadedBy} · {date(version.createdAt)}</span></div>
      {version.note && <p>{version.note}</p>}
      <div className="business-records-actions"><button type="button" className="command-button secondary" disabled={busy} onClick={() => onRead(version, false)}>下载原文件</button>
        {canPreviewAttachment(version.contentType) && <button type="button" className="command-button secondary" disabled={busy} onClick={() => onRead(version, true)}>预览</button>}
        {item.canEdit && !item.isArchived && version.revision !== item.currentRevision && <button type="button" className="command-button secondary" disabled={busy} onClick={() => void update(version.revision, false)}>设为有效版本</button>}</div>
    </li>)}</ol>
    {details.events.length > 0 && <details><summary>确认与停用记录（共 {details.eventCount} 条，显示最近 {details.events.length} 条）</summary>
      <ul className="business-records-list">{details.events.map((event, index) => <li key={index} className="attachment-version">
        <p>{date(event.createdAt)} · {event.actorName} · {event.action === "Archive" ? "停用" : event.action === "Restore" ? "恢复" : `确认 v${event.revision}`}</p><p>{event.note}</p>
      </li>)}</ul></details>}
  </section>;
}
