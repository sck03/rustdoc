import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiFileStorageMigrationRequest, ApiFileStoragePolicy, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { isTerminalJob } from "../jobs/jobPresentation.ts";

const emptyTarget = (): ApiFileStorageMigrationRequest => ({ kind: "local", directory: "", s3: { endpoint: "", bucket: "", region: "us-east-1", prefix: "", accessKeyId: "", pathStyle: true } });

export function useFileStorage(client: ExportDocManagerApiClient, enabled: boolean) {
  const cache = useQueryClient();
  const requestConfirmation = useConfirmation();
  const [policy, setPolicy] = useState<ApiFileStoragePolicy | null>(null);
  const [version, setVersion] = useState<number | null>(null);
  const [target, setTarget] = useState(emptyTarget);
  const [message, setMessage] = useState("");
  const [success, setSuccess] = useState("");
  const [jobId, setJobId] = useState<string | null>(null);
  useEffect(() => {
    setPolicy(null); setVersion(null); setTarget(emptyTarget()); setJobId(null); setMessage(""); setSuccess("");
  }, [client, enabled]);
  const query = useQuery({ queryKey: queryKeys.fileStorage(), queryFn: ({ signal }) => client.getFileStorage({ signal }), enabled });
  const job = useQuery({
    queryKey: ["file-storage-job", jobId], queryFn: ({ signal }) => client.getJob({ jobId: jobId! }, { signal }),
    enabled: enabled && Boolean(jobId), refetchInterval: q => q.state.error || isTerminalJob(q.state.data?.status) ? false : 2000,
    refetchIntervalInBackground: false,
  });
  const active = Boolean(jobId && !isTerminalJob(job.data?.status));
  useEffect(() => { if (isTerminalJob(job.data?.status)) void cache.invalidateQueries({ queryKey: queryKeys.fileStorage() }); }, [job.data?.status, cache]);
  useUnsavedChangesGuard({ isDirty: policy !== null || Boolean(target.directory || target.secret || target.token || target.s3?.endpoint), message: "文件存储有未保存的设置。" });
  const fail = (error: unknown) => { setMessage(readApiError(error)); setSuccess(""); };
  const save = useScopedMutation("file-storage-policy", client, {
    mutationFn: (_: void, signal) => client.updateFileStoragePolicy({ body: { expectedVersion: version ?? query.data!.versionNumber!, policy: policy! } }, { signal }),
    onSuccess: response => { cache.setQueryData(queryKeys.fileStorage(), response); setPolicy(null); setVersion(null); setMessage(""); setSuccess("容量策略已保存。"); },
    refreshKeys: () => [queryKeys.fileStoragePolicy()], onError: fail,
  });
  const start = useScopedMutation("file-storage-task", client, {
    mutationFn: async (action: "verify" | "reconcile" | "migrate" | "resume" | "discardMigration", signal) => {
      if (action === "reconcile" && !await requestConfirmation({ title: "回收无引用文件", description: "将删除当前存储中没有业务引用的文件；历史材料和备份文件保留。", confirmLabel: "回收" })) return null;
      if (action === "discardMigration" && !await requestConfirmation({ title: "撤销未完成迁移", description: "保留当前存储及已复制文件，撤销这次迁移计划。", confirmLabel: "撤销迁移" })) return null;
      signal.throwIfAborted();
      if (action === "verify") return client.verifyFileStorage({ signal });
      if (action === "reconcile") return client.reconcileFileStorage({ signal });
      const body = action === "discardMigration" ? { cancel: true } : action === "resume" ? { resume: true } : { ...target, expectedVersion: query.data!.versionNumber };
      setTarget(current => ({ ...current, secret: "", token: "" }));
      return client.startFileStorageMigration({ body }, { signal });
    },
    onSuccess: (result, action) => { if (result) { setJobId(result.jobId); if (action === "migrate") setTarget(current => ({ ...current, directory: "", secret: "", token: "", s3: { ...current.s3!, endpoint: "", bucket: "", accessKeyId: "" } })); setMessage(""); setSuccess("任务已启动，可在文件任务中查看。"); } },
    refreshKeys: () => [queryKeys.fileStorage(), queryKeys.jobsRoot()], onError: fail,
  });
  const cancel = useScopedMutation(`file-storage-cancel:${jobId}`, client, {
    mutationFn: (_: void, signal) => client.cancelJob({ jobId: jobId! }, { signal }),
    refreshKeys: () => [["file-storage-job", jobId]], onError: fail,
  });
  return { query, status: query.data, policy: policy ?? query.data?.policy, target, setTarget, job: job.data, active,
    message: message || (query.isError ? readApiError(query.error) : job.isError ? readApiError(job.error) : ""), success,
    busy: save.isPending || start.isPending || cancel.isPending || active, dirty: policy !== null,
    patchPolicy: (key: keyof ApiFileStoragePolicy, value: number) => { setVersion(current => current ?? query.data!.versionNumber!); setPolicy(current => ({ ...(current ?? query.data!.policy!), [key]: value })); },
    save: () => save.mutate(), start: (action: "verify" | "reconcile" | "migrate" | "resume" | "discardMigration") => start.mutate(action), cancel: () => cancel.mutate(),
    jobError: job.isError, retryJob: () => void job.refetch(),
    discard: () => { setPolicy(null); setVersion(null); setMessage(""); void query.refetch(); },
  };
}
