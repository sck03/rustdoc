import { useState, type FormEvent } from "react";
import type { BusinessAttachmentCategoryRecord, BusinessAttachmentRecord } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useAttachmentDraft } from "../../ui/useAttachmentDraft.ts";
import { AttachmentDraftFields } from "../../ui/AttachmentDraftFields.tsx";
import { attachmentMetadata, fileSizeLabel, type AttachmentUploadInput } from "./attachmentModel.ts";
import { BusinessAttachmentFields } from "./BusinessAttachmentFields.tsx";

export function BusinessAttachmentUploadForm({ invoiceId, attachment, categories, maximumBytes, remainingBytes, busy, onUpload, onSave, onClose }: {
  invoiceId: number; attachment?: BusinessAttachmentRecord; maximumBytes: number; remainingBytes: number; busy: boolean;
  categories: BusinessAttachmentCategoryRecord[];
  onUpload: (request: AttachmentUploadInput, signal: AbortSignal) => Promise<BusinessAttachmentRecord>;
  onSave: (work: (signal: AbortSignal) => Promise<BusinessAttachmentRecord[]>) => Promise<boolean>;
  onClose: () => void;
}) {
  const attachments = useAttachmentDraft<BusinessAttachmentRecord[]>([]);
  const [metadata, setMetadata] = useState(() => attachmentMetadata(attachment, categories[0]?.id));
  const [titles, setTitles] = useState<Record<string, string>>({});
  const [note, setNote] = useState("");
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState("");
  const multiple = !attachment && attachments.files.length + attachments.completed.length > 1;
  const fileTitle = (file: File) => file.name.replace(/\.[^.]+$/, "");
  const fields = { ...metadata, title: metadata.title || (attachments.files.length === 1 ? fileTitle(attachments.files[0].file) : "") };
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: dirty, message: "当前资料尚未上传，离开会丢失已填写内容。" });
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    if (!attachments.files.length) { setError("请选择文件。"); return; }
    if (!categories.some(item => item.id === metadata.categoryId)) { setError("请选择有效的资料分类。"); return; }
    if (attachments.files.reduce((sum, entry) => sum + entry.file.size, 0) > remainingBytes) { setError("所选文件超过本单剩余附件容量。"); return; }
    setError("");
    await onSave(async signal => {
      const records = await attachments.save("attachments", signal, async current => current ?? [], async (current, entry, signal) => {
        const title = multiple ? titles[entry.key] ?? fileTitle(entry.file) : fields.title;
        const values = { invoiceId, attachmentId: attachment?.id, expectedVersion: attachment?.versionNumber ?? 0, ...metadata, title, note };
        const result = await onUpload({ ...values, uploadKey: attachments.requestKey(entry, values), file: entry.file }, signal);
        return [...current, result];
      });
      setDirty(false); return records;
    });
  }
  return <form className="business-records-form" aria-label={attachment ? "上传资料新版本" : "上传业务资料"} onSubmit={event => void submit(event)} onChangeCapture={() => setDirty(true)}>
    <div className="business-records-heading"><h3>{attachment ? `${attachment.title} · 上传新版本` : "新增业务资料"}</h3>
      <button className="command-button secondary" type="button" disabled={busy} onClick={() => void confirmDiscardChanges("关闭上传表单").then(yes => { if (yes) onClose(); })}>取消</button></div>
    {error && <InlineNotice tone="error">{error}</InlineNotice>}
    {attachments.failedKey && <p role="status">已上传文件会保留，再次提交只处理剩余文件。</p>}
    <AttachmentDraftFields draft={attachments} busy={busy} label="选择业务资料文件" onChange={() => setDirty(true)}
      selection={{ accept: ".pdf,.png,.jpg,.jpeg,.gif,.webp,.xls,.xlsx,.doc,.docx,.pptx,.txt,.csv", pattern: /\.(pdf|png|jpe?g|gif|webp|xlsx?|docx?|pptx|txt|csv)$/iu,
        maxBytes: maximumBytes, maxFiles: attachment ? 1 : 20, maxTotalBytes: remainingBytes, multiple: !attachment,
        description: `每个最多 ${fileSizeLabel(maximumBytes)}；${attachment ? "选择一个新版本文件" : "一次最多 20 个文件，每个文件分别归档"}。` }}
      renderFileDetails={multiple ? entry => <label>资料名称 · {entry.file.name}<input required maxLength={200} disabled={busy} value={titles[entry.key] ?? fileTitle(entry.file)} onChange={event => setTitles(current => ({ ...current, [entry.key]: event.target.value }))} /></label> : undefined} />
    <BusinessAttachmentFields value={fields} categories={categories} hideTitle={multiple} disabled={busy || Boolean(attachment)} onChange={value => {
      setMetadata(value);
      const entry = attachments.files[0];
      if (attachments.files.length === 1 && entry) setTitles(current => ({ ...current, [entry.key]: value.title }));
    }} />
    <label>版本说明（选填）<input value={note} maxLength={500} disabled={busy} onChange={event => setNote(event.target.value)} /></label>
    <p className="business-records-muted">{multiple && "本次文件共用分类、PO、款号与版本说明。"}新上传的版本须另行确认后才成为有效版本。</p>
    <div className="business-records-actions"><button className="command-button" type="submit" disabled={busy}>{busy ? "正在上传…" : "上传并保留版本"}{!busy && attachments.files.length > 1 ? `（${attachments.files.length}）` : ""}</button></div>
  </form>;
}
