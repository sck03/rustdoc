import { FormEvent, useEffect, useMemo, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { type ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../../desktop/desktopBridge.ts";
import { useServerPageNumber } from "../../../ui/useServerPageNumber.ts";
import { normalizeListPageSize } from "../../../ui/listViewState.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { readDefaultExportDirectory } from "../../settings/settingsPaths.ts";
import { type AuditLogFilters, describeAuditLogFilters, hasActiveAuditLogFilters } from "../auditLogMaintenanceModel.ts";
import { useAuditLogCommands } from "./useAuditLogCommands.ts";
import { loadAuditLogViewState, saveAuditLogViewState, createCommittedAuditLogFilters, entityLabels, actionLabels } from "../auditLogViewState.ts";
export { entityLabels, actionLabels, entityOptions, actionOptions } from "../auditLogViewState.ts";

export function useAuditLogWorkspace({ client, canManageAuditLogs = false }: {
  client: ExportDocManagerApiClient;
  canManageAuditLogs?: boolean;
}) {
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

  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  const commands = useAuditLogCommands({
    client, canManageAuditLogs, committedFilters, exportPath, cleanupDaysNumber,
    totalCount: page?.totalCount ?? 0, defaultExportDirectory, setExportPath,
    setActionMessage, clearSelection: () => setSelectedLogId(null),
  });
  const isActionBusy = commands.isPending;
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

  function handleSelectExportPath() {
    if (canManageAuditLogs && !isActionBusy) commands.mutate("select-path");
  }

  function handleExport() {
    if (!canExport) {
      return;
    }

    setActionMessage(null);
    commands.mutate("export");
  }

  function handleDownload() {
    if (!canDownload) {
      return;
    }

    setActionMessage(null);
    commands.mutate("download");
  }

  async function handleDeleteCurrent() {
    if (!canDeleteCurrent) {
      return;
    }

    setActionMessage(null);
    await commands.mutateAsync("delete");
  }

  async function handleCleanup() {
    if (!canCleanup) {
      return;
    }

    setActionMessage(null);
    await commands.mutateAsync("cleanup");
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
