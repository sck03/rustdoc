import { queryKeys } from "../../api/queryKeys.ts";

/** All invoice writes invalidate the same dependent views, regardless of entry point. */
export function invoiceViewKeys(id?: number) {
  const roots = [queryKeys.invoicesRoot(), queryKeys.queryInvoicesRoot(), queryKeys.dashboard(), queryKeys.invoiceParties(),
    queryKeys.reportTemplatePreviewSourcesRoot(), queryKeys.singleWindowOperationCenterRoot()];
  return [
    ...roots,
    ...(id ? [queryKeys.invoiceStatusHistory(id), queryKeys.singleWindowCustomsCooDocument(id), queryKeys.singleWindowCustomsCooExportReview(id),
      queryKeys.singleWindowAgentConsignmentDocument(id), queryKeys.singleWindowAgentConsignmentExportReview(id)] : []),
  ];
}
