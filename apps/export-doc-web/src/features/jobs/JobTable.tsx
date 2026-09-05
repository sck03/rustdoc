import { Ban, Download, RefreshCw, Trash2 } from "lucide-react";
import type { BackgroundJobSnapshot } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import {
  fileNameFromPath,
  formatDateTime,
  formatJobStatus,
  formatProgress,
  isTerminalJob,
  readJobMessage,
} from "./jobPresentation.ts";

export function JobTable({
  data,
  focusedJobId,
  isBusy,
  hasError,
  canOperate,
  canRetry,
  canManage,
  canDownload,
  onMessage,
  onCancel,
  onRetry,
  onDelete,
  onDownload,
  desktopAvailable,
}: {
  data: BackgroundJobSnapshot[];
  focusedJobId: string;
  isBusy: boolean;
  hasError: boolean;
  canOperate: boolean;
  canRetry: (job: BackgroundJobSnapshot) => boolean;
  canManage: boolean;
  canDownload: boolean;
  onMessage: (message: string | null) => void;
  onCancel: (job: BackgroundJobSnapshot) => void;
  onRetry: (jobId: string) => void;
  onDelete: (job: BackgroundJobSnapshot) => void;
  onDownload: (job: BackgroundJobSnapshot) => void;
  desktopAvailable: boolean;
}) {
  return (
    <ResponsiveTableFrame label="后台任务列表" busy={isBusy} mobileLayout="scroll">
      <table className="job-table">
        <thead><tr><th>任务</th><th>类型</th><th>状态</th><th>进度</th><th>消息</th><th>输出</th><th>创建</th><th>完成</th><th>操作</th></tr></thead>
        <tbody>
          {data.length === 0 && !hasError ? (
            <tr><td colSpan={9} className="empty-cell">{isBusy ? "加载中" : "暂无任务"}</td></tr>
          ) : data.map((job) => {
            const isFocused = Boolean(focusedJobId) && job.jobId.toLowerCase() === focusedJobId.toLowerCase();
            return (
              <tr key={job.jobId} className={isFocused ? "job-row-focused" : undefined}>
                <td><div className="job-title-cell"><strong title={job.title}>{job.title || job.jobId}</strong><span title={job.jobId}>{job.jobId}</span></div></td>
                <td>{job.kind || "-"}</td>
                <td><span className="status-pill">{formatJobStatus(job.status)}</span></td>
                <td className="amount-cell">{formatProgress(job.progressPercent)}</td>
                <td className="message-cell" title={readJobMessage(job)}>{readJobMessage(job)}</td>
                <td className="path-cell" title={desktopAvailable ? job.outputPath : undefined}>
                  <div className="table-path-cell job-output-path-cell">
                    <span>{desktopAvailable ? (job.outputPath || "-") : (job.outputPath ? fileNameFromPath(job.outputPath) : "-")}</span>
                    {desktopAvailable && job.outputPath?.trim() ? renderOpenPathAction(job.outputPath, "打开任务输出", onMessage) : null}
                    {canDownload && !desktopAvailable && job.status.toLowerCase() === "succeeded" && job.outputPath ? (
                      <button className="icon-button compact-icon-button" type="button" title="下载任务结果" aria-label="下载任务结果" onClick={() => onDownload(job)}>
                        <Download size={16} aria-hidden="true" />
                      </button>
                    ) : null}
                  </div>
                </td>
                <td>{formatDateTime(job.createdAt)}</td>
                <td>{formatDateTime(job.completedAt)}</td>
                <td><div className="job-row-actions">
                  <button className="icon-button compact-icon-button" type="button" title="重试任务" aria-label="重试任务" disabled={isBusy || !job.canRetry || !canRetry(job)} onClick={() => onRetry(job.jobId)}><RefreshCw size={16} aria-hidden="true" /></button>
                  <button className="icon-button compact-icon-button" type="button" title="取消任务" aria-label="取消任务" disabled={!canOperate || isBusy || !job.canCancel} onClick={() => onCancel(job)}><Ban size={16} aria-hidden="true" /></button>
                  <button className="icon-button compact-icon-button" type="button" title="删除任务记录" aria-label="删除任务记录" disabled={!canManage || isBusy || !isTerminalJob(job.status)} onClick={() => onDelete(job)}><Trash2 size={16} aria-hidden="true" /></button>
                </div></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}
