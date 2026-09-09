import { useState, type FormEvent } from "react";
import type { BusinessAttachmentCategoryRecord, BusinessAttachmentRecord } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { attachmentMetadata, fileSizeLabel, type AttachmentUploadInput } from "./attachmentModel.ts";
import { BusinessAttachmentFields } from "./BusinessAttachmentFields.tsx";

export function BusinessAttachmentUploadForm({ invoiceId, attachment, categories, maximumBytes, busy, onUpload, onClose }: {
  invoiceId: number; attachment?: BusinessAttachmentRecord; maximumBytes: number; busy: boolean;
  categories: BusinessAttachmentCategoryRecord[];
  onUpload: (request: AttachmentUploadInput) => Promise<boolean>; onClose: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [metadata, setMetadata] = useState(() => attachmentMetadata(attachment, categories[0]?.id));
  const [note, setNote] = useState("");
  const [uploadKey, setUploadKey] = useState(createRequestKey);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState("");
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: dirty, message: "当前资料尚未上传，离开会丢失已填写内容。" });
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file || file.size === 0 || file.size > maximumBytes) { setError(`请选择非空且不超过 ${fileSizeLabel(maximumBytes)} 的文件。`); return; }
    if (!categories.some((item) => item.id === metadata.categoryId)) { setError("请选择有效的资料分类。"); return; }
    setError("");
    if (await onUpload({ invoiceId, attachmentId: attachment?.id, expectedVersion: attachment?.versionNumber ?? 0,
      uploadKey, ...metadata, note, file })) setDirty(false);
  }
  return <form className="business-records-form" aria-label={attachment ? "上传资料新版本" : "上传业务资料"} onSubmit={(event) => void submit(event)}
    onChangeCapture={() => { setDirty(true); setUploadKey(createRequestKey()); }}>
    <div className="business-records-heading"><h3>{attachment ? `${attachment.title} · 上传新版本` : "新增业务资料"}</h3>
      <button className="command-button secondary" type="button" disabled={busy} onClick={() => void confirmDiscardChanges("关闭上传表单").then((yes) => { if (yes) onClose(); })}>取消</button></div>
    {error && <InlineNotice tone="error">{error}</InlineNotice>}
    <label>文件（最多 {fileSizeLabel(maximumBytes)}）<input type="file" required disabled={busy}
      accept=".pdf,.png,.jpg,.jpeg,.gif,.webp,.xls,.xlsx,.doc,.docx,.pptx,.txt,.csv"
      onChange={(event) => { const next = event.target.files?.[0] ?? null; setFile(next); if (!attachment && !metadata.title) setMetadata({ ...metadata, title: next?.name.replace(/\.[^.]+$/, "") ?? "" }); }} /></label>
    <BusinessAttachmentFields value={metadata} categories={categories} disabled={busy || Boolean(attachment)} onChange={setMetadata} />
    <label>版本说明（选填）<input value={note} maxLength={500} disabled={busy} onChange={(event) => setNote(event.target.value)} /></label>
    <p className="business-records-muted">新上传的版本须另行确认后才成为有效版本。正式输出请上传实际交付的原文件。</p>
    <div className="business-records-actions"><button className="command-button" type="submit" disabled={busy}>{busy ? "正在上传…" : "上传并保留版本"}</button></div>
  </form>;
}
