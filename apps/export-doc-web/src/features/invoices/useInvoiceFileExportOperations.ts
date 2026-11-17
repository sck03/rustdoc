import { useMemo, useState } from "react";
import type { ApiReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectSaveExcelPath, selectSavePdfPath } from "../../desktop/desktopBridge.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { buildReportPdfDefaultFileName } from "../reports/reportFileNames.ts";
import { buildInvoiceBookingSheetDefaultFileName } from "./invoiceReportPreviewModel.ts";

type Options = {
  client: ExportDocManagerApiClient;
  invoiceId: number;
  invoiceNo?: string;
  sourceRevision?: unknown;
  catalogRevision?: number;
  pdfAllowed: boolean;
  bookingAllowed: boolean;
  templates: ApiReportTemplateDto[];
  selectedTemplatePath: string;
  withSeal: boolean;
  desktopAvailable: boolean;
  defaultExportDirectory: string;
  feedback: { clear(): void; showError(message: string): void; showJob(message: string, jobId: string): void };
};
type ExportInput = { kind: "pdf" | "booking"; mode: "pick" | "dialog" | "generate" };

export function useInvoiceFileExportOperations(options: Options) {
  const { client, invoiceId, invoiceNo, templates, selectedTemplatePath, withSeal, desktopAvailable, defaultExportDirectory, feedback } = options;
  const [pdfDestinationPath, setPdfDestinationPath] = useState("");
  const [bookingSheetDestinationPath, setBookingSheetDestinationPath] = useState("");
  const owner = useMemo(() => ({}), [client, invoiceId, options.sourceRevision, options.catalogRevision,
    options.pdfAllowed, options.bookingAllowed, selectedTemplatePath, withSeal, desktopAvailable, templates]);
  const pdfDefaultFileName = buildReportPdfDefaultFileName({
    templatePath: selectedTemplatePath,
    displayName: templates.find(item => item.templatePath === selectedTemplatePath)?.displayName,
    fallbackTitle: "ExportDocument", documentNumber: invoiceNo?.trim() || `invoice-${invoiceId}`,
  });
  const bookingSheetDefaultFileName = buildInvoiceBookingSheetDefaultFileName(invoiceNo, invoiceId);
  const command = useScopedMutation(`invoice-export:${invoiceId}`, owner, {
    mutationFn: async ({ kind, mode }: ExportInput, signal, { checkCurrent }) => {
      const pdf = kind === "pdf";
      if (!(pdf ? options.pdfAllowed && selectedTemplatePath : options.bookingAllowed)) return null;
      const fileName = pdf ? pdfDefaultFileName : bookingSheetDefaultFileName;
      let destinationPath = pdf ? pdfDestinationPath : bookingSheetDestinationPath;
      feedback.clear();
      if (desktopAvailable && mode !== "generate") {
        const selected = await (pdf ? selectSavePdfPath : selectSaveExcelPath)(fileName, defaultExportDirectory);
        checkCurrent();
        if (!selected) return null;
        destinationPath = selected;
        (pdf ? setPdfDestinationPath : setBookingSheetDestinationPath)(selected);
      }
      if (mode === "pick") return null;
      checkCurrent();
      const job = pdf
        ? desktopAvailable
          ? await client.startInvoiceReportPdfSaveToPathJob({ invoiceId, body: { reportType: "ExportDocument", templatePath: selectedTemplatePath, withSeal, destinationPath: destinationPath.trim() } }, { signal })
          : await client.startInvoiceReportPdfDownloadJob({ invoiceId, body: { reportType: "ExportDocument", templatePath: selectedTemplatePath, withSeal, destinationPath: "" } }, { signal })
        : desktopAvailable
          ? await client.startInvoiceBookingSheetSaveToPathJob({ body: { invoiceId, destinationPath: destinationPath.trim() } }, { signal })
          : await client.startInvoiceBookingSheetDownloadJob({ invoiceId }, { signal });
      checkCurrent();
      if (!desktopAvailable) await downloadJobResultWhenReady(client, job, fileName, { signal });
      return job;
    },
    onSuccess: (job, { kind }) => {
      if (job) feedback.showJob(desktopAvailable ? `已创建${kind === "pdf" ? "报表 PDF" : "发票托单"}任务：${job.jobId}` : "文件已交给浏览器下载。", job.jobId);
    },
    refreshKeys: job => job ? [queryKeys.jobsRoot()] : [],
    onError: error => feedback.showError(readApiError(error)),
  });
  return {
    pdfDestinationPath, bookingSheetDestinationPath, isPending: command.isPending,
    changePdfDestination(value: string) { setPdfDestinationPath(value); feedback.clear(); },
    changeBookingSheetDestination(value: string) { setBookingSheetDestinationPath(value); feedback.clear(); },
    pickPdfDestination: () => command.mutateAsync({ kind: "pdf", mode: "pick" }),
    pickBookingSheetDestination: () => command.mutateAsync({ kind: "booking", mode: "pick" }),
    exportPdfWithSaveDialog: () => command.mutateAsync({ kind: "pdf", mode: "dialog" }),
    exportBookingSheetWithSaveDialog: () => command.mutateAsync({ kind: "booking", mode: "dialog" }),
    generatePdf: () => command.mutate({ kind: "pdf", mode: "generate" }),
    generateBookingSheet: () => command.mutate({ kind: "booking", mode: "generate" }),
  };
}

export type InvoiceFileExportOperations = ReturnType<typeof useInvoiceFileExportOperations>;
