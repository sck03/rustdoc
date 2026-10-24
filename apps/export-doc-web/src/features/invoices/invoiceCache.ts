import type { QueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../api/queryKeys.ts";
export const invoiceControlMutationKey = (id: number) => ["invoice-control-write", id] as const;

/** All invoice writes invalidate the same dependent views, regardless of entry point. */
export function invalidateInvoiceViews(client: QueryClient, id?: number) {
  const roots = [queryKeys.invoicesRoot(), queryKeys.queryInvoicesRoot(), queryKeys.dashboard(), queryKeys.invoiceParties(),
    queryKeys.reportTemplatePreviewSourcesRoot(), queryKeys.singleWindowOperationCenterRoot()];
  return Promise.all([
    ...roots.map(queryKey => client.invalidateQueries({ queryKey }, { throwOnError: true })),
    ...(id ? [queryKeys.invoiceStatusHistory(id), queryKeys.singleWindowCustomsCooDocument(id), queryKeys.singleWindowCustomsCooExportReview(id),
      queryKeys.singleWindowAgentConsignmentDocument(id), queryKeys.singleWindowAgentConsignmentExportReview(id)]
      .map(queryKey => client.invalidateQueries({ queryKey }, { throwOnError: true })) : []),
  ]);
}
