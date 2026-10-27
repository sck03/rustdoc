import { useMutation } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
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
  mode: string;
  canDesignTemplates: boolean;
  withSeal: boolean;
  previewInvoiceId: number;
  previewPaymentId: number;
  onPreviewed: (response: ApiReportTemplatePreviewResponse) => void;
  onError: (error: unknown) => void;
}) {
  const [previewFormat, setPreviewFormat] = useState<"Svg" | "PdfPage">("Svg");
  const [page, setPage] = useState({ pageIndex: 0, pageCount: 0, previewRevision: "" });
  const scope = useMemo(() => ({ mode, reportType, selectedTemplatePath, content, sampleProfile, withSeal, previewInvoiceId, previewPaymentId, previewFormat }),
    [mode, reportType, selectedTemplatePath, content, sampleProfile, withSeal, previewInvoiceId, previewPaymentId, previewFormat]);
  useEffect(() => setPage({ pageIndex: 0, pageCount: 0, previewRevision: "" }), [scope]);
  type PageRequest = { pageIndex?: number; previewRevision?: string; content?: string };
  const request = (target?: PageRequest) => ({ previewFormat, pageIndex: target?.pageIndex ?? 0, previewRevision: target?.previewRevision || undefined });
  const accept = (response: ApiReportTemplatePreviewResponse) => {
    setPage({ pageIndex: response.pageIndex ?? 0, pageCount: response.pageCount ?? 0, previewRevision: response.previewRevision ?? "" });
    onPreviewed(response);
  };
  const run = useAbortableOperation(scope, { cancelPrevious: true });
  const failed = (error: unknown) => { if (!isAbortError(error)) onError(error); };
  const samplePreviewMutation = useMutation({
    gcTime: 0,
    mutationFn: (input?: string | PageRequest) =>
      run(signal => client.previewReportTemplateContent({
        body: reportType === "ExportDocument"
          ? { reportType, content: typeof input === "string" ? input : content, withSeal, sampleProfile, ...request(typeof input === "object" ? input : undefined) }
          : { reportType, content: typeof input === "string" ? input : content, sampleProfile, ...request(typeof input === "object" ? input : undefined) },
      }, { signal })),
    onSuccess: accept,
    onError: failed,
  });

  const invoicePreviewMutation = useMutation({
    gcTime: 0,
    mutationFn: (target?: PageRequest) =>
      run(signal => client.previewInvoiceReportHtml({
        invoiceId: previewInvoiceId,
        body: { reportType, templatePath: selectedTemplatePath, withSeal, content: canDesignTemplates ? target?.content ?? content : undefined, ...request(target) },
      }, { signal })),
    onSuccess: (response) => accept({ ...response, withSeal: response.withSeal ?? withSeal }),
    onError: failed,
  });

  const paymentPreviewMutation = useMutation({
    gcTime: 0,
    mutationFn: (target?: PageRequest) =>
      run(signal => client.previewPaymentVoucherHtml({
        paymentId: previewPaymentId,
        body: { templatePath: selectedTemplatePath, content: canDesignTemplates ? target?.content ?? content : undefined, ...request(target) },
      }, { signal })),
    onSuccess: (response) => accept({ ...response, withSeal: null }),
    onError: failed,
  });

  return { samplePreviewMutation, invoicePreviewMutation, paymentPreviewMutation, previewFormat, setPreviewFormat, page };
}
