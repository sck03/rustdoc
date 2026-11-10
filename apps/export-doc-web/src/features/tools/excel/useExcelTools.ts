import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectExcelFile, selectSaveExcelPath } from "../../../desktop/desktopBridge.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../../ui/downloadJobResult.ts";
import { isAbortError } from "../../../ui/useAbortableOperation.ts";
import { useScopedSave } from "../../../ui/useScopedSave.ts";
import { readDefaultExportDirectory } from "../../settings/settingsPaths.ts";

export function buildBookingSheetFileName(path: string) {
  return `${path.split(/[\\/]/).pop()?.replace(/\.[^.]+$/, "") || "BookingSheet"}-BookingSheet.xlsx`;
}

export function useExcelTools(client: ExportDocManagerApiClient, canOperate: boolean) {
  const queries = useQueryClient();
  const boundary = useScopedSave(String(canOperate), client);
  const desktopAvailable = isDesktopBridgeAvailable();
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [templateDestinationPath, setTemplateDestinationPath] = useState("");
  const [blankBookingDestinationPath, setBlankBookingDestinationPath] = useState("");
  const [convertSourcePath, setConvertSourcePath] = useState("");
  const [convertDestinationPath, setConvertDestinationPath] = useState("");
  const [convertUploadFile, setConvertUploadFile] = useState<File | null>(null);
  useEffect(() => { setConvertUploadFile(null); setMessage(null); setLastCreatedJobId(null); }, [client]);
  const settingsQuery = useQuery({ queryKey: queryKeys.settings(), queryFn: ({ signal }) => client.getSettings({ signal }), staleTime: 300_000 });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  function showError(value: string) { setMessage(value); setMessageType("error"); }
  async function run(action: Parameters<typeof boundary.run>[0]) {
    if (!canOperate) return;
    try { await boundary.run(action); } catch (error) { if (!isAbortError(error)) showError(readApiError(error)); }
  }
  const pickConvertSource = () => run(async ({ checkCurrent }) => {
    const path = await selectExcelFile(); checkCurrent();
    if (path) { setConvertSourcePath(path); setMessage(null); }
  });
  const pickExcelDestination = (fileName: string, accept: (path: string) => void) => run(async ({ checkCurrent }) => {
    const path = await selectSaveExcelPath(fileName, defaultExportDirectory); checkCurrent();
    if (path) { accept(path); setMessage(null); }
  });
  const exportFile = (kind: "template" | "blank" | "convert", paths?: { sourcePath?: string; destinationPath: string }) => run(async ({ signal, checkCurrent }) => {
    let sourcePath = paths?.sourcePath ?? "";
    const file = convertUploadFile;
    if (kind === "convert" && desktopAvailable && !paths) { sourcePath = await selectExcelFile() ?? ""; checkCurrent(); if (!sourcePath) return; }
    if (kind === "convert" && !desktopAvailable && !file) return;
    const fileName = kind === "template" ? "导入数据模板.xlsx" : kind === "blank" ? "空白托单模板.xlsx" : buildBookingSheetFileName(desktopAvailable ? sourcePath : file?.name ?? "");
    const destinationPath = desktopAvailable ? paths?.destinationPath ?? await selectSaveExcelPath(fileName, defaultExportDirectory) : "";
    checkCurrent();
    if (desktopAvailable && !destinationPath) return;
    if (kind === "template") setTemplateDestinationPath(destinationPath!);
    if (kind === "blank") setBlankBookingDestinationPath(destinationPath!);
    if (kind === "convert") { setConvertSourcePath(sourcePath); setConvertDestinationPath(destinationPath!); }
    const init = { signal };
    const job = kind === "template"
      ? desktopAvailable ? await client.startExcelTemplateSaveToPathJob({ body: { destinationPath: destinationPath! } }, init) : await client.startExcelTemplateDownloadJob(init)
      : kind === "blank"
        ? desktopAvailable ? await client.startBlankBookingSheetSaveToPathJob({ body: { destinationPath: destinationPath! } }, init) : await client.startBlankBookingSheetDownloadJob(init)
        : desktopAvailable ? await client.startBookingSheetConvertSaveToPathJob({ body: { sourcePath, destinationPath: destinationPath! } }, init)
          : await client.uploadAndStartBookingSheetConvertDownloadJob({ fileName: file!.name, body: file! }, init);
    checkCurrent();
    if (kind === "convert") setConvertUploadFile(null);
    setLastCreatedJobId(job.jobId);
    if (!desktopAvailable) await downloadJobResultWhenReady(client, job, fileName, init);
    checkCurrent();
    setMessage(`已创建${kind === "template" ? "Excel 模板导出" : kind === "blank" ? "空白托单导出" : "托单转换"}任务：${job.jobId}`);
    setMessageType("success");
    try { await queries.invalidateQueries({ queryKey: queryKeys.jobsRoot() }, { throwOnError: true }); }
    catch (error) { checkCurrent(); showError(`任务已创建，任务列表刷新失败：${readApiError(error)}`); }
  });
  const isBusy = boundary.isSaving, available = canOperate && !isBusy;
  return {
    desktopAvailable, message, messageType, lastCreatedJobId, setMessage, showError,
    templateDestinationPath, setTemplateDestinationPath, blankBookingDestinationPath, setBlankBookingDestinationPath,
    convertSourcePath, setConvertSourcePath, convertDestinationPath, setConvertDestinationPath, convertUploadFile, setConvertUploadFile,
    isBusy, canExportTemplate: available, canExportBlankBooking: available,
    canExportTemplateByPath: available && Boolean(templateDestinationPath.trim()),
    canExportBlankBookingByPath: available && Boolean(blankBookingDestinationPath.trim()),
    canConvertBooking: available && (desktopAvailable || Boolean(convertUploadFile)),
    canConvertBookingByPath: available && Boolean(convertSourcePath.trim() && convertDestinationPath.trim()),
    pickConvertSource, pickExcelDestination,
    handleExportTemplate: () => exportFile("template"),
    handleExportTemplateByPath: () => exportFile("template", { destinationPath: templateDestinationPath.trim() }),
    handleExportBlankBooking: () => exportFile("blank"),
    handleExportBlankBookingByPath: () => exportFile("blank", { destinationPath: blankBookingDestinationPath.trim() }),
    handleConvertBooking: () => exportFile("convert"),
    handleConvertBookingByPath: () => exportFile("convert", { sourcePath: convertSourcePath.trim(), destinationPath: convertDestinationPath.trim() }),
  };
}
