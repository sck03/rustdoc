import type { BackupPlanController } from "./useBackupPlan.ts";
import { formatRuntimeDate } from "./settingsFormatters.ts";

export function BackupSchedulePanel({ controller: c, disabled }: { controller: BackupPlanController; disabled: boolean }) {
  if (!c.draft || !c.status) return null;
  const { plan } = c.draft;
  return <section className="form-section" aria-label="日常灾备">
    <div className="section-header"><h2>日常灾备</h2><strong>{c.status.plan.enabled ? "定时备份已启用" : "定时备份已关闭"}</strong></div>
    <p className="section-description">保存完整加密包，包含业务数据库、图片、附件、凭据主密钥和用户模板。默认关闭；服务运行时按业务时区执行，停机错过的当天计划会在启动后补做。</p>
    <fieldset className="settings-fieldset" disabled={disabled}>
      <label className="checkbox-field"><input type="checkbox" checked={plan.enabled} onChange={(event) => c.updatePlan("enabled", event.target.checked)} /><span>启用日常灾备</span></label>
      {plan.enabled && <div className="field-grid">
        <label><span>备份周期</span><select value={plan.frequency} onChange={(event) => c.updatePlan("frequency", event.target.value as "Daily" | "Weekly")}><option value="Daily">每天</option><option value="Weekly">每周</option></select></label>
        <label><span>备份时间（{c.status.timeZone}）</span><input type="time" value={plan.time} onChange={(event) => c.updatePlan("time", event.target.value)} /></label>
        {plan.frequency === "Weekly" && <label><span>每周执行日</span><select value={plan.weekday} onChange={(event) => c.updatePlan("weekday", Number(event.target.value))}>{["日", "一", "二", "三", "四", "五", "六"].map((day, index) => <option key={day} value={index}>星期{day}</option>)}</select></label>}
      </div>}
      <div className="field-grid">
        <label><span>{c.status.encryptionPasswordSet ? "更换备份包密码" : "备份包密码"}</span><input type="password" autoComplete="new-password" value={c.draft.encryptionPassword} placeholder={c.status.encryptionPasswordSet ? "留空保留已保存密码" : "至少 12 个字符"} onChange={(event) => c.setPassword(event.target.value)} /></label>
        <label><span>再次输入新密码</span><input type="password" autoComplete="new-password" value={c.confirmation} onChange={(event) => c.setConfirmation(event.target.value)} /></label>
      </div>
      <p className="settings-helper-text">请单独保管密码。旧备份仍须用创建时的密码打开；修改密码不会重新加密旧包。</p>
      <details className="maintenance-advanced-details"><summary>保留策略</summary>
        <div className="field-grid">
          <label><span>保留最新份数</span><input type="number" min={1} max={365} step={1} value={plan.retentionCount} onChange={(event) => c.updatePlan("retentionCount", Number(event.target.value))} /></label>
          <label><span>保留天数</span><input type="number" min={0} max={3650} step={1} value={plan.retentionDays} onChange={(event) => c.updatePlan("retentionDays", Number(event.target.value))} /><small>0 表示只按份数保留。</small></label>
        </div>
        <p className="settings-helper-text">新备份及所选副本完成后才清理到期或超额备份，至少保留最新一份。只管理本计划创建的文件。S3 开启版本控制时，旧对象版本的保留由存储端生命周期策略控制。</p>
      </details>
    </fieldset>
    <div className="detail-grid"><div className="detail-item"><span>下次执行（{c.status.timeZone}）</span><strong>{formatRuntimeDate(c.status.nextRunAt ?? undefined, c.status.timeZone) || "未启用"}</strong></div><div className="detail-item"><span>备份内容</span><strong>完整加密快照</strong></div></div>
    <button className="command-button" type="button" disabled={disabled || c.dirty || c.activeJob || !c.status.encryptionPasswordSet} onClick={() => c.start("backup")}>立即备份一次</button>
    <p className="settings-helper-text">手动执行不要求启用定时计划。请先保存密码及存储设置；计划关闭时不会启动定时线程。</p>
  </section>;
}
