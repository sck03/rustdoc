import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiBackupPlan, ApiBackupPlanSaveRequest, ApiBackupPlanStatus, ApiBackupS3Settings, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { startDownloadFromTicket } from "../../ui/downloadJobResult.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { backupPlanDraft, validateBackupPlan } from "./backupPlanModel.ts";

export function useBackupPlan(client: ExportDocManagerApiClient, canManage: boolean,
  onSettingsSaved: (status: ApiBackupPlanStatus, previousRevision: number) => void) {
  const queryClient = useQueryClient();
  const run = useAbortableOperation(client);
  const [draft, setDraft] = useState<ApiBackupPlanSaveRequest | null>(null);
  const [baseline, setBaseline] = useState<ApiBackupPlanStatus | null>(null);
  const [confirmation, setConfirmation] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [lastJobId, setLastJobId] = useState<string | null>(null);
  const dirty = Boolean(draft && baseline && (confirmation || JSON.stringify(draft) !== JSON.stringify(backupPlanDraft(baseline))));
  const { confirmDiscardChanges } = useUnsavedChangesGuard({ isDirty: dirty, message: "灾备计划或异机存储有未保存的修改。" });
  const query = useQuery({
    queryKey: queryKeys.backupPlan(),
    queryFn: ({ signal }) => client.getBackupPlan({ signal }),
    enabled: canManage,
    refetchInterval: (query) => query.state.data?.runningJobId ? 3_000 : query.state.data?.plan.enabled ? 30_000 : false,
    refetchIntervalInBackground: false,
  });
  const apply = (status: ApiBackupPlanStatus) => {
    setDraft(backupPlanDraft(status)); setBaseline(status); setConfirmation("");
  };
  useEffect(() => { if (query.data && !dirty) apply(query.data); }, [query.data, dirty]);
  const jobId = query.data?.runningJobId ?? lastJobId ?? null;
  const job = useQuery({
    queryKey: ["backup-plan-job", jobId],
    queryFn: ({ signal }) => client.getJob({ jobId: jobId! }, { signal }),
    enabled: Boolean(jobId) && canManage,
    refetchInterval: (query) => ["Succeeded", "Failed", "Canceled"].includes(query.state.data?.status ?? "") ? false : 2_000,
    refetchIntervalInBackground: false,
  });
  const activeJob = Boolean(jobId && !["Succeeded", "Failed", "Canceled"].includes(job.data?.status ?? "") && !job.isError);
  useEffect(() => {
    if (job.data && ["Succeeded", "Failed", "Canceled"].includes(job.data.status)) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.backupPlan() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.serverMigrationStatus() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.disasterRecoveryStatus() });
    }
  }, [job.data?.status, job.data?.jobId, queryClient]);
  const fail = (error: unknown) => { if (!isAbortError(error)) { setMessage(readApiError(error)); setSuccess(null); } };
  const save = useMutation({
    mutationFn: (body: ApiBackupPlanSaveRequest) => run((signal) => client.saveBackupPlan({ body }, { signal })),
    onSuccess: (response, request) => {
      apply(response); setMessage(null); setSuccess("灾备设置已保存。");
      queryClient.setQueryData(queryKeys.backupPlan(), response);
      onSettingsSaved(response, request.expectedSettingsRevision);
      void queryClient.invalidateQueries({ queryKey: queryKeys.settings() });
    },
    onError: fail,
  });
  const start = useMutation({
    mutationFn: (kind: "backup" | "test") => run((signal) => kind === "backup" ? client.runBackupPlan({ signal }) : client.testBackupReplica({ signal })),
    onSuccess: (job) => {
      setLastJobId(job.jobId); setMessage(null); setSuccess("任务已启动，关闭本页后仍会在后台继续。");
      void queryClient.invalidateQueries({ queryKey: queryKeys.backupPlan() });
    },
    onError: fail,
  });
  const cancel = useMutation({
    mutationFn: () => run((signal) => client.cancelJob({ jobId: jobId! }, { signal })),
    onSuccess: () => { setSuccess("已请求取消，已完成的本地备份会保留。"); void job.refetch(); },
    onError: fail,
  });
  const download = useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => run(async (signal) => {
      const ticket = await client.createRecoveryArchiveDownloadTicket({ id }, { signal });
      signal.throwIfAborted(); startDownloadFromTicket(client, ticket, name);
    }),
    onError: fail,
  });
  const busy = save.isPending || start.isPending || cancel.isPending || download.isPending;
  return {
    query, status: query.data, draft, dirty, confirmation, setConfirmation, message, success, busy, activeJob, job: job.data, jobId,
    updatePlan: <K extends keyof ApiBackupPlan>(key: K, value: ApiBackupPlan[K]) => setDraft((current) => current && ({ ...current, plan: { ...current.plan, [key]: value } })),
    updateS3: <K extends keyof ApiBackupS3Settings>(key: K, value: ApiBackupS3Settings[K]) => setDraft((current) => current && ({ ...current, plan: { ...current.plan, s3: { ...current.plan.s3, [key]: value } } })),
    updateWebDav: (key: "url" | "userName", value: string) => setDraft((current) => current && ({ ...current, webDav: { ...current.webDav, [key]: value, enabled: true } })),
    updateSecret: (key: "webDavPassword" | "s3SecretAccessKey" | "s3SessionToken", value: string | null) => setDraft((current) => current && ({ ...current, [key]: value })),
    setPassword: (value: string) => setDraft((current) => current && ({ ...current, encryptionPassword: value })),
    save: () => {
      if (!draft || !baseline || busy) return;
      const request = { ...draft, webDav: { ...draft.webDav, enabled: draft.plan.replicaMode === "WebDav" || draft.webDav.enabled } };
      const problem = validateBackupPlan(request, baseline, confirmation);
      if (problem) { setMessage(problem); setSuccess(null); return; }
      save.mutate(request);
    },
    reload: async () => {
      if (!await confirmDiscardChanges("重新加载灾备设置")) return;
      const result = await query.refetch();
      if (result.data && !result.isError) { apply(result.data); setMessage(null); setSuccess(null); }
    },
    start: (kind: "backup" | "test") => { if (!dirty && !busy && !activeJob) start.mutate(kind); },
    cancel: () => cancel.mutate(),
    download: (id: string, name: string) => download.mutate({ id, name }),
    trackJob: (id: string) => setLastJobId(id),
  };
}
export type BackupPlanController = ReturnType<typeof useBackupPlan>;
