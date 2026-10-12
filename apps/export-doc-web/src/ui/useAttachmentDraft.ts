import { useRef, useState } from "react";
import { createRequestKey } from "./createRequestKey.ts";
import { useUnsavedChangesGuard } from "./unsavedChangesGuard.tsx";
import { documentExtension } from "./documentAttachments.ts";

export type DraftAttachment = { key: string; kind: string; file: File };
export type AttachmentSelectionPolicy = { pattern?: RegExp; maxFiles?: number; maxTotalBytes?: number };

/** Retain the successful record and remaining files across partial failures. */
export function useAttachmentDraft<R>(initial?: R) {
  const current = useRef(initial);
  const savedForm = useRef<string | null>(null);
  const requestKeys = useRef(new Map<string, { payload: string; key: string }>());
  const [saved, setSaved] = useState<R | undefined>(initial);
  const [files, setFiles] = useState<DraftAttachment[]>([]);
  const [selectionError, setSelectionError] = useState("");
  const [completed, setCompleted] = useState<{ kind: string; name: string }[]>([]);
  const [uploadingKey, setUploadingKey] = useState<string | null>(null);
  const [failedKey, setFailedKey] = useState<string | null>(null);
  useUnsavedChangesGuard({ isDirty: files.length > 0, message: "有尚未保存的图片或文档。" });
  function select(kind: string, selected: File[], multiple: boolean, maxBytes: number, policy: AttachmentSelectionPolicy = {}) {
    setSelectionError("");
    if (!selected.length) return;
    const pattern = policy.pattern ?? (kind === "document" ? documentExtension : /\.(png|jpe?g)$/iu);
    if (selected.some(file => !pattern.test(file.name) || file.size === 0 || file.size > maxBytes)) {
      setSelectionError(`文件类型无效、内容为空或超过每个 ${maxBytes / 1024 / 1024} MiB 的限制，请核对所选文件。`);
      return;
    }
    const remaining = multiple ? files : files.filter(file => file.kind !== kind);
    const next = [...remaining, ...(multiple ? selected : selected.slice(0, 1)).map(file => ({ key: createRequestKey(), kind, file }))];
    const documents = next.filter(file => file.kind === "document");
    const maxFiles = policy.maxFiles ?? 20, maxTotalBytes = policy.maxTotalBytes ?? 50 * 1024 * 1024;
    if (documents.length > maxFiles || documents.reduce((sum, item) => sum + item.file.size, 0) > maxTotalBytes) {
      setSelectionError(`当前最多再选 ${maxFiles} 个文档附件、合计 ${(maxTotalBytes / 1024 / 1024).toFixed(1)} MiB。`); return;
    }
    setFiles(next);
    setFailedKey(null);
    if (!files.length) setCompleted([]);
  }
  async function save(formKey: string, signal: AbortSignal,
    write: (record: R | undefined, signal: AbortSignal) => Promise<R>,
    upload: (record: R, entry: DraftAttachment, signal: AbortSignal) => Promise<R>): Promise<R> {
    signal.throwIfAborted();
    if (!current.current || savedForm.current !== formKey) {
      current.current = await write(current.current, signal);
      savedForm.current = formKey;
      setSaved(current.current);
    }
    for (const entry of files) {
      signal.throwIfAborted();
      setUploadingKey(entry.key); setFailedKey(null);
      try { current.current = await upload(current.current, entry, signal); }
      catch (error) { setFailedKey(entry.key); throw error; }
      finally { setUploadingKey(null); }
      setSaved(current.current);
      setCompleted(done => [...done, { kind: entry.kind, name: entry.file.name }]);
      setFiles(remaining => remaining.filter(file => file.key !== entry.key));
    }
    return current.current;
  }
  function requestKey(entry: DraftAttachment, values: unknown) {
    const payload = JSON.stringify(values);
    let request = requestKeys.current.get(entry.key);
    if (!request || request.payload !== payload) { request = { payload, key: createRequestKey() }; requestKeys.current.set(entry.key, request); }
    return request.key;
  }
  return { files, saved, selectionError, completed, uploadingKey, failedKey, select, save, requestKey,
    remove: (key: string) => setFiles(remaining => remaining.filter(file => file.key !== key)) };
}
