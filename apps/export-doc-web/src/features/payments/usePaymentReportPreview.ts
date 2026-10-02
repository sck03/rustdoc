import { useEffect, useState } from "react";
import type { ApiPaymentDto, ApiPaymentReportHtmlPreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";

type PreviewRequest = { key: string; paymentId: number; paymentDraft?: ApiPaymentDto; templatePath: string };
type PreviewResult = { request: PreviewRequest; preview?: ApiPaymentReportHtmlPreviewResponse; error?: string };

export function usePaymentReportPreview(client: ExportDocManagerApiClient, source: Omit<PreviewRequest, "key">, revision: string, enabled: boolean) {
  const key = JSON.stringify([source.paymentId, source.paymentDraft, source.templatePath, revision, enabled]);
  const [request, setRequest] = useState<PreviewRequest | null>(null);
  const [result, setResult] = useState<PreviewResult | null>(null);
  useEffect(() => {
    if (!request || request.key !== key || !enabled) return;
    const controller = new AbortController();
    const { paymentId, paymentDraft, templatePath } = request;
    const operation = paymentDraft
      ? client.previewPaymentVoucherDraftHtml({ body: { payment: paymentDraft, templatePath } }, { signal: controller.signal })
      : client.previewPaymentVoucherHtml({ paymentId, body: { templatePath } }, { signal: controller.signal });
    void operation.then(preview => {
      if (!controller.signal.aborted) setResult({ request, preview });
    }, error => {
      if (!controller.signal.aborted) setResult({ request, error: readApiError(error) });
    });
    return () => controller.abort();
  }, [client, enabled, key, request]);
  // Check identity during render too: old output must never become printable
  // for a different draft, record, or template, even if cancellation is late.
  const active = enabled && request?.key === key;
  const settled = active && result?.request === request ? result : null;
  return {
    preview: settled?.preview ?? null, error: settled?.error ?? null,
    busy: Boolean(active && !settled),
    generate: () => { if (enabled) setRequest({ ...source, key }); },
  };
}
