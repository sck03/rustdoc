import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiRepositoryMaintenanceRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { Button } from "../../ui/Button.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { isTerminalJob, readJobMessage } from "../jobs/jobPresentation.ts";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";
export function RecoveryRepositoryPanel({ client, disabled }: { client: ExportDocManagerApiClient; disabled: boolean }) {
  const [jobId, setJobId] = useState("");
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const [pointId, setPointId] = useState("");
  const [reason, setReason] = useState("");
  const [password, setPassword] = useState("");
  const [isolated, setIsolated] = useState(false);
  const status = useQuery({ queryKey: ["recovery-repository-maintenance"], queryFn: ({ signal }) => client.getRecoveryRepositoryMaintenance({ signal }) });
  const job = useQuery({ queryKey: ["recovery-repository-job", jobId], enabled: Boolean(jobId), queryFn: ({ signal }) => client.getJob({ jobId }, { signal }), refetchInterval: q => isTerminalJob(q.state.data?.status) ? false : 2000 });
  useEffect(() => { if (isTerminalJob(job.data?.status)) void status.refetch(); }, [job.data?.status]);
  const busy = disabled || pending || Boolean(jobId && !isTerminalJob(job.data?.status));
  const start = async (body: ApiRepositoryMaintenanceRequest) => { setPending(true); setError(""); try { const result = await client.maintainRecoveryRepository({ body }); setJobId(result.jobId); setPassword(""); } catch (cause) { setError(readApiError(cause)); } finally { setPending(false); } };
  const data = status.data;
  return <CollapsibleSection title="仓库核验与保留" error={Boolean(error)}>
    {error && <InlineNotice tone="error">{error}</InlineNotice>}{status.isError && <InlineNotice tone="error">{readApiError(status.error)}</InlineNotice>}
    {data?.message && <InlineNotice tone="info">{data.message}</InlineNotice>}
    <p className="settings-helper-text">最近全库核验：{data?.verifiedAt ? formatRuntimeDate(data.verifiedAt) : "尚未记录"}</p>
    <div className="form-actions"><Button variant="secondary" disabled={busy} onClick={() => void start({ action: "Verify" })}>完整核验有效恢复点</Button><Button variant="secondary" disabled={busy} onClick={() => void start({ action: "Preview" })}>预览仓库回收</Button></div>
    {data?.token && <InlineNotice tone="warning" action={<Button disabled={busy || !data.count} onClick={() => void start({ action: "Collect", token: data.token })}>执行已预检回收</Button>}>可回收 {data.count ?? 0} 个对象、{formatBytes(data.bytes ?? 0)}；保护 {data.protectedObjects ?? 0} 个；隔离中 {data.waitingObjects ?? 0} 个。有效至 {formatRuntimeDate(data.expiresAt)}。</InlineNotice>}
    <div className="field-grid"><TextField label="恢复点编号" value={pointId} onChange={setPointId} /><TextField label="暂停或解除原因" value={reason} onChange={setReason} /></div>
    <div className="form-actions"><Button variant="secondary" disabled={busy || pointId.length !== 32 || !reason} onClick={() => void start({ action: "Hold", pointId, reason })}>暂停保留</Button><Button variant="secondary" disabled={busy || pointId.length !== 32 || !reason} onClick={() => void start({ action: "Release", pointId, reason })}>解除暂停</Button></div>
    <CollapsibleSection title="恢复凭证与中断处理">
      <label><span>新的恢复凭证</span><input type="password" autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} /></label>
      <Button disabled={busy || password.length < 12} onClick={() => void start({ action: "RotatePassword", newPassword: password })}>更换并核验恢复凭证</Button>
      <label className="checkbox-field"><input type="checkbox" checked={isolated} onChange={event => setIsolated(event.target.checked)} /><span>旧备份、恢复和导出实例均已停止并隔离</span></label>
      <Button variant="secondary" disabled={busy || !isolated} onClick={() => void start({ action: "RecoverLock", oldInstanceIsolated: true })}>解除中断任务的仓库保护</Button>
    </CollapsibleSection>
    {job.data && <InlineNotice tone={job.data.status === "Failed" ? "error" : "info"} action={<ViewJobButton jobId={jobId} />}>{job.data.title} · {job.data.statusText} {readJobMessage(job.data)}</InlineNotice>}
  </CollapsibleSection>;
}
