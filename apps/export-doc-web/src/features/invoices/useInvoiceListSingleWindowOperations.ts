import { useState } from "react";
import type { QueryClient } from "@tanstack/react-query";
import type { ApiInvoiceListItemDto, ExportDocManagerApiClient, SingleWindowExportReview } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, openPath } from "../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import type { SingleWindowActionDraft } from "./SingleWindowActionsPanel.tsx";
import { requestExcelSavePath, requestSingleWindowPackageOpenPath, requestSingleWindowPackageSavePath } from "./invoiceListDesktopPaths.ts";
import { buildBookingSheetDefaultFileName, buildSingleWindowPackageDefaultFileName, type SingleWindowBusinessType } from "./invoiceListFileNames.ts";
import { buildSingleWindowReviewMessage, getAutoRepairGroupKeys, matchesSingleWindowReview } from "./invoiceListModels.ts";

type Options = { client: ExportDocManagerApiClient; queryClient: QueryClient; defaultExportDirectory: string; canView: boolean; canOperate: boolean; canExportBookingSheet: boolean };
type OperationContext = { signal: AbortSignal; checkCurrent: () => void };

export function useInvoiceListSingleWindowOperations({ client, queryClient, defaultExportDirectory, canView, canOperate, canExportBookingSheet }: Options) {
  const [draft, setDraft] = useState<SingleWindowActionDraft | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [jobId, setJobId] = useState<string | null>(null);
  const [packagePath, setPackagePath] = useState<string | null>(null);
  const [review, setReview] = useState<SingleWindowExportReview | null>(null);
  const [reviewBusinessType, setReviewBusinessType] = useState<SingleWindowBusinessType | null>(null);
  const [reviewInvoiceId, setReviewInvoiceId] = useState<number | null>(null);
  const [stationAssignmentCode, setStationAssignmentCode] = useState("");
  const [action, setAction] = useState<"review" | "export">("review");
  const operation = useScopedSave(`${canView}:${canOperate}:${canExportBookingSheet}`, client);

  function clearResult() { setMessage(null); setJobId(null); setPackagePath(null); }
  function showReview(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType, value: SingleWindowExportReview, text?: string) {
    setReview(value); setReviewBusinessType(businessType); setReviewInvoiceId(invoice.id);
    setMessage(text || buildSingleWindowReviewMessage(value, businessType));
    setMessageType(value.totalErrorCount > 0 ? "error" : "success");
  }
  async function run(kind: typeof action, work: (context: OperationContext) => Promise<void>) {
    try {
      await operation.run(async (context) => { setAction(kind); clearResult(); await work(context); });
    } catch (error) {
      if (!isAbortError(error)) { setMessage(readApiError(error)); setMessageType("error"); }
    }
  }
  function close() {
    operation.cancel(); setDraft(null); clearResult(); setReview(null);
    setReviewBusinessType(null); setReviewInvoiceId(null); setStationAssignmentCode("");
  }
  function open(invoice: ApiInvoiceListItemDto) { if (canView) { close(); setDraft({ invoice }); } }
  function buildReview(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType) {
    if (!canView) return;
    void run("review", async ({ signal, checkCurrent }) => {
      const value = await client.getSingleWindowExportReview({ businessType, sourceId: invoice.id }, { signal });
      checkCurrent(); showReview(invoice, businessType, value);
    });
  }
  function repairReview() {
    if (!canOperate || !draft || !review || !reviewBusinessType) return;
    const groupKeys = getAutoRepairGroupKeys(review);
    if (!groupKeys.length) { setMessage("当前预检结果没有可自动修复的分组。"); setMessageType("error"); return; }
    void run("review", async ({ signal, checkCurrent }) => {
      const response = await client.repairSingleWindowExportReviewGroups({ businessType: reviewBusinessType, sourceId: draft.invoice.id, body: { groupKeys } }, { signal });
      checkCurrent(); showReview(draft.invoice, reviewBusinessType, response.review, response.message);
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    });
  }
  async function exportPackage(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType) {
    if (!canOperate) return;
    await run("export", async ({ signal, checkCurrent }) => {
      const assignmentCode = stationAssignmentCode.trim();
      if (!assignmentCode) throw new Error("请先粘贴目标持卡机操作档案的授权码，再导出提交包。");
      let expectedDraftRevision = review?.draftRevision ?? 0;
      if (!matchesSingleWindowReview(invoice.id, businessType, reviewInvoiceId, reviewBusinessType, review)) {
        const value = await client.getSingleWindowExportReview({ businessType, sourceId: invoice.id }, { signal });
        checkCurrent(); showReview(invoice, businessType, value);
        if (value.hasIssues || value.totalErrorCount > 0 || value.totalWarningCount > 0) return;
        expectedDraftRevision = value.draftRevision;
      }
      if (expectedDraftRevision <= 0) throw new Error("请先进入申报单据，保存并复核草稿后再导出。");
      const body = { stationAssignmentCode: assignmentCode, expectedDraftRevision };
      if (isDesktopBridgeAvailable()) {
        const targetPath = await requestSingleWindowPackageSavePath(invoice, businessType, defaultExportDirectory);
        checkCurrent(); if (!targetPath) return;
        const response = businessType === "CustomsCoo"
          ? await client.saveCustomsCooSubmitPackageToPath({ sourceId: invoice.id, body: { ...body, packagePath: targetPath } }, { signal })
          : await client.saveAgentConsignmentSubmitPackageToPath({ sourceId: invoice.id, body: { ...body, packagePath: targetPath } }, { signal });
        checkCurrent(); setPackagePath(response.packagePath || null); setMessage(response.message || "单一窗口提交包已导出。");
      } else {
        const blob = businessType === "CustomsCoo"
          ? await client.downloadCustomsCooSubmitPackage({ sourceId: invoice.id, body }, { signal })
          : await client.downloadAgentConsignmentSubmitPackage({ sourceId: invoice.id, body }, { signal });
        checkCurrent(); downloadBlob(blob, buildSingleWindowPackageDefaultFileName(invoice, businessType));
        setMessage("单一窗口提交包已交给浏览器下载。");
      }
      setMessageType("success");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    });
  }
  async function exportBookingSheet(invoice: ApiInvoiceListItemDto) {
    if (!canExportBookingSheet) return;
    await run("export", async ({ signal, checkCurrent }) => {
      setDraft({ invoice });
      const desktop = isDesktopBridgeAvailable();
      const destinationPath = desktop ? await requestExcelSavePath(buildBookingSheetDefaultFileName(invoice), defaultExportDirectory) : "";
      checkCurrent(); if (desktop && !destinationPath) return;
      const job = desktop
        ? await client.startInvoiceBookingSheetSaveToPathJob({ body: { invoiceId: invoice.id, destinationPath } }, { signal })
        : await client.startInvoiceBookingSheetDownloadJob({ invoiceId: invoice.id }, { signal });
      checkCurrent();
      if (!desktop) await downloadJobResultWhenReady(client, job, buildBookingSheetDefaultFileName(invoice), { signal });
      checkCurrent(); setJobId(job.jobId); setMessage(`已创建托单导出任务：${job.jobId}`); setMessageType("success");
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    });
  }
  async function importReceipt() {
    if (!canOperate) return;
    await run("export", async ({ signal, checkCurrent }) => {
      const targetPath = await requestSingleWindowPackageOpenPath();
      checkCurrent(); if (!targetPath) return;
      const response = await client.importSingleWindowReceiptPackage({ body: { packagePath: targetPath, keepWorkingDirectory: false } }, { signal });
      checkCurrent(); setPackagePath(response.packagePath || null); setMessageType("success");
      setMessage(`${response.message || "单一窗口回执包已导入。"} ${response.persistedReceiptCount > 0 ? `新增回执 ${response.persistedReceiptCount} 条。` : "没有新增回执。"}`);
      await Promise.all([queryKeys.invoicesRoot(), queryKeys.queryInvoicesRoot(), queryKeys.dashboard(), queryKeys.singleWindowOperationCenterRoot()].map(queryKey => queryClient.invalidateQueries({ queryKey })));
    });
  }
  async function openPackagePath() {
    if (!packagePath) return;
    const path = packagePath;
    await run("export", async ({ checkCurrent }) => { setPackagePath(path); await openPath(path); checkCurrent(); });
  }
  return { draft, message, messageType, jobId, packagePath, review, reviewBusinessType, reviewInvoiceId, stationAssignmentCode, setStationAssignmentCode, isBusy: operation.isSaving, isActionBusy: operation.isSaving && action === "export", isReviewBusy: operation.isSaving && action === "review", open, close, buildReview, repairReview, exportPackage, exportBookingSheet, importReceipt, openPackagePath };
}
