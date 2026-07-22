import { InlineNotice } from "./PageState.tsx";

export type OperationFeedbackTone = "success" | "warning" | "error" | "info";

export type OperationFeedbackState = {
  text: string;
  tone: OperationFeedbackTone;
};

export const successFeedback = (text: string): OperationFeedbackState => ({ text, tone: "success" });
export const warningFeedback = (text: string): OperationFeedbackState => ({ text, tone: "warning" });
export const errorFeedback = (text: string): OperationFeedbackState => ({ text, tone: "error" });
export const infoFeedback = (text: string): OperationFeedbackState => ({ text, tone: "info" });

export function OperationFeedback({ feedback }: { feedback: OperationFeedbackState | null }) {
  if (!feedback) return null;
  return <InlineNotice tone={feedback.tone}>{feedback.text}</InlineNotice>;
}
