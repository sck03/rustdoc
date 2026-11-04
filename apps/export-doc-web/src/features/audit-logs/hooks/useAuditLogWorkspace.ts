import { FormEvent, useEffect, useMemo, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { type ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSaveExcelPath } from "../../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../../ui/DesktopPathActions.tsx";
import { downloadBlob } from "../../../ui/downloadBlob.ts";
import { useServerPageNumber } from "../../../ui/useServerPageNumber.ts";
import { readStoredJsonObject, writeStoredJson } from "../../../ui/browserStorage.ts";
import { normalizeListPageSize } from "../../../ui/listViewState.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { readDefaultExportDirectory } from "../../settings/settingsPaths.ts";
import { type AuditLogFilters, describeAuditLogFilters, hasActiveAuditLogFilters } from "../auditLogMaintenanceModel.ts";
export const entityLabels: Record<string, string> = {
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

export const actionLabels: Record<string, string> = { Added: "新增", Modified: "修改", Deleted: "删除" };

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

export const entityOptions = ["", "Invoice", "Item", "Customer", "Exporter", "Payment", "Payee", "Product", "HsCode", "User", "UserReportTemplate", "UserReportTemplateVersion"];

export const actionOptions = ["", "Added", "Modified", "Deleted"];

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

export function useAuditLogWorkspace({ client, canManageAuditLogs = false }: {
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
    queryFn: ({ signal }) =>
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
      }, { signal }),
    placeholderData: keepPreviousData,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled: canManageAuditLogs,
    staleTime: 5 * 60 * 1000,
  });

  useServerPageNumber(logsQuery, pageNumber, setPageNumber);

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
  return {
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
  };
}
