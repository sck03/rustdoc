import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import type { BackupPlanController } from "./useBackupPlan.ts";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

export function BackupArchiveRestorePanel({ client, controller: c, disabled, postgres, selectedId }: {
  client: ExportDocManagerApiClient; controller: BackupPlanController; disabled: boolean; postgres: boolean; selectedId: string;
}) {
  const run = useAbortableOperation(client);
  const [id, setId] = useState(selectedId);
  const [password, setPassword] = useState("");
  const [adminPassword, setAdminPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => { setId(selectedId); }, [selectedId]);
  useEffect(() => { setPassword(""); setAdminPassword(""); setConfirmation(""); setMessage(null); }, [id]);
  const restore = useMutation({
    mutationFn: () => run((signal) => client.stageRecoveryArchiveRestore({ id, body: { password, adminPassword, confirmationText: confirmation as "MIGRATE" | "RECOVER" } }, { signal })),
    onSuccess: (job) => { c.trackJob(job.jobId); setPassword(""); setAdminPassword(""); setConfirmation(""); setMessage(null); },
    onError: (error) => { setMessage(readApiError(error)); setPassword(""); setAdminPassword(""); },
  });
  const confirm = postgres ? "MIGRATE" : "RECOVER";
  const busy = disabled || c.activeJob || restore.isPending;
  return <section className="form-section" aria-label="从本地完整备份恢复">
    <div className="section-header"><h3>从本地完整备份恢复</h3></div>
    <p className="section-description">直接使用服务端已保存的完整包，无需下载后再上传。先校验密码、完整性和版本，再安排维护恢复。</p>
    {message && <InlineNotice tone="error">{message}</InlineNotice>}
    <fieldset className="settings-fieldset" disabled={busy}><div className="field-grid">
      <label><span>选择完整备份</span><select value={id} onChange={(event) => setId(event.target.value)}><option value="">请选择备份</option>{c.status?.archives.map((archive) => <option key={archive.id} value={archive.id}>{formatRuntimeDate(archive.createdAt)} · {formatBytes(archive.sizeBytes)}</option>)}</select></label>
      <label><span>所选备份的密码</span><input type="password" autoComplete="off" value={password} onChange={(event) => setPassword(event.target.value)} /></label>
      {postgres && <label><span>管理员当前密码</span><input type="password" autoComplete="current-password" value={adminPassword} onChange={(event) => setAdminPassword(event.target.value)} /></label>}
      <label><span>恢复确认文本</span><input value={confirmation} placeholder={confirm} onChange={(event) => setConfirmation(event.target.value)} /></label>
    </div></fieldset>
    <button className="command-button danger-command" type="button" disabled={busy || !id || !password || (postgres && !adminPassword) || confirmation !== confirm} onClick={() => restore.mutate()}>校验并安排完整恢复</button>
    <p className="settings-helper-text">{postgres ? "校验任务成功后，管理员停止应用并运行恢复维护命令，再启动应用。普通重启不会执行恢复。" : "校验任务成功后，退出并重新打开桌面程序。"}恢复前会保留原数据副本；旧备份可按受支持的数据库版本逐步升级。</p>
  </section>;
}
