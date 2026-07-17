import { useState } from "react";
import { CalendarClock, Download, FileSpreadsheet, FolderOpen, SlidersHorizontal, Trash2 } from "lucide-react";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";

type ConfirmationAction = "delete-filtered" | "cleanup" | null;

export function AuditLogMaintenancePanel({
  currentResultCount,
  filterSummary,
  isDesktopRuntime,
  exportPath,
  retentionDays,
  isBusy,
  canExport,
  canDownload,
  canDeleteFiltered,
  canCleanup,
  onExportPathChange,
  onSelectExportPath,
  onExport,
  onDownload,
  onRetentionDaysChange,
  onDeleteFiltered,
  onCleanup,
  onActionError,
}: {
  currentResultCount: number;
  filterSummary: string[];
  isDesktopRuntime: boolean;
  exportPath: string;
  retentionDays: string;
  isBusy: boolean;
  canExport: boolean;
  canDownload: boolean;
  canDeleteFiltered: boolean;
  canCleanup: boolean;
  onExportPathChange: (value: string) => void;
  onSelectExportPath: () => void;
  onExport: () => void;
  onDownload: () => void;
  onRetentionDaysChange: (value: string) => void;
  onDeleteFiltered: () => Promise<void>;
  onCleanup: () => Promise<void>;
  onActionError: (message: string) => void;
}) {
  const [confirmationAction, setConfirmationAction] = useState<ConfirmationAction>(null);
  const retentionDaysNumber = Math.max(1, Math.trunc(Number(retentionDays) || 0));

  async function confirmAction(action: Exclude<ConfirmationAction, null>) {
    try {
      if (action === "delete-filtered") {
        await onDeleteFiltered();
      } else {
        await onCleanup();
      }
      setConfirmationAction(null);
    } catch {
      // Mutation callbacks already publish the user-facing API error.
    }
  }

  return (
    <section className="form-section audit-log-management-panel" aria-label="审计日志导出与维护">
      <div className="section-header">
        <div>
          <h2>导出与维护</h2>
          <span>当前查询共 {currentResultCount} 条；维护操作不会写入系统用户目录。</span>
        </div>
      </div>

      <div className="audit-log-maintenance-grid">
        <article className="audit-log-maintenance-card">
          <div className="audit-log-maintenance-card-heading">
            <FileSpreadsheet size={19} aria-hidden="true" />
            <div>
              <h3>导出当前结果</h3>
              <p>
                {isDesktopRuntime
                  ? "按当前筛选条件导出 Excel，保存位置由管理员明确选择。"
                  : "服务器在内存中生成 Excel，并交由浏览器下载，不接收服务器文件路径。"}
              </p>
            </div>
          </div>
          {isDesktopRuntime ? (
            <>
              <label className="inline-filter audit-log-export-path-filter">
                <span>Excel 保存位置</span>
                <input
                  value={exportPath}
                  placeholder="AuditLogs.xlsx"
                  disabled={isBusy}
                  onChange={(event) => onExportPathChange(event.target.value)}
                />
              </label>
              <div className="audit-log-maintenance-actions">
              <button className="command-button secondary" type="button" title="选择 Excel 保存位置" disabled={isBusy} onClick={onSelectExportPath}>
                <FolderOpen size={16} aria-hidden="true" />
                <span>选择位置</span>
              </button>
                {renderOpenPathAction(exportPath, "打开导出文件", onActionError)}
                <button className="command-button" type="button" title="导出 Excel" disabled={!canExport} onClick={onExport}>
                  <Download size={17} aria-hidden="true" />
                  <span>导出到文件</span>
                </button>
              </div>
            </>
          ) : (
            <div className="audit-log-browser-download">
              <p>文件将保存到浏览器的默认下载目录，服务器不会创建导出副本。</p>
              <button className="command-button" type="button" title="下载 Excel" disabled={!canDownload} onClick={onDownload}>
                <Download size={17} aria-hidden="true" />
                <span>下载 Excel</span>
              </button>
            </div>
          )}
        </article>

        <article className="audit-log-maintenance-card">
          <div className="audit-log-maintenance-card-heading">
            <CalendarClock size={19} aria-hidden="true" />
            <div>
              <h3>按保留期清理</h3>
              <p>保留最近指定天数，删除更早的历史日志；当前筛选条件不参与清理。</p>
            </div>
          </div>
          <label className="inline-filter audit-log-retention-filter">
            <span>保留最近</span>
            <div className="audit-log-number-field">
              <input
                type="number"
                min="1"
                step="1"
                value={retentionDays}
                disabled={isBusy}
                onChange={(event) => onRetentionDaysChange(event.target.value)}
              />
              <span>天</span>
            </div>
          </label>
          <div className="audit-log-maintenance-actions">
            <button
              className="command-button secondary danger-button"
              type="button"
              title="清理过期审计日志"
              disabled={!canCleanup}
              onClick={() => setConfirmationAction("cleanup")}
            >
              <Trash2 size={17} aria-hidden="true" />
              <span>清理过期日志</span>
            </button>
          </div>
        </article>
      </div>

      <details className="audit-log-advanced-maintenance">
        <summary>
          <SlidersHorizontal size={16} aria-hidden="true" />
          <span>高级操作：删除筛选结果</span>
        </summary>
        <div className="audit-log-advanced-maintenance-content">
          <div>
            <strong>只删除已提交筛选条件命中的日志</strong>
            <p>
              适合清除测试账号或特定业务记录。必须先设置筛选条件并点击查询；无筛选时请使用上方保留期清理。
            </p>
            {filterSummary.length ? (
              <ul>
                {filterSummary.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            ) : (
              <span className="audit-log-filter-hint">当前没有已提交的筛选条件。</span>
            )}
          </div>
          <button
            className="command-button secondary danger-button"
            type="button"
            title="删除当前筛选结果"
            disabled={!canDeleteFiltered}
            onClick={() => setConfirmationAction("delete-filtered")}
          >
            <Trash2 size={17} aria-hidden="true" />
            <span>删除当前 {currentResultCount} 条结果</span>
          </button>
        </div>
      </details>

      {confirmationAction === "delete-filtered" ? (
        <ConfirmationDialog
          title="删除筛选结果？"
          description="这些审计记录会被永久删除，且不会进入回收站。"
          details={[`将删除当前查询命中的 ${currentResultCount} 条记录。`, ...filterSummary]}
          confirmLabel={`确认删除 ${currentResultCount} 条`}
          isBusy={isBusy}
          onCancel={() => setConfirmationAction(null)}
          onConfirm={() => void confirmAction("delete-filtered")}
        />
      ) : null}

      {confirmationAction === "cleanup" ? (
        <ConfirmationDialog
          title="清理过期审计日志？"
          description={`将保留最近 ${retentionDaysNumber} 天的日志，永久删除更早的记录。`}
          details={["当前查询和筛选条件不会影响本次清理范围。", "清理完成后无法从程序内恢复。"]}
          confirmLabel="确认清理过期日志"
          isBusy={isBusy}
          tone="warning"
          onCancel={() => setConfirmationAction(null)}
          onConfirm={() => void confirmAction("cleanup")}
        />
      ) : null}
    </section>
  );
}
