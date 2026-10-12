import { InlineNotice } from "./PageState.tsx";
import { useBlobUrl } from "./useBlobUrl.ts";
import type { DraftAttachment, AttachmentSelectionPolicy } from "./useAttachmentDraft.ts";
import { documentAccept, documentDescription } from "./documentAttachments.ts";
import type { ReactNode } from "react";
import "../styles/attachment-draft.css";

type Selection = { files: DraftAttachment[]; selectionError: string;
  completed?: { kind: string; name: string }[]; uploadingKey?: string | null; failedKey?: string | null;
  select: (kind: string, files: File[], multiple: boolean, maxBytes: number, policy?: AttachmentSelectionPolicy) => void; remove: (key: string) => void };
export function AttachmentDraftFields({ draft, busy, kind = "document", label = "选择图片或文档", onChange, existingFiles = [], selection, renderFileDetails }: {
  draft: Selection; busy: boolean; kind?: string; label?: string; onChange?: () => void;
  existingFiles?: { sizeBytes: number }[];
  selection?: AttachmentSelectionPolicy & { accept: string; maxBytes: number; multiple?: boolean; description: string };
  renderFileDetails?: (entry: DraftAttachment) => ReactNode;
}) {
  const document = kind === "document";
  const multiple = selection?.multiple ?? document;
  const files = draft.files.filter(file => file.kind === kind);
  const completed = draft.completed?.filter(file => file.kind === kind) ?? [];
  const policy = { maxFiles: Math.max(0, 20 - existingFiles.length), maxTotalBytes: Math.max(0, 50 * 1024 * 1024 - existingFiles.reduce((sum, file) => sum + file.sizeBytes, 0)), ...selection };
  return <section className="attachment-draft office-field-wide" aria-label={label}>
    <label className="office-field"><span>{label}{multiple ? "（可多选）" : ""}</span><input type="file" multiple={multiple} disabled={busy || document && policy.maxFiles === 0}
      accept={selection?.accept ?? (document ? documentAccept : ".png,.jpg,.jpeg")}
      onChange={event => { draft.select(kind, Array.from(event.target.files ?? []), multiple, selection?.maxBytes ?? (document ? 10 : 5) * 1024 * 1024, policy); event.target.value = ""; onChange?.(); }} /></label>
    <p className="office-muted">{selection?.description ?? (document ? documentDescription : "PNG、JPEG；每张 5 MiB。")}</p>
    {(files.length > 0 || completed.length > 0) && <p role="status">待保存 {files.length} 个{completed.length > 0 ? ` · 本次已保存 ${completed.length} 个` : ""}</p>}
    <ul>{files.map(entry => <li key={entry.key} className="office-card-actions">
      {files.length === 1 && <DraftImage file={entry.file} />}<span>{entry.file.name} · {draft.uploadingKey === entry.key ? "正在上传…" : draft.failedKey === entry.key ? "未完成，可重试" : "待保存"}</span>
      <button type="button" className="command-button secondary" disabled={busy} aria-label={`移除待保存文件 ${entry.file.name}`} onClick={() => { draft.remove(entry.key); onChange?.(); }}>移除</button>
      {renderFileDetails?.(entry)}
    </li>)}</ul>
    {completed.length > 0 && <details><summary>已保存的文件（{completed.length}）</summary><ul>{completed.map((entry, index) => <li key={index}>{entry.name} · 已保存</li>)}</ul></details>}
    {draft.selectionError && <InlineNotice tone="error">{draft.selectionError}</InlineNotice>}
  </section>;
}
function DraftImage({ file }: { file: File }) {
  const url = useBlobUrl(/\.(png|jpe?g)$/iu.test(file.name) ? file : null);
  return url ? <img src={url} alt={file.name} width={64} height={64} /> : null;
}
