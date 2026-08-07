import type { ApiServerMigrationStatusResponse } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatRuntimeDate } from "./settingsFormatters.ts";

export function MaintenanceBackgroundTaskStatusPanel({ status }: { status: ApiServerMigrationStatusResponse | null | undefined }) {
  if (!status) return null;
  const phase = status.restorePhase?.trim().toLowerCase() ?? "";
  const tone = phase === "failed"
    ? "error"
    : phase === "completed"
      ? "success"
      : status.pendingRestore || phase === "rolling-back" || !status.supported || !status.toolsReady
        ? "warning"
        : "info";
  return (
    <section className="form-section maintenance-background-task-status-panel" aria-label="后台任务状态">
      <div className="section-header"><div><h3>后台任务状态</h3><p className="section-description">备份、恢复和迁移均在后台执行；请在服务重启前确认当前阶段。</p></div></div>
      <InlineNotice tone={tone}>
        {status.message}
        {status.restorePhase ? ` 当前阶段：${formatRestorePhase(status.restorePhase)}。` : ""}
        {status.restoreDetail ? ` ${status.restoreDetail}` : ""}
        {status.restoreUpdatedAtUtc ? ` 更新时间：${formatRuntimeDate(status.restoreUpdatedAtUtc)}。` : ""}
      </InlineNotice>
    </section>
  );
}

function formatRestorePhase(value: string) {
  const labels: Record<string, string> = {
    pending: "等待服务重启",
    validating: "验证迁移包",
    "safety-backup": "创建安全备份",
    "applying-database": "恢复数据库",
    "applying-files": "替换运行文件",
    "rolling-back": "自动回滚",
    completed: "已完成",
    failed: "失败",
  };
  return labels[value.trim().toLowerCase()] ?? value;
}
