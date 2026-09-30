import { InlineNotice } from "./PageState.tsx";
import { useBlobUrl } from "./useBlobUrl.ts";
import type { DraftAttachment } from "./useAttachmentDraft.ts";
import { documentAccept, documentDescription } from "./documentAttachments.ts";

type Selection = { files: DraftAttachment[]; selectionError: string;
  select: (kind: string, files: File[], multiple: boolean, maxBytes: number) => void; remove: (key: string) => void };
export function AttachmentDraftFields({ draft, busy, kind = "document", label = "选择图片或文档", onChange }: {
  draft: Selection; busy: boolean; kind?: string; label?: string; onChange?: () => void;
}) {
  const document = kind === "document";
  return <section className="office-field-wide" aria-label={label}>
    <label className="office-field"><span>{label}</span><input type="file" multiple={document} disabled={busy}
      accept={document ? documentAccept : ".png,.jpg,.jpeg"}
      onChange={event => { draft.select(kind, Array.from(event.target.files ?? []), document, (document ? 10 : 5) * 1024 * 1024); event.target.value = ""; onChange?.(); }} /></label>
    <p className="office-muted">{document ? documentDescription : "PNG、JPEG；每张 5 MiB。"}随资料一起保存。</p>
    <ul>{draft.files.filter(file => file.kind === kind).map(entry => <li key={entry.key} className="office-card-actions">
      <DraftImage file={entry.file} /><span>{entry.file.name} · 待保存</span>
      <button type="button" className="command-button secondary" disabled={busy} aria-label={`移除待保存文件 ${entry.file.name}`} onClick={() => { draft.remove(entry.key); onChange?.(); }}>移除</button>
    </li>)}</ul>
    {draft.selectionError && <InlineNotice tone="error">{draft.selectionError}</InlineNotice>}
  </section>;
}
function DraftImage({ file }: { file: File }) {
  const url = useBlobUrl(/\.(png|jpe?g)$/iu.test(file.name) ? file : null);
  return url ? <img src={url} alt={file.name} width={64} height={64} /> : null;
}
