import type { BackupPlanController } from "./useBackupPlan.ts";
import { S3AddressingField } from "./S3AddressingField.tsx";

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
  return <section className="form-section" aria-label="备份目标">
    <div className="section-header"><h2>备份目标</h2></div>
    <p className="section-description">恢复点直接写入所选仓库。同机备份不能抵御整机故障。</p>
    <fieldset className="settings-fieldset" disabled={disabled}>
      <label><span>备份存储</span><select value={plan.replicaMode} onChange={(event) => c.updatePlan("replicaMode", event.target.value as typeof plan.replicaMode)}><option value="None">服务器／本机</option><option value="Directory">已登记目录／移动硬盘</option><option value="WebDav">WebDAV／NAS</option><option value="S3" disabled={!c.status.s3Available}>S3／兼容对象存储</option></select></label>
      {plan.replicaMode === "Directory" && <label><span>存储位置</span><select value={plan.backupLocation ?? ""} onChange={(event) => c.updatePlan("backupLocation", event.target.value)}><option value="">请选择已登记位置</option>{c.status.locations?.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}</select></label>}
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
          <S3AddressingField value={s3.addressing} onChange={value => c.updateS3("addressing", value)} />
          <SecretField label="Session Token" value={c.draft.s3SessionToken} saved={c.status.s3SessionTokenSet} onChange={(value) => c.updateSecret("s3SessionToken", value)} />
          <p className="settings-helper-text">长期凭证通常不需要 Session Token。临时凭证过期后需要更新。</p>
        </details>
      </>}
    </fieldset>
    <button className="command-button secondary" type="button" disabled={disabled || c.dirty || c.activeJob} onClick={() => c.start("test")}>核验已保存的备份目标</button>
    <p className="settings-helper-text">恢复凭证须在 A 之外保管。切换目标后，旧仓库继续保留；目录和移动盘须先由部署管理员登记。</p>
  </section>;
}
