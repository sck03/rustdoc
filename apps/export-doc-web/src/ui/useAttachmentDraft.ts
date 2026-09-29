import { useRef, useState } from "react";
import { createRequestKey } from "./createRequestKey.ts";
import { useUnsavedChangesGuard } from "./unsavedChangesGuard.tsx";

export type DraftAttachment = { key: string; kind: string; file: File };

/** Retain the successful record and remaining files across partial failures. */
export function useAttachmentDraft<R>(initial?: R) {
  const current = useRef(initial);
  const savedForm = useRef<string | null>(null);
  const [saved, setSaved] = useState<R | undefined>(initial);
  const [files, setFiles] = useState<DraftAttachment[]>([]);
  const [selectionError, setSelectionError] = useState("");
  useUnsavedChangesGuard({ isDirty: files.length > 0, message: "有尚未保存的图片或文档。" });
  function select(kind: string, selected: File[], multiple: boolean, maxBytes: number) {
    setSelectionError("");
    if (!selected.length) return;
    const pattern = kind === "document" ? /\.(pdf|png|jpe?g)$/iu : /\.(png|jpe?g)$/iu;
    if (selected.some(file => !pattern.test(file.name) || file.size === 0 || file.size > maxBytes)) {
      setSelectionError(`请选择有效的${kind === "document" ? "PDF、PNG 或 JPEG 文件" : "PNG 或 JPEG 图片"}，每个不超过 ${maxBytes / 1024 / 1024} MiB。`);
      return;
    }
    const remaining = multiple ? files : files.filter(file => file.kind !== kind);
    const next = [...remaining, ...selected.map(file => ({ key: createRequestKey(), kind, file }))];
    const documents = next.filter(file => file.kind === "document");
    if (documents.length > 20 || documents.reduce((sum, item) => sum + item.file.size, 0) > 50 * 1024 * 1024) {
      setSelectionError("每份资料最多选择 20 个文档附件、合计 50 MiB。"); return;
    }
    setFiles(next);
  }
  async function save(formKey: string, signal: AbortSignal,
    write: (record: R | undefined, signal: AbortSignal) => Promise<R>,
    upload: (record: R, entry: DraftAttachment, signal: AbortSignal) => Promise<R>): Promise<R> {
    if (!current.current || savedForm.current !== formKey) {
      current.current = await write(current.current, signal);
      savedForm.current = formKey;
      setSaved(current.current);
    }
    for (const entry of files) {
      signal.throwIfAborted();
      current.current = await upload(current.current, entry, signal);
      setSaved(current.current);
      setFiles(remaining => remaining.filter(file => file.key !== entry.key));
    }
    return current.current;
  }
  return { files, saved, selectionError, select, save,
    remove: (key: string) => setFiles(remaining => remaining.filter(file => file.key !== key)) };
}
