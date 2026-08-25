import { useQuery } from "@tanstack/react-query";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { previewSourcePageSize, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateWorkspaceQueries({
  client,
  reportType,
  enabled,
  includeDesignerData,
  selectedUserTemplateId,
  selectedTemplatePath,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  enabled: boolean;
  includeDesignerData: boolean;
  selectedUserTemplateId: number;
  selectedTemplatePath: string;
}) {
  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(reportType),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType }, { signal }),
    enabled,
    staleTime: 5 * 60 * 1000,
  });

  const userTemplatesQuery = useQuery({
    queryKey: queryKeys.userReportTemplates(reportType),
    queryFn: ({ signal }) => client.listUserReportTemplates({ reportType, includeInactive: true }, { signal }),
    enabled,
    staleTime: 60 * 1000,
  });

  const userTemplateVersionsQuery = useQuery({
    queryKey: queryKeys.userReportTemplateVersions(selectedUserTemplateId),
    queryFn: ({ signal }) => client.listUserReportTemplateVersions({ id: selectedUserTemplateId }, { signal }),
    enabled: enabled && selectedUserTemplateId > 0,
    staleTime: 30 * 1000,
  });

  const fieldCatalogQuery = useQuery({
    queryKey: queryKeys.reportTemplateFields(reportType),
    queryFn: ({ signal }) => client.getReportTemplateFieldCatalog({ reportType }, { signal }),
    enabled: enabled && includeDesignerData,
    staleTime: 60 * 60 * 1000,
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
    enabled: enabled && includeDesignerData && reportType === "ExportDocument",
    staleTime: 60 * 1000,
  });

  const previewPaymentsQuery = useQuery({
    queryKey: queryKeys.reportTemplatePreviewPayments(previewSourcePageSize),
    queryFn: ({ signal }) => client.listPayments({ pageNumber: 1, pageSize: previewSourcePageSize }, { signal }),
    enabled: enabled && includeDesignerData && reportType === "PaymentVoucher",
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
    userTemplateVersionsQuery,
    fieldCatalogQuery,
    previewInvoicesQuery,
    previewPaymentsQuery,
    settingsQuery,
    templateContentQuery,
  };
}
