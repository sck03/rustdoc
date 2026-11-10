import { useQueryClient } from "@tanstack/react-query";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
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
  onArchived: () => void;
  onRestored: (saved: ApiUserReportTemplateDto) => void;
  onStatusUpdated: (saved: ApiUserReportTemplateDto, action: UserReportTemplateLifecycleAction) => void;
  onError: (error: unknown) => void;
}) {
  const queryClient = useQueryClient();

  const scope = `${reportType}:${selectedTemplatePath}`;
  const refreshKeys = (saved: ApiUserReportTemplateDto) => [
    queryKeys.userReportTemplates(reportType), queryKeys.reportTemplates(reportType),
    queryKeys.userReportTemplateVersions(saved.id),
  ];
  function cacheTemplate(saved: ApiUserReportTemplateDto) {
    queryClient.setQueryData(queryKeys.userReportTemplateContent(reportType, saved.id), saved);
  }
  function handleCreated(created: ApiUserReportTemplateDto) {
    queryClient.setQueryData(queryKeys.userReportTemplateContent(reportType, created.id), created);
    onCreated(created);
  }

  const createBlankUserTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) => client.createUserReportTemplate({
      body: buildUserTemplateCreatePayload({
        reportType,
        name: newTemplateName,
      }),
    }, { signal }),
    onSuccess: handleCreated,
    refreshKeys,
    onError,
  });

  const cloneUserTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (_: void, signal) => client.cloneUserReportTemplate({
      body: buildUserTemplateClonePayload({
        reportType,
        selectedTemplatePath,
        selectedUserTemplateId,
        name: newTemplateName,
      }),
    }, { signal }),
    onSuccess: handleCreated,
    refreshKeys,
    onError,
  });

  const archiveUserTemplateMutation = useScopedMutation(scope, client, {
    mutationFn: (template: ApiUserReportTemplateDto, signal) =>
      client.archiveUserReportTemplate({ id: template.id, expectedVersion: template.versionNumber }, { signal }),
    onSuccess: (saved) => {
      cacheTemplate(saved);
      onArchived();
    },
    refreshKeys,
    onError,
  });

  const restoreUserTemplateVersionMutation = useScopedMutation(scope, client, {
    mutationFn: (versionNumber: number, signal) => {
      if (!currentUserTemplate) {
        throw new Error("当前未选择可恢复的报表模板。");
      }
      return client.restoreUserReportTemplateVersion({
        id: currentUserTemplate.id,
        versionNumber,
        body: { expectedVersion: currentUserTemplate.versionNumber },
      }, { signal });
    },
    onSuccess: (saved) => {
      cacheTemplate(saved);
      onRestored(saved);
    },
    refreshKeys,
    onError,
  });

  const updateUserTemplateStatusMutation = useScopedMutation(scope, client, {
    mutationFn: (action: UserReportTemplateLifecycleAction, signal) => {
      if (!currentUserTemplate) {
        throw new Error("当前未选择可操作的报表模板。");
      }
      const id = currentUserTemplate.id;
      const expectedVersion = currentUserTemplate.versionNumber;
      switch (action.kind) {
        case "publish":
          return client.publishUserReportTemplate({ id, body: { expectedVersion } }, { signal });
        case "share":
          return client.shareUserReportTemplate({ id, body: { shareScope: action.shareScope, expectedVersion } }, { signal });
        case "disable":
          return client.disableUserReportTemplate({ id, body: { expectedVersion } }, { signal });
        case "restore":
          return client.restoreUserReportTemplate({ id, body: { expectedVersion } }, { signal });
      }
    },
    onSuccess: (saved, action) => {
      cacheTemplate(saved);
      onStatusUpdated(saved, action);
    },
    refreshKeys,
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
