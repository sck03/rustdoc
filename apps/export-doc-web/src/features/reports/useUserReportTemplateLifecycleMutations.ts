import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ApiUserReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { type ReportTypeOption } from "./reportTemplateDesignerModel.ts";

export function useUserReportTemplateLifecycleMutations({
  client,
  reportType,
  selectedTemplatePath,
  selectedUserTemplateId,
  userTemplates,
  currentUserTemplate,
  content,
  newTemplateName,
  newTemplateShareScope,
  onCreated,
  onDeleted,
  onRestored,
  onStatusUpdated,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  userTemplates: ApiUserReportTemplateDto[];
  currentUserTemplate: ApiUserReportTemplateDto | null;
  content: string;
  newTemplateName: string;
  newTemplateShareScope: string;
  onCreated: (created: ApiUserReportTemplateDto) => void;
  onDeleted: () => void | Promise<void>;
  onRestored: (saved: ApiUserReportTemplateDto) => void;
  onStatusUpdated: (saved: ApiUserReportTemplateDto) => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  const createUserTemplateMutation = useMutation({
    mutationFn: () => {
      const sourceUserTemplate = userTemplates.find((template) => template.id === selectedUserTemplateId);
      return client.createUserReportTemplate({
        body: {
          reportType,
          name: newTemplateName.trim(),
          contentHtml: sourceUserTemplate ? content : "",
          sourceTemplatePath: sourceUserTemplate ? "" : selectedTemplatePath,
          isActive: true,
          isShared: newTemplateShareScope !== "Private",
          shareScope: newTemplateShareScope,
          expectedVersion: 0,
        },
      });
    },
    onSuccess: async (created) => {
      queryClient.setQueryData<ApiUserReportTemplateDto[]>(
        queryKeys.userReportTemplates(reportType),
        (current) => [...(current ?? []).filter((item) => item.id !== created.id), created],
      );
      onCreated(created);
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
    },
    onError,
  });

  const deleteUserTemplateMutation = useMutation({
    mutationFn: (id: number) => client.deleteUserReportTemplate({ id }),
    onSuccess: async () => {
      await onDeleted();
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
    },
    onError,
  });

  const restoreUserTemplateVersionMutation = useMutation({
    mutationFn: (versionNumber: number) =>
      client.restoreUserReportTemplateVersion({ id: selectedUserTemplateId, versionNumber }),
    onSuccess: async (saved) => {
      onRestored(saved);
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplateVersions(saved.id) });
    },
    onError,
  });

  const updateUserTemplateStatusMutation = useMutation({
    mutationFn: (next: { isActive?: boolean; shareScope?: string }) => {
      if (!currentUserTemplate || !currentUserTemplate.canEdit) {
        throw new Error("当前模板只读，无法修改发布状态。");
      }

      return client.updateUserReportTemplate({
        id: currentUserTemplate.id,
        body: {
          id: currentUserTemplate.id,
          reportType,
          name: currentUserTemplate.name,
          contentHtml: content,
          isActive: next.isActive ?? currentUserTemplate.isActive,
          isShared: next.shareScope ? next.shareScope !== "Private" : currentUserTemplate.isShared,
          shareScope: next.shareScope ?? currentUserTemplate.shareScope,
          expectedVersion: currentUserTemplate.versionNumber,
        },
      });
    },
    onSuccess: async (saved) => {
      onStatusUpdated(saved);
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplateVersions(saved.id) });
    },
    onError,
  });

  return {
    createUserTemplateMutation,
    deleteUserTemplateMutation,
    restoreUserTemplateVersionMutation,
    updateUserTemplateStatusMutation,
  };
}
