import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { selectSaveExcelPath } from "../../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../../ui/downloadBlob.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useScopedMutation } from "../../../ui/useScopedMutation.ts";
import type { AuditLogFilters } from "../auditLogMaintenanceModel.ts";
import { buildAuditLogExportFileName, createAuditLogFilterRequest } from "../auditLogViewState.ts";

type Command = "export" | "download" | "delete" | "cleanup" | "select-path";
export function useAuditLogCommands({
  client, canManageAuditLogs, committedFilters, exportPath, cleanupDaysNumber,
  totalCount, defaultExportDirectory, setExportPath, setActionMessage, clearSelection,
}: {
  client: ExportDocManagerApiClient;
  canManageAuditLogs: boolean;
  committedFilters: AuditLogFilters;
  exportPath: string;
  cleanupDaysNumber: number;
  totalCount: number;
  defaultExportDirectory: string;
  setExportPath(path: string): void;
  setActionMessage(message: { kind: "success" | "error"; text: string } | null): void;
  clearSelection(): void;
}) {
  return useScopedMutation<void, Command>(
    JSON.stringify([canManageAuditLogs, committedFilters]), client, {
      mutationFn: async (command, signal, { checkCurrent }) => {
        if (!canManageAuditLogs) return;
        const body = createAuditLogFilterRequest(committedFilters);
        if (command === "select-path") {
          const path = await selectSaveExcelPath(buildAuditLogExportFileName(), defaultExportDirectory);
          checkCurrent();
          if (path) { setExportPath(path); setActionMessage(null); }
          return;
        }
        if (command === "download") {
          const blob = await client.downloadAuditLogs({ body }, { signal });
          checkCurrent();
          downloadBlob(blob, buildAuditLogExportFileName());
          setActionMessage({ kind: "success", text: `已下载 ${totalCount} 条审计日志。` });
          return;
        }
        const response = command === "export"
          ? await client.saveAuditLogsToPath({ body: { ...body, destinationPath: exportPath.trim() } }, { signal })
          : command === "delete"
            ? await client.deleteAuditLogsByCriteria({ body: { ...body, confirmed: true } }, { signal })
            : await client.cleanupAuditLogs({ body: { daysToKeep: cleanupDaysNumber, confirmed: true } }, { signal });
        checkCurrent();
        setActionMessage({ kind: "success", text: response.destinationPath ? `${response.message} ${response.destinationPath}` : response.message });
        if (command === "delete" || command === "cleanup") clearSelection();
      },
      refreshKeys: (_, command) => command === "delete" || command === "cleanup" ? [queryKeys.auditLogsRoot()] : [],
      onError: error => setActionMessage({ kind: "error", text: readApiError(error) }),
    },
  );
}
