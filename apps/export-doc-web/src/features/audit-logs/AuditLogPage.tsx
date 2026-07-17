import { FormEvent, useEffect, useMemo, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, Search } from "lucide-react";
import { type ApiAuditLogDto, type ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSaveExcelPath } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { readStoredJsonObject, writeStoredJson } from "../../ui/browserStorage.ts";
import { listPageSizeOptions, normalizeListPageSize } from "../../ui/listViewState.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { AuditLogMaintenancePanel } from "./AuditLogMaintenancePanel.tsx";
import {
  type AuditLogFilters,
  describeAuditLogFilters,
  hasActiveAuditLogFilters,
} from "./auditLogMaintenanceModel.ts";

const entityOptions = ["", "Invoice", "Item", "Customer", "Exporter", "Payment", "Payee", "Product", "HsCode", "User", "UserReportTemplate", "UserReportTemplateVersion"];
const actionOptions = ["", "Added", "Modified", "Deleted"];
const entityLabels: Record<string, string> = {
  Invoice: "发票",
  Item: "发票商品明细",
  Customer: "单证客户",
  Exporter: "出口商",
  Payment: "付款报销",
  Payee: "收款对象",
  Product: "商品资料",
  HsCode: "HS 编码",
  User: "系统账号",
  UserReportTemplate: "用户报表模板",
  UserReportTemplateVersion: "报表模板版本",
};
const actionLabels: Record<string, string> = { Added: "新增", Modified: "修改", Deleted: "删除" };
const auditLogViewStateStorageKey = "export-doc-manager.audit-log.list-view-state.v1";

type AuditLogViewState = {
  invoiceKeyword: string;
  entityName: string;
  action: string;
  userId: string;
  keyword: string;
  enableDateRange: boolean;
  startTime: string;
  endTime: string;
  retentionDays: string;
  pageSize: number;
};

function loadAuditLogViewState(): AuditLogViewState {
  const parsed = readStoredAuditLogViewState();
  return {
    invoiceKeyword: readStoredString(parsed.invoiceKeyword),
    entityName: readStoredOption(parsed.entityName, entityOptions),
    action: readStoredOption(parsed.action, actionOptions),
    userId: readStoredString(parsed.userId),
    keyword: readStoredString(parsed.keyword),
    enableDateRange: parsed.enableDateRange === true,
    startTime: readStoredString(parsed.startTime) || readDateTimeLocalValue(addDays(new Date(), -7)),
    endTime: readStoredString(parsed.endTime) || readDateTimeLocalValue(new Date()),
    retentionDays: readPositiveIntegerText(parsed.retentionDays, "180"),
    pageSize: normalizeListPageSize(parsed.pageSize),
  };
}

function saveAuditLogViewState(state: AuditLogViewState) {
  writeStoredJson(auditLogViewStateStorageKey, {
    invoiceKeyword: state.invoiceKeyword.trim(),
    entityName: state.entityName,
    action: state.action,
    userId: state.userId.trim(),
    keyword: state.keyword.trim(),
    enableDateRange: state.enableDateRange,
    startTime: state.startTime,
    endTime: state.endTime,
    retentionDays: readPositiveIntegerText(state.retentionDays, "180"),
    pageSize: normalizeListPageSize(state.pageSize),
  });
}

function createCommittedAuditLogFilters(state: AuditLogViewState): AuditLogFilters {
  return {
    invoiceKeyword: state.invoiceKeyword.trim(),
    entityName: state.entityName,
    action: state.action,
    userId: state.userId.trim(),
    keyword: state.keyword.trim(),
    startTime: state.enableDateRange ? readIsoDateTime(state.startTime) : "",
    endTime: state.enableDateRange ? readIsoDateTime(state.endTime) : "",
  };
}

function readStoredAuditLogViewState(): Record<string, unknown> {
  return readStoredJsonObject(auditLogViewStateStorageKey);
}

function readStoredString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function readStoredOption(value: unknown, options: readonly string[]) {
  const text = readStoredString(value);
  return options.includes(text) ? text : "";
}

function readPositiveIntegerText(value: unknown, fallback: string) {
  const numericValue = Number(value);
  if (!Number.isFinite(numericValue) || numericValue <= 0) {
    return fallback;
  }

  return String(Math.max(1, Math.trunc(numericValue)));
}

