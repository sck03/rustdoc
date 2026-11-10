import { useQueryClient } from "@tanstack/react-query";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import {
  ApiReportTemplateContentDto,
  ApiUserReportTemplateDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateSaveMutations({
  client,
  reportType,
  selectedTemplatePath,
  expectedRevision,
  expectedUserVersion,
  currentUserTemplate,
  content,
  userTemplateName,
  onDefaultTemplateSaved,
  onUserTemplateSaved,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  expectedRevision: string;
  expectedUserVersion: number;
  currentUserTemplate: ApiUserReportTemplateDto | null;
  content: string;
  userTemplateName: string;
  onDefaultTemplateSaved: (saved: ApiReportTemplateContentDto) => void;
  onUserTemplateSaved: (saved: ApiUserReportTemplateDto) => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  const scope = `${reportType}:${selectedTemplatePath}`;
  const saveDefaultTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (nextContent: string | undefined, signal) =>
      client.saveReportTemplateContent({
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          content: nextContent ?? content,
          expectedRevision,
        },
      }, { signal }),
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, saved.templatePath), saved);
      onDefaultTemplateSaved(saved);
    },
    refreshKeys: () => [queryKeys.reportTemplates(reportType)],
    onError,
  });

  const saveUserTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (nextContent: string | undefined, signal) => {
      const current = currentUserTemplate;
      if (!current || !current.canEdit) {
        throw new Error("当前共享模板只读，请先复制为自己的模板。");
      }

      return client.saveUserReportTemplateDraft({
        id: current.id,
        body: {
          reportType,
          name: userTemplateName.trim() || current.name,
          contentHtml: nextContent ?? content,
          expectedVersion: expectedUserVersion,
        },
      }, { signal });
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(queryKeys.userReportTemplateContent(reportType, saved.id), saved);
      onUserTemplateSaved(saved);
    },
    refreshKeys: saved => [queryKeys.userReportTemplates(reportType), queryKeys.reportTemplates(reportType), queryKeys.userReportTemplateVersions(saved.id)],
    onError,
  });

  return { saveDefaultTemplateMutation, saveUserTemplateMutation };
}
