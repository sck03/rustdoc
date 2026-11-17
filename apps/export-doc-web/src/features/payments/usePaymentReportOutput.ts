import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSavePdfPath } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { printReportPreviewHtml } from "../reports/printReportPreview.ts";

export function usePaymentReportOutput(client: ExportDocManagerApiClient, source: {
  paymentId: number; templatePath: string; defaultFileName: string; defaultExportDirectory: string;
  canPrint: boolean; canExport: boolean;
}) {
  const [result, setResult] = useState<{ message?: string; error?: string; jobId?: string } | null>(null);
  const desktop = isDesktopBridgeAvailable();
  const scope = JSON.stringify([source.paymentId, source.templatePath, source.canPrint, source.canExport]);
  useEffect(() => setResult(null), [scope, client]);
  const commands = useScopedMutation<boolean, { kind: "pdf" } | { kind: "print"; html: string }>(scope, client, {
    mutationFn: async (command, signal, { checkCurrent }) => {
      if (command.kind === "print") {
        if (!source.canPrint) return false;
        await printReportPreviewHtml(command.html, "付款/报销单打印预览");
        checkCurrent();
        setResult({ message: "已打开打印对话框。" });
        return false;
      }
      if (!source.canExport) return false;
      const { paymentId, templatePath, defaultFileName, defaultExportDirectory } = source;
      const destinationPath = desktop ? await selectSavePdfPath(defaultFileName, defaultExportDirectory) : "";
      checkCurrent();
      if (desktop && !destinationPath) return false;
      const job = desktop
        ? await client.startPaymentVoucherPdfSaveToPathJob({ paymentId, body: { templatePath, destinationPath: destinationPath! } }, { signal })
        : await client.startPaymentVoucherPdfDownloadJob({ paymentId, body: { templatePath, destinationPath: "" } }, { signal });
      checkCurrent();
      if (!desktop) await downloadJobResultWhenReady(client, job, defaultFileName, { signal });
      checkCurrent();
      setResult({ jobId: job.jobId, message: desktop
        ? `已创建 PDF 任务：${defaultFileName}，可查看进度和结果。`
        : `${defaultFileName} 已交给浏览器下载。` });
      return true;
    },
    refreshKeys: exported => exported ? [queryKeys.jobsRoot()] : [],
    onError: error => setResult({ error: readApiError(error) }),
    onRefreshError: error => setResult(current => ({ ...current, error: `输出已完成，任务列表刷新失败：${readApiError(error)}` })),
  });
  return {
    desktop, busy: commands.isPending, error: result?.error ?? null,
    message: result?.message ?? null, jobId: result?.jobId ?? null,
    print: (html: string) => { setResult(null); commands.mutate({ kind: "print", html }); },
    exportPdf: () => { setResult(null); commands.mutate({ kind: "pdf" }); },
  };
}
