import { useMutation, useQueryClient } from "@tanstack/react-query";
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

  const saveDefaultTemplateMutation = useMutation({
    mutationFn: (nextContent?: string) =>
      client.saveReportTemplateContent({
        body: {
          reportType,
          templatePath: selectedTemplatePath,
          content: nextContent ?? content,
          expectedRevision,
        },
      }),
    onSuccess: async (saved) => {
      queryClient.setQueryData(queryKeys.reportTemplateContent(reportType, saved.templatePath), saved);
      onDefaultTemplateSaved(saved);
      await queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplates(reportType) });
      await queryClient.invalidateQueries({
        queryKey: queryKeys.reportTemplateContent(reportType, selectedTemplatePath),
      });
    },
    onError,
  });

  const saveUserTemplateMutation = useMutation({
    mutationFn: (nextContent?: string) => {
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
      });
    },
    onSuccess: async (saved) => {
      queryClient.setQueryData(queryKeys.userReportTemplateContent(reportType, saved.id), saved);
      onUserTemplateSaved(saved);
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplateVersions(saved.id) });
    },
    onError,
  });

  return { saveDefaultTemplateMutation, saveUserTemplateMutation };
}
