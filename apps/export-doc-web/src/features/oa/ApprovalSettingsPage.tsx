import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { Button } from "../../ui/Button.tsx";
import { Link, useSearchParams } from "react-router-dom";
import { useState } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings, OaApprovalRule, OaRoutingUnit } from "../../api/index.ts";
import { ApprovalSimulationDialog } from "./ApprovalSimulationDialog.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useApprovalSettings, useApprovalSettingsDraft, type ApprovalAccount, type ApprovalSettingsField } from "./useApprovalSettings.ts";
import { oaModules, personnelChangeLabels } from "./oaModel.ts";
import { HandlingServicesEditor } from "./HandlingServicesEditor.tsx";
import { ApprovalGraphEditor } from "./ApprovalGraphEditor.tsx";
import { ApprovalSheetTemplates } from "./ApprovalSheetTemplates.tsx";
import { UnitWorkflowTasksPanel } from "./UnitWorkflowTasksPanel.tsx";
import { ApprovalDefinitionHistory } from "./ApprovalDefinitionHistory.tsx";
import { WorkflowExceptionsPanel } from "./WorkflowExceptionsPanel.tsx";
import { BusinessCalendarEditor } from "./BusinessCalendarEditor.tsx";
import { OaFormDesigner, newFormField } from "./OaFormDesigner.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

const views = [{key:"definitions",label:"流程定义"},{key:"calendar",label:"工作日历"},{key:"handling",label:"办理分工"},
  {key:"delegations",label:"审批代理"},{key:"templates",label:"审批单模板"},{key:"runtime",label:"流程运行"}] as const;
const editorTabs = [{key:"basic",label:"基本信息"},{key:"graph",label:"节点图"},{key:"notify",label:"抄送与通知"},
  {key:"form",label:"申请表单"},
  {key:"history",label:"版本与历史"},{key:"publish",label:"仿真与发布"}] as const;

