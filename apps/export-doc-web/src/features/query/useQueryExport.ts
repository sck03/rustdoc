import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { isDesktopBridgeAvailable, selectSaveExcelPath } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { toApiQueryFilters, type QueryFilters } from "./queryModel.ts";

export function useQueryExport(client: ExportDocManagerApiClient, filters: QueryFilters, allowed: boolean, defaultDirectory: string) {
  const desktop = isDesktopBridgeAvailable();
  const [exportPath, setExportPath] = useState("");
  const [actionMessage, setActionMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const scope = JSON.stringify([filters, allowed]);
  useEffect(() => { setActionMessage(null); setLastCreatedJobId(null); }, [scope, client]);
  const commands = useScopedMutation<void, "export" | "select-path">(scope, client, {
    mutationFn: async (command, signal, { checkCurrent }) => {
      if (!allowed) return;
      const fileName = buildQueryExportFileName();
      if (command === "select-path") {
        const path = await selectSaveExcelPath(fileName, defaultDirectory);
        checkCurrent();
        if (path) { setExportPath(path); setActionMessage(null); setLastCreatedJobId(null); }
        return;
      }
      const body = toApiQueryFilters(filters);
      const job = desktop
        ? await client.saveQueriedInvoicesToPath({ body: { ...body, destinationPath: exportPath.trim() } }, { signal })
        : await client.downloadQueriedInvoices({ body }, { signal });
      checkCurrent();
      setLastCreatedJobId(job.jobId);
      if (!desktop) await downloadJobResultWhenReady(client, job, fileName, { signal });
      checkCurrent();
      setActionMessage({ kind: "success", text: desktop
        ? "查询结果 Excel 已加入“文件任务”，完成后将保存到所选路径。"
        : "查询结果 Excel 已生成并交给浏览器下载。" });
    },
    onError: error => setActionMessage({ kind: "error", text: readApiError(error) }),
  });
  const canExport = allowed && (!desktop || Boolean(exportPath.trim())) && !commands.isPending;
  return {
    exportPath, setExportPath, actionMessage, setActionMessage, lastCreatedJobId, canExport,
    isActionBusy: commands.isPending,
    reset: () => { commands.cancel(); setActionMessage(null); setLastCreatedJobId(null); },
    handleSelectExportPath: () => commands.mutate("select-path"),
    handleExport: () => {
      if (!canExport) return;
      setActionMessage(null); setLastCreatedJobId(null); commands.mutate("export");
    },
  };
}

function buildQueryExportFileName() {
  const now = new Date();
  const pad = (number: number) => String(number).padStart(2, "0");
  return `QueryResults_${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}.xlsx`;
}
