import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import type { BackupPlanController } from "./useBackupPlan.ts";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

export function BackupArchiveRestorePanel({ client, controller: c, disabled, postgres, selectedId }: {
  client: ExportDocManagerApiClient; controller: BackupPlanController; disabled: boolean; postgres: boolean; selectedId: string;
}) {
  const [id, setId] = useState(selectedId);
  const [password, setPassword] = useState("");
  const [adminPassword, setAdminPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [fileMode, setFileMode] = useState<"Local" | "Directory" | "Reconnect">("Local");
  const [targetLocation, setTargetLocation] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => { setId(selectedId); }, [selectedId]);
  useEffect(() => { setPassword(""); setAdminPassword(""); setConfirmation(""); setMessage(null); }, [id]);
  const restore = useScopedMutation(`recovery:${id}`, client, {
    mutationFn: (_: void, signal) => client.stageRecoveryArchiveRestore({ id, body: { password, adminPassword, confirmationText: confirmation as "MIGRATE" | "RECOVER", fileMode, targetLocation: fileMode === "Directory" ? targetLocation : "" } }, { signal }),
    onSuccess: (job) => { c.trackJob(job.jobId); setPassword(""); setAdminPassword(""); setConfirmation(""); setMessage(null); },
    onError: (error) => { setMessage(readApiError(error)); setPassword(""); setAdminPassword(""); },
  });
  useEffect(() => { if (!c.archives.some((archive) => archive.id === id && archive.available)) setId(""); }, [c.archives, id]);
  const confirm = postgres ? "MIGRATE" : "RECOVER";
  const busy = disabled || c.activeJob || restore.isPending;
  return <section className="form-section" aria-label="从恢复点恢复">
    <div className="section-header"><h3>从恢复点恢复</h3></div>
    {message && <InlineNotice tone="error">{message}</InlineNotice>}
    <fieldset className="settings-fieldset" disabled={busy}><div className="field-grid">
      <label><span>选择完整备份</span><select value={id} onChange={(event) => setId(event.target.value)}><option value="">请选择备份</option>{c.archives.map((archive) => <option key={archive.id} value={archive.id} disabled={!archive.available}>{formatRuntimeDate(archive.createdAt)} · {formatBytes(archive.sizeBytes)}</option>)}</select></label>
      <label><span>所选备份的密码</span><input type="password" autoComplete="off" value={password} onChange={(event) => setPassword(event.target.value)} /></label>
      <label><span>附件恢复方式</span><select value={fileMode} onChange={event => setFileMode(event.target.value as typeof fileMode)}><option value="Local">重建到本机文件库</option><option value="Directory">重建到已登记位置</option><option value="Reconnect">接回完好的原文件库</option></select></label>
      {fileMode === "Directory" && <label><span>目标位置</span><select value={targetLocation} onChange={event => setTargetLocation(event.target.value)}><option value="">请选择目标位置</option>{c.status?.locations?.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}</select></label>}
      {postgres && <label><span>管理员当前密码</span><input type="password" autoComplete="current-password" value={adminPassword} onChange={(event) => setAdminPassword(event.target.value)} /></label>}
      <label><span>恢复确认文本</span><input value={confirmation} placeholder={confirm} onChange={(event) => setConfirmation(event.target.value)} /></label>
    </div></fieldset>
    <button className="command-button danger" type="button" disabled={busy || !id || !password || (fileMode === "Directory" && !targetLocation) || (postgres && !adminPassword) || confirmation !== confirm} onClick={() => restore.mutate()}>校验并安排恢复</button>
    <p className="settings-helper-text">{postgres ? "校验任务成功后，管理员停止应用并运行恢复维护命令，再启动应用。普通重启不会执行恢复。" : "校验任务成功后，退出并重新打开桌面程序。"}恢复前会保留原数据副本；仅接受当前数据库模型的备份。</p>
  </section>;
}