export function ApprovalSettingsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const query = useApprovalSettings(client, user);
  if (!user.capabilities.canManageUsers && !user.capabilities.unitManagement?.workflows) return <PageState tone="permission" title="需要当前单位的流程管理授权" />;
  if (query.isPending) return <PageState tone="loading" title="正在读取审批设置" />;
  if (!query.data) return <PageState tone="error" title="审批设置读取失败" description={readApiError(query.error)} action={<Button type="button" onClick={() => void query.refetch()}>重试</Button>} />;
  return <ApprovalSettingsEditor key={`${user.id}:${user.companyScope}:${user.contextId}`} client={client} user={user} initial={query.data.settings} accounts={query.data.accounts} handlers={query.data.handlers} units={query.data.routingUnits} />;
}
function ruleLabel(rule: OaApprovalRule) {
  if(rule.form) return rule.name;
  const label = rule.kind === "room" ? "会议室预约" : rule.kind === "supply" ? "物品领用" : oaModules[rule.kind].name;
  return label + (rule.variant ? ` · ${personnelChangeLabels[rule.variant as keyof typeof personnelChangeLabels]}` : "");
}
function ApprovalSettingsEditor({ client, user, initial, accounts, handlers, units }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; initial: OaApprovalSettings;
  accounts: ApprovalAccount[]; handlers: ApprovalAccount[]; units: OaRoutingUnit[];
}) {
  const [params,setParams] = useSearchParams();
  const view = views.find(item => item.key === params.get("view"))?.key ?? "definitions";
  const tab = editorTabs.find(item => item.key === params.get("tab"))?.key ?? "basic";
  const [simulating,setSimulating] = useState<OaApprovalRule|null>(null);
  const [publishing,setPublishing] = useState(false);
  const { draft, setDraft, operation, formRef, changeRule, changeDelegate, save, publish, disable, remove, cancelActivation, reload, calendarInputs, changeCalendarInput, changedElsewhere } = useApprovalSettingsDraft(client, initial);
  const index = draft.rules.findIndex(rule => rule.key === params.get("definition"));
  const rule = index >= 0 ? draft.rules[index] : null;
  const publication = rule && draft.publications.find(item => item.key === rule.key);
  const section: ApprovalSettingsField = view === "calendar" ? "calendar" : view === "handling" ? "handlingServices" : view === "delegations" ? "delegations" : view === "templates" ? "sheetTemplates" : "rules";
  const update = (patch: Record<string,string|null>) => setParams(current => { const next=new URLSearchParams(current);for(const [key,value] of Object.entries(patch)) { if(value===null) next.delete(key);else next.set(key,value); }return next; });
  const accountOptions = <><option value={0}>请选择账号</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username} · {account.departmentId}</option>)}</>;
  const simulate = (publish: boolean) => { if(rule) { setPublishing(publish);setSimulating(rule); } };
  const addApplication = (source?:OaApprovalRule) => {
    const variant=createRequestKey().replace(/[^a-zA-Z0-9_]/g,"").slice(0,40),key=`general:${variant}`;
    const template=structuredClone(source??draft.rules.find(rule=>rule.key==="general")!);
    const created:OaApprovalRule={...template,key,variant,kind:"general",name:source?`${source.name}副本`:"新审批事项",form:template.form??{category:"自定义事项",fields:[newFormField()]}};
    setDraft(value=>({...value,rules:[...value.rules,created]}));update({definition:key,tab:"basic"});
  };
  return <EditorWorkspace className="office-workspace oa-workspace" title="流程管理" actions={<Link to="/office/approvals">返回申请与审批</Link>}
    description="在途申请保留提交时的流程版本。保存当前分区或当前流程，其他未保存草稿继续保留；发布和停用分别办理。">
    <SegmentedNav label="流程管理分区" value={view} items={views} onChange={view => update({view,definition:null,tab:null})} />
    {changedElsewhere && <InlineNotice tone="warning">设置已被其他管理员修改。当前草稿已保留，请核对后重新载入最新设置。</InlineNotice>}
    {view !== "runtime" && <form ref={formRef} onInvalidCapture={event => { const section=(event.target as HTMLElement).closest("details");if(section)section.open=true; }} onSubmit={event => {event.preventDefault();void save(section, rule?.key);}}>
      <fieldset disabled={operation.busy} className="office-form-grid">
        <legend>{views.find(item => item.key === view)?.label}</legend>
        {view==="definitions"&&!rule&&<Button type="button" disabled={draft.rules.length>=113} onClick={()=>addApplication()}>新建审批事项</Button>}
        {view === "definitions" && !rule && <ResponsiveTableFrame className="office-field-wide" mobileLayout="cards"><table aria-label="流程定义列表"><thead><tr><th>名称</th><th>类型</th><th>当前版本</th><th>启用状态</th><th>操作</th></tr></thead><tbody>
          {draft.rules.map(rule => { const published=draft.publications.find(item => item.key===rule.key);return <tr key={rule.key} data-definition-key={rule.key}>
            <td data-label="名称">{rule.name}</td><td data-label="类型">{ruleLabel(rule)}</td><td data-label="当前版本">{published?.version ?? "尚未发布"}</td><td data-label="启用状态">{published?.enabled ? "已启用" : "未启用"}</td>
            <td data-label="操作"><Button type="button" onClick={() => update({definition:rule.key,tab:"basic"})}>编辑流程</Button></td>
          </tr>; })}
        </tbody></table></ResponsiveTableFrame>}
        {view === "definitions" && rule && <section className="office-field-wide" data-definition-key={rule.key} aria-label={ruleLabel(rule)}>
          <div className="section-header"><h2>{ruleLabel(rule)}</h2><Button type="button" onClick={() => update({definition:null,tab:null})}>返回定义列表</Button></div>
          <SegmentedNav label="流程定义编辑" value={tab} items={editorTabs.filter(item=>item.key!=="form"||rule.form)} onChange={tab => update({tab})} />
          {tab==="form"&&rule.form&&<OaFormDesigner value={rule.form} onChange={form=>changeRule(index,{form})}/>}
          {tab === "basic" && <><OfficeField label="流程名称"><input required maxLength={100} value={rule.name} onChange={event => changeRule(index,{name:event.target.value})} /></OfficeField>
            <p className="office-muted">{publication ? `当前发布版本 ${publication.version} · ${publication.enabled ? "已启用" : "已停用"}` : "此流程尚未发布"}</p></>}
          {tab === "graph" && <ApprovalGraphEditor key={rule.key} definition={rule} client={client} currentCompanyCode={user.companyScope} definitions={draft.rules} stages={rule.stages} accounts={accounts} handlers={handlers} units={units} onChange={stages => changeRule(index,{stages})} />}
          {tab === "notify" && <OfficeField label="流程抄送（可查看进度，完成时通知）"><select multiple value={rule.notifyUserIds.map(String)} onChange={event => changeRule(index,{notifyUserIds:Array.from(event.target.selectedOptions,option => Number(option.value))})}>{accounts.map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username}</option>)}</select></OfficeField>}
          {tab === "history" && <ApprovalDefinitionHistory client={client} definitionKey={rule.key} onCopy={definition => changeRule(index,definition)} />}
          {tab === "publish" && <div className="office-card-actions"><Button type="button" onClick={() => simulate(false)}>模拟当前流程</Button><Button type="button" onClick={() => simulate(true)}>仿真并发布此流程</Button>{publication?.enabled && <Button type="button" onClick={() => void disable(rule.key)}>停用新申请</Button>}</div>}
        </section>}
        {view==="definitions"&&rule?.form&&<div className="office-card-actions"><Button disabled={draft.rules.length>=113} onClick={()=>addApplication(rule)}>复制为新事项</Button>{!publication&&<Button variant="danger" onClick={()=>void remove(rule.key,()=>update({definition:null,tab:null}))}>删除未发布草稿</Button>}</div>}
        {view === "calendar" && <BusinessCalendarEditor dateInputs={calendarInputs} onDateInput={changeCalendarInput} disabled={operation.busy} value={draft.calendar} onChange={calendar => setDraft(value => ({...value,calendar}))} />}
        {view === "handling" && <HandlingServicesEditor services={draft.handlingServices ?? []} accounts={handlers} disabled={operation.busy} onChange={handlingServices => setDraft(value => ({...value,handlingServices}))} />}
        {view === "templates" && <ApprovalSheetTemplates client={client} settings={draft} disabled={operation.busy} onChange={sheetTemplates => setDraft(value => ({...value,sheetTemplates}))} />}
        {view === "delegations" && <section className="oa-settings-section">
          <p className="office-muted">代理仅在有效期内生效；每个节点分别签署并记录实际办理人。</p>
          <div className="office-resource-grid">
          {draft.delegations.map((item,index) => <section className="office-resource-card" key={item.key} aria-label={`代理 ${index+1}`}>
            <OfficeField label="原审批人"><select value={item.principalUserId} onChange={event => changeDelegate(index,{principalUserId:Number(event.target.value)})}>{accountOptions}</select></OfficeField>
            <OfficeField label="代理人"><select value={item.delegateUserId} onChange={event => changeDelegate(index,{delegateUserId:Number(event.target.value)})}>{accountOptions}</select></OfficeField>
            {(["startsAt","endsAt"] as const).map(key => <OfficeField key={key} label={key === "startsAt" ? "代理开始时间" : "代理结束时间"}><input type="datetime-local" required value={toBusinessDateTimeLocalInput(item[key],user.businessTimeZone)} onChange={event => { try { changeDelegate(index,{[key]:businessDateTimeLocalInputToIso(event.target.value,user.businessTimeZone) ?? ""}); } catch(error) { event.target.setCustomValidity(readApiError(error));event.target.reportValidity(); } }} onInput={event => event.currentTarget.setCustomValidity("")} /></OfficeField>)}
            <label className="checkbox-field"><input type="checkbox" checked={item.isActive} onChange={event => changeDelegate(index,{isActive:event.target.checked})} />启用代理</label>
            <div className="office-card-actions"><Button type="button" onClick={() => setDraft(value => ({...value,delegations:value.delegations.filter((_,i) => i!==index)}))}>移除代理设置</Button></div>
          </section>)}
          </div>
          <div className="office-card-actions"><Button type="button" disabled={draft.delegations.length >= 50} onClick={() => setDraft(value => ({...value,delegations:[...value.delegations,{key:createRequestKey(),principalUserId:0,delegateUserId:0,startsAt:new Date().toISOString(),endsAt:new Date(Date.now()+86400000).toISOString(),isActive:true}]}))}>添加代理</Button></div>
        </section>}
      </fieldset>
      {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
      <OfficeSubmit busy={operation.busy} disabled={view==="definitions"&&!rule} label={view === "definitions" ? "保存当前流程" : `保存${views.find(item => item.key === view)?.label}`}>
        <Button type="button" disabled={operation.busy} onClick={() => void reload()}>重新载入审批设置</Button>
      </OfficeSubmit>
    </form>}
    {view === "runtime" && <>
      <UnitWorkflowTasksPanel client={client} user={user} accounts={accounts} /><WorkflowExceptionsPanel client={client} />
      <section className="office-resource-card" aria-label="定时发布"><h3>定时发布与待处理</h3><ul>{draft.activations?.map(entry => <li key={entry.id}>{draft.rules.find(rule => rule.key === entry.definitionKey)?.name ?? entry.definitionKey} · 版本 {entry.definitionVersion} · {entry.activateAt} · {entry.state === "Blocked" ? `待处理：${entry.errorCode}` : "等待启用"}<Button type="button" disabled={operation.busy} onClick={() => void cancelActivation(entry)}>取消定时启用</Button></li>)}</ul>{!draft.activations?.length && <p>暂无待启用或失败调度。</p>}</section>
    </>}
    {simulating && <ApprovalSimulationDialog client={client} user={user} rule={simulating} busy={operation.busy} error={operation.error} onPublish={publishing ? (samples,activateAt) => publish(simulating.key,samples,activateAt,() => setSimulating(null)) : undefined} onClose={() => setSimulating(null)} />}
  </EditorWorkspace>;
}
