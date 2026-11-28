import { startDownloadFromTicket } from "../../ui/downloadJobResult.ts";
import { useEffect, useState } from "react";
import type { ApiRetentionRow, ExportDocManagerApiClient } from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { Button } from "../../ui/Button.tsx";
import { NumberField, TextField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { readJobMessage } from "../jobs/jobPresentation.ts";
import { useStorageGovernance } from "./useStorageGovernance.ts";
import { JobTrackingRecovery } from "../jobs/JobTrackingRecovery.tsx";
import { RetentionReview } from "./RetentionReview.tsx";
import { StorageCapacityPanel } from "./StorageCapacityPanel.tsx";
export function StorageGovernancePanel({ client, enabled }: { client: ExportDocManagerApiClient; enabled: boolean }) {
  const c = useStorageGovernance(client, enabled);
  const { policy, setPolicy } = c;
  const [selected, setSelected] = useState<string[]>([]);
  const [editing, setEditing] = useState<ApiRetentionRow | null>(null);
  const [reason, setReason] = useState("");
  const [days, setDays] = useState(0);
  const [archiveLocation,setArchiveLocation]=useState("");
  const [archiveClass,setArchiveClass]=useState("");
  useEffect(()=>{setSelected([]);setEditing(null);setReason("");setArchiveLocation("");setArchiveClass("");},[client,enabled]);
  useEffect(()=>{setSelected([]);},[c.after,archiveClass]);
  useUnsavedChangesGuard({isDirty:policy!==null||editing!==null,message:"容量或档案保留设置尚未保存。"});
  const capacity = c.capacity.data;
  const allRows = c.retention.data?.items ?? [];
  const rows = archiveClass ? allRows.filter(row=>row.archiveClass===archiveClass) : allRows;
  const disabled = !enabled || c.busy;
  const selectedRows = selected.map(id => rows.find(row => row.id === id));
  const archiveBlocker = selectedRows.find(row => row?.archiveBlocker)?.archiveBlocker;
  return <>
    {c.error && <InlineNotice tone="error">{c.error}</InlineNotice>}{c.message && <InlineNotice tone="success">{c.message}</InlineNotice>}
    <StorageCapacityPanel capacity={capacity} policy={policy} setPolicy={setPolicy} savePolicy={c.savePolicy} disabled={disabled} />
    <CollapsibleSection title="正式档案保留" error={Boolean(c.error)}>
      <InlineNotice tone="info">移除业务引用的永久文件默认长期保留。到期处置须由另一名管理员复核；已有恢复点按自身保留期保留历史资料。</InlineNotice>
      <div className="field-grid">
        <label>
        <span>本页档案类别</span>
        <select value={archiveClass} onChange={event=>setArchiveClass(event.target.value)}>
        <option value="">全部类别</option>{[...new Set(allRows.map(row=>row.archiveClass))].map(name=>
        <option key={name}>{name}</option>)}</select>
        </label>
        <Button variant="secondary" disabled={disabled||!rows.length} onClick={()=>setSelected(rows.map(row=>row.id))}>选择本页</Button>
        </div>
      <ResponsiveTableFrame label="正式档案保留记录" className="summary-table-frame">
        <table className="data-table" aria-label="正式档案保留记录">
        <thead>
        <tr>
        <th>选择</th>
        <th>文件</th>
        <th>档案类别</th>
        <th>起算日期</th>
        <th>到期日期</th>
        <th>保护状态</th>
        <th>操作</th>
        </tr>
        </thead>
        <tbody>{rows.map(row => <tr key={row.id}>
        <td>
        <input type="checkbox" aria-label={`选择${row.fileName}`} checked={selected.includes(row.id)} disabled={disabled} onChange={event => setSelected(current => event.target.checked ? [...current, row.id] : current.filter(id => id !== row.id))} />
        </td>
        <td>{row.fileName}</td>
        <td>{row.archiveClass}{row.archiveIdentity && "（已归档）"}</td>
        <td>{row.basisDate || "待确认"}</td>
        <td>{row.expiresOn || "长期保留"}</td>
        <td>{row.held ? `暂停保留：${row.holdReason}` : row.activeReferences > 0 ? "仍有业务引用" : row.eligible ? "可申请处置" : "保留中"}</td>
        <td>
        <Button variant="secondary" disabled={disabled} onClick={() => void c.run(async signal => { const ticket = await client.createRetainedFileDownloadTicket({ id: row.id }, { signal }); signal.throwIfAborted(); startDownloadFromTicket(client, ticket, row.fileName); })}>取回</Button>
        <Button variant="secondary" disabled={disabled} onClick={() => { setEditing(row); setDays(row.days); setReason(""); }}>管理保留</Button>
        </td>
        </tr>)}</tbody>
        </table>
        </ResponsiveTableFrame>
      {!rows.length && <p>当前没有待管理的归档引用。</p>}
      <div className="form-actions">{c.after && <Button variant="secondary" disabled={disabled} onClick={() => c.setAfter("")}>返回首页</Button>}{c.retention.data?.nextCursor && <Button variant="secondary" disabled={disabled} onClick={() => c.setAfter(c.retention.data!.nextCursor)}>下一页</Button>}</div>
      {editing && <fieldset className="settings-fieldset" disabled={disabled}>
        <legend>{editing.fileName}</legend>
          <div className="field-grid">
          <TextField label="档案类别" value={editing.archiveClass} onChange={archiveClass => setEditing({ ...editing, archiveClass })} />
          <label>
          <span>结案或归档日期</span>
          <input type="date" value={editing.basisDate} onChange={event => setEditing({ ...editing, basisDate: event.target.value })} />
          </label>
          <NumberField label="保留天数（0 为长期保留）" value={days} onChange={setDays} />
          <TextField label="依据或暂停原因" value={reason} onChange={setReason} />
          </div>
        <div className="form-actions">
          <Button disabled={!reason || !editing.basisDate} onClick={() => void c.run(signal => client.classifyRetainedFile({ body: { id: editing.id, archiveClass: editing.archiveClass, basisDate: editing.basisDate, expectedVersion: editing.revision, reason } }, { signal })).then(ok => { if (ok) setEditing(null); })}>确认归档日期</Button>
          <Button variant="secondary" disabled={!reason || rows.find(row=>row.id===editing.id)?.archiveClass!==editing.archiveClass} onClick={() => void c.run(signal => client.saveRetentionPolicy({ body: { archiveClass: editing.archiveClass, days, expectedVersion: editing.policyRevision, reason } }, { signal })).then(ok => { if (ok) setEditing(null); })}>保存类别政策</Button>
          <Button variant="secondary" disabled={!reason} onClick={() => void c.run(signal => client.holdRetainedFile({ body: { digest: editing.digest, active: !editing.held, expectedVersion: editing.holdRevision, reason } }, { signal })).then(ok => { if (ok) setEditing(null); })}>{editing.held ? "解除暂停" : "暂停保留"}</Button>
          <Button variant="secondary" onClick={() => setEditing(null)}>关闭</Button>
        </div>
      </fieldset>}
      {archiveBlocker && <InlineNotice tone="warning">{archiveBlocker}</InlineNotice>}
      <div className="field-grid">
        <label>
        <span>归档位置</span>
        <select value={archiveLocation} disabled={disabled} onChange={event=>setArchiveLocation(event.target.value)}>
        <option value="">选择已登记位置</option>{(c.locations.data?.registeredRoots??[]).map(name=>
        <option key={name}>{name}</option>)}</select>
        </label>
        <Button disabled={disabled||!archiveLocation||!selected.length||!reason||selectedRows.some(row => !row?.canArchive)} onClick={()=>void c.run(signal=>client.archiveRetainedFiles({body:{ids:selected,location:archiveLocation,expectedVersion:c.locations.data!.versionNumber!,reason}},{signal})).then(ok=>{if(ok)setSelected([]);})}>迁移所选档案</Button>
        </div>
      {c.job&&<div className="settings-notice"><p role="status">{c.job.statusText} {readJobMessage(c.job)}</p><ViewJobButton jobId={c.job.jobId}/></div>}
      {c.tracking.uncertain && <JobTrackingRecovery jobId={c.tracking.jobId} pending={c.tracking.job.isFetching} retry={c.tracking.retry} dismiss={c.tracking.dismiss} />}
      <TextField label="归档或处置理由" value={reason} onChange={setReason} />
      <Button disabled={disabled || !selected.length || !reason || selected.some(id=>!rows.find(row=>row.id===id)?.eligible)} onClick={() => void c.run(signal => client.previewRetentionDeletion({ body: { ids: selected, reason } }, { signal })).then(ok => { if (ok) setSelected([]); })}>预检所选档案</Button>
      {c.retention.data?.plan && <RetentionReview key={c.retention.data.plan.token} plan={c.retention.data.plan} disabled={disabled} approve={() => { void c.run(signal => client.approveRetentionDeletion({ body: { token: c.retention.data!.plan!.token } }, { signal })); }} />}
    </CollapsibleSection>
  </>;
}
