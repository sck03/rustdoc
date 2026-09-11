import type { ApiInvoiceReportZipRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";

export async function createInvoiceReportExport(client: ExportDocManagerApiClient, body: ApiInvoiceReportZipRequest, desktop: boolean, signal: AbortSignal) {
  const job = desktop ? await client.startInvoiceReportPdfZipSaveToPathJob({ body }, { signal })
    : await client.startInvoiceReportPdfZipDownloadJob({ body }, { signal });
  if (!desktop) await downloadJobResultWhenReady(client, job, "invoice-reports.zip", { signal });
  return job;
}
