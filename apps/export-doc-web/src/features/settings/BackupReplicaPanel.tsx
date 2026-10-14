import type { BackupPlanController } from "./useBackupPlan.ts";

function SecretField({ label, value, saved, onChange }: { label: string; value: string | null; saved: boolean; onChange: (value: string | null) => void }) {
  return <div className="backup-secret-field">
    <label className="checkbox-field"><input type="checkbox" checked={value !== null} onChange={(event) => onChange(event.target.checked ? "" : null)} /><span>修改{label}{saved ? "（已保存）" : "（未设置）"}</span></label>
    <label><span>{label}</span><input type="password" autoComplete="new-password" value={value ?? ""} disabled={value === null} placeholder={saved ? "启用修改后，留空将清除" : "启用修改后填写"} onChange={(event) => onChange(event.target.value)} /></label>
  </div>;
}
export function BackupReplicaPanel({ controller: c, disabled }: { controller: BackupPlanController; disabled: boolean }) {
  if (!c.draft || !c.status) return null;
  const { plan, webDav } = c.draft;
  const s3 = plan.s3;
  return <section className="form-section" aria-label="异机副本">
    <div className="section-header"><h2>异机副本</h2></div>
    <p className="section-description">每份本地加密备份完成后，再复制到指定存储。上传失败会保留本地文件和旧备份，结果显示为“部分完成”。</p>
    <fieldset className="settings-fieldset" disabled={disabled}>
      <label><span>副本存储</span><select value={plan.replicaMode} onChange={(event) => c.updatePlan("replicaMode", event.target.value as "None" | "WebDav" | "S3")}><option value="None">仅保存在服务器／本机</option><option value="WebDav">WebDAV／NAS</option><option value="S3" disabled={!c.status.s3Available}>S3／兼容对象存储</option></select></label>
      {plan.replicaMode === "WebDav" && <div className="field-grid">
        <label><span>WebDAV 地址</span><input type="url" value={webDav.url} placeholder="https://nas.example.com/backups/" onChange={(event) => c.updateWebDav("url", event.target.value)} /><small>填写已存在的备份目录，支持局域网 IP。</small></label>
        <label><span>WebDAV 用户名</span><input autoComplete="off" value={webDav.userName} onChange={(event) => c.updateWebDav("userName", event.target.value)} /></label>
        <SecretField label="WebDAV 密码" value={c.draft.webDavPassword} saved={c.status.webDavPasswordSet} onChange={(value) => c.updateSecret("webDavPassword", value)} />
      </div>}
      {plan.replicaMode === "S3" && <>
        <div className="field-grid">
          <label><span>S3 端点</span><input type="url" value={s3.endpoint} placeholder="https://s3.example.com" onChange={(event) => c.updateS3("endpoint", event.target.value)} /><small>AWS、MinIO 或其他兼容服务的 API 地址。</small></label>
          <label><span>区域</span><input value={s3.region} placeholder="us-east-1" onChange={(event) => c.updateS3("region", event.target.value)} /></label>
          <label><span>存储桶</span><input value={s3.bucket} onChange={(event) => c.updateS3("bucket", event.target.value)} /></label>
          <label><span>对象前缀</span><input value={s3.prefix} onChange={(event) => c.updateS3("prefix", event.target.value)} /></label>
          <label><span>Access Key ID</span><input autoComplete="off" value={s3.accessKeyId} onChange={(event) => c.updateS3("accessKeyId", event.target.value)} /></label>
          <SecretField label="Secret Access Key" value={c.draft.s3SecretAccessKey} saved={c.status.s3SecretAccessKeySet} onChange={(value) => c.updateSecret("s3SecretAccessKey", value)} />
        </div>
        <details className="maintenance-advanced-details"><summary>寻址方式与临时凭证</summary>
          <label className="checkbox-field"><input type="checkbox" checked={s3.pathStyle} onChange={(event) => c.updateS3("pathStyle", event.target.checked)} /><span>使用路径寻址（适用于多数 NAS／MinIO）</span></label>
          <SecretField label="Session Token" value={c.draft.s3SessionToken} saved={c.status.s3SessionTokenSet} onChange={(value) => c.updateSecret("s3SessionToken", value)} />
          <p className="settings-helper-text">长期凭证通常不需要 Session Token。临时凭证过期后需要更新。</p>
        </details>
      </>}
      {plan.replicaMode !== "None" && <label className="checkbox-field"><input type="checkbox" checked={plan.verifyReplica} onChange={(event) => c.updatePlan("verifyReplica", event.target.checked)} /><span>上传后完整回读并校验 SHA-256（增加一次下载流量）</span></label>}
    </fieldset>
    {plan.replicaMode !== "None" && <button className="command-button secondary" type="button" disabled={disabled || c.dirty || c.activeJob} onClick={() => c.start("test")}>测试已保存的副本存储</button>}
    <p className="settings-helper-text">副本保存在本安装独立的子目录／前缀中。切换存储目标后，旧目标上的副本由管理员自行保留或清理。</p>
  </section>;
}
