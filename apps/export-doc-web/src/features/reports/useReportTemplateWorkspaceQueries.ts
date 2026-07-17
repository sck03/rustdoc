import { useQuery } from "@tanstack/react-query";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { previewSourcePageSize, type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateWorkspaceQueries({
  client,
  reportType,
  enabled,
  selectedUserTemplateId,
  selectedTemplatePath,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  enabled: boolean;
  selectedUserTemplateId: number;
  selectedTemplatePath: string;
}) {
  const templatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(reportType),
    queryFn: () => client.listReportTemplates({ reportType }),
    enabled,
    staleTime: 5 * 60 * 1000,
  });

  const userTemplatesQuery = useQuery({
    queryKey: queryKeys.userReportTemplates(reportType),
    queryFn: () => client.listUserReportTemplates({ reportType, includeInactive: true }),
    enabled,
    staleTime: 60 * 1000,
  });

  const userTemplateVersionsQuery = useQuery({
    queryKey: queryKeys.userReportTemplateVersions(selectedUserTemplateId),
    queryFn: () => client.listUserReportTemplateVersions({ id: selectedUserTemplateId }),
    enabled: enabled && selectedUserTemplateId > 0,
    staleTime: 30 * 1000,
  });

  const fieldCatalogQuery = useQuery({
    queryKey: queryKeys.reportTemplateFields(reportType),
    queryFn: () => client.getReportTemplateFieldCatalog({ reportType }),
    enabled,
    staleTime: 60 * 60 * 1000,
  });

  const previewInvoicesQuery = useQuery({
    queryKey: queryKeys.reportTemplatePreviewInvoices(previewSourcePageSize),
    queryFn: () =>
      client.listInvoices({
        pageNumber: 1,
        pageSize: previewSourcePageSize,
        sortColumn: "InvoiceDate",
        ascending: false,
      }),
    enabled: enabled && reportType === "ExportDocument",
    staleTime: 60 * 1000,
  });

  const previewPaymentsQuery = useQuery({
    queryKey: queryKeys.reportTemplatePreviewPayments(previewSourcePageSize),
    queryFn: () => client.listPayments({ pageNumber: 1, pageSize: previewSourcePageSize }),
    enabled: enabled && reportType === "PaymentVoucher",
    staleTime: 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    staleTime: 5 * 60 * 1000,
  });

  const templateContentQuery = useQuery({
    queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath),
    queryFn: () => client.getReportTemplateContent({ reportType, templatePath: selectedTemplatePath }),
    enabled: enabled && Boolean(selectedTemplatePath) && selectedUserTemplateId <= 0,
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
