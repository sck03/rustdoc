import { InlineNotice } from "./PageState.tsx";
import { isConcurrencyConflict, readApiError } from "./formUtils.ts";

export type OperationFeedbackTone = "success" | "warning" | "error" | "info";

export type OperationFeedbackState = {
  text: string;
  tone: OperationFeedbackTone;
};

export const successFeedback = (text: string): OperationFeedbackState => ({ text, tone: "success" });
export const warningFeedback = (text: string): OperationFeedbackState => ({ text, tone: "warning" });
export const errorFeedback = (text: string): OperationFeedbackState => ({ text, tone: "error" });
export const infoFeedback = (text: string): OperationFeedbackState => ({ text, tone: "info" });

export function requestErrorFeedback(error: unknown): OperationFeedbackState {
  const message = readApiError(error);
  return isConcurrencyConflict(error)
    ? warningFeedback(`${message} 请重新打开或刷新当前记录，确认最新内容后再保存。`)
    : errorFeedback(message);
}

export function OperationFeedback({ feedback }: { feedback: OperationFeedbackState | null }) {
  if (!feedback) return null;
  return <InlineNotice tone={feedback.tone}>{feedback.text}</InlineNotice>;
}
