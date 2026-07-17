import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ApiReportTemplateContentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useDefaultReportTemplateLifecycleMutations({
  client,
  reportType,
  selectedTemplatePath,
  newTemplateFileName,
  newTemplateDisplayName,
  renameTemplateFileName,
  onCreated,
  onRenamed,
  onDeleted,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  newTemplateFileName: string;
  newTemplateDisplayName: string;
  renameTemplateFileName: string;
  onCreated: (created: ApiReportTemplateContentDto) => void;
  onRenamed: (renamed: ApiReportTemplateContentDto) => void;
  onDeleted: () => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  const createTemplateMutation = useMutation({
    mutationFn: () =>
      client.createReportTemplate({
        body: {
          reportType,
          templatePath: newTemplateFileName.trim(),
          displayName: newTemplateDisplayName.trim(),
        },
      }),
    onSuccess: async (created) => {
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, created.templatePath), created);
      onCreated(created);
      await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates(reportType) });
    },
    onError,
  });

  const renameTemplateMutation = useMutation({
    mutationFn: () =>
      client.renameReportTemplate({
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          newTemplatePath: renameTemplateFileName.trim(),
        },
      }),
    onSuccess: async (renamed) => {
      queryClient.removeQueries({ queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath) });
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, renamed.templatePath), renamed);
      onRenamed(renamed);
      await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates(reportType) });
    },
    onError,
  });

  const deleteTemplateMutation = useMutation({
    mutationFn: () => client.deleteReportTemplate({ reportType, templatePath: selectedTemplatePath }),
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath) });
      onDeleted();
      await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates(reportType) });
    },
    onError,
  });

  return { createTemplateMutation, renameTemplateMutation, deleteTemplateMutation };
}
