import { AlertCircle, AlertTriangle, CheckCircle2, Info } from "lucide-react";

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
  const Icon = feedback.tone === "success" ? CheckCircle2
    : feedback.tone === "warning" ? AlertTriangle
      : feedback.tone === "error" ? AlertCircle
        : Info;

  return <div
    className="operation-feedback"
    data-tone={feedback.tone}
    role={feedback.tone === "error" ? "alert" : "status"}
    aria-live={feedback.tone === "error" ? "assertive" : "polite"}
  >
    <Icon size={17} aria-hidden="true" />
    <span>{feedback.text}</span>
  </div>;
}
