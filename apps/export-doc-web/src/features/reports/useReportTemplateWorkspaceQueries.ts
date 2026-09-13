import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { previewSourcePageSize, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateWorkspaceQueries({
  client,
  reportType,
  enabled,
  includeDesignerData,
  includeArchived,
  canPreviewInvoiceSource,
  canPreviewPaymentSource,
  selectedUserTemplateId,
  selectedTemplatePath,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  enabled: boolean;
  includeDesignerData: boolean;
  includeArchived: boolean;
  canPreviewInvoiceSource: boolean;
  canPreviewPaymentSource: boolean;
  selectedUserTemplateId: number;
  selectedTemplatePath: string;
}) {
  const [directoryState, setDirectory] = useState({ reportType, search: "", pageNumber: 1 });
  const directory = directoryState.reportType === reportType ? directoryState : { reportType, search: "", pageNumber: 1 };
  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(reportType),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType }, { signal }),
    enabled,
    staleTime: 5 * 60 * 1000,
  });

  const userTemplatesQuery = useQuery({
    queryKey: [...queryKeys.userReportTemplates(reportType), "directory", directory.search, directory.pageNumber, includeArchived],
    queryFn: ({ signal }) => client.listUserReportTemplates({ reportType, includeArchived, keyword: directory.search, pageNumber: directory.pageNumber, pageSize: 50 }, { signal }),
    enabled,
    staleTime: 60 * 1000,
  });

  const userTemplateContentQuery = useQuery({
    queryKey: queryKeys.userReportTemplateContent(reportType, selectedUserTemplateId),
    queryFn: ({ signal }) => client.getUserReportTemplate({ id: selectedUserTemplateId }, { signal }),
    enabled: enabled && selectedUserTemplateId > 0,
    staleTime: 30 * 1000,
  });
  useEffect(() => {
    if (userTemplatesQuery.data && directory.pageNumber > Math.max(1, userTemplatesQuery.data.totalPages)) {
      setDirectory({ ...directory, pageNumber: Math.max(1, userTemplatesQuery.data.totalPages) });
    }
  }, [directory, userTemplatesQuery.data]);

  const fieldCatalogQuery = useQuery({
    queryKey: queryKeys.reportTemplateFields(reportType),
    queryFn: ({ signal }) => client.getReportTemplateFieldCatalog({ reportType }, { signal }),
    enabled: enabled && includeDesignerData,
    staleTime: 0,
  });

  const previewInvoicesQuery = useQuery({
    queryKey: queryKeys.reportTemplatePreviewInvoices(previewSourcePageSize),
    queryFn: ({ signal }) =>
      client.listInvoices({
        pageNumber: 1,
        pageSize: previewSourcePageSize,
        sortColumn: "InvoiceDate",
        ascending: false,
      }, { signal }),
    enabled: enabled && includeDesignerData && canPreviewInvoiceSource && reportType === "ExportDocument",
    staleTime: 60 * 1000,
  });

  const previewPaymentsQuery = useQuery({
    queryKey: queryKeys.reportTemplatePreviewPayments(previewSourcePageSize),
    queryFn: ({ signal }) => client.listPayments({ pageNumber: 1, pageSize: previewSourcePageSize }, { signal }),
    enabled: enabled && includeDesignerData && canPreviewPaymentSource && reportType === "PaymentVoucher",
    staleTime: 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    staleTime: 5 * 60 * 1000,
  });

  const templateContentQuery = useQuery({
    queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath),
    queryFn: ({ signal }) => client.getReportTemplateContent({ reportType, templatePath: selectedTemplatePath }, { signal }),
    enabled: enabled && Boolean(selectedTemplatePath) && selectedUserTemplateId <= 0 && !selectedTemplatePath.startsWith("user-template:"),
  });

  return {
    templatesQuery,
    userTemplatesQuery,
    userTemplateContentQuery,
    userTemplateDirectory: {
      search: directory.search,
      pageNumber: directory.pageNumber,
      totalPages: userTemplatesQuery.data?.totalPages ?? 1,
      totalCount: userTemplatesQuery.data?.totalCount ?? 0,
      loading: userTemplatesQuery.isFetching,
      onSearchChange: (search: string) => setDirectory({ reportType, search, pageNumber: 1 }),
      onPageChange: (pageNumber: number) => setDirectory({ ...directory, pageNumber }),
    },
    fieldCatalogQuery,
    previewInvoicesQuery,
    previewPaymentsQuery,
    settingsQuery,
    templateContentQuery,
  };
}
