import { useMemo, useState } from "react";
import { useTransientPreview } from "../../ui/useTransientPreview.ts";
import {
  ApiReportTemplatePreviewResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { type ReportTypeOption, type TemplatePreviewMode } from "./reportTemplateDesignerModel.ts";

export function useReportTemplatePreview({
  client,
  reportType,
  selectedTemplatePath,
  content,
  sampleProfile,
  mode,
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
  mode: TemplatePreviewMode;
  canDesignTemplates: boolean;
  withSeal: boolean;
  previewInvoiceId: number;
  previewPaymentId: number;
  onPreviewed: (response: ApiReportTemplatePreviewResponse) => void;
  onError: (error: unknown) => void;
}) {
  const [previewFormat, setPreviewFormat] = useState<"Svg" | "PdfPage">("Svg");
  const scope = useMemo(() => ({ client, mode, reportType, selectedTemplatePath, content, sampleProfile, withSeal, canDesignTemplates, previewInvoiceId, previewPaymentId, previewFormat }),
    [client, mode, reportType, selectedTemplatePath, content, sampleProfile, withSeal, canDesignTemplates, previewInvoiceId, previewPaymentId, previewFormat]);
  type PageRequest = { pageIndex?: number; previewRevision?: string; content?: string };
  const operation = useTransientPreview<ApiReportTemplatePreviewResponse, PageRequest | undefined>(
    scope,
    async (signal, target) => {
      const request = { previewFormat, pageIndex: target?.pageIndex ?? 0, previewRevision: target?.previewRevision || undefined };
      const draft = target?.content ?? content;
      if (mode === "sample") {
        return client.previewReportTemplateContent({ body: {
          reportType, content: draft, sampleProfile, ...request,
          ...(reportType === "ExportDocument" ? { withSeal } : {}),
        } }, { signal });
      }
      const body = { templatePath: selectedTemplatePath, content: canDesignTemplates ? draft : undefined, ...request };
      if (reportType === "PaymentVoucher") {
        const response = await client.previewPaymentVoucherHtml({ paymentId: previewPaymentId, body }, { signal });
        return { ...response, withSeal: null };
      }
      const response = await client.previewInvoiceReportHtml({ invoiceId: previewInvoiceId, body: { ...body, reportType, withSeal } }, { signal });
      return { ...response, withSeal: response.withSeal ?? withSeal };
    },
    onError,
    onPreviewed,
  );

  return {
    ...operation,
    previewFormat,
    setPreviewFormat,
    page: {
      pageIndex: operation.preview?.pageIndex ?? 0,
      pageCount: operation.preview?.pageCount ?? 0,
      previewRevision: operation.preview?.previewRevision ?? "",
    },
  };
}
