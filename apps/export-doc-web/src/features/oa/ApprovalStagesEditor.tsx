import type { OaApprovalStage, OaApprovalRule } from "../../api/index.ts";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";

const sources: Record<OaApprovalStage["source"], string> = { Named: "指定人员会签", DepartmentManager: "任职部门负责人", LineManager: "直属负责人", FunctionalManagers: "职能负责人会签", CompanyExecutive: "公司执行负责人（CEO）" };
export const initialStage = (): OaApprovalStage => ({ source: "DepartmentManager", approverUserIds: [] });

export function ApprovalStagesEditor({ stages, accounts, onChange }: { stages: OaApprovalStage[]; accounts: ApprovalAccount[]; onChange: (stages: OaApprovalStage[]) => void }) {
  const patch = (index: number, stage: OaApprovalStage) => onChange(stages.map((old, i) => i === index ? stage : old));
  return <div className="oa-stages-editor">
    <p className="office-muted">阶段按顺序推进。同一阶段须全部批准，任一驳回即结束；申请人不能自批，同一人不能重复签署。组织来源在提交时解析并保留快照。</p>
    {stages.map((stage, index) => <fieldset className="office-resource-card" key={index}><legend>第 {index + 1} 阶段</legend>
      <OfficeField label="审批人来源"><select value={stage.source} onChange={event => patch(index, { source: event.target.value as OaApprovalStage["source"], approverUserIds: [] })}>{Object.entries(sources).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></OfficeField>
      {stage.source === "Named" && <>
        {stage.approverUserIds.map((id, signer) => <div className="office-card-actions" key={signer}><OfficeField label={`会签人 ${signer + 1}`}><select required value={id || ""} onChange={event => patch(index, { ...stage, approverUserIds: stage.approverUserIds.map((old, i) => i === signer ? Number(event.target.value) : old) })}><option value="">选择会签账号</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.fullName} · {account.username}</option>)}</select></OfficeField>
          <button type="button" className="command-button secondary" onClick={() => patch(index, { ...stage, approverUserIds: stage.approverUserIds.filter((_, i) => i !== signer) })}>移除会签人</button></div>)}
        <button type="button" className="command-button secondary" disabled={stage.approverUserIds.length >= 10} onClick={() => patch(index, { ...stage, approverUserIds: [...stage.approverUserIds, 0] })}>添加会签人</button>
      </>}
      <div className="office-card-actions"><button type="button" className="command-button secondary" disabled={index === 0} onClick={() => { const next = [...stages]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; onChange(next); }}>阶段上移</button>
        <button type="button" className="command-button secondary" onClick={() => onChange(stages.filter((_, i) => i !== index))}>移除阶段</button></div>
    </fieldset>)}
    <button type="button" className="command-button secondary" disabled={stages.length >= 10} onClick={() => onChange([...stages, initialStage()])}>添加审批阶段</button>
  </div>;
}

export function ApprovalAmountEditor({ rule, accounts, onChange }: { rule: OaApprovalRule; accounts: ApprovalAccount[]; onChange: (patch: Partial<OaApprovalRule>) => void }) {
  const branches = rule.amountBranches ?? [];
  const patch = (index: number, value: Partial<typeof branches[number]>) => onChange({ amountBranches: branches.map((old, i) => i === index ? { ...old, ...value } : old) });
  return <details><summary>金额条件审批（{branches.length} 条）</summary><p className="office-muted">按原币金额判断，下限包含、上限不包含。同币种区间不能重叠；未命中使用上方默认流程，不做隐式汇率换算。</p>
    {branches.map((branch, index) => <fieldset key={branch.key} className="office-resource-card"><legend>金额条件 {index + 1}</legend><div className="office-form-grid">
      <OfficeField label="条件名称"><input required maxLength={100} value={branch.name} onChange={event => patch(index, { name: event.target.value })} /></OfficeField>
      <OfficeField label="条件币种"><select value={branch.currency} onChange={event => patch(index, { currency: event.target.value })}>{["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].map(currency => <option key={currency}>{currency}</option>)}</select></OfficeField>
      <OfficeField label="金额下限（含）"><input required inputMode="decimal" pattern="[0-9]+([.][0-9]{1,6})?" value={branch.minimum} onChange={event => patch(index, { minimum: event.target.value })} /></OfficeField>
      <OfficeField label="金额上限（不含，空为不限）"><input inputMode="decimal" pattern="[0-9]+([.][0-9]{1,6})?" value={branch.maximum ?? ""} onChange={event => patch(index, { maximum: event.target.value || null })} /></OfficeField>
    </div><ApprovalStagesEditor stages={branch.stages} accounts={accounts} onChange={stages => patch(index, { stages })} />
      <button type="button" className="command-button secondary" onClick={() => onChange({ amountBranches: branches.filter((_, i) => i !== index) })}>移除金额条件</button></fieldset>)}
    <button type="button" className="command-button secondary" disabled={branches.length >= 10} onClick={() => onChange({ amountBranches: [...branches, { key: createRequestKey(), name: "金额审批", currency: "CNY", minimum: "0", maximum: null, stages: [initialStage()] }] })}>添加金额条件</button>
  </details>;
}
