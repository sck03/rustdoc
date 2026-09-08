import { useState, type FormEvent } from "react";
import type { BusinessAttachmentCategory, BusinessAttachmentRecord } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { attachmentCategories, fileSizeLabel, type AttachmentUploadInput } from "./attachmentModel.ts";

export function BusinessAttachmentUploadForm({ invoiceId, attachment, maximumBytes, busy, onUpload, onClose }: {
  invoiceId: number; attachment?: BusinessAttachmentRecord; maximumBytes: number; busy: boolean;
  onUpload: (request: AttachmentUploadInput) => Promise<boolean>; onClose: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState(attachment?.title ?? "");
  const [category, setCategory] = useState<BusinessAttachmentCategory>(attachment?.category ?? "Original");
  const [poNumber, setPoNumber] = useState(attachment?.poNumber ?? "");
  const [styleNo, setStyleNo] = useState(attachment?.styleNo ?? "");
  const [note, setNote] = useState("");
  const [uploadKey, setUploadKey] = useState(createRequestKey);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState("");
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: dirty, message: "当前资料尚未上传，离开会丢失已填写内容。" });
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file || file.size === 0 || file.size > maximumBytes) { setError(`请选择非空且不超过 ${fileSizeLabel(maximumBytes)} 的文件。`); return; }
    setError("");
    if (await onUpload({ invoiceId, attachmentId: attachment?.id, expectedVersion: attachment?.versionNumber ?? 0,
      uploadKey, title, category, poNumber, styleNo, note, file })) setDirty(false);
  }
  return <form className="business-records-form" aria-label={attachment ? "上传资料新版本" : "上传业务资料"} onSubmit={(event) => void submit(event)}
    onChangeCapture={() => { setDirty(true); setUploadKey(createRequestKey()); }}>
    <div className="business-records-heading"><h3>{attachment ? `${attachment.title} · 上传新版本` : "新增业务资料"}</h3>
      <button className="command-button secondary" type="button" disabled={busy} onClick={() => void confirmDiscardChanges("关闭上传表单").then((yes) => { if (yes) onClose(); })}>取消</button></div>
    {error && <InlineNotice tone="error">{error}</InlineNotice>}
    <label>文件（最多 {fileSizeLabel(maximumBytes)}）<input type="file" required disabled={busy}
      accept=".pdf,.png,.jpg,.jpeg,.gif,.webp,.xls,.xlsx,.doc,.docx,.pptx,.txt,.csv"
      onChange={(event) => { const next = event.target.files?.[0] ?? null; setFile(next); if (!attachment && !title) setTitle(next?.name.replace(/\.[^.]+$/, "") ?? ""); }} /></label>
    <fieldset disabled={busy || Boolean(attachment)}>
      <label>资料名称<input value={title} onChange={(event) => setTitle(event.target.value)} maxLength={200} required /></label>
      <label>分类<select value={category} onChange={(event) => {
        const value = event.target.value as BusinessAttachmentCategory;
        if (Object.keys(attachmentCategories).includes(value)) setCategory(value);
      }}>{Object.entries(attachmentCategories).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label>客户 PO（选填）<input value={poNumber} maxLength={100} onChange={(event) => setPoNumber(event.target.value)} /></label>
      <label>款号（选填）<input value={styleNo} maxLength={200} onChange={(event) => setStyleNo(event.target.value)} /></label>
    </fieldset>
    <label>版本说明（选填）<input value={note} maxLength={500} disabled={busy} onChange={(event) => setNote(event.target.value)} /></label>
    <p className="business-records-muted">新上传的版本须另行确认后才成为有效版本。正式输出请上传实际交付的原文件。</p>
    <div className="business-records-actions"><button className="command-button" type="submit" disabled={busy}>{busy ? "正在上传…" : "上传并保留版本"}</button></div>
  </form>;
}
