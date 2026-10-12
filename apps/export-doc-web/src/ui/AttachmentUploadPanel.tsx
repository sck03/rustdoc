import { AttachmentDraftFields } from "./AttachmentDraftFields.tsx";
import { attachmentUploadForm } from "./documentAttachments.ts";
import { useAttachmentDraft } from "./useAttachmentDraft.ts";

/** Existing records use the same sequential, version-aware queue as their editors. */
export function AttachmentUploadPanel<R extends { versionNumber: number; attachments?: { sizeBytes: number }[] }>({ record, upload, operation, label = "上传附件" }: {
  record: R; label?: string;
  operation: { busy: boolean; run: <T>(work: (signal: AbortSignal) => Promise<T>, done: (value: T) => void) => Promise<void> };
  upload: (record: R, body: FormData, signal: AbortSignal) => Promise<R>;
}) {
  const draft = useAttachmentDraft(record);
  const latest = draft.saved && draft.saved.versionNumber > record.versionNumber ? draft.saved : record;
  return <div>
    <AttachmentDraftFields draft={draft} busy={operation.busy} label={label} existingFiles={latest.attachments} />
    {draft.failedKey && <p role="status">已保存文件会保留，重试继续处理剩余文件。</p>}
    <button className="command-button" type="button" disabled={operation.busy || !draft.files.length} onClick={() => void operation.run(signal =>
      draft.save(String(record.versionNumber), signal, async () => record, (current, entry, signal) => {
        return upload(current, attachmentUploadForm(entry.file, current.versionNumber), signal);
      }), () => {})}>{operation.busy ? "正在上传…" : `上传所选附件${draft.files.length ? `（${draft.files.length}）` : ""}`}</button>
  </div>;
}
