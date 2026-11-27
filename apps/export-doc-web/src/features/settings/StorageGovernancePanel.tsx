import { startBrowserDownload } from "../../ui/downloadBlob.ts";
import { useEffect, useState } from "react";
import type { ApiCapacityPolicy, ApiRetentionRow, ExportDocManagerApiClient } from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { Button } from "../../ui/Button.tsx";
import { NumberField, TextField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { readJobMessage } from "../jobs/jobPresentation.ts";
import { useStorageGovernance } from "./useStorageGovernance.ts";
import { JobTrackingRecovery } from "../jobs/JobTrackingRecovery.tsx";
import { RetentionReview } from "./RetentionReview.tsx";
const stateLabels: Record<string, string> = { Normal: "正常", Warning: "预警", Critical: "容量不足", Unavailable: "不可用", Unknown: "未知" };
export function StorageGovernancePanel({ client, enabled }: { client: ExportDocManagerApiClient; enabled: boolean }) {
  const c = useStorageGovernance(client, enabled);
  const [policy, setPolicy] = useState<{ version: number; value: ApiCapacityPolicy } | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [editing, setEditing] = useState<ApiRetentionRow | null>(null);
  const [reason, setReason] = useState("");
  const [days, setDays] = useState(0);
  const [archiveLocation,setArchiveLocation]=useState("");
  const [archiveClass,setArchiveClass]=useState("");
  useEffect(()=>{setPolicy(null);setSelected([]);setEditing(null);setReason("");setArchiveLocation("");setArchiveClass("");},[client,enabled]);
  useEffect(()=>{setSelected([]);},[c.after,archiveClass]);
  useUnsavedChangesGuard({isDirty:policy!==null||editing!==null,message:"容量或档案保留设置尚未保存。"});
  const capacity = c.capacity.data;
  const allRows = c.retention.data?.items ?? [];
  const rows = archiveClass ? allRows.filter(row=>row.archiveClass===archiveClass) : allRows;
  const disabled = !enabled || c.busy;
  return <>
    {c.error && <InlineNotice tone="error">{c.error}</InlineNotice>}{c.message && <InlineNotice tone="success">{c.message}</InlineNotice>}
    <CollapsibleSection title="容量趋势与告警" defaultOpen>
      {capacity && <>
        <ResponsiveTableFrame label="容量区域统计" className="summary-table-frame"><table className="data-table" aria-label="容量区域统计"><thead><tr><th>区域</th><th>物理占用</th><th>引用容量</th><th>剩余容量</th><th>状态</th></tr></thead><tbody>{capacity.areas.map(area => <tr key={area.id}><td>{area.name}</td><td>{area.physicalBytes === undefined ? "未知" : formatBytes(area.physicalBytes)}</td><td>{area.logicalBytes === undefined ? "—" : formatBytes(area.logicalBytes)}</td><td>{area.availableBytes === undefined ? "未知" : formatBytes(area.availableBytes)}</td><td title={area.message}>{stateLabels[area.state] ?? area.state}</td></tr>)}</tbody></table></ResponsiveTableFrame>
        {capacity.areas.filter(area => ["Warning", "Critical", "Unavailable"].includes(area.state)).map(area => <InlineNotice key={area.id} tone="warning">{area.name}：{area.message}</InlineNotice>)}
        <p className="settings-helper-text">最近采样：{formatRuntimeDate(capacity.updatedAt)} · 正式档案受保护容量：{formatBytes(capacity.areas.find(a => a.id === "Files")?.protectedBytes ?? 0)}</p>
        <CollapsibleSection title="日汇总与阈值">
          <div className="field-grid">{([["warningPercent", "预警占用（%）"], ["criticalPercent", "严重占用（%）"], ["growthDays", "增长观察期（天）"]] as const).map(([key, label]) => <NumberField key={key} label={label} value={(policy?.value ?? capacity.policy)[key]} onChange={value => setPolicy(current => ({ version: current?.version ?? capacity.revision, value: { ...(current?.value ?? capacity.policy), [key]: value } }))} />)}
            <NumberField label="最少空闲容量（MiB）" value={(policy?.value ?? capacity.policy).minimumFreeBytes / 1048576} onChange={value => setPolicy(current => ({ version: current?.version ?? capacity.revision, value: { ...(current?.value ?? capacity.policy), minimumFreeBytes: Math.round(value * 1048576) } }))} />
          </div>
          <Button disabled={disabled || !policy} onClick={() => void c.run(signal => client.saveStorageCapacityPolicy({ body: { expectedVersion: policy!.version, policy: policy!.value } }, { signal })).then(ok => { if (ok) setPolicy(null); })}>保存告警策略</Button>
          <ResponsiveTableFrame label="容量日汇总" className="summary-table-frame"><table className="data-table" aria-label="容量日汇总"><thead><tr><th>日期</th><th>位置</th><th>物理占用</th><th>可回收</th></tr></thead><tbody>{capacity.samples.map(sample => <tr key={`${sample.day}:${sample.location}`}><td>{sample.day}</td><td>{sample.location === "Files" ? "生产文件库" : sample.location}</td><td>{formatBytes(sample.physicalBytes)}</td><td>{formatBytes(sample.reclaimableBytes)}</td></tr>)}</tbody></table></ResponsiveTableFrame>
        </CollapsibleSection>
      </>}
    </CollapsibleSection>
    <CollapsibleSection title="正式档案保留" error={Boolean(c.error)}>
      <InlineNotice tone="info">移除业务引用的永久文件默认长期保留。到期处置须由另一名管理员复核；已有恢复点按自身保留期保留历史资料。</InlineNotice>
      <div className="field-grid"><label><span>本页档案类别</span><select value={archiveClass} onChange={event=>setArchiveClass(event.target.value)}><option value="">全部类别</option>{[...new Set(allRows.map(row=>row.archiveClass))].map(name=><option key={name}>{name}</option>)}</select></label><Button variant="secondary" disabled={disabled||!rows.length} onClick={()=>setSelected(rows.map(row=>row.id))}>选择本页</Button></div>
      <ResponsiveTableFrame label="正式档案保留记录" className="summary-table-frame"><table className="data-table" aria-label="正式档案保留记录"><thead><tr><th>选择</th><th>文件</th><th>档案类别</th><th>起算日期</th><th>到期日期</th><th>保护状态</th><th>操作</th></tr></thead><tbody>{rows.map(row => <tr key={row.id}><td><input type="checkbox" aria-label={`选择${row.fileName}`} checked={selected.includes(row.id)} disabled={disabled} onChange={event => setSelected(current => event.target.checked ? [...current, row.id] : current.filter(id => id !== row.id))} /></td><td>{row.fileName}</td><td>{row.archiveClass}{row.archiveIdentity && "（已归档）"}</td><td>{row.basisDate || "待确认"}</td><td>{row.expiresOn || "长期保留"}</td><td>{row.held ? `暂停保留：${row.holdReason}` : row.activeReferences > 0 ? "仍有业务引用" : row.eligible ? "可申请处置" : "保留中"}</td><td><Button variant="secondary" disabled={disabled} onClick={() => void c.run(async signal => { const ticket = await client.createRetainedFileDownloadTicket({ id: row.id }, { signal }); signal.throwIfAborted(); startBrowserDownload(ticket.downloadUrl, row.fileName); })}>取回</Button><Button variant="secondary" disabled={disabled} onClick={() => { setEditing(row); setDays(row.days); setReason(""); }}>管理保留</Button></td></tr>)}</tbody></table></ResponsiveTableFrame>
      {!rows.length && <p>当前没有待管理的归档引用。</p>}
      <div className="form-actions">{c.after && <Button variant="secondary" disabled={disabled} onClick={() => c.setAfter("")}>返回首页</Button>}{c.retention.data?.nextCursor && <Button variant="secondary" disabled={disabled} onClick={() => c.setAfter(c.retention.data!.nextCursor)}>下一页</Button>}</div>
      {editing && <fieldset className="settings-fieldset" disabled={disabled}>
        <legend>{editing.fileName}</legend><div className="field-grid"><TextField label="档案类别" value={editing.archiveClass} onChange={archiveClass => setEditing({ ...editing, archiveClass })} /><label><span>结案或归档日期</span><input type="date" value={editing.basisDate} onChange={event => setEditing({ ...editing, basisDate: event.target.value })} /></label><NumberField label="保留天数（0 为长期保留）" value={days} onChange={setDays} /><TextField label="依据或暂停原因" value={reason} onChange={setReason} /></div>
        <div className="form-actions">
          <Button disabled={!reason || !editing.basisDate} onClick={() => void c.run(signal => client.classifyRetainedFile({ body: { id: editing.id, archiveClass: editing.archiveClass, basisDate: editing.basisDate, expectedVersion: editing.revision, reason } }, { signal })).then(ok => { if (ok) setEditing(null); })}>确认归档日期</Button>
          <Button variant="secondary" disabled={!reason || rows.find(row=>row.id===editing.id)?.archiveClass!==editing.archiveClass} onClick={() => void c.run(signal => client.saveRetentionPolicy({ body: { archiveClass: editing.archiveClass, days, expectedVersion: editing.policyRevision, reason } }, { signal })).then(ok => { if (ok) setEditing(null); })}>保存类别政策</Button>
          <Button variant="secondary" disabled={!reason} onClick={() => void c.run(signal => client.holdRetainedFile({ body: { digest: editing.digest, active: !editing.held, expectedVersion: editing.holdRevision, reason } }, { signal })).then(ok => { if (ok) setEditing(null); })}>{editing.held ? "解除暂停" : "暂停保留"}</Button>
          <Button variant="secondary" onClick={() => setEditing(null)}>关闭</Button>
        </div>
      </fieldset>}
      <div className="field-grid"><label><span>归档位置</span><select value={archiveLocation} disabled={disabled} onChange={event=>setArchiveLocation(event.target.value)}><option value="">选择已登记位置</option>{(c.locations.data?.registeredRoots??[]).map(name=><option key={name}>{name}</option>)}</select></label><Button disabled={disabled||!archiveLocation||!selected.length||!reason||selected.some(id=>!rows.find(row=>row.id===id)?.basisDate)} onClick={()=>void c.run(signal=>client.archiveRetainedFiles({body:{ids:selected,location:archiveLocation,expectedVersion:c.locations.data!.versionNumber!,reason}},{signal})).then(ok=>{if(ok)setSelected([]);})}>迁移所选档案</Button></div>
      {c.job&&<div className="settings-notice"><p role="status">{c.job.statusText} {readJobMessage(c.job)}</p><ViewJobButton jobId={c.job.jobId}/></div>}
      {c.tracking.uncertain && <JobTrackingRecovery jobId={c.tracking.jobId} pending={c.tracking.job.isFetching} retry={c.tracking.retry} dismiss={c.tracking.dismiss} />}
      <TextField label="归档或处置理由" value={reason} onChange={setReason} />
      <Button disabled={disabled || !selected.length || !reason || selected.some(id=>!rows.find(row=>row.id===id)?.eligible)} onClick={() => void c.run(signal => client.previewRetentionDeletion({ body: { ids: selected, reason } }, { signal })).then(ok => { if (ok) setSelected([]); })}>预检所选档案</Button>
      {c.retention.data?.plan && <RetentionReview key={c.retention.data.plan.token} plan={c.retention.data.plan} disabled={disabled} approve={() => { void c.run(signal => client.approveRetentionDeletion({ body: { token: c.retention.data!.plan!.token } }, { signal })); }} />}
    </CollapsibleSection>
  </>;
}
