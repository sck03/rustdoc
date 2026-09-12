import { InlineNotice } from "../../ui/PageState.tsx";

export function ReportTemplateFeedback({ message, type, onReload }: { message: string | null; type: "success" | "error" | null; onReload?: () => void }) {
  return message ? <InlineNotice tone={type === "error" ? "error" : "success"} action={type === "error" && onReload ? <button className="command-button secondary" type="button" onClick={onReload}>重新加载</button> : undefined}>{message}</InlineNotice> : null;
}
