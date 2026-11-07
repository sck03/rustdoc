import { useMutation } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import {
  ApiReportTemplatePreviewResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { type ReportTypeOption, type TemplatePreviewMode } from "./reportTemplateDesignerModel.ts";

export function useReportTemplatePreviewMutations({
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
  const [page, setPage] = useState({ pageIndex: 0, pageCount: 0, previewRevision: "" });
  const scope = useMemo(() => ({ mode, reportType, selectedTemplatePath, content, sampleProfile, withSeal, canDesignTemplates, previewInvoiceId, previewPaymentId, previewFormat }),
    [mode, reportType, selectedTemplatePath, content, sampleProfile, withSeal, canDesignTemplates, previewInvoiceId, previewPaymentId, previewFormat]);
  useEffect(() => setPage({ pageIndex: 0, pageCount: 0, previewRevision: "" }), [scope]);
  type PageRequest = { pageIndex?: number; previewRevision?: string; content?: string };
  const run = useAbortableOperation(scope, { cancelPrevious: true });
  const previewMutation = useMutation({
    gcTime: 0,
    mutationFn: (target?: PageRequest) => run(async signal => {
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
    }),
    onSuccess: response => {
      setPage({ pageIndex: response.pageIndex ?? 0, pageCount: response.pageCount ?? 0, previewRevision: response.previewRevision ?? "" });
      onPreviewed(response);
    },
    onError: error => { if (!isAbortError(error)) onError(error); },
  });

  return { previewMutation, previewFormat, setPreviewFormat, page };
}
