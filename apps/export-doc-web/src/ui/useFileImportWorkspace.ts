import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { errorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "./OperationFeedback.tsx";
import { readApiError } from "./formUtils.ts";
import { isAbortError } from "./useAbortableOperation.ts";
import { useScopedSave } from "./useScopedSave.ts";
import { useTransientPreview } from "./useTransientPreview.ts";

/** A preview belongs to the last selected file; importing consumes that exact preview. */
export function useFileImportWorkspace<Preview extends { validRows: number }, Result>(options: {
  owner: unknown;
  enabled: boolean;
  preview: (file: File, signal: AbortSignal) => Promise<Preview>;
  commit: (preview: Preview, signal: AbortSignal) => Promise<Result>;
  describe: (result: Result) => string;
  onImported: (signal: AbortSignal) => Promise<void>;
}) {
  const scope = useMemo(() => ({}), [options.owner, options.enabled]);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const submitting = useRef<object | null>(null);
  const save = useScopedSave("file-import", scope);
  const preview = useTransientPreview<Preview, File>(scope,
    (signal, file) => options.preview(file, signal),
    error => setFeedback(errorFeedback(readApiError(error))));
  useLayoutEffect(() => { submitting.current = null; setFeedback(null); }, [scope]);

  function select(file?: File) {
    if (!options.enabled || !file || submitting.current) return;
    setFeedback(null);
    void preview.generate(file);
  }
  async function commit() {
    if (!options.enabled || !preview.preview?.validRows || preview.isPending || submitting.current) return;
    const selected = preview.preview;
    const request = {};
    submitting.current = request;
    try {
      await save.run(async ({ signal, checkCurrent }) => {
        const result = await options.commit(selected, signal);
        checkCurrent();
        preview.clear();
        setFeedback(successFeedback(options.describe(result)));
        try { await options.onImported(signal); }
        catch (error) { checkCurrent(); setFeedback(warningFeedback(`导入已完成，但目录刷新失败：${readApiError(error)}`)); }
      });
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    } finally { if (submitting.current === request) submitting.current = null; }
  }
  return { preview: preview.preview, feedback, select, commit, busy: preview.isPending || save.isSaving, importing: save.isSaving };
}
