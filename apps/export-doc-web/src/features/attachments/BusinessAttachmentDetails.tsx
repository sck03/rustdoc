import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import type { BusinessAttachmentCategoryRecord, BusinessAttachmentDelete, BusinessAttachmentDetails as AttachmentDetails, BusinessAttachmentMetadataUpdate, BusinessAttachmentRevisionRecord, BusinessAttachmentUpdate } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { attachmentVersionLabel, canPreviewAttachment, fileSizeLabel } from "./attachmentModel.ts";
import { BusinessAttachmentMetadataForm } from "./BusinessAttachmentMetadataForm.tsx";

export function BusinessAttachmentDetails({ details, categories, busy, editing, onEditingChange, timeZone, onClose, onReplace, onUpdate, onRead, onEdit, onDelete }: {
  details: AttachmentDetails; busy: boolean; timeZone: string; onClose: () => void; onReplace: () => void;
  editing: boolean; onEditingChange: (editing: boolean) => void;
  categories: BusinessAttachmentCategoryRecord[];
  onEdit: (request: BusinessAttachmentMetadataUpdate) => Promise<boolean>;
  onDelete: (request: BusinessAttachmentDelete) => Promise<boolean>;
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
  async function remove() {
    if (!note.trim()) { setError("请先填写删除原因。"); return; }
    if (!await confirmAction({ title: "永久删除业务资料", description: `${item.title}：${note.trim()}`,
      details: [`将删除全部 ${item.latestRevision} 个版本，包括当前有效文件。`, "删除后无法恢复；如仅停止使用，请选择停用资料。"], confirmLabel: "永久删除", tone: "danger" })) return;
    setError("");
    await onDelete({ expectedVersion: item.versionNumber, note });
  }
  return <section className="attachment-detail" aria-label="业务资料版本" ref={region} tabIndex={-1}>
    <header className="business-records-heading"><div><h2>{item.title}</h2><p>{attachmentVersionLabel(item)}</p></div>
      <div className="business-records-actions"><Link to={`/invoices/${item.invoiceId}`} className="command-button secondary">打开单据 {item.invoiceNo}</Link>
        <button className="command-button secondary" type="button" disabled={busy || editing} onClick={onClose}>返回资料列表</button></div></header>
    <p className="business-records-muted">{item.categoryName}{item.poNumber ? ` · PO ${item.poNumber}` : ""}{item.styleNo ? ` · 款号 ${item.styleNo}` : ""}</p>
    {editing && <BusinessAttachmentMetadataForm item={item} categories={categories} busy={busy} onSave={onEdit} onClose={() => onEditingChange(false)} />}
    {(item.canEdit || item.canDelete) && !editing && <div className="business-records-form">
      <label>操作说明<input value={note} onChange={(event) => setNote(event.target.value)} disabled={busy} maxLength={500} placeholder="填写确认依据或停用、恢复、删除原因" /></label>
      {error && <InlineNotice tone="error">{error}</InlineNotice>}
      <div className="business-records-actions">{item.canEdit && <>
        <button type="button" className="command-button secondary" disabled={busy} onClick={() => onEditingChange(true)}>修正资料信息</button>
        {!item.isArchived && <button type="button" className="command-button" disabled={busy} onClick={onReplace}>上传新版本</button>}
        <button type="button" className="command-button secondary" disabled={busy} onClick={() => void update(item.currentRevision, !item.isArchived)}>{item.isArchived ? "恢复资料" : "停用资料"}</button>
      </>}{item.canDelete && <button type="button" className="command-button danger" disabled={busy} onClick={() => void remove()}>删除业务资料</button>}</div>
    </div>}
    <ol className="business-records-list">{details.revisions.map((version) => <li key={version.revision} className="attachment-version" data-current={version.revision === item.currentRevision}>
      <div className="business-records-heading"><h3>v{version.revision} · {version.fileName}{version.revision === item.currentRevision ? " · 当前有效" : ""}</h3>
        <span className="business-records-muted">{fileSizeLabel(version.length)} · {version.uploadedBy} · {date(version.createdAt)}</span></div>
      {version.note && <p>{version.note}</p>}
      <div className="business-records-actions"><button type="button" className="command-button secondary" disabled={busy} onClick={() => onRead(version, false)}>下载原文件</button>
        {canPreviewAttachment(version.contentType) && <button type="button" className="command-button secondary" disabled={busy} onClick={() => onRead(version, true)}>预览</button>}
        {item.canEdit && !item.isArchived && version.revision !== item.currentRevision && <button type="button" className="command-button secondary" disabled={busy || editing} onClick={() => void update(version.revision, false)}>设为有效版本</button>}</div>
    </li>)}</ol>
    {details.events.length > 0 && <details><summary>资料操作记录（共 {details.eventCount} 条，显示最近 {details.events.length} 条）</summary>
      <ul className="business-records-list">{details.events.map((event, index) => <li key={index} className="attachment-version">
        <p>{date(event.createdAt)} · {event.actorName} · {event.action === "Edit" ? "修正信息" : event.action === "Archive" ? "停用" : event.action === "Restore" ? "恢复" : `确认 v${event.revision}`}</p><p>{event.note}</p>
      </li>)}</ul></details>}
  </section>;
}
