export function ReportTemplateFeedback({ message, type }: { message: string | null; type: "success" | "error" | null }) {
  return message ? <div className={type === "error" ? "alert" : "success-alert"}>{message}</div> : null;
}
