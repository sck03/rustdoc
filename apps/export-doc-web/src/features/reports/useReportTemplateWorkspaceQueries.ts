import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";
import { useReportTemplatePreviewSources } from "./useReportTemplatePreviewSources.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

export function useReportTemplateWorkspaceQueries({
  client,
  reportType,
  enabled,
  includeDesignerData,
  includePreviewData,
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
  includePreviewData: boolean;
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
    ...largeDocumentQueryOptions,
    queryKey: queryKeys.userReportTemplateContent(reportType, selectedUserTemplateId),
    queryFn: ({ signal }) => client.getUserReportTemplate({ id: selectedUserTemplateId }, { signal }),
    enabled: enabled && selectedUserTemplateId > 0,
    staleTime: 30 * 1000,
  });
  useServerPageNumber(userTemplatesQuery, directory.pageNumber, pageNumber => setDirectory({ ...directory, pageNumber }));

  const fieldCatalogQuery = useQuery({
    queryKey: queryKeys.reportTemplateFields(reportType),
    queryFn: ({ signal }) => client.getReportTemplateFieldCatalog({ reportType }, { signal }),
    enabled: enabled && includeDesignerData,
    staleTime: 0,
  });

  const previewSources = useReportTemplatePreviewSources({
    client, reportType, canPreviewInvoiceSource, canPreviewPaymentSource,
    enabled: enabled && includeDesignerData && includePreviewData,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    staleTime: 5 * 60 * 1000,
  });

  const templateContentQuery = useQuery({
    ...largeDocumentQueryOptions,
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
    ...previewSources,
    settingsQuery,
    templateContentQuery,
  };
}
