import { useQueryClient } from "@tanstack/react-query";
import type { ApiReportTemplateContentDto, ApiUserReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import type { ReportTypeOption } from "./reportTemplateDesignerModel.ts";

type LoadedTemplate = { kind: "user"; template: ApiUserReportTemplateDto }
  | { kind: "file"; template: ApiReportTemplateContentDto };

export function useReportTemplateReload({ client, reportType, selectedTemplatePath, selectedUserTemplateId, onLoaded, onError }: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  onLoaded: (result: LoadedTemplate) => void;
  onError: (error: unknown) => void;
}) {
  const cache = useQueryClient();
  return useScopedMutation(`${reportType}:${selectedTemplatePath}:${selectedUserTemplateId}`, client, {
    mutationFn: async (_: void, signal): Promise<LoadedTemplate> => selectedUserTemplateId > 0
      ? { kind: "user", template: await client.getUserReportTemplate({ id: selectedUserTemplateId }, { signal }) }
      : { kind: "file", template: await client.getReportTemplateContent({ reportType, templatePath: selectedTemplatePath }, { signal }) },
    onSuccess: result => {
      const key = result.kind === "user"
        ? queryKeys.userReportTemplateContent(reportType, result.template.id)
        : queryKeys.reportTemplateContent(reportType, result.template.templatePath);
      cache.setQueryData(key, result.template);
      onLoaded(result);
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType), queryKeys.userReportTemplates(reportType)],
    onError,
  });
}
