import { requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "./OperationFeedback.tsx";
import { readApiError } from "./formUtils.ts";
import { isAbortError } from "./useAbortableOperation.ts";
import type { useScopedSave } from "./useScopedSave.ts";

type Save = ReturnType<typeof useScopedSave>;
type Context = Parameters<Parameters<Save["run"]>[0]>[0];

/** Lifecycle actions share the editor lock, including confirmation and refresh. */
export async function runRecordAction<T>(save: Save, feedback: (value: OperationFeedbackState) => void, action: {
  confirm: (() => Promise<boolean>)[];
  request: (signal: AbortSignal) => Promise<T>;
  apply: (value: T, context: Context) => void;
  success: string;
  refresh?: (value: T, signal: AbortSignal) => Promise<void>;
}) {
  try {
    await save.run(async context => {
      for (const askConfirmation of action.confirm) {
        const accepted = await askConfirmation();
        context.checkCurrent();
        if (!accepted) return;
      }
      const value = await action.request(context.signal);
      context.checkCurrent();
      action.apply(value, context);
      feedback(successFeedback(action.success));
      try { await action.refresh?.(value, context.signal); }
      catch (error) { context.checkCurrent(); feedback(warningFeedback(`${action.success}目录刷新失败：${readApiError(error)}`)); }
    });
  } catch (error) { if (!isAbortError(error)) feedback(requestErrorFeedback(error)); }
}