export function AuditLogPage({
  client,
  canManageAuditLogs = false,
}: {
  client: ExportDocManagerApiClient;
  canManageAuditLogs?: boolean;
}) {
  const queryClient = useQueryClient();
  const [initialViewState] = useState(() => loadAuditLogViewState());
  const [invoiceKeyword, setInvoiceKeyword] = useState(initialViewState.invoiceKeyword);
  const [entityName, setEntityName] = useState(initialViewState.entityName);
  const [action, setAction] = useState(initialViewState.action);
  const [userId, setUserId] = useState(initialViewState.userId);
  const [keyword, setKeyword] = useState(initialViewState.keyword);
  const [enableDateRange, setEnableDateRange] = useState(initialViewState.enableDateRange);
  const [startTime, setStartTime] = useState(initialViewState.startTime);
  const [endTime, setEndTime] = useState(initialViewState.endTime);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialViewState.pageSize);
  const [committedFilters, setCommittedFilters] = useState<AuditLogFilters>(() => createCommittedAuditLogFilters(initialViewState));
  const [selectedLogId, setSelectedLogId] = useState<number | null>(null);
  const [exportPath, setExportPath] = useState("");
  const [retentionDays, setRetentionDays] = useState(initialViewState.retentionDays);
  const [actionMessage, setActionMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);

  const logsQuery = useQuery({
    queryKey: queryKeys.auditLogs(pageNumber, pageSize, committedFilters),
    queryFn: () =>
      client.listAuditLogs({
        pageNumber,
        pageSize,
        invoiceKeyword: committedFilters.invoiceKeyword || undefined,
        entityName: committedFilters.entityName || undefined,
        action: committedFilters.action || undefined,
        userId: committedFilters.userId || undefined,
        keyword: committedFilters.keyword || undefined,
        startTime: committedFilters.startTime || undefined,
        endTime: committedFilters.endTime || undefined,
      }),
    placeholderData: keepPreviousData,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    enabled: canManageAuditLogs,
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (logsQuery.data && logsQuery.data.pageNumber !== pageNumber) {
      setPageNumber(logsQuery.data.pageNumber);
    }
  }, [logsQuery.data, pageNumber]);

  useEffect(() => {
    saveAuditLogViewState({
      invoiceKeyword,
      entityName,
      action,
      userId,
      keyword,
      enableDateRange,
      startTime,
      endTime,
      retentionDays,
      pageSize,
    });
  }, [action, enableDateRange, endTime, entityName, invoiceKeyword, keyword, pageSize, retentionDays, startTime, userId]);

  const page = logsQuery.data ?? null;
  const logs = page?.items ?? [];
  const totalPages = Math.max(page?.totalPages ?? 1, 1);
  const selectedLog = useMemo(
    () => logs.find((log) => log.id === selectedLogId) ?? logs[0] ?? null,
    [logs, selectedLogId],
  );
  const isBusy = logsQuery.isFetching;
  const message = logsQuery.isError ? readApiError(logsQuery.error) : null;
  const parsedRetentionDays = Number(retentionDays);
  const cleanupDaysNumber = Number.isInteger(parsedRetentionDays) && parsedRetentionDays > 0 ? parsedRetentionDays : 0;
  const activeFilterSummary = useMemo(
    () => describeAuditLogFilters(committedFilters, { entityLabels, actionLabels }),
    [committedFilters],
  );
  const hasActiveFilters = hasActiveAuditLogFilters(committedFilters);
  const isDesktopRuntime = isDesktopBridgeAvailable();

  const exportMutation = useMutation({
    mutationFn: () =>
      client.saveAuditLogsToPath({
        body: {
          ...createAuditLogFilterRequest(committedFilters),
          destinationPath: exportPath.trim(),
        },
      }),
    onSuccess: (response) => {
      setActionMessage({
        kind: "success",
        text: response.destinationPath
          ? `${response.message} ${response.affectedCount} 条，${response.destinationPath}`
          : `${response.message} ${response.affectedCount} 条`,
      });
    },
    onError: (error) => setActionMessage({ kind: "error", text: readApiError(error) }),
  });

  const downloadMutation = useMutation({
    mutationFn: () =>
      client.downloadAuditLogs({
        body: createAuditLogFilterRequest(committedFilters),
      }),
    onSuccess: (blob) => {
      downloadBlob(blob, buildAuditLogExportFileName());
      setActionMessage({
        kind: "success",
        text: `已下载 ${page?.totalCount ?? 0} 条审计日志。`,
      });
    },
    onError: (error) => setActionMessage({ kind: "error", text: readApiError(error) }),
  });

  const deleteMutation = useMutation({
    mutationFn: () =>
      client.deleteAuditLogsByCriteria({
        body: {
          ...createAuditLogFilterRequest(committedFilters),
          confirmed: true,
        },
      }),
    onSuccess: (response) => {
      setActionMessage({ kind: "success", text: `${response.message}` });
      setSelectedLogId(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.auditLogsRoot() });
    },
    onError: (error) => setActionMessage({ kind: "error", text: readApiError(error) }),
  });

  const cleanupMutation = useMutation({
    mutationFn: () =>
      client.cleanupAuditLogs({
        body: {
          daysToKeep: cleanupDaysNumber,
          confirmed: true,
        },
      }),
    onSuccess: (response) => {
      setActionMessage({ kind: "success", text: response.message });
      setSelectedLogId(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.auditLogsRoot() });
    },
    onError: (error) => setActionMessage({ kind: "error", text: readApiError(error) }),
  });

  const isActionBusy =
    exportMutation.isPending ||
    downloadMutation.isPending ||
    deleteMutation.isPending ||
    cleanupMutation.isPending;
  const hasExportRows = (page?.totalCount ?? 0) > 0;
  const canExport = canManageAuditLogs && isDesktopRuntime && hasExportRows && Boolean(exportPath.trim()) && !isActionBusy;
  const canDownload = canManageAuditLogs && !isDesktopRuntime && hasExportRows && !isActionBusy;
  const canDeleteCurrent =
    canManageAuditLogs &&
    hasActiveFilters &&
    (page?.totalCount ?? 0) > 0 &&
    !isActionBusy;
  const canCleanup =
    canManageAuditLogs &&
    cleanupDaysNumber > 0 &&
    !isActionBusy;
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCommittedFilters(
      createCommittedAuditLogFilters({
        invoiceKeyword,
        entityName,
        action,
        userId,
        keyword,
        enableDateRange,
        startTime,
        endTime,
        retentionDays,
        pageSize,
      }),
    );
    setSelectedLogId(null);
    setPageNumber(1);
  }

  function handlePageSizeChange(nextPageSize: number) {
    setPageSize(normalizeListPageSize(nextPageSize));
    setSelectedLogId(null);
    setPageNumber(1);
  }

  async function handleSelectExportPath() {
    try {
      const selected = await selectSaveExcelPath(buildAuditLogExportFileName(), defaultExportDirectory);
      if (selected) {
        setExportPath(selected);
        setActionMessage(null);
      }
    } catch (error) {
      setActionMessage({ kind: "error", text: readDesktopError(error) });
    }
  }

  function handleExport() {
    if (!canExport) {
      return;
    }

    setActionMessage(null);
    exportMutation.mutate();
  }

  function handleDownload() {
    if (!canDownload) {
      return;
    }

    setActionMessage(null);
    downloadMutation.mutate();
  }

  async function handleDeleteCurrent() {
    if (!canDeleteCurrent) {
      return;
    }

    setActionMessage(null);
    await deleteMutation.mutateAsync();
  }

  async function handleCleanup() {
    if (!canCleanup) {
      return;
    }

    setActionMessage(null);
    await cleanupMutation.mutateAsync();
  }

  return (
    <section className="work-surface audit-log-surface" aria-label="审计日志">
      <form className="toolbar audit-log-toolbar" onSubmit={handleSearch}>
        <div className="filter-bar audit-log-filter-bar">
          <label className="inline-filter audit-log-text-filter">
            <span>发票</span>
            <input value={invoiceKeyword} onChange={(event) => setInvoiceKeyword(event.target.value)} />
          </label>
          <label className="inline-filter">
            <span>实体</span>
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
          </label>
          <label className="inline-filter">
            <span>动作</span>
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
          </label>
          <label className="inline-filter audit-log-text-filter">
            <span>操作人</span>
            <input value={userId} onChange={(event) => setUserId(event.target.value)} />
          </label>
          <label className="inline-filter audit-log-text-filter audit-log-keyword-filter">
            <span>关键字</span>
            <input value={keyword} onChange={(event) => setKeyword(event.target.value)} />
          </label>
          <label className="inline-check">
            <input
              type="checkbox"
              checked={enableDateRange}
              onChange={(event) => setEnableDateRange(event.target.checked)}
            />
            <span>时间范围</span>
          </label>
          <label className="inline-filter audit-log-time-filter">
            <span>开始</span>
            <input
              type="datetime-local"
              value={startTime}
              disabled={!enableDateRange}
              onChange={(event) => setStartTime(event.target.value)}
            />
          </label>
          <label className="inline-filter audit-log-time-filter">
            <span>结束</span>
            <input
              type="datetime-local"
              value={endTime}
              disabled={!enableDateRange}
              onChange={(event) => setEndTime(event.target.value)}
            />
          </label>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新" disabled={isBusy} onClick={() => void logsQuery.refetch()}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button solid" type="submit" title="查询" disabled={isBusy}>
            <Search size={18} aria-hidden="true" />
          </button>
        </div>
      </form>

      {message ? <div className="alert">{message}</div> : null}
      {actionMessage ? <div className={actionMessage.kind === "success" ? "success-alert" : "alert"}>{actionMessage.text}</div> : null}

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
        <div className="table-frame audit-log-table-frame">
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
              {!logs.length ? (
                <tr>
                  <td className="empty-cell" colSpan={7}>
                    {isBusy ? "加载中" : "暂无审计日志"}
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>

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

      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={totalPages}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
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

function readIsoDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString();
}

function readDateTimeLocalValue(value: Date) {
  const pad = (number: number) => String(number).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}`;
}

function addDays(value: Date, days: number) {
  const next = new Date(value);
  next.setDate(next.getDate() + days);
  return next;
}

function createAuditLogFilterRequest(filters: {
  invoiceKeyword: string;
  entityName: string;
  action: string;
  userId: string;
  keyword: string;
  startTime: string;
  endTime: string;
}) {
  return {
    invoiceKeyword: filters.invoiceKeyword || undefined,
    entityName: filters.entityName || undefined,
    action: filters.action || undefined,
    userId: filters.userId || undefined,
    keyword: filters.keyword || undefined,
    startTime: filters.startTime || undefined,
    endTime: filters.endTime || undefined,
  };
}

function buildAuditLogExportFileName() {
  const now = new Date();
  const pad = (number: number) => String(number).padStart(2, "0");
  return `AuditLogs_${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}.xlsx`;
}
