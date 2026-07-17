import { getBusinessStatusTone } from "./businessStatusModel.ts";

export function BusinessStatusBadge({ value }: { value?: string | null }) {
  const label = value?.trim() || "未设置";
  return <span className="status-pill business-status-badge" data-tone={getBusinessStatusTone(label)}>{label}</span>;
}
