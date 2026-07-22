import { InlineNotice } from "../../ui/PageState.tsx";

export function ReportTemplateFeedback({ message, type }: { message: string | null; type: "success" | "error" | null }) {
  return message ? <InlineNotice tone={type === "error" ? "error" : "success"}>{message}</InlineNotice> : null;
}
