import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { ApiReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectSaveExcelPath, selectSavePdfPath } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { buildReportPdfDefaultFileName } from "../reports/reportFileNames.ts";
import { buildInvoiceBookingSheetDefaultFileName } from "./invoiceReportPreviewModel.ts";

type Feedback = {
  clear(): void;
  showError(message: string): void;
  showJob(message: string, jobId: string): void;
};

type Options = {
  client: ExportDocManagerApiClient;
  invoiceId: number;
  invoiceNo?: string;
  templates: ApiReportTemplateDto[];
  selectedTemplatePath: string;
  withSeal: boolean;
  desktopAvailable: boolean;
  defaultExportDirectory: string;
  feedback: Feedback;
};

export function useInvoiceFileExportOperations(options: Options) {
  const {
    client,
    invoiceId,
    invoiceNo,
    templates,
    selectedTemplatePath,
    withSeal,
    desktopAvailable,
    defaultExportDirectory,
    feedback,
  } = options;
  const queryClient = useQueryClient();
  const [pdfDestinationPath, setPdfDestinationPath] = useState("");
  const [bookingSheetDestinationPath, setBookingSheetDestinationPath] = useState("");

  const pdfDefaultFileName = buildReportPdfDefaultFileName({
    templatePath: selectedTemplatePath,
    displayName: templates.find((item) => item.templatePath === selectedTemplatePath)?.displayName,
    fallbackTitle: "ExportDocument",
    documentNumber: invoiceNo?.trim() || `invoice-${invoiceId}`,
  });
  const bookingSheetDefaultFileName = buildInvoiceBookingSheetDefaultFileName(invoiceNo, invoiceId);

  const pdfMutation = useMutation({
    mutationFn: async (destinationPath?: string) => {
      const job = desktopAvailable
        ? await client.startInvoiceReportPdfSaveToPathJob({
            invoiceId,
            body: {
              reportType: "ExportDocument",
              templatePath: selectedTemplatePath,
              withSeal,
              destinationPath: (destinationPath ?? pdfDestinationPath).trim(),
            },
          })
        : await client.startInvoiceReportPdfDownloadJob({
            invoiceId,
            body: { reportType: "ExportDocument", templatePath: selectedTemplatePath, withSeal, destinationPath: "" },
          });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, pdfDefaultFileName);
      }
      return job;
    },
    onSuccess: async (job) => {
      feedback.showJob(desktopAvailable ? `已创建报表 PDF 任务：${job.jobId}` : "PDF 已交给浏览器下载。", job.jobId);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });

  const bookingSheetMutation = useMutation({
    mutationFn: async (destinationPath?: string) => {
      const job = desktopAvailable
        ? await client.startInvoiceBookingSheetSaveToPathJob({
            body: { invoiceId, destinationPath: (destinationPath ?? bookingSheetDestinationPath).trim() },
          })
        : await client.startInvoiceBookingSheetDownloadJob({ invoiceId });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, bookingSheetDefaultFileName);
      }
      return job;
    },
    onSuccess: async (job) => {
      feedback.showJob(desktopAvailable ? `已创建发票托单任务：${job.jobId}` : "托单 Excel 已交给浏览器下载。", job.jobId);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => feedback.showError(readApiError(error)),
  });

  async function pickPdfDestination() {
    try {
      const selected = await selectSavePdfPath(pdfDefaultFileName, defaultExportDirectory);
      if (selected) {
        setPdfDestinationPath(selected);
        feedback.clear();
      }
    } catch (error) {
      feedback.showError(readDesktopError(error));
    }
  }

  async function exportPdfWithSaveDialog() {
    if (!desktopAvailable) {
      feedback.clear();
      pdfMutation.mutate(undefined);
      return;
    }

    try {
      const selected = await selectSavePdfPath(pdfDefaultFileName, defaultExportDirectory);
      if (selected) {
        setPdfDestinationPath(selected);
        feedback.clear();
        pdfMutation.mutate(selected);
      }
    } catch (error) {
      feedback.showError(readDesktopError(error));
    }
  }

  async function pickBookingSheetDestination() {
    try {
      const selected = await selectSaveExcelPath(bookingSheetDefaultFileName, defaultExportDirectory);
      if (selected) {
        setBookingSheetDestinationPath(selected);
        feedback.clear();
      }
    } catch (error) {
      feedback.showError(readDesktopError(error));
    }
  }

  async function exportBookingSheetWithSaveDialog() {
    if (!desktopAvailable) {
      feedback.clear();
      bookingSheetMutation.mutate(undefined);
      return;
    }

    try {
      const selected = await selectSaveExcelPath(bookingSheetDefaultFileName, defaultExportDirectory);
      if (selected) {
        setBookingSheetDestinationPath(selected);
        feedback.clear();
        bookingSheetMutation.mutate(selected);
      }
    } catch (error) {
      feedback.showError(readDesktopError(error));
    }
  }

  return {
    pdfDestinationPath,
    bookingSheetDestinationPath,
    isPending: pdfMutation.isPending || bookingSheetMutation.isPending,
    changePdfDestination(value: string) {
      setPdfDestinationPath(value);
      feedback.clear();
    },
    changeBookingSheetDestination(value: string) {
      setBookingSheetDestinationPath(value);
      feedback.clear();
    },
    pickPdfDestination,
    pickBookingSheetDestination,
    exportPdfWithSaveDialog,
    exportBookingSheetWithSaveDialog,
    generatePdf: () => pdfMutation.mutate(undefined),
    generateBookingSheet: () => bookingSheetMutation.mutate(undefined),
  };
}

export type InvoiceFileExportOperations = ReturnType<typeof useInvoiceFileExportOperations>;
