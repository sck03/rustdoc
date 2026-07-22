import { useState } from "react";
import { useMutation, type QueryClient } from "@tanstack/react-query";
import type { ApiReportTemplateDto, BackgroundJobSnapshot, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";

type Options = {
  client: ExportDocManagerApiClient;
  queryClient: QueryClient;
  canOperate: boolean;
  canManage: boolean;
  canCreateInvoiceReportZip: boolean;
  desktopAvailable: boolean;
  defaultExportDirectory: string;
  jobsCount: number;
  focusJob(jobId: string, message: string): void;
  clearFocusedJob(): void;
};

function isTerminalJob(value?: string) {
  return ["succeeded", "failed", "canceled"].includes((value || "").toLowerCase());
}

function fileNameFromPath(value: string) {
  const normalized = value.trim().replace(/\\/g, "/");
  return normalized.slice(normalized.lastIndexOf("/") + 1);
}

export function useJobCenterOperations({ client, queryClient, canOperate, canManage, canCreateInvoiceReportZip, desktopAvailable, defaultExportDirectory, jobsCount, focusJob, clearFocusedJob }: Options) {
  const requestConfirmation = useConfirmation();
  const [message, setMessage] = useState<string | null>(null);
  const [messageTone, setMessageTone] = useState<"success" | "error">("success");
  const [pdfSources, setPdfSources] = useState("");
  const [pdfDestination, setPdfDestination] = useState("");
  const [pdfUploadFiles, setPdfUploadFiles] = useState<File[]>([]);
  const [reportInvoiceIds, setReportInvoiceIds] = useState("");
  const [reportZipDestination, setReportZipDestination] = useState("");
  const [reportTemplatePath, setReportTemplatePath] = useState("");
  const [reportWithSeal, setReportWithSeal] = useState(true);

  function clearFeedback() { setMessage(null); setMessageTone("success"); }
  function showSuccess(nextMessage: string) { setMessage(nextMessage); setMessageTone("success"); }
  function showError(nextMessage: string) { setMessage(nextMessage); setMessageTone("error"); }
  function handleChildMessage(nextMessage: string | null) { nextMessage ? showError(nextMessage) : clearFeedback(); }

  const cancelMutation = useMutation({
    mutationFn: (jobId: string) => client.cancelJob({ jobId }),
    onSuccess: async (response) => { showSuccess(response.message || "已请求取消任务。"); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => showError(readApiError(error)),
  });
  const retryMutation = useMutation({
    mutationFn: (jobId: string) => client.retryJob({ jobId }),
    onSuccess: async (job) => { focusJob(job.jobId, `已重新创建任务：${job.jobId}`); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => showError(readApiError(error)),
  });
  const deleteMutation = useMutation({
    mutationFn: (jobId: string) => client.deleteJob({ jobId }),
    onSuccess: async (response) => { showSuccess(response.message || "已删除任务记录。"); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => showError(readApiError(error)),
  });
  const clearFinishedMutation = useMutation({
    mutationFn: () => client.clearFinishedJobs(),
    onSuccess: async (response) => { showSuccess(response.message || "已清理已结束任务记录。"); clearFocusedJob(); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => showError(readApiError(error)),
  });
  const pdfMergeMutation = useMutation({
    mutationFn: async () => {
      if (!desktopAvailable) {
        const form = new FormData();
        pdfUploadFiles.forEach((file) => form.append("files", file, file.name));
        const job = await client.uploadAndStartPdfMergeDownloadJob({ body: form });
        await downloadJobResultWhenReady(client, job, "merged.pdf");
        return job;
      }
      return client.startPdfMergeSaveToPathJob({ body: { sourceFiles: readPathLines(pdfSources), destinationPath: pdfDestination.trim() } });
    },
    onSuccess: async (job) => { focusJob(job.jobId, `已创建 PDF 合并任务：${job.jobId}`); setPdfDestination(""); setPdfUploadFiles([]); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => showError(readApiError(error)),
  });
  const reportZipMutation = useMutation({
    mutationFn: async () => {
      const body = { invoiceIds: readPositiveIntegerTokens(reportInvoiceIds), reportType: "ExportDocument", templatePath: reportTemplatePath.trim(), withSeal: reportWithSeal, destinationPath: desktopAvailable ? reportZipDestination.trim() : "" };
      const job = desktopAvailable ? await client.startInvoiceReportPdfZipSaveToPathJob({ body }) : await client.startInvoiceReportPdfZipDownloadJob({ body });
      if (!desktopAvailable) await downloadJobResultWhenReady(client, job, "invoice-reports.zip");
      return job;
    },
    onSuccess: async (job) => { focusJob(job.jobId, `已创建批量报表 ZIP 任务：${job.jobId}`); setReportZipDestination(""); await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
    onError: (error) => showError(readApiError(error)),
  });
  const downloadMutation = useMutation({
    mutationFn: async (job: BackgroundJobSnapshot) => { const blob = await client.downloadJobResult({ jobId: job.jobId }); downloadBlob(blob, fileNameFromPath(job.outputPath) || `${job.kind || "download"}.bin`); },
    onError: (error) => showError(readApiError(error)),
  });

  async function handleCancelJob(job: BackgroundJobSnapshot) {
    if (!canOperate || isBusy || !job.canCancel) return;
    if (!await requestConfirmation({ title: "取消后台任务", description: `确定取消“${job.title || job.jobId}”吗？`, details: ["正在执行的处理会尽快停止；是否已生成部分输出取决于任务当前阶段。"], confirmLabel: "确认取消", tone: "danger" })) return;
    clearFeedback(); cancelMutation.mutate(job.jobId);
  }
  async function handleDeleteJob(job: BackgroundJobSnapshot) {
    if (!canManage || isBusy || !isTerminalJob(job.status)) return;
    if (!await requestConfirmation({ title: "删除任务记录", description: `确定删除“${job.title || job.jobId}”的任务记录吗？`, details: ["该操作不会删除用户自行选择位置保存的文件。", "程序托管的浏览器临时下载结果会随记录一并清理。"], confirmLabel: "确认删除", tone: "danger" })) return;
    clearFeedback(); deleteMutation.mutate(job.jobId);
  }
  async function handleClearFinishedJobs() {
    if (!canManage || isBusy || jobsCount === 0) return;
    if (!await requestConfirmation({ title: "清理已结束任务", description: "确定清理当前账号可管理的所有已完成、失败和已取消任务记录吗？", details: ["这会清理全部分页中的已结束记录，不只当前页。", "程序托管的浏览器临时下载结果会一并清理。"], confirmLabel: "确认清理", tone: "danger" })) return;
    clearFeedback(); clearFinishedMutation.mutate();
  }

  const isBusy = cancelMutation.isPending || retryMutation.isPending || deleteMutation.isPending || clearFinishedMutation.isPending || pdfMergeMutation.isPending || reportZipMutation.isPending;
  const canStartPdfMerge = desktopAvailable ? readPathLines(pdfSources).length > 0 && Boolean(pdfDestination.trim()) && !isBusy : pdfUploadFiles.length >= 2 && !isBusy;
  const reportInvoiceIdList = readPositiveIntegerTokens(reportInvoiceIds);
  const canStartReportZip = canCreateInvoiceReportZip && reportInvoiceIdList.length > 0 && (!desktopAvailable || Boolean(reportZipDestination.trim())) && !isBusy;
  return { message, messageTone, pdfSources, pdfDestination, pdfUploadFiles, reportInvoiceIds, reportZipDestination, reportTemplatePath, reportWithSeal, setPdfSources, setPdfDestination, setPdfUploadFiles, setReportInvoiceIds, setReportZipDestination, setReportTemplatePath, setReportWithSeal, clearFeedback, showSuccess, handleChildMessage, handleCancelJob, handleDeleteJob, handleClearFinishedJobs, cancelMutation, retryMutation, deleteMutation, clearFinishedMutation, pdfMergeMutation, reportZipMutation, downloadMutation, isBusy, canStartPdfMerge, canStartReportZip, reportInvoiceIdList, defaultExportDirectory };
}

function readPathLines(value: string) { return value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean); }
function readPositiveIntegerTokens(value: string) { return value.split(/[\s,;]+/).map((item) => Number(item)).filter((item) => Number.isInteger(item) && item > 0); }
