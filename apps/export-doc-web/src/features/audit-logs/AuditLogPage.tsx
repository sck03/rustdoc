import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { Field, FilterBar } from "../../ui/primitives/Field.tsx";
import "../../styles/audit-query.css";
import { RefreshCw, Search } from "lucide-react";
import { type ApiAuditLogDto, type ExportDocManagerApiClient } from "../../api/index.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { listPageSizeOptions } from "../../ui/listViewState.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { AuditLogMaintenancePanel } from "./AuditLogMaintenancePanel.tsx";
import { useAuditLogWorkspace, entityLabels, actionLabels, entityOptions, actionOptions } from "./hooks/useAuditLogWorkspace.ts";

export function AuditLogPage({
  client,
  canManageAuditLogs = false,
}: {
  client: ExportDocManagerApiClient;
  canManageAuditLogs?: boolean;
}) {
  const {
    invoiceKeyword, setInvoiceKeyword, entityName, setEntityName, action,
    setAction, userId, setUserId, keyword, setKeyword,
    enableDateRange, setEnableDateRange, startTime, setStartTime, endTime,
    setEndTime, pageNumber, setPageNumber, pageSize, setSelectedLogId,
    exportPath, setExportPath, retentionDays, setRetentionDays, actionMessage,
    setActionMessage, logsQuery, page, logs, totalPages,
    selectedLog, isBusy, message, activeFilterSummary, isDesktopRuntime,
    isActionBusy, canExport, canDownload, canDeleteCurrent, canCleanup,
    handleSearch, handlePageSizeChange, handleSelectExportPath, handleExport, handleDownload,
    handleDeleteCurrent, handleCleanup,
  } = useAuditLogWorkspace({ client, canManageAuditLogs });

  return (
    <ListWorkspace className="audit-log-surface" aria-label="审计日志">
      <form className="toolbar audit-log-toolbar" onSubmit={handleSearch}>
        <FilterBar className="audit-log-filter-bar" label="审计筛选条件">
          <Field label="发票" className="audit-log-text-filter">
            <input value={invoiceKeyword} onChange={(event) => setInvoiceKeyword(event.target.value)} />
          </Field>
          <Field label="实体">
            <select value={entityName} onChange={(event) => setEntityName(event.target.value)}>
              <option value="">全部</option>
              {entityOptions
                .filter(Boolean)
                .map((value) => (
                  <option key={value} value={value}>
                    {entityLabels[value] || value}
                  </option>
                ))}
            </select>
          </Field>
          <Field label="动作">
            <select value={action} onChange={(event) => setAction(event.target.value)}>
              <option value="">全部</option>
              {actionOptions
                .filter(Boolean)
                .map((value) => (
                  <option key={value} value={value}>
                    {actionLabels[value] || value}
                  </option>
                ))}
            </select>
          </Field>
          <Field label="操作人" className="audit-log-text-filter">
            <input value={userId} onChange={(event) => setUserId(event.target.value)} />
          </Field>
          <Field label="关键字" className="audit-log-text-filter audit-log-keyword-filter">
            <input value={keyword} onChange={(event) => setKeyword(event.target.value)} />
          </Field>
          <label className="inline-check">
            <input
              type="checkbox"
              checked={enableDateRange}
              onChange={(event) => setEnableDateRange(event.target.checked)}
            />
            <span>时间范围</span>
          </label>
          <Field label="开始" className="audit-log-time-filter">
            <input
              type="datetime-local"
              value={startTime}
              disabled={!enableDateRange}
              onChange={(event) => setStartTime(event.target.value)}
            />
          </Field>
          <Field label="结束" className="audit-log-time-filter">
            <input
              type="datetime-local"
              value={endTime}
              disabled={!enableDateRange}
              onChange={(event) => setEndTime(event.target.value)}
            />
          </Field>
        </FilterBar>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={() => void logsQuery.refetch()}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button solid" type="submit" title="查询" aria-label="查询" disabled={isBusy}>
            <Search size={18} aria-hidden="true" />
          </button>
        </div>
      </form>

      {message ? <InlineNotice tone="error" title="审计日志操作失败">{message}</InlineNotice> : null}
      {actionMessage ? <InlineNotice tone={actionMessage.kind === "success" ? "success" : "error"}>{actionMessage.text}</InlineNotice> : null}

      {canManageAuditLogs ? (
        <AuditLogMaintenancePanel
          currentResultCount={page?.totalCount ?? 0}
          filterSummary={activeFilterSummary}
          isDesktopRuntime={isDesktopRuntime}
          exportPath={exportPath}
          retentionDays={retentionDays}
          isBusy={isActionBusy}
          canExport={canExport}
          canDownload={canDownload}
          canDeleteFiltered={canDeleteCurrent}
          canCleanup={canCleanup}
          onExportPathChange={setExportPath}
          onSelectExportPath={() => void handleSelectExportPath()}
          onExport={handleExport}
          onDownload={handleDownload}
          onRetentionDaysChange={setRetentionDays}
          onDeleteFiltered={handleDeleteCurrent}
          onCleanup={handleCleanup}
          onActionError={(text) => setActionMessage({ kind: "error", text })}
        />
      ) : null}

      <div className="audit-log-layout">
        <ResponsiveTableFrame label="审计日志列表" className="audit-log-table-frame" busy={logsQuery.isFetching} mobileLayout="scroll">
          <table className="audit-log-table">
            <thead>
              <tr>
                <th>时间</th>
                <th>实体</th>
                <th>动作</th>
                <th>实体 ID</th>
                <th>操作人</th>
                <th>变更前</th>
                <th>变更后</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((log) => (
                <tr
                  key={log.id}
                  className={selectedLog?.id === log.id ? "clickable-row audit-log-row-selected" : "clickable-row"}
                  tabIndex={0}
                  onClick={() => setSelectedLogId(log.id)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter" || event.key === " ") {
                      event.preventDefault();
                      setSelectedLogId(log.id);
                    }
                  }}
                >
                  <td>{formatDateTime(log.timestamp)}</td>
                  <td>{formatEntityName(log.entityName)}</td>
                  <td>
                    <span className="status-pill">{formatAction(log.action)}</span>
                  </td>
                  <td>{log.entityId || "-"}</td>
                  <td>{log.userId || "-"}</td>
                  <td className="audit-log-preview-cell">{log.oldValuesPreview || log.oldValues || "-"}</td>
                  <td className="audit-log-preview-cell">{log.newValuesPreview || log.newValues || "-"}</td>
                </tr>
              ))}
              {!isBusy && !message && !logs.length ? (
                <tr>
                  <td className="empty-cell" colSpan={7}>
                    {isBusy ? "加载中" : "暂无审计日志"}
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </ResponsiveTableFrame>

        <aside className="audit-log-detail" aria-label="审计日志详情">
          <div className="section-header">
            <div>
              <h2>详情</h2>
              <span>{selectedLog ? `${formatEntityName(selectedLog.entityName)} · ${formatAction(selectedLog.action)}` : "未选择"}</span>
            </div>
          </div>
          <pre>{selectedLog ? buildDetailText(selectedLog) : "选择一条日志查看详情。"}</pre>
        </aside>
      </div>

      <Pagination
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={totalPages}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </ListWorkspace>
  );
}

function buildDetailText(log: ApiAuditLogDto) {
  return [
    `时间: ${formatDateTime(log.timestamp)}`,
    `实体: ${formatEntityName(log.entityName)}`,
    `动作: ${formatAction(log.action)}`,
    `实体 ID: ${log.entityId || "-"}`,
    `操作人: ${log.userId || "-"}`,
    "",
    "变更前",
    log.oldValues || "-",
    "",
    "变更后",
    log.newValues || "-",
  ].join("\n");
}

function formatEntityName(value?: string) {
  const normalized = value?.trim() || "";
  return normalized ? entityLabels[normalized] || normalized : "-";
}

function formatAction(value?: string) {
  const normalized = value?.trim() || "";
  return normalized ? actionLabels[normalized] || normalized : "-";
}

function formatDateTime(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString("zh-CN", {
        hour12: false,
      });
}
