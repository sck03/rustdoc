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
  selectedUserTemplateId,
  userTemplates,
  content,
  renameTemplateFileName,
  onDefaultTemplateSaved,
  onUserTemplateSaved,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  userTemplates: ApiUserReportTemplateDto[];
  content: string;
  renameTemplateFileName: string;
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
        },
      }),
    onSuccess: async (saved) => {
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
      const current = userTemplates.find((template) => template.id === selectedUserTemplateId);
      if (!current || !current.canEdit) {
        throw new Error("当前共享模板只读，请先复制为自己的模板。");
      }

      return client.updateUserReportTemplate({
        id: current.id,
        body: {
          id: current.id,
          reportType,
          name: renameTemplateFileName.trim() || current.name,
          contentHtml: nextContent ?? content,
          isActive: current.isActive,
          isShared: current.isShared,
          shareScope: current.shareScope,
          expectedVersion: current.versionNumber,
        },
      });
    },
    onSuccess: async (saved) => {
      onUserTemplateSaved(saved);
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplateVersions(saved.id) });
    },
    onError,
  });

  return { saveDefaultTemplateMutation, saveUserTemplateMutation };
}
