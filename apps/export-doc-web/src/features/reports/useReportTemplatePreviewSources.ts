import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { previewSourcePageSize, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplatePreviewSources({ client, reportType, enabled, canPreviewInvoiceSource, canPreviewPaymentSource }: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  enabled: boolean;
  canPreviewInvoiceSource: boolean;
  canPreviewPaymentSource: boolean;
}) {
  const [state, setState] = useState({ reportType, search: "", pageNumber: 1 });
  const directory = state.reportType === reportType ? state : { reportType, search: "", pageNumber: 1 };
  const keyword = useDebouncedValue(directory.search.trim());
  const ready = enabled && keyword === directory.search.trim();
  const input = { pageNumber: directory.pageNumber, pageSize: previewSourcePageSize, keyword };
  const previewInvoicesQuery = useQuery({
    queryKey: [...queryKeys.reportTemplatePreviewSourcesRoot(), "invoices", input],
    queryFn: ({ signal }) => client.listInvoices({ ...input, sortColumn: "InvoiceDate", ascending: false }, { signal }),
    enabled: ready && canPreviewInvoiceSource && reportType === "ExportDocument",
    staleTime: 60 * 1000,
    gcTime: 0,
  });
  const previewPaymentsQuery = useQuery({
    queryKey: [...queryKeys.reportTemplatePreviewSourcesRoot(), "payments", input],
    queryFn: ({ signal }) => client.listPayments(input, { signal }),
    enabled: ready && canPreviewPaymentSource && reportType === "PaymentVoucher",
    staleTime: 60 * 1000,
    gcTime: 0,
  });
  const query = reportType === "PaymentVoucher" ? previewPaymentsQuery : previewInvoicesQuery;
  const onPageChange = (pageNumber: number) => setState({ ...directory, pageNumber });
  useServerPageNumber(query, directory.pageNumber, onPageChange);
  return {
    previewInvoicesQuery,
    previewPaymentsQuery,
    previewSourceDirectory: {
      search: directory.search,
      pageNumber: directory.pageNumber,
      pageSize: previewSourcePageSize,
      totalCount: query.data?.totalCount ?? 0,
      loading: query.isFetching || keyword !== directory.search.trim(),
      allowed: reportType === "PaymentVoucher" ? canPreviewPaymentSource : canPreviewInvoiceSource,
      onSearchChange: (search: string) => setState({ reportType, search, pageNumber: 1 }),
      onPageChange,
    },
  };
}

export type ReportPreviewSourceDirectory = ReturnType<typeof useReportTemplatePreviewSources>["previewSourceDirectory"];
