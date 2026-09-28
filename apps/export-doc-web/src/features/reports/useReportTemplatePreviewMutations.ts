import { useMutation } from "@tanstack/react-query";
import {
  ApiReportTemplatePreviewResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplatePreviewMutations({
  client,
  reportType,
  selectedTemplatePath,
  content,
  sampleProfile,
  canDesignTemplates,
  withSeal,
  previewInvoiceId,
  previewPaymentId,
  onPreviewed,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  content: string;
  sampleProfile: string;
  canDesignTemplates: boolean;
  withSeal: boolean;
  previewInvoiceId: number;
  previewPaymentId: number;
  onPreviewed: (response: ApiReportTemplatePreviewResponse) => void;
  onError: (error: unknown) => void;
}) {
  const samplePreviewMutation = useMutation({
    mutationFn: (nextContent?: string) =>
      client.previewReportTemplateContent({
        body: reportType === "ExportDocument"
          ? { reportType, content: nextContent ?? content, withSeal, sampleProfile }
          : { reportType, content: nextContent ?? content, sampleProfile },
      }),
    onSuccess: onPreviewed,
    onError,
  });

  const invoicePreviewMutation = useMutation({
    mutationFn: () =>
      client.previewInvoiceReportHtml({
        invoiceId: previewInvoiceId,
        body: { reportType, templatePath: selectedTemplatePath, withSeal, content: canDesignTemplates ? content : undefined },
      }),
    onSuccess: (response) => onPreviewed({ reportType: response.reportType, withSeal: response.withSeal ?? withSeal, html: response.html }),
    onError,
  });

  const paymentPreviewMutation = useMutation({
    mutationFn: () =>
      client.previewPaymentVoucherHtml({
        paymentId: previewPaymentId,
        body: { templatePath: selectedTemplatePath, content: canDesignTemplates ? content : undefined },
      }),
    onSuccess: (response) => onPreviewed({ reportType: response.reportType, withSeal: null, html: response.html }),
    onError,
  });

  return { samplePreviewMutation, invoicePreviewMutation, paymentPreviewMutation };
}
