import { Button } from "../../ui/Button.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ViewJobButton } from "./ViewJobButton.tsx";

export function JobTrackingRecovery({ jobId, pending, retry, dismiss }: {
  jobId: string; pending: boolean; retry: () => void; dismiss: () => void;
}) {
  return <InlineNotice tone="warning">
    任务状态尚未确认，请查询回执后再提交。停止跟踪不会取消后台任务。
    <div className="toolbar-actions">
      <Button variant="secondary" disabled={pending} onClick={retry}>重试查询</Button>
      <ViewJobButton jobId={jobId} />
      <Button variant="secondary" onClick={dismiss}>停止跟踪</Button>
    </div>
  </InlineNotice>;
}
