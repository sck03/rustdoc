import { useState } from "react";
import { useMutation, type QueryClient } from "@tanstack/react-query";
import type { ApiInvoiceListItemDto, ApiSingleWindowImportedPackageResponse, ExportDocManagerApiClient, SingleWindowExportReview } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, openPath } from "../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import type { SingleWindowActionDraft } from "./SingleWindowActionsPanel.tsx";
import { readPathDialogError, requestExcelSavePath, requestSingleWindowPackageOpenPath, requestSingleWindowPackageSavePath } from "./invoiceListDesktopPaths.ts";
import { buildBookingSheetDefaultFileName, buildSingleWindowPackageDefaultFileName, type SingleWindowBusinessType } from "./invoiceListFileNames.ts";
import { buildSingleWindowReviewMessage, getAutoRepairGroupKeys, matchesSingleWindowReview } from "./invoiceListModels.ts";

type Options = { client: ExportDocManagerApiClient; queryClient: QueryClient; defaultExportDirectory: string; canView: boolean; canOperate: boolean; canExportBookingSheet: boolean };

export function useInvoiceListSingleWindowOperations({ client, queryClient, defaultExportDirectory, canView, canOperate, canExportBookingSheet }: Options) {
  const [draft, setDraft] = useState<SingleWindowActionDraft | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [jobId, setJobId] = useState<string | null>(null);
  const [packagePath, setPackagePath] = useState<string | null>(null);
  const [review, setReview] = useState<SingleWindowExportReview | null>(null);
  const [reviewBusinessType, setReviewBusinessType] = useState<SingleWindowBusinessType | null>(null);
  const [reviewInvoiceId, setReviewInvoiceId] = useState<number | null>(null);
  const clearResult = () => { setMessage(null); setJobId(null); setPackagePath(null); };

  const bookingSheetMutation = useMutation({
    mutationFn: async ({ invoice, destinationPath }: { invoice: ApiInvoiceListItemDto; destinationPath: string }) => {
      const job = isDesktopBridgeAvailable() ? await client.startInvoiceBookingSheetSaveToPathJob({ body: { invoiceId: invoice.id, destinationPath } }) : await client.startInvoiceBookingSheetDownloadJob({ invoiceId: invoice.id });
      if (!isDesktopBridgeAvailable()) await downloadJobResultWhenReady(client, job, buildBookingSheetDefaultFileName(invoice));
      return job;
    },
    onSuccess: async (job) => { setMessage(`已创建托单导出任务：${job.jobId}`); setMessageType("success"); setJobId(job.jobId); setPackagePath(null); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => { setMessage(readApiError(error)); setMessageType("error"); setJobId(null); setPackagePath(null); },
  });
  const submitMutation = useMutation({
    mutationFn: async ({ invoice, businessType, packagePath: targetPath }: { invoice: ApiInvoiceListItemDto; businessType: SingleWindowBusinessType; packagePath: string }) => {
      if (isDesktopBridgeAvailable()) {
        const response = businessType === "CustomsCoo" ? await client.saveCustomsCooSubmitPackageToPath({ invoiceId: invoice.id, body: { packagePath: targetPath } }) : await client.saveAgentConsignmentSubmitPackageToPath({ invoiceId: invoice.id, body: { packagePath: targetPath } });
        return { mode: "desktop" as const, response };
      }
      const blob = businessType === "CustomsCoo" ? await client.downloadCustomsCooSubmitPackage({ invoiceId: invoice.id }) : await client.downloadAgentConsignmentSubmitPackage({ invoiceId: invoice.id });
      downloadBlob(blob, buildSingleWindowPackageDefaultFileName(invoice, businessType));
      return { mode: "browser" as const };
    },
    onSuccess: async (result) => { setMessage(result.mode === "desktop" ? result.response.message || "单一窗口提交包已导出。" : "单一窗口提交包已交给浏览器下载。"); setMessageType("success"); setJobId(null); setPackagePath(result.mode === "desktop" ? result.response.packagePath || null : null); await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }); },
    onError: (error) => { setMessage(readApiError(error)); setMessageType("error"); setJobId(null); setPackagePath(null); },
  });
  const reviewMutation = useMutation({
    mutationFn: async ({ invoice, businessType }: { invoice: ApiInvoiceListItemDto; businessType: SingleWindowBusinessType }) => ({ invoice, businessType, review: await client.getSingleWindowExportReview({ businessType, invoiceId: invoice.id }) }),
    onSuccess: ({ invoice, businessType, review: nextReview }) => { setDraft({ invoice }); setReview(nextReview); setReviewBusinessType(businessType); setReviewInvoiceId(invoice.id); setPackagePath(null); setJobId(null); setMessage(buildSingleWindowReviewMessage(nextReview, businessType)); setMessageType(nextReview.totalErrorCount > 0 ? "error" : "success"); },
    onError: (error) => { setMessage(readApiError(error)); setMessageType("error"); setJobId(null); setReview(null); setReviewBusinessType(null); setReviewInvoiceId(null); },
  });
  const repairMutation = useMutation({
    mutationFn: async ({ invoice, businessType, groupKeys }: { invoice: ApiInvoiceListItemDto; businessType: SingleWindowBusinessType; groupKeys: string[] }) => ({ invoice, businessType, response: await client.repairSingleWindowExportReviewGroups({ businessType, invoiceId: invoice.id, body: { groupKeys } }) }),
    onSuccess: ({ invoice, businessType, response }) => { setDraft({ invoice }); setReview(response.review); setReviewBusinessType(businessType); setReviewInvoiceId(invoice.id); setPackagePath(null); setJobId(null); setMessage(response.message || buildSingleWindowReviewMessage(response.review, businessType)); setMessageType(response.review.totalErrorCount > 0 ? "error" : "success"); void queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }); },
    onError: (error) => { setMessage(readApiError(error)); setMessageType("error"); setJobId(null); },
  });
  const receiptMutation = useMutation({
    mutationFn: (targetPath: string) => client.importSingleWindowReceiptPackage({ body: { packagePath: targetPath, keepWorkingDirectory: false } }),
    onSuccess: async (response: ApiSingleWindowImportedPackageResponse) => { const receiptText = response.persistedReceiptCount > 0 ? `新增回执 ${response.persistedReceiptCount} 条。` : "没有新增回执。"; setMessage(`${response.message || "单一窗口回执包已导入。"} ${receiptText}`); setMessageType("success"); setJobId(null); setPackagePath(response.packagePath || null); await Promise.all([queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }), queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }), queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }), queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() })]); },
    onError: (error) => { setMessage(readApiError(error)); setMessageType("error"); setJobId(null); setPackagePath(null); },
  });
  function open(invoice: ApiInvoiceListItemDto) { if (!canView) return; setDraft({ invoice }); setMessage(null); setJobId(null); setPackagePath(null); setReview(null); setReviewBusinessType(null); setReviewInvoiceId(null); }
  function close() { setDraft(null); setMessage(null); setPackagePath(null); setReview(null); setReviewBusinessType(null); setReviewInvoiceId(null); }
  function buildReview(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType) { if (!canView || reviewMutation.isPending) return; clearResult(); reviewMutation.mutate({ invoice, businessType }); }
  function repairReview() { if (!canOperate || !draft || !review || !reviewBusinessType || repairMutation.isPending) return; const groupKeys = getAutoRepairGroupKeys(review); if (!groupKeys.length) { setMessage("当前预检结果没有可自动修复的分组。"); setMessageType("error"); setJobId(null); return; } setMessage(null); setJobId(null); repairMutation.mutate({ invoice: draft.invoice, businessType: reviewBusinessType, groupKeys }); }
  async function exportPackage(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType) {
    if (!canOperate || submitMutation.isPending || reviewMutation.isPending) return;
    if (!matchesSingleWindowReview(invoice.id, businessType, reviewInvoiceId, reviewBusinessType, review)) { try { const result = await reviewMutation.mutateAsync({ invoice, businessType }); if (result.review.hasIssues || result.review.totalErrorCount > 0 || result.review.totalWarningCount > 0) return; } catch { return; } }
    if (!isDesktopBridgeAvailable()) { clearResult(); submitMutation.mutate({ invoice, businessType, packagePath: "" }); return; }
    const targetPath = await requestSingleWindowPackageSavePath(invoice, businessType, defaultExportDirectory).catch((error) => { setMessage(readPathDialogError(error)); setMessageType("error"); setJobId(null); setPackagePath(null); return ""; });
    if (!targetPath) return; clearResult(); submitMutation.mutate({ invoice, businessType, packagePath: targetPath });
  }
  async function exportBookingSheet(invoice: ApiInvoiceListItemDto) { if (!canExportBookingSheet || bookingSheetMutation.isPending) return; setDraft({ invoice }); if (!isDesktopBridgeAvailable()) { clearResult(); bookingSheetMutation.mutate({ invoice, destinationPath: "" }); return; } const destinationPath = await requestExcelSavePath(buildBookingSheetDefaultFileName(invoice), defaultExportDirectory).catch((error) => { setMessage(readPathDialogError(error)); setMessageType("error"); setJobId(null); setPackagePath(null); return ""; }); if (!destinationPath) return; clearResult(); bookingSheetMutation.mutate({ invoice, destinationPath }); }
  async function importReceipt() { if (!canOperate || receiptMutation.isPending) return; const targetPath = await requestSingleWindowPackageOpenPath().catch((error) => { setMessage(readPathDialogError(error)); setMessageType("error"); setJobId(null); setPackagePath(null); return ""; }); if (!targetPath) return; clearResult(); receiptMutation.mutate(targetPath); }
  async function openPackagePath() { if (!packagePath) return; try { await openPath(packagePath); } catch (error) { setMessage(error instanceof Error ? error.message : "打开单一窗口包失败。"); setMessageType("error"); setJobId(null); } }
  const isBusy = bookingSheetMutation.isPending || submitMutation.isPending || reviewMutation.isPending || repairMutation.isPending || receiptMutation.isPending;
  return { draft, message, messageType, jobId, packagePath, review, reviewBusinessType, reviewInvoiceId, isBusy, isActionBusy: bookingSheetMutation.isPending || submitMutation.isPending || receiptMutation.isPending, isReviewBusy: reviewMutation.isPending || repairMutation.isPending, open, close, buildReview, repairReview, exportPackage, exportBookingSheet, importReceipt, openPackagePath };
}
