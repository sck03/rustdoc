import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiInvoiceListItemDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { InvoiceReportZipJobPanel } from "../jobs/JobCreationPanels.tsx";
import { createInvoiceReportExport } from "../jobs/invoiceReportExport.ts";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { readDefaultReportTemplatePath } from "../reports/reportTemplateSelectionModel.ts";
import { invoiceReportBatchIssue, invoiceReportBatchLimits, useInvoiceReportBatchOptions } from "../jobs/useInvoiceReportBatchOptions.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";

export function InvoiceBatchReportPanel({ client, selected, onChange, hidden = false }: {
  client: ExportDocManagerApiClient; selected: ApiInvoiceListItemDto[]; onChange: (items: ApiInvoiceListItemDto[]) => void;
  hidden?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [destinationPath, setDestinationPath] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const run = useAbortableOperation();
  const queries = useQueryClient();
  const desktop = isDesktopBridgeAvailable();
  const templatesQuery = useQuery({ queryKey: queryKeys.reportTemplates("ExportDocument"),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType: "ExportDocument" }, { signal }), enabled: open && !hidden });
  const settingsQuery = useQuery({ queryKey: queryKeys.settings(), queryFn: ({ signal }) => client.getSettings({ signal }), enabled: open && !hidden });
  const templates = templatesQuery.data ?? [];
  const { templatePaths, setTemplatePaths, withSeal, setWithSeal } = useInvoiceReportBatchOptions(templates, readDefaultReportTemplatePath(settingsQuery.data?.settings, "ExportDocument"));
  const issue = invoiceReportBatchIssue(selected.length, templatePaths, templates);
  const exportMutation = useMutation({
    mutationFn: () => run((signal) => createInvoiceReportExport(client, {
      invoiceIds: selected.map((item) => item.id), reportType: "ExportDocument", templatePath: "", templatePaths,
      withSeal, destinationPath: desktop ? destinationPath : "",
    }, desktop, signal)),
    onSuccess: () => { setOpen(false); void queries.invalidateQueries({ queryKey: queryKeys.jobsRoot() }); },
  });
  return <div className="invoice-batch-report" hidden={hidden}>
    {!selected.length && !open ? <p className="section-description">勾选下方发票后可生成批量报表，支持跨页选择。</p> : <div className="list-workspace-selection"><strong>已选 {selected.length} 张发票</strong>
      <button className="command-button secondary" type="button" disabled={!selected.length || exportMutation.isPending} onClick={() => setOpen((value) => !value)}>{open ? "收起批量报表" : "生成批量报表"}</button>
      {selected.length > 0 && <button className="command-button secondary" type="button" disabled={exportMutation.isPending} onClick={() => onChange([])}>清空选择</button>}
      <span className="section-description">可跨页选择，单次最多生成 {invoiceReportBatchLimits.maxReports} 份 PDF</span>
    </div>}
    {exportMutation.isError && <InlineNotice tone="error">{readApiError(exportMutation.error)}</InlineNotice>}
    {exportMutation.data && <InlineNotice tone="success" action={<ViewJobButton jobId={exportMutation.data.jobId} />}>批量报表任务已创建，可在文件任务中查看进度和结果。</InlineNotice>}
    {message && <InlineNotice tone="error">{message}</InlineNotice>}
    {open && <InvoiceReportZipJobPanel client={client} invoices={selected} onInvoicesChange={onChange}
      destinationPath={destinationPath} onDestinationPathChange={setDestinationPath} templatePaths={templatePaths} onTemplatePathsChange={setTemplatePaths}
      withSeal={withSeal} onWithSealChange={setWithSeal} templates={templates}
      templateErrorMessage={templatesQuery.isError ? readApiError(templatesQuery.error) : null} isTemplateLoading={templatesQuery.isFetching}
      disabled={exportMutation.isPending} canSubmit={Boolean(!issue && (!desktop || destinationPath) && !exportMutation.isPending && !templatesQuery.isFetching && !templatesQuery.isError)}
      onSubmit={() => exportMutation.mutate()} onMessage={setMessage} defaultExportDirectory={readDefaultExportDirectory(settingsQuery.data?.settings)} />}
  </div>;
}
