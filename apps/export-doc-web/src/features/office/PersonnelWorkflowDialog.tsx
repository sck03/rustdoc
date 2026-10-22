import { useState, type FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDepartmentRecord, PersonnelRecord } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { type PersonnelWorkflow } from "./personnelModel.ts";
import { applyPersonnelWorkflow, usePersonnelClearance, usePersonnelOperation } from "./usePersonnelData.ts";
import { personnelChangeLabels } from "../oa/oaModel.ts";
import { departmentOptions } from "../organization/organizationModel.ts";
import { JobTitleInput } from "./PersonnelJobTitleInput.tsx";
import { usePersonnelPositions } from "./usePersonnelPositions.ts";
import { PersonnelOrganizationEditor, emptyOrganization } from "./PersonnelOrganizationEditor.tsx";

export function PersonnelWorkflowDialog({ client, user, record, action, departments, onClose }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; record: PersonnelRecord; action: PersonnelWorkflow;
  departments: PersonnelDepartmentRecord[]; onClose: () => void;
}) {
  const operation = usePersonnelOperation();
  const positions = usePersonnelPositions(client, user, record.employee.id);
  const clearance = usePersonnelClearance(client, user, record.employee.id);
  const [departmentId, setDepartmentId] = useState(record.employee.departmentId);
  const [organization, setOrganization] = useState(action === "Rehire" ? emptyOrganization() : record.organization ?? emptyOrganization());
  const changesJob = action === "Transfer" || action === "Rehire";
  const needsClearance = action === "Depart" || action === "Transfer";
  const blocked = needsClearance && (clearance.isFetching || clearance.isError ||
    !(action === "Depart" ? clearance.data?.canDepart : clearance.data?.isClear));
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked) return;
    const form = new FormData(event.currentTarget);
    void operation.run((signal) => applyPersonnelWorkflow(client, record.employee.id, action, {
      expectedVersion: record.versionNumber, effectiveDate: String(form.get("effectiveDate") ?? ""), note: String(form.get("note") ?? ""),
      departmentId: changesJob ? departmentId : null, jobTitle: changesJob ? String(form.get("jobTitle") ?? "") : null, onProbation: form.has("onProbation"),
      ...(changesJob ? { organization } : {}),
    }, signal), onClose);
  }
  return <OfficeDialog title={`办理${personnelChangeLabels[action]} · ${record.employee.fullName}`} onClose={onClose} {...operation} protectChanges
    hasChanges={changesJob && JSON.stringify(organization) !== JSON.stringify(action === "Rehire" ? emptyOrganization() : record.organization ?? emptyOrganization())}>
    <p>{record.employee.employeeNumber} · {record.employee.departmentName} · {record.employee.jobTitle}</p>
    <p className="office-muted">{action === "Depart" ? `完成交接后归档人员，保留全部历史记录。${record.account ? "关联账号将停用并撤销会话。" : ""}`
      : action === "Rehire" ? `登记新一轮任职，清除上一轮试用及合同截止日期，再按新合同补充。${record.account ? "关联账号由系统管理员复核权限后启用。" : ""}`
        : action === "Transfer" ? `登记部门或岗位调整，原业务记录保留原归属。${record.account ? "关联账号将同步部门范围并要求重新登录。" : ""}` : "将试用人员转为正式在职，并记录生效日期与办理说明。"}</p>
    {needsClearance && <InlineNotice tone={!blocked ? "success" : "warning"} title="行政交接核对"
      action={<button className="command-button secondary" type="button" disabled={clearance.isFetching} onClick={() => void clearance.refetch()}>重新核对</button>}>
      {clearance.isError ? "无法完成交接核对，请重新核对后提交。" : clearance.isFetching ? "正在核对…"
        : action === "Depart" && (clearance.data?.managedDepartments.length ?? 0) > 0 ? "该人员仍担任部门负责人，请先由管理员在组织架构中调整负责人。"
        : clearance.data?.isClear ? "未结清事项为 0，可以办理。"
        : `还有预约 ${clearance.data?.meetingCount ?? 0} 笔、物品申请／借用 ${clearance.data?.supplyCount ?? 0} 笔、OA 申请／审批 ${clearance.data?.approvalCount ?? 0} 笔、办理分工 ${clearance.data?.handlingCount ?? 0} 项。请返回档案的“交接事项”处理。`}
    </InlineNotice>}
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      <OfficeField label="生效日期"><input type="date" name="effectiveDate" required min={record.lastEffectiveDate} max={user.businessDate} defaultValue={user.businessDate} /></OfficeField>
      {changesJob && <>
        <OfficeField label="新部门"><select required value={departmentId} onChange={(event) => setDepartmentId(event.target.value)}><option value="">请选择部门</option>
          {departmentOptions(departments).filter((item) => item.isActive).map((item) => <option key={item.code} value={item.code}>{item.label}</option>)}</select></OfficeField>
        <OfficeField label="新职位"><JobTitleInput defaultValue={record.employee.jobTitle} positions={positions.data?.positions} /></OfficeField>
        <PersonnelOrganizationEditor client={client} company={record.companyScope ?? user.companyScope} departments={departments} primaryDepartment={departmentId} value={organization} onChange={setOrganization} />
      </>}
      {action === "Rehire" && <label className="checkbox-field"><input type="checkbox" name="onProbation" defaultChecked />返聘后先进入试用期</label>}
      <OfficeField label="办理说明（必填）" wide><textarea name="note" required maxLength={500} rows={3} /></OfficeField>
    </fieldset><p className="office-muted">提交后立即生效。日期用于记录实际发生时间，不会安排未来自动执行。</p>
      <OfficeSubmit busy={operation.busy} disabled={blocked} label={`办理${personnelChangeLabels[action]}`} />
    </form>
  </OfficeDialog>;
}
