import { useRef, useState } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings, OaDeploymentApply, OaDeploymentResult } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { OfficeDialog, OfficeField } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { OrganizationManagerPicker } from "../organization/OrganizationManagerPicker.tsx";

export function ApprovalDeploymentDialog({ client, user, settings, action, onClose, onSaved }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; settings: OaApprovalSettings;
  action: "Publish" | "Disable"; onClose: () => void; onSaved: (settings: OaApprovalSettings) => void;
}) {
  const eligible = settings.rules.filter(rule => action === "Publish" || settings.publications.some(row => row.key === rule.key && row.enabled));
  const [selected, setSelected] = useState(() => eligible.slice(0, 50).map(rule => rule.key));
  const [keyword,setKeyword]=useState("");
  const visible=eligible.filter(rule=>!keyword||rule.name.includes(keyword));
  const [applicant, setApplicant] = useState<{ employeeId: number; appointmentId: string; name: string }>();
  const [choosing, setChoosing] = useState(false);
  const [result, setResult] = useState<OaDeploymentResult>();
  const attempt = useRef<OaDeploymentApply | undefined>(undefined);
  const operation = useOfficeOperation([["office", "approval-settings"]], false);
  const invalidate = () => { setResult(undefined); attempt.current = undefined; };
  const preview = () => void operation.run(signal => client.previewOaDeployment({ body: {
    action, items: selected.map(definitionKey => ({ definitionKey })),
    ...(applicant ? { sample: { employeeId: applicant.employeeId, appointmentId: applicant.appointmentId } } : {}),
  } }, { signal }), setResult, false);
  const apply = () => {
    if (!result || result.items.some(item => !item.ready)) return;
    attempt.current ??= { action, expectedVersion: result.expectedVersion, catalogRevision: result.catalogRevision,
      commandKey: createRequestKey(), items: result.items.map(({ definitionKey, expectedRevision, samples }) => ({ definitionKey, expectedRevision, samples })) };
    void operation.run(signal => client.applyOaDeployment({ body: attempt.current! }, { signal }), saved => { onSaved(saved); onClose(); });
  };
  return <><OfficeDialog title={action === "Publish" ? "批量发布流程" : "批量停用流程"} wide busy={operation.busy} error={operation.error} onClose={onClose}>
    {action === "Disable" && <InlineNotice tone="warning">所选流程停止接收新申请，在途申请继续按原发布版本办理。</InlineNotice>}
    <fieldset disabled={operation.busy}>
      <legend>选择流程（{selected.length}／50）</legend>
      <OfficeField label="搜索待部署流程"><input type="search" value={keyword} maxLength={100} onChange={event=>setKeyword(event.target.value)}/></OfficeField>
      <div className="office-card-actions"><Button type="button" onClick={() => { setSelected(visible.slice(0,50).map(rule => rule.key)); invalidate(); }}>选择筛选结果（最多 50 个）</Button><Button type="button" onClick={() => { setSelected([]); invalidate(); }}>清空</Button></div>
      <div className="office-form-grid">{visible.map(rule => <label key={rule.key} className="checkbox-field"><input type="checkbox" checked={selected.includes(rule.key)} disabled={!selected.includes(rule.key) && selected.length >= 50} onChange={event => { setSelected(values => event.target.checked ? [...values, rule.key] : values.filter(key => key !== rule.key)); invalidate(); }}/>{rule.name}</label>)}</div>
      {action === "Publish" && <div className="office-card-actions"><span>校验申请人：{applicant?.name ?? "尚未选择"}</span><Button type="button" onClick={() => setChoosing(true)}>选择人员任职</Button></div>}
      <Button type="button" disabled={!selected.length || action === "Publish" && !applicant} onClick={preview}>批量预检</Button>
    </fieldset>
    {result && <><ResponsiveTableFrame mobileLayout="cards"><table aria-label="流程部署预检结果"><thead><tr><th>流程</th><th>结果</th><th>待处理内容</th></tr></thead><tbody>{result.items.map(item => <tr key={item.definitionKey}><td data-label="流程">{item.name}</td><td data-label="结果">{item.ready ? "可执行" : "需修正"}</td><td data-label="待处理内容">{item.failure?.message ?? "—"}</td></tr>)}</tbody></table></ResponsiveTableFrame>
      <p className="office-muted">确认后再次核对配置与权限；任一流程未通过，本批不会提交。</p>
      {result.items.some(item=>!item.ready)&&result.items.some(item=>item.ready)&&<Button type="button" onClick={()=>{setSelected(result.items.filter(item=>item.ready).map(item=>item.definitionKey));invalidate();}}>仅选择通过项并重新预检</Button>}
      <Button type="button" variant="primary" busy={operation.busy} disabled={result.items.some(item => !item.ready)} onClick={apply}>{action === "Publish" ? `确认发布 ${result.items.length} 个流程` : `确认停用 ${result.items.length} 个流程`}</Button>
    </>}
  </OfficeDialog>{choosing && <OrganizationManagerPicker client={client} companyCode={user.companyScope} onClose={() => setChoosing(false)} onSelect={person => { setApplicant({employeeId:person.id,appointmentId:person.appointmentId,name:`${person.fullName} · ${person.departmentName}`}); setChoosing(false); invalidate(); }}/>}</>;
}
