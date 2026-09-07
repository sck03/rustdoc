import { useState, type FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelAccountRecord, PersonnelDepartmentRecord, PersonnelRecord } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficePager, OfficeQueryState, OfficeSubmit } from "./OfficeUi.tsx";
import { personnelActionLabels, type PersonnelWorkflow } from "./personnelModel.ts";
import { applyPersonnelWorkflow, usePersonnelAccountOptions, usePersonnelClearance } from "./usePersonnelData.ts";
import { useOfficeOperation } from "./useOfficeData.ts";

export function PersonnelWorkflowDialog({ client, user, record, action, departments, onClose }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; record: PersonnelRecord; action: PersonnelWorkflow;
  departments: PersonnelDepartmentRecord[]; onClose: () => void;
}) {
  const operation = useOfficeOperation();
  const clearance = usePersonnelClearance(client, user, record.employee.id);
  const [departmentId, setDepartmentId] = useState(record.employee.departmentId);
  const changesJob = action === "transfer" || action === "rehire";
  const needsClearance = action === "depart" || action === "rehire" || action === "transfer" && departmentId !== record.employee.departmentId;
  const blocked = needsClearance && (clearance.isFetching || clearance.isError || !clearance.data?.isClear);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked) return;
    const form = new FormData(event.currentTarget);
    void operation.run((signal) => applyPersonnelWorkflow(client, record.employee.id, action, {
      expectedVersion: record.versionNumber, effectiveDate: String(form.get("effectiveDate") ?? ""), note: String(form.get("note") ?? ""),
      departmentId: changesJob ? departmentId : null, jobTitle: changesJob ? String(form.get("jobTitle") ?? "") : null, onProbation: form.has("onProbation"),
    }, signal), onClose);
  }
  return <OfficeDialog title={`${personnelActionLabels[action]} · ${record.employee.fullName}`} onClose={onClose} {...operation} protectChanges>
    <p>{record.employee.employeeNumber} · {record.employee.departmentName} · {record.employee.jobTitle}</p>
    <p className="office-muted">{action === "depart" ? `完成交接后归档人员，保留全部历史记录。${record.account ? "关联账号将停用并撤销会话。" : ""}`
      : action === "rehire" ? `登记新一轮任职，试用与合同日期可在档案中重新设置。${record.account ? "关联账号由系统管理员复核权限后启用。" : ""}`
        : action === "transfer" ? `登记部门或岗位调整，原业务记录保留原归属。${record.account ? "关联账号将同步部门范围并要求重新登录。" : ""}` : "将试用人员转为正式在职，并记录生效日期与办理说明。"}</p>
    {needsClearance && <InlineNotice tone={clearance.data?.isClear && !clearance.isError ? "success" : "warning"} title="行政交接核对">
      {clearance.isError ? "无法完成交接核对，请重新核对后提交。" : clearance.isFetching ? "正在核对…" : clearance.data?.isClear ? "未结清事项为 0，可以办理。"
        : `还有预约 ${clearance.data?.meetingCount ?? 0} 笔、物品申请／借用 ${clearance.data?.supplyCount ?? 0} 笔。请返回档案的“交接事项”处理。`}
      <button type="button" disabled={clearance.isFetching} onClick={() => void clearance.refetch()}>重新核对</button>
    </InlineNotice>}
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      <OfficeField label="生效日期"><input type="date" name="effectiveDate" required min={record.lastEffectiveDate} max={user.businessDate} defaultValue={user.businessDate} /></OfficeField>
      {changesJob && <>
        <OfficeField label="新部门"><select required value={departmentId} onChange={(event) => setDepartmentId(event.target.value)}><option value="">请选择部门</option>
          {departments.filter((item) => item.isActive).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></OfficeField>
        <OfficeField label="新岗位"><input name="jobTitle" required maxLength={120} defaultValue={record.employee.jobTitle} /></OfficeField>
      </>}
      {action === "rehire" && <label className="checkbox-field"><input type="checkbox" name="onProbation" defaultChecked />返聘后先进入试用期</label>}
      <OfficeField label="办理说明（必填）" wide><textarea name="note" required maxLength={500} rows={3} /></OfficeField>
    </fieldset><p className="office-muted">提交后立即生效。日期用于记录实际发生时间，不会安排未来自动执行。</p>
      <OfficeSubmit busy={operation.busy} disabled={blocked} label={personnelActionLabels[action]} />
    </form>
  </OfficeDialog>;
}

export function PersonnelAccountDialog({ client, user, record, onClose }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; record: PersonnelRecord; onClose: () => void;
}) {
  const operation = useOfficeOperation();
  const model = usePersonnelAccountOptions(client, user, record.employee.id);
  const [selected, setSelected] = useState<PersonnelAccountRecord | null>(null);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected) return;
    void operation.run((signal) => client.linkPersonnelAccount({ id: record.employee.id, body: { expectedVersion: record.versionNumber,
      userId: selected.id, expectedAccountVersion: selected.versionNumber } }, { signal }), onClose);
  }
  return <OfficeDialog title={`关联账号 · ${record.employee.fullName}`} onClose={onClose} {...operation} protectChanges>
    <p className="office-muted">选择本公司同部门的启用普通账号。关联后姓名和组织归属由人员档案维护，离职时同步停用账号；关联关系保留以便追溯，确认前请核对本人身份。</p>
    <form className="office-search" onSubmit={(event) => { event.preventDefault(); setSelected(null); model.search(); }}>
      <input aria-label="搜索可关联账号" placeholder="账号或姓名" value={model.keyword} onChange={(event) => model.setKeyword(event.target.value)} maxLength={100} disabled={operation.busy} />
      <button type="submit" disabled={operation.busy}>搜索</button>
    </form>
    <OfficeQueryState query={model.query} emptyTitle="没有可关联账号，请先在账号与权限中创建或调整同部门普通账号" />
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}><legend>选择账号</legend>
      <ul className="personnel-account-list office-field-wide">{!model.query.isError && model.query.data?.items.map((item) => <li key={item.id}><label>
        <input type="radio" name="account" value={item.id} checked={selected?.id === item.id} onChange={() => setSelected(item)} />
        <span>{item.username} · {item.fullName || "未填写姓名"}</span>
      </label></li>)}</ul>
    </fieldset>
      {selected && <p>已选：{selected.username}。将关联到 {record.employee.employeeNumber} / {record.employee.fullName}。</p>}
      <OfficeSubmit busy={operation.busy} disabled={!selected || model.query.isError} label="确认关联" />
    </form>
    <OfficePager page={model.query.data} paging={model.paging} busy={model.query.isFetching || operation.busy} />
  </OfficeDialog>;
}
