import { useQueryClient } from "@tanstack/react-query";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { ApiReportTemplateContentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useDefaultReportTemplateLifecycleMutations({
  client,
  reportType,
  selectedTemplatePath,
  expectedRevision,
  newTemplateFileName,
  newTemplateDisplayName,
  currentTemplateDisplayName,
  renameTemplateFileName,
  onCreated,
  onRenamed,
  onDisplayNameUpdated,
  onDefaultSet,
  onDeleted,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  expectedRevision: string;
  newTemplateFileName: string;
  newTemplateDisplayName: string;
  currentTemplateDisplayName: string;
  renameTemplateFileName: string;
  onCreated: (created: ApiReportTemplateContentDto) => void;
  onRenamed: (renamed: ApiReportTemplateContentDto) => void;
  onDisplayNameUpdated: (updated: ApiReportTemplateContentDto) => void;
  onDefaultSet: (message: string) => void;
  onDeleted: () => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();
  const scope = `${reportType}:${selectedTemplatePath}`;

  const createTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) =>
      client.createReportTemplate({
        body: {
          reportType,
          templatePath: newTemplateFileName.trim(),
          displayName: newTemplateDisplayName.trim(),
        },
      }, { signal }),
    onSuccess: (created) => {
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, created.templatePath), created);
      onCreated(created);
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  const renameTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) =>
      client.renameReportTemplate({
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          newTemplatePath: renameTemplateFileName.trim(),
          expectedRevision,
        },
      }, { signal }),
    onSuccess: (renamed) => {
      queryClient.removeQueries({ queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath) });
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, renamed.templatePath), renamed);
      onRenamed(renamed);
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType), queryKeys.settings()],
    onError,
  });

  const updateDisplayNameMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) =>
      client.updateReportTemplateDisplayName({
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          displayName: currentTemplateDisplayName.trim(),
          expectedRevision,
        },
      }, { signal }),
    onSuccess: (updated) => {
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, updated.templatePath), updated);
      onDisplayNameUpdated(updated);
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  const setDefaultTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) =>
      client.setDefaultReportTemplate({
        body: {
          reportType,
          templatePath: selectedTemplatePath,
        },
      }, { signal }),
    onSuccess: (result) => {
      onDefaultSet(result.message);
    },
    refreshKeys: () => [queryKeys.settings(), queryKeys.userReportTemplates(reportType)],
    onError,
  });

  const deleteTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) => client.deleteReportTemplate({ reportType, templatePath: selectedTemplatePath, expectedRevision }, { signal }),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath) });
      onDeleted();
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  return {
    createTemplateMutation,
    renameTemplateMutation,
    updateDisplayNameMutation,
    setDefaultTemplateMutation,
    deleteTemplateMutation,
  };
}
