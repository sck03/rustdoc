import { Link } from "react-router-dom";
import { useState } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings, OaApprovalRule, OaRoutingUnit } from "../../api/index.ts";
import { ApprovalSimulationDialog } from "./ApprovalSimulationDialog.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useApprovalSettings, useApprovalSettingsDraft, type ApprovalAccount } from "./useApprovalSettings.ts";
import { oaModules, personnelChangeLabels } from "./oaModel.ts";
import { HandlingServicesEditor } from "./HandlingServicesEditor.tsx";
import { ApprovalGraphEditor } from "./ApprovalGraphEditor.tsx";
import { ApprovalSheetTemplates } from "./ApprovalSheetTemplates.tsx";
import { UnitWorkflowTasksPanel } from "./UnitWorkflowTasksPanel.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function ApprovalSettingsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const query = useApprovalSettings(client, user);
  if (!user.capabilities.canManageUsers && !user.capabilities.unitManagement?.workflows) return <PageState tone="permission" title="需要当前单位的流程管理授权" />;
  if (query.isPending) return <PageState tone="loading" title="正在读取审批设置" />;
  if (!query.data) return <PageState tone="error" title="审批设置读取失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} />;
  return <ApprovalSettingsEditor key={`${user.id}:${user.companyScope}`} client={client} user={user} initial={query.data.settings} accounts={query.data.accounts} handlers={query.data.handlers} units={query.data.routingUnits} />;
}
function ApprovalSettingsEditor({ client, user, initial, accounts, handlers, units }: { client: ExportDocManagerApiClient; user: ApiUserDto; initial: OaApprovalSettings; accounts: ApprovalAccount[]; handlers: ApprovalAccount[]; units: OaRoutingUnit[] }) {
  const [simulating,setSimulating] = useState<OaApprovalRule|null>(null);
  const { draft, setDraft, operation, formRef, changeRule, changeDelegate, save, publish, disable, reload, changedElsewhere } = useApprovalSettingsDraft(client, initial);
  const accountOptions = <><option value={0}>请选择账号</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username} · {account.departmentId}</option>)}</>;
  return <section className="work-surface office-workspace oa-workspace" aria-label="审批规则与代理">
    <header className="oa-heading"><h2>审批规则与代理</h2><Link to="/office/approvals">返回申请与审批</Link></header>
    <p className="office-muted">设置仅用于本公司后续提交的申请。在途申请保留提交时的步骤；代理按当前有效期校验，审批人与代理人仍须具有对应查看和审批权限。</p>
    <p className="office-muted">先保存草稿，再独立发布需要启用的流程。停用只阻止新提交；在途申请继续使用原版本。人员变动按入职、转正、调岗、离职、返聘分别配置。</p>
    {changedElsewhere && <InlineNotice tone="warning">审批设置已被其他管理员修改。当前草稿已保留，请记录需要保留的内容后重新载入最新设置。</InlineNotice>}
    <form ref={formRef} onInvalidCapture={event => { const section = (event.target as HTMLElement).closest("details"); if (section) section.open = true; }} onSubmit={event => { event.preventDefault(); void save(); }}>
      <fieldset disabled={operation.busy} className="office-form-grid">
        <legend>申请审批规则</legend>
        <p className="office-muted office-field-wide">正式入职、转正、调岗、离职和返聘通过已发布的人员变动流程办理。首次建档和普通档案维护由获授权的人事操作；管理员也不能跳过正式审批。</p>
        {draft.rules.map((rule,index)=>{
          const publication=draft.publications.find(p=>p.key===rule.key);
          const label=rule.kind==="room"?"会议室预约":rule.kind==="supply"?"物品领用":oaModules[rule.kind].name;
          return <details className="office-resource-card office-field-wide" data-definition-key={rule.key} key={rule.key}>
            <summary>{label}{rule.variant? ` · ${personnelChangeLabels[rule.variant as keyof typeof personnelChangeLabels]}`:""} · {publication?`版本 ${publication.version} · ${publication.enabled?"已启用":"已停用"}`:"尚未发布"}</summary>
            <OfficeField label="流程名称"><input required maxLength={100} value={rule.name} onChange={e=>changeRule(index,{name:e.target.value})}/></OfficeField>
            <ApprovalGraphEditor stages={rule.stages} accounts={accounts} units={units} onChange={stages=>changeRule(index,{stages})}/>
            <OfficeField label="流程抄送（可查看进度，完成时通知）"><select multiple value={rule.notifyUserIds.map(String)} onChange={e=>changeRule(index,{notifyUserIds:Array.from(e.target.selectedOptions,o=>Number(o.value))})}>{accounts.map(a=><option key={a.id} value={a.id}>{a.fullName} · {a.username}</option>)}</select></OfficeField>
            <div className="office-card-actions"><button type="button" className="command-button secondary" onClick={()=>setSimulating(rule)}>模拟当前流程</button><button type="button" className="command-button" onClick={()=>void publish(rule.key)}>保存并发布此流程</button>
              {publication?.enabled&&<button type="button" className="command-button secondary" onClick={()=>void disable(rule.key)}>停用新申请</button>}</div>
          </details>;
        })}
      </fieldset>
      <BusinessCalendarEditor value={draft.calendar} onChange={calendar=>setDraft(value=>({...value,calendar}))}/>
      <HandlingServicesEditor services={draft.handlingServices ?? []} accounts={handlers} disabled={operation.busy} onChange={handlingServices => setDraft(value => ({ ...value, handlingServices }))} />
      <ApprovalSheetTemplates client={client} settings={draft} disabled={operation.busy} onChange={sheetTemplates => setDraft(value => ({ ...value, sheetTemplates }))} />
      <details><summary>审批代理</summary>
        <p className="office-muted">代理用于有指定审批人的步骤；只接受直接代理；每个节点单独签署并记录实际办理人。停用或到期立即失效。</p>
        {draft.delegations.map((item, index) => <fieldset disabled={operation.busy} className="office-form-grid office-resource-card" key={item.key}><legend>代理 {index + 1}</legend>
          <OfficeField label="原审批人"><select value={item.principalUserId} onChange={event => changeDelegate(index, { principalUserId: Number(event.target.value) })}>{accountOptions}</select></OfficeField>
          <OfficeField label="代理人"><select value={item.delegateUserId} onChange={event => changeDelegate(index, { delegateUserId: Number(event.target.value) })}>{accountOptions}</select></OfficeField>
          {(["startsAt", "endsAt"] as const).map((key) => <OfficeField label={key === "startsAt" ? "代理开始时间" : "代理结束时间"} key={key}><input type="datetime-local" required value={toBusinessDateTimeLocalInput(item[key], user.businessTimeZone)} onChange={event => { try { changeDelegate(index, { [key]: businessDateTimeLocalInputToIso(event.target.value, user.businessTimeZone) ?? "" }); } catch (error) { event.target.setCustomValidity(readApiError(error)); event.target.reportValidity(); } }} onInput={event => event.currentTarget.setCustomValidity("")} /></OfficeField>)}
          <label className="checkbox-field"><input type="checkbox" checked={item.isActive} onChange={event => changeDelegate(index, { isActive: event.target.checked })} />启用代理</label>
          <button className="command-button secondary" type="button" onClick={() => setDraft(value => ({ ...value, delegations: value.delegations.filter((_, i) => i !== index) }))}>移除代理设置</button>
        </fieldset>)}
        <button className="command-button secondary" type="button" disabled={operation.busy || draft.delegations.length >= 50} onClick={() => setDraft(value => ({ ...value, delegations: [...value.delegations, { key: createRequestKey(), principalUserId: 0, delegateUserId: 0, startsAt: new Date().toISOString(), endsAt: new Date(Date.now() + 86400000).toISOString(), isActive: true }] }))}>添加代理</button>
      </details>
      {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
      <OfficeSubmit busy={operation.busy} label="保存审批设置" />
      <button className="command-button secondary" type="button" disabled={operation.busy} onClick={() => void reload()}>重新载入审批设置</button>
    </form>
    <UnitWorkflowTasksPanel client={client} user={user} accounts={accounts} />
    {simulating && <ApprovalSimulationDialog client={client} user={user} rule={simulating} onClose={()=>setSimulating(null)} />}
  </section>;
}
import { BusinessCalendarEditor } from "./BusinessCalendarEditor.tsx";
