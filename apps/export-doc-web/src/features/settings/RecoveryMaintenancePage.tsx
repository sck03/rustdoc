import { useState } from "react";
import "../../styles/workspaces.css";
import "../../styles/responsive.css";
import "../../styles/settings-center.css";
import { Button } from "../../ui/Button.tsx";
import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import type { MaintenanceUnlock } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError, formatErrorResponse } from "../../ui/formUtils.ts";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";
import { RecoveryConnectionFields, emptyRecoveryS3 } from "./RecoveryConnectionFields.tsx";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { useRecoveryMaintenance } from "./useRecoveryMaintenance.ts";

export function RecoveryMaintenancePage() {
  const c = useRecoveryMaintenance();
  const requestConfirmation=useConfirmation();
  const [cleanupSecret,setCleanupSecret]=useState("");
  const [cleanupToken,setCleanupToken]=useState("");
  const [token, setToken] = useState("");
  const [source, setSource] = useState<MaintenanceUnlock>({ repositoryId: "", locationId: "", password: "", oldInstanceIsolated: false, sourceKind: "Directory" });
  const data = c.status.data;
  const patch = <K extends keyof MaintenanceUnlock>(key: K, value: MaintenanceUnlock[K]) => setSource(current => ({ ...current, [key]: value }));
  const finished = ["Completed", "Abandoned"].includes(data?.state ?? "");
  return <main className="recovery-maintenance"><EditorWorkspace title="灾备维护"><section className="form-section dashboard-section-stack">
    {c.error && <InlineNotice tone="error">{c.error}</InlineNotice>}
    {c.status.isError && <InlineNotice tone="error">{readApiError(c.status.error)}</InlineNotice>}
    {!c.csrf ? <form className="dashboard-section-stack" onSubmit={event => { event.preventDefault(); void c.login(token).then(ok => { if (ok) setToken(""); }); }}>
      <label><span>部署端一次性凭证</span><input type="password" autoComplete="off" value={token} onChange={event => setToken(event.target.value)} /></label>
      <Button type="submit" disabled={c.pending || token.length < 32}>进入维护</Button>
    </form> : <>
      {data && <InlineNotice tone={data.state === "Failed" ? "error" : data.state === "Completed" ? "success" : "info"}>{data.failure ? formatErrorResponse(data.failure) : data.message}</InlineNotice>}
      <fieldset className="settings-fieldset dashboard-section-stack" disabled={c.active || finished}>
        <div className="field-grid">
          <label><span>恢复源</span><select value={source.sourceKind} onChange={event => patch("sourceKind", event.target.value as MaintenanceUnlock["sourceKind"])}><option value="Directory">登记目录或移动盘</option><option value="S3">S3 仓库</option><option value="WebDav">WebDAV 仓库</option></select></label>
          <label><span>恢复仓库编号</span><input value={source.repositoryId} onChange={event => patch("repositoryId", event.target.value)} /></label>
          {source.sourceKind === "Directory" && <label><span>仓库位置</span><select value={source.locationId} onChange={event => patch("locationId", event.target.value)}><option value="">本机备份目录</option>{data?.locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}</select></label>}
          <label><span>恢复凭证</span><input type="password" autoComplete="off" value={source.password} onChange={event => patch("password", event.target.value)} /></label>
        </div>
        {source.sourceKind === "S3" && <RecoveryConnectionFields value={source.s3 ?? emptyRecoveryS3} onChange={value => patch("s3", value)} secret={source.secretAccessKey ?? ""} onSecret={value => patch("secretAccessKey", value)} token={source.sessionToken ?? ""} onToken={value => patch("sessionToken", value)} />}
        {source.sourceKind === "WebDav" && <div className="field-grid">
          <label><span>WebDAV 地址</span><input value={source.webDav?.url ?? ""} onChange={event => patch("webDav", { enabled: true, userName: "", ...source.webDav, url: event.target.value })} /></label>
          <label><span>WebDAV 账号</span><input value={source.webDav?.userName ?? ""} onChange={event => patch("webDav", { enabled: true, url: "", ...source.webDav, userName: event.target.value })} /></label>
          <label><span>WebDAV 密码</span><input type="password" autoComplete="off" value={source.webDavPassword ?? ""} onChange={event => patch("webDavPassword", event.target.value)} /></label>
        </div>}
        {source.sourceKind === "Directory" && <CollapsibleSection title="从便携文件读取"><label><span>维护收件目录中的文件名</span><input value={source.portableFile ?? ""} onChange={event => patch("portableFile", event.target.value)} /></label><p className="settings-helper-text">将 .edmpoint 文件放入 DataRoot/Recovery/Inbox。</p></CollapsibleSection>}
        <label className="checkbox-field"><input type="checkbox" checked={source.oldInstanceIsolated} onChange={event => patch("oldInstanceIsolated", event.target.checked)} /><span>旧业务实例已停止并隔离，不能继续写入文件库和备份仓库</span></label>
        <Button variant="secondary" type="button" disabled={!source.oldInstanceIsolated || !source.password || (!source.portableFile && source.repositoryId.length !== 32)} onClick={() => void c.call(async signal => { await c.client.unlockRecoveryRepository({ body: source }, { ...c.init, signal }); setSource(current => ({ ...current, password: "", secretAccessKey: "", sessionToken: "", webDavPassword: "" })); })}>解锁仓库</Button>
      </fieldset>
      <fieldset className="settings-fieldset dashboard-section-stack" disabled={c.active || !data?.points.length || finished}>
        <div className="field-grid">
          <label><span>恢复点</span><select value={c.request.pointId} onChange={event => c.update("pointId", event.target.value)}><option value="">请选择恢复点</option>{data?.points.map(point => <option key={point.id} value={point.id}>{formatRuntimeDate(point.createdAt)} · {point.provider} · {formatBytes(point.sizeBytes)}</option>)}</select></label>
          <label><span>附件恢复方式</span><select value={c.request.fileMode} onChange={event => c.update("fileMode", event.target.value as typeof c.request.fileMode)}><option value="Local">重建到本机</option><option value="Directory">重建到登记位置</option><option value="S3">重建到新 S3 文件库</option><option value="Reconnect">接回完好的原文件库</option></select></label>
          {c.request.fileMode === "Directory" && <label><span>新文件库位置</span><select value={c.request.targetLocation} onChange={event => c.update("targetLocation", event.target.value)}><option value="">请选择目标</option>{data?.locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}</select></label>}
        </div>
        {data?.nextCursor && <Button variant="secondary" onClick={() => void c.call(signal => c.client.listMaintenancePoints({ body: { after: data.nextCursor } }, { ...c.init, signal }))}>下一页恢复点</Button>}
        {(c.request.fileMode === "S3" || c.request.fileMode === "Reconnect") && <CollapsibleSection title={c.request.fileMode === "S3" ? "新 S3 文件库" : "重签 S3 访问凭据（可选）"} defaultOpen={c.request.fileMode === "S3"}><RecoveryConnectionFields value={c.request.s3 ?? emptyRecoveryS3} onChange={value => c.update("s3", value)} secret={c.request.secretAccessKey ?? ""} onSecret={value => c.update("secretAccessKey", value)} token={c.request.sessionToken ?? ""} onToken={value => c.update("sessionToken", value)} /></CollapsibleSection>}
        <div className="toolbar-actions"><Button variant="secondary" disabled={!c.request.pointId} onClick={() => void c.preflight()}>完整预检</Button><Button variant="danger" disabled={data?.state !== "Ready" || !c.ready} onClick={() => void c.apply()}>执行恢复与切换</Button></div>
      </fieldset>
      {c.active && data?.state !== "Committing" && <Button variant="secondary" onClick={() => void c.call(signal => c.client.cancelMaintenanceOperation({ ...c.init, signal }))}>取消当前操作</Button>}
      {Boolean(data?.artifacts?.length) && <CollapsibleSection title="未发布的恢复工件">
        <div className="field-grid"><label><span>S3 清理凭据（仅远端工件需要）</span><input type="password" autoComplete="off" value={cleanupSecret} onChange={event=>setCleanupSecret(event.target.value)}/></label><label><span>S3 会话凭据（可选）</span><input type="password" autoComplete="off" value={cleanupToken} onChange={event=>setCleanupToken(event.target.value)}/></label></div>
        <ul>{data?.artifacts?.map(item=><li key={item.id}>{item.location} <Button variant="secondary" disabled={c.active} onClick={()=>void c.call(async signal=>{if(!await requestConfirmation({title:"清理未发布恢复工件",description:`将删除 ${item.location} 中本次未启用的恢复文件。完整恢复点仍保留。`,confirmLabel:"确认清理"}))return;signal.throwIfAborted();await c.client.discardRecoveryArtifacts({body:{id:item.id,secretAccessKey:cleanupSecret,sessionToken:cleanupToken}},{...c.init,signal});setCleanupSecret("");setCleanupToken("");})}>清理</Button></li>)}</ul>
      </CollapsibleSection>}
      {!c.active && !finished && <Button variant="secondary" onClick={() => void c.call(signal => c.client.abandonMaintenance({ ...c.init, signal }))}>放弃本次维护</Button>}
    </>}
  </section></EditorWorkspace></main>;
}
