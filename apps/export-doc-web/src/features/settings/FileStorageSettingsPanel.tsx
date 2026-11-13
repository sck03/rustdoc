import type { ApiBackupS3Settings, ExportDocManagerApiClient } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { FieldShell, NumberField, SelectField, TextField } from "../../ui/FormFields.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { useFileStorage } from "./useFileStorage.ts";
import { formatBytes } from "./settingsFormatters.ts";

const MiB = 1024 * 1024;
export default function FileStorageSettingsPanel({ client, canManage }: { client: ExportDocManagerApiClient; canManage: boolean }) {
  const c = useFileStorage(client, canManage);
  const { status, policy, target } = c;
  const disabled = !canManage || c.busy;
  const s3 = target.s3!;
  const patchS3 = (key: keyof ApiBackupS3Settings, value: string | boolean) => c.setTarget(current => ({ ...current, s3: { ...current.s3!, [key]: value } }));
  return <section className="form-section" aria-label="文件与存储">
    <div className="section-header"><h2>文件与存储</h2><Button variant="secondary" disabled={c.busy} onClick={() => void c.query.refetch()}>检查状态</Button></div>
    {c.message && <InlineNotice tone="error" action={c.jobError ? <Button variant="secondary" onClick={c.retryJob}>重新读取任务</Button> : undefined}>{c.message}</InlineNotice>}
    {c.success && <InlineNotice tone="success">{c.success}</InlineNotice>}
    {!status && c.query.isPending && <PageState tone="loading" title="正在读取文件存储" />}
    {status && <>
      <div className="detail-grid">
        <div className="detail-item"><span>存储位置</span><strong>{status.kind === "s3" ? `S3 · ${status.s3?.bucket}` : status.directory}</strong></div>
        <div className="detail-item"><span>连接状态</span><strong>{status.healthy ? "正常" : "不可用"}</strong></div>
        <div className="detail-item"><span>文件对象</span><strong>{status.fileCount?.toLocaleString()}</strong></div>
        <div className="detail-item"><span>实际文件容量</span><strong>{formatBytes(status.physicalBytes ?? 0)}</strong></div>
        <div className="detail-item"><span>业务引用容量</span><strong>{formatBytes(status.logicalBytes ?? 0)}</strong></div>
        <div className="detail-item"><span>无引用容量</span><strong>{formatBytes(status.unreferencedBytes ?? 0)}</strong></div>
      </div>
      {!status.healthy && <InlineNotice tone="error">{status.healthMessage}</InlineNotice>}
      {policy && <CollapsibleSection title="容量策略" error={Boolean(c.message) && c.dirty}>
        <fieldset className="settings-fieldset" disabled={disabled}><div className="field-grid">
          {([["fileBytes", "单文件上限（MiB）", MiB], ["requestBytes", "每份申请附件总量（MiB）", MiB], ["requestFiles", "每份申请附件数量", 1], ["quotaBytes", "组织配额（MiB）", MiB]] as const).map(([key, label, unit]) =>
            <NumberField key={key} label={label} description={key === "quotaBytes" ? "0 为不限" : undefined} value={policy[key] / unit} onChange={value => c.patchPolicy(key, Math.round(value * unit))} />)}
        </div></fieldset>
        <div className="form-actions"><Button disabled={disabled || !c.dirty} onClick={c.save}>保存容量策略</Button><Button variant="secondary" disabled={disabled || !c.dirty} onClick={c.discard}>放弃修改并刷新</Button></div>
      </CollapsibleSection>}
      <CollapsibleSection title="迁移存储" error={Boolean(c.message) && !c.dirty}>
        {status.migrationPending ? <><InlineNotice tone="info">迁移尚未完成，已核验的复制进度会保留。</InlineNotice><div className="form-actions"><Button disabled={disabled} onClick={() => c.start("resume")}>继续迁移</Button><Button variant="secondary" disabled={disabled} onClick={() => c.start("discardMigration")}>撤销迁移</Button></div></> : <>
          <fieldset className="settings-fieldset" disabled={disabled}><div className="field-grid">
            <SelectField label="目标类型" value={target.kind!} includeEmptyOption={false} options={[{ value: "local", label: "本地受管目录" }, { value: "s3", label: "S3 对象存储" }]} onChange={kind => c.setTarget(current => ({ ...current, kind }))} />
            {target.kind === "local" ? <><SelectField label="已登记的独立位置" value={(target.directory ?? "").startsWith("@") ? target.directory!.split("/")[0] : ""} options={(status.registeredRoots ?? []).map(name => ({ value: `@${name}`, label: name }))} onChange={directory => c.setTarget(current => ({ ...current, directory }))} /><TextField label="目标目录" value={target.directory} description="选择已登记的位置，或填写数据根下的空相对目录。" onChange={directory => c.setTarget(current => ({ ...current, directory }))} /></> : <>
              {([["endpoint", "服务地址"], ["bucket", "存储桶"], ["region", "区域"], ["prefix", "对象前缀"], ["accessKeyId", "Access Key ID"]] as const).map(([key, label]) => <TextField key={key} label={label} value={s3[key]} onChange={value => patchS3(key, value)} />)}
              {([["secret", "Secret Access Key"], ["token", "会话令牌（可选）"]] as const).map(([key, label]) => <FieldShell key={key} label={label}>{descriptionId => <input type="password" aria-label={label} aria-describedby={descriptionId} autoComplete="new-password" value={target[key] ?? ""} onChange={event => c.setTarget(current => ({ ...current, [key]: event.target.value }))} />}</FieldShell>)}
              <label className="checkbox-field"><input type="checkbox" checked={s3.pathStyle} onChange={event => patchS3("pathStyle", event.target.checked)} /><span>使用路径式地址</span></label>
            </>}
          </div></fieldset>
          <p className="settings-helper-text">核验完成后切换，源文件保留。切换期间会短暂暂停业务写入。</p>
          <Button disabled={disabled || c.dirty} onClick={() => c.start("migrate")}>核验并迁移</Button>
        </>}
      </CollapsibleSection>
      <CollapsibleSection title="检查与回收"><div className="form-actions"><Button variant="secondary" disabled={disabled} onClick={() => c.start("verify")}>检查文件完整性</Button><Button variant="secondary" disabled={disabled || status.migrationPending} onClick={() => c.start("reconcile")}>回收无引用文件</Button></div></CollapsibleSection>
    </>}
    {c.job && <div role="status"><p>{c.job.title} · {c.job.statusText} {c.job.detailText || c.job.errorMessage}</p><ViewJobButton jobId={c.job.jobId} />{c.job.canCancel && <Button variant="secondary" onClick={c.cancel}>取消任务</Button>}</div>}
  </section>;
}
