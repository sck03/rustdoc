import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSavePdfPath } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";

export function usePaymentReportOutput(client: ExportDocManagerApiClient, source: {
  paymentId: number; templatePath: string; defaultFileName: string; defaultExportDirectory: string;
}) {
  const queryClient = useQueryClient();
  const run = useAbortableOperation();
  const [printing, setPrinting] = useState(false);
  const [printResult, setPrintResult] = useState<{ message?: string; error?: string } | null>(null);
  const desktop = isDesktopBridgeAvailable();
  const pdf = useMutation({
    mutationFn: () => run(async signal => {
      const { paymentId, templatePath, defaultFileName, defaultExportDirectory } = source;
      const destinationPath = desktop ? await selectSavePdfPath(defaultFileName, defaultExportDirectory) : "";
      // Cancelling the dialog or leaving the page cannot enqueue an output job.
      if (signal.aborted || desktop && !destinationPath) return null;
      const job = desktop
        ? await client.startPaymentVoucherPdfSaveToPathJob({ paymentId, body: { templatePath, destinationPath: destinationPath! } }, { signal })
        : await client.startPaymentVoucherPdfDownloadJob({ paymentId, body: { templatePath, destinationPath: "" } }, { signal });
      if (!desktop) await downloadJobResultWhenReady(client, job, defaultFileName, { signal });
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
      return { job, fileName: defaultFileName };
    }),
  });
  async function print(html: string) {
    setPrinting(true);
    setPrintResult(null);
    pdf.reset();
    try {
      await printReportPreviewHtml(html, "付款/报销单打印预览");
      setPrintResult({ message: "已打开打印对话框。" });
    } catch (error) {
      setPrintResult({ error: readApiError(error) });
    } finally {
      setPrinting(false);
    }
  }
  return {
    desktop, busy: printing || pdf.isPending,
    error: printResult?.error ?? (pdf.isError ? readApiError(pdf.error) : null),
    message: printResult?.message ?? (pdf.data ? desktop ? `已创建 PDF 任务：${pdf.data.fileName}，可查看进度和结果。` : `${pdf.data.fileName} 已交给浏览器下载。` : null),
    jobId: pdf.data?.job.jobId ?? null, print,
    exportPdf: () => { setPrintResult(null); pdf.mutate(); },
  };
}
