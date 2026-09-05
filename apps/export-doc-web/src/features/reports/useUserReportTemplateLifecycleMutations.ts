import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ApiUserReportTemplateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  buildUserTemplateClonePayload,
  buildUserTemplateCreatePayload,
  type ReportTypeOption,
} from "./reportTemplateDesignerModel.ts";

export type UserReportTemplateLifecycleAction =
  | { kind: "publish" }
  | { kind: "share"; shareScope: string }
  | { kind: "disable" }
  | { kind: "restore" };

export function useUserReportTemplateLifecycleMutations({
  client,
  reportType,
  selectedTemplatePath,
  selectedUserTemplateId,
  currentUserTemplate,
  newTemplateName,
  onCreated,
  onArchived,
  onRestored,
  onStatusUpdated,
  onError,
}: {
  client: ExportDocManagerApiClient;
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  currentUserTemplate: ApiUserReportTemplateDto | null;
  newTemplateName: string;
  onCreated: (created: ApiUserReportTemplateDto) => void;
  onArchived: () => void | Promise<void>;
  onRestored: (saved: ApiUserReportTemplateDto) => void;
  onStatusUpdated: (saved: ApiUserReportTemplateDto, action: UserReportTemplateLifecycleAction) => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  async function invalidateTemplateQueries(saved?: ApiUserReportTemplateDto) {
    await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplates(reportType) });
    if (saved) {
      await queryClient.invalidateQueries({ queryKey: queryKeys.userReportTemplateVersions(saved.id) });
    }
  }

  async function handleCreated(created: ApiUserReportTemplateDto) {
    queryClient.setQueryData<ApiUserReportTemplateDto[]>(
      queryKeys.userReportTemplates(reportType),
      (current) => [...(current ?? []).filter((item) => item.id !== created.id), created],
    );
    onCreated(created);
    await invalidateTemplateQueries(created);
  }

  const createBlankUserTemplateMutation = useMutation({
    mutationFn: () => client.createUserReportTemplate({
      body: buildUserTemplateCreatePayload({
        reportType,
        name: newTemplateName,
      }),
    }),
    onSuccess: handleCreated,
    onError,
  });

  const cloneUserTemplateMutation = useMutation({
    mutationFn: () => client.cloneUserReportTemplate({
      body: buildUserTemplateClonePayload({
        reportType,
        selectedTemplatePath,
        selectedUserTemplateId,
        name: newTemplateName,
      }),
    }),
    onSuccess: handleCreated,
    onError,
  });

  const archiveUserTemplateMutation = useMutation({
    mutationFn: (template: ApiUserReportTemplateDto) =>
      client.archiveUserReportTemplate({ id: template.id, expectedVersion: template.versionNumber }),
    onSuccess: async (saved) => {
      await onArchived();
      await invalidateTemplateQueries(saved);
    },
    onError,
  });

  const restoreUserTemplateVersionMutation = useMutation({
    mutationFn: (versionNumber: number) => {
      if (!currentUserTemplate) {
        throw new Error("当前未选择可恢复的报表模板。");
      }
      return client.restoreUserReportTemplateVersion({
        id: currentUserTemplate.id,
        versionNumber,
        body: { expectedVersion: currentUserTemplate.versionNumber },
      });
    },
    onSuccess: async (saved) => {
      onRestored(saved);
      await invalidateTemplateQueries(saved);
    },
    onError,
  });

  const updateUserTemplateStatusMutation = useMutation({
    mutationFn: (action: UserReportTemplateLifecycleAction) => {
      if (!currentUserTemplate) {
        throw new Error("当前未选择可操作的报表模板。");
      }
      const id = currentUserTemplate.id;
      const expectedVersion = currentUserTemplate.versionNumber;
      switch (action.kind) {
        case "publish":
          return client.publishUserReportTemplate({ id, body: { expectedVersion } });
        case "share":
          return client.shareUserReportTemplate({ id, body: { shareScope: action.shareScope, expectedVersion } });
        case "disable":
          return client.disableUserReportTemplate({ id, body: { expectedVersion } });
        case "restore":
          return client.restoreUserReportTemplate({ id, body: { expectedVersion } });
      }
    },
    onSuccess: async (saved, action) => {
      onStatusUpdated(saved, action);
      await invalidateTemplateQueries(saved);
    },
    onError,
  });

  return {
    createBlankUserTemplateMutation,
    cloneUserTemplateMutation,
    archiveUserTemplateMutation,
    restoreUserTemplateVersionMutation,
    updateUserTemplateStatusMutation,
  };
}
