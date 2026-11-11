import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { isDesktopBridgeAvailable, selectSaveExcelPath } from "../../../desktop/desktopBridge.ts";
import { downloadJobResultWhenReady, waitForJobCompletion } from "../../../ui/downloadJobResult.ts";

export async function downloadExcelTemplate(client: ExportDocManagerApiClient, kind: "invoice" | "coo",
  { signal, checkCurrent }: { signal: AbortSignal; checkCurrent: () => void }, defaultDirectory?: string) {
  const fileName = kind === "coo" ? "单一窗口产地证导入模板.xlsx" : "invoice-import-template.xlsx";
  const desktop = isDesktopBridgeAvailable();
  const path = desktop ? await selectSaveExcelPath(fileName, defaultDirectory) : undefined;
  checkCurrent();
  if (desktop && !path) return null;
  const request = { body: { destinationPath: path! } }, init = { signal };
  const job = kind === "coo"
    ? path ? await client.startSingleWindowExcelTemplateSaveToPathJob(request, init) : await client.startSingleWindowExcelTemplateDownloadJob(init)
    : path ? await client.startExcelTemplateSaveToPathJob(request, init) : await client.startExcelTemplateDownloadJob(init);
  checkCurrent();
  if (path) await waitForJobCompletion(client, job, init);
  else await downloadJobResultWhenReady(client, job, fileName, init);
  checkCurrent();
  return path ? "导入模板已保存。" : "导入模板已下载。";
}
