import { useState } from "react";
import type { ApiInvoiceListItemDto, ApiReportTemplateDto, BackgroundJobSnapshot, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { downloadCompletedJobResult, downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { fileNameFromPath, isTerminalJob, readPathLines } from "./jobPresentation.ts";
import { createInvoiceReportExport } from "./invoiceReportExport.ts";
import { invoiceReportBatchIssue, useInvoiceReportBatchOptions } from "./useInvoiceReportBatchOptions.ts";

type Options = {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  canManage: boolean;
  canCreateInvoiceReportZip: boolean;
  desktopAvailable: boolean;
  defaultExportDirectory: string;
  jobsCount: number;
  reportTemplates: ApiReportTemplateDto[];
  configuredReportTemplatePath: string;
  focusJob(jobId: string, message: string): void;
  clearFocusedJob(): void;
};

export function useJobCenterOperations({ client, canOperate, canManage, canCreateInvoiceReportZip, desktopAvailable, defaultExportDirectory, jobsCount, reportTemplates, configuredReportTemplatePath, focusJob, clearFocusedJob }: Options) {
  const requestConfirmation = useConfirmation();
  const confirmation = useScopedSave(`${canOperate}:${canManage}`, client);
  const [message, setMessage] = useState<string | null>(null);
  const [messageTone, setMessageTone] = useState<"success" | "error">("success");
  const [pdfSources, setPdfSources] = useState("");
  const [pdfDestination, setPdfDestination] = useState("");
  const [pdfUploadFiles, setPdfUploadFiles] = useState<File[]>([]);
  const [reportInvoices, setReportInvoices] = useState<ApiInvoiceListItemDto[]>([]);
  const [reportZipDestination, setReportZipDestination] = useState("");
  const { templatePaths: reportTemplatePaths, setTemplatePaths: setReportTemplatePaths, withSeal: reportWithSeal, setWithSeal: setReportWithSeal } = useInvoiceReportBatchOptions(reportTemplates, configuredReportTemplatePath);

  function clearFeedback() { setMessage(null); setMessageTone("success"); }
  function showSuccess(nextMessage: string) { setMessage(nextMessage); setMessageTone("success"); }
  function showError(nextMessage: string) { setMessage(nextMessage); setMessageTone("error"); }
  function handleChildMessage(nextMessage: string | null) { nextMessage ? showError(nextMessage) : clearFeedback(); }

  const refreshKeys = () => [queryKeys.jobsRoot()];
  const cancelMutation = useScopedMutation("jobs", client, {
    mutationFn: (jobId: string, signal) => client.cancelJob({ jobId }, { signal }),
    onSuccess: (response) => showSuccess(response.message || "已请求取消任务。"),
    refreshKeys,
    onError: (error) => showError(readApiError(error)),
  });
  const retryMutation = useScopedMutation("jobs", client, {
    mutationFn: (jobId: string, signal) => client.retryJob({ jobId }, { signal }),
    onSuccess: (job) => focusJob(job.jobId, `已重新创建任务：${job.jobId}`),
    refreshKeys,
    onError: (error) => showError(readApiError(error)),
  });
  const deleteMutation = useScopedMutation("jobs", client, {
    mutationFn: (jobId: string, signal) => client.deleteJob({ jobId }, { signal }),
    onSuccess: (response) => showSuccess(response.message || "已删除任务记录。"),
    refreshKeys,
    onError: (error) => showError(readApiError(error)),
  });
  const clearFinishedMutation = useScopedMutation("jobs", client, {
    mutationFn: (_: void, signal) => client.clearFinishedJobs({ signal }),
    onSuccess: (response) => { showSuccess(response.message || "已清理已结束任务记录。"); clearFocusedJob(); },
    refreshKeys,
    onError: (error) => showError(readApiError(error)),
  });
  const pdfMergeMutation = useScopedMutation("jobs", client, {
    mutationFn: async (_: void, signal) => {
      if (!desktopAvailable) {
        const form = new FormData();
        pdfUploadFiles.forEach((file) => form.append("files", file, file.name));
        const job = await client.uploadAndStartPdfMergeDownloadJob({ body: form }, { signal });
        await downloadJobResultWhenReady(client, job, "merged.pdf", { signal });
        return job;
      }
      return client.startPdfMergeSaveToPathJob({ body: { sourceFiles: readPathLines(pdfSources), destinationPath: pdfDestination.trim() } }, { signal });
    },
    onSuccess: (job) => { focusJob(job.jobId, `已创建 PDF 合并任务：${job.jobId}`); setPdfDestination(""); setPdfUploadFiles([]); },
    refreshKeys,
    onError: (error) => showError(readApiError(error)),
  });
  const reportZipMutation = useScopedMutation("jobs", client, {
    mutationFn: async (_: void, signal) => {
      const body = { invoiceIds: reportInvoices.map((invoice) => invoice.id), reportType: "ExportDocument", templatePath: "", templatePaths: reportTemplatePaths, withSeal: reportWithSeal, destinationPath: desktopAvailable ? reportZipDestination.trim() : "" };
      return createInvoiceReportExport(client, body, desktopAvailable, signal);
    },
    onSuccess: (job) => { focusJob(job.jobId, `已创建批量报表 ZIP 任务：${job.jobId}`); setReportZipDestination(""); },
    refreshKeys,
    onError: (error) => showError(readApiError(error)),
  });
  const downloadMutation = useScopedMutation("jobs", client, {
    mutationFn: (job: BackgroundJobSnapshot, signal) => downloadCompletedJobResult(client, job, fileNameFromPath(job.outputPath) || `${job.kind || "download"}.bin`, signal),
    onError: (error) => showError(readApiError(error)),
  });

  async function confirmAction(options: Parameters<typeof requestConfirmation>[0], action: () => Promise<void>) {
    try {
      await confirmation.run(async ({ checkCurrent }) => {
        if (!await requestConfirmation(options)) return;
        checkCurrent();
        clearFeedback();
        await action();
      });
    } catch (error) { if (!isAbortError(error)) showError(readApiError(error)); }
  }
  async function handleCancelJob(job: BackgroundJobSnapshot) {
    if (!canOperate || isBusy || !job.canCancel) return;
    await confirmAction({ title: "取消后台任务", description: `确定取消“${job.title || job.jobId}”吗？`, details: ["正在执行的处理会尽快停止；是否已生成部分输出取决于任务当前阶段。"], confirmLabel: "确认取消", tone: "danger" }, () => cancelMutation.mutateAsync(job.jobId));
  }
  async function handleDeleteJob(job: BackgroundJobSnapshot) {
    if (!canManage || isBusy || !isTerminalJob(job.status)) return;
    await confirmAction({ title: "删除任务记录", description: `确定删除“${job.title || job.jobId}”的任务记录吗？`, details: ["该操作不会删除用户自行选择位置保存的文件。", "程序托管的浏览器临时下载结果会随记录一并清理。"], confirmLabel: "确认删除", tone: "danger" }, () => deleteMutation.mutateAsync(job.jobId));
  }
  async function handleClearFinishedJobs() {
    if (!canManage || isBusy || jobsCount === 0) return;
    await confirmAction({ title: "清理已结束任务", description: "确定清理当前账号可管理的所有已完成、失败和已取消任务记录吗？", details: ["这会清理全部分页中的已结束记录，不只当前页。", "程序托管的浏览器临时下载结果会一并清理。"], confirmLabel: "确认清理", tone: "danger" }, () => clearFinishedMutation.mutateAsync());
  }

  const isBusy = confirmation.isSaving || cancelMutation.isPending || retryMutation.isPending || deleteMutation.isPending || clearFinishedMutation.isPending || pdfMergeMutation.isPending || reportZipMutation.isPending;
  const canStartPdfMerge = desktopAvailable ? readPathLines(pdfSources).length > 0 && Boolean(pdfDestination.trim()) && !isBusy : pdfUploadFiles.length >= 2 && !isBusy;
  const reportInvoiceIdList = reportInvoices.map((invoice) => invoice.id);
  const canStartReportZip = canCreateInvoiceReportZip && !invoiceReportBatchIssue(reportInvoiceIdList.length, reportTemplatePaths, reportTemplates) && (!desktopAvailable || Boolean(reportZipDestination.trim())) && !isBusy;
  return { message, messageTone, pdfSources, pdfDestination, pdfUploadFiles, reportInvoices, reportZipDestination, reportTemplatePaths, reportWithSeal, setPdfSources, setPdfDestination, setPdfUploadFiles, setReportInvoices, setReportZipDestination, setReportTemplatePaths, setReportWithSeal, clearFeedback, showSuccess, handleChildMessage, handleCancelJob, handleDeleteJob, handleClearFinishedJobs, cancelMutation, retryMutation, deleteMutation, clearFinishedMutation, pdfMergeMutation, reportZipMutation, downloadMutation, isBusy, canStartPdfMerge, canStartReportZip, reportInvoiceIdList, defaultExportDirectory };
}
