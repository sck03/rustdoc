import { getBusinessStatusTone } from "./businessStatusModel.ts";
import { StatusBadge } from "./primitives/Card.tsx";

export function BusinessStatusBadge({ value }: { value?: string | null }) {
  const label = value?.trim() || "未设置";
  return <StatusBadge className="business-status-badge" tone={getBusinessStatusTone(label)}>{label}</StatusBadge>;
}
