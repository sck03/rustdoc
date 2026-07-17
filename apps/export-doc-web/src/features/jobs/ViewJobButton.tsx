import { ListChecks } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { navigateToJobCenter, normalizeJobId } from "./jobNavigation.ts";

export function ViewJobButton({
  jobId,
  disabled,
  label = "查看任务",
}: {
  jobId: string | null | undefined;
  disabled?: boolean;
  label?: string;
}) {
  const navigate = useNavigate();
  const normalizedJobId = normalizeJobId(jobId);
  if (!normalizedJobId) {
    return null;
  }

  return (
    <button
      className="command-button secondary compact-command-button"
      type="button"
      disabled={disabled}
      onClick={() => navigateToJobCenter(navigate, normalizedJobId)}
    >
      <ListChecks size={15} aria-hidden="true" />
      <span>{label}</span>
    </button>
  );
}
