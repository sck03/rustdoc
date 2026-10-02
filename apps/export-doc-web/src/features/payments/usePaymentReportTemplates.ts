import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { buildPaymentTemplateViews } from "./paymentReportTemplates.ts";
import { readDefaultReportTemplatePath, resolveReportTemplatePath } from "../reports/reportTemplateSelectionModel.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";

export function usePaymentReportTemplates(client: ExportDocManagerApiClient, enabled: boolean) {
  const [requestedPath, selectTemplate] = useState("");
  const templates = useQuery({
    queryKey: queryKeys.reportTemplates("PaymentVoucher"),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType: "PaymentVoucher" }, { signal }),
    enabled, staleTime: 5 * 60 * 1000,
  });
  const settings = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled, staleTime: 5 * 60 * 1000,
  });
  const views = useMemo(() => buildPaymentTemplateViews(templates.data ?? [], settings.data?.settings), [templates.data, settings.data]);
  // Removed or disabled catalog entries cannot linger in selection.
  const selectedPath = resolveReportTemplatePath({ templates: views, currentPath: requestedPath,
    configuredPath: readDefaultReportTemplatePath(settings.data?.settings, "PaymentVoucher"),
    fallbackFileName: "payment_voucher_template.dtpl" });
  return {
    views, selectedPath, selectTemplate,
    ready: enabled && templates.isSuccess && settings.isSuccess && views.length > 0,
    busy: templates.isFetching || settings.isFetching,
    revision: `${templates.dataUpdatedAt}:${settings.dataUpdatedAt}`,
    error: templates.isError ? readApiError(templates.error) : settings.isError ? readApiError(settings.error) : null,
    defaultExportDirectory: readDefaultExportDirectory(settings.data?.settings),
    refresh: () => Promise.all([templates.refetch(), settings.refetch()]),
  };
}
