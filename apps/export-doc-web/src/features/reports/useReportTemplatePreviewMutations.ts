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
  withSeal: boolean;
  previewInvoiceId: number;
  previewPaymentId: number;
  onPreviewed: (response: ApiReportTemplatePreviewResponse) => void;
  onError: (error: unknown) => void;
}) {
  const samplePreviewMutation = useMutation({
    mutationFn: (nextContent?: string) =>
      client.previewReportTemplateContent({
        body: { reportType, content: nextContent ?? content, withSeal },
      }),
    onSuccess: onPreviewed,
    onError,
  });

  const invoicePreviewMutation = useMutation({
    mutationFn: () =>
      client.previewInvoiceReportHtml({
        invoiceId: previewInvoiceId,
        body: { reportType, templatePath: selectedTemplatePath, withSeal },
      }),
    onSuccess: (response) => onPreviewed({ reportType: response.reportType, withSeal: response.withSeal, html: response.html }),
    onError,
  });

  const paymentPreviewMutation = useMutation({
    mutationFn: () =>
      client.previewPaymentVoucherHtml({
        paymentId: previewPaymentId,
        body: { reportType: "PaymentVoucher", templatePath: selectedTemplatePath, withSeal },
      }),
    onSuccess: (response) => onPreviewed({ reportType: response.reportType, withSeal: response.withSeal, html: response.html }),
    onError,
  });

  return { samplePreviewMutation, invoicePreviewMutation, paymentPreviewMutation };
}
