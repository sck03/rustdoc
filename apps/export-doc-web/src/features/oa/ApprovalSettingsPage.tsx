import { Link } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient, OaApprovalSettings } from "../../api/index.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useApprovalSettings, useApprovalSettingsDraft, type ApprovalAccount } from "./useApprovalSettings.ts";
import { oaModules } from "./oaModel.ts";
import { HandlingServicesEditor } from "./HandlingServicesEditor.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

export function ApprovalSettingsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const query = useApprovalSettings(client, user);
  if (!user.capabilities.canManageUsers) return <PageState tone="permission" title="只有管理员可以设置审批规则" />;
  if (query.isPending) return <PageState tone="loading" title="正在读取审批设置" />;
  if (!query.data) return <PageState tone="error" title="审批设置读取失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重试</button>} />;
  return <ApprovalSettingsEditor key={`${user.id}:${user.companyScope}`} client={client} user={user} initial={query.data.settings} accounts={query.data.accounts} />;
}
function ApprovalSettingsEditor({ client, user, initial, accounts }: { client: ExportDocManagerApiClient; user: ApiUserDto; initial: OaApprovalSettings; accounts: ApprovalAccount[] }) {
  const { draft, setDraft, operation, formRef, changeRule, changeDelegate, save, reload, changedElsewhere } = useApprovalSettingsDraft(client, initial);
  const accountOptions = <><option value={0}>请选择账号</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username} · {account.departmentId}</option>)}</>;
  return <section className="work-surface office-workspace oa-workspace" aria-label="审批规则与代理">
    <header className="oa-heading"><h2>审批规则与代理</h2><Link to="/office/approvals">返回申请与审批</Link></header>
    <p className="office-muted">设置仅用于本公司后续提交的申请。在途申请保留提交时的步骤；代理按当前有效期校验，审批人与代理人仍须具有对应查看和审批权限。</p>
    {changedElsewhere && <InlineNotice tone="warning">审批设置已被其他管理员修改。当前草稿已保留，请记录需要保留的内容后重新载入最新设置。</InlineNotice>}
    <form ref={formRef} onInvalidCapture={event => { const section = (event.target as HTMLElement).closest("details"); if (section) section.open = true; }} onSubmit={event => { event.preventDefault(); void save(); }}>
      <fieldset disabled={operation.busy} className="office-form-grid">
        <legend>六类申请审批规则</legend>
        {draft.rules.map((rule, index) => <section className="office-resource-card office-field-wide" key={rule.kind} aria-label={`${oaModules[rule.kind].name}审批规则`}>
          <h3>{oaModules[rule.kind].name}</h3><OfficeField label="审批方式"><select value={rule.mode} onChange={event => changeRule(index, { mode: event.target.value as typeof rule.mode, approverUserIds: [] })}>
            <option value="Single">单步审批（按岗位权限）</option><option value="DepartmentChain">部门负责人逐级审批</option><option value="Named">指定审批人（按顺序，可单人）</option>
          </select></OfficeField>
          {rule.mode === "DepartmentChain" && <p className="office-muted">从申请人所在部门逐级到顶层部门，负责人须关联启用账号；跳过申请人本人和重复负责人，缺少负责人时阻止提交。</p>}
          {rule.mode === "Named" && <><ol>{rule.approverUserIds.map((id, step) => <li key={step} className="office-card-actions">
            <OfficeField label={`第 ${step + 1} 步审批人`}><select value={id} onChange={event => changeRule(index, { approverUserIds: rule.approverUserIds.map((value, i) => i === step ? Number(event.target.value) : value) })}>{accountOptions}</select></OfficeField>
            <button className="command-button secondary" type="button" disabled={step === 0} onClick={() => { const ids = [...rule.approverUserIds]; [ids[step - 1], ids[step]] = [ids[step], ids[step - 1]]; changeRule(index, { approverUserIds: ids }); }}>上移</button>
            <button className="command-button secondary" type="button" onClick={() => changeRule(index, { approverUserIds: rule.approverUserIds.filter((_, i) => i !== step) })}>移除步骤</button>
          </li>)}</ol><button className="command-button secondary" type="button" disabled={rule.approverUserIds.length >= 10} onClick={() => changeRule(index, { approverUserIds: [...rule.approverUserIds, 0] })}>添加审批步骤</button></>}
        </section>)}
      </fieldset>
      <HandlingServicesEditor services={draft.handlingServices ?? []} accounts={accounts} disabled={operation.busy} onChange={handlingServices => setDraft(value => ({ ...value, handlingServices }))} />
      <details><summary>审批代理</summary>
        <p className="office-muted">代理用于有指定审批人的步骤；只接受直接代理，同一人不能代批同一申请的多个步骤。停用或到期立即失效。</p>
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
  </section>;
}
