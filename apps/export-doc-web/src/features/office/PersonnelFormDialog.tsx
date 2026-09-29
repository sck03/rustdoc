import { useState, type FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDepartmentRecord, PersonnelProfile, PersonnelRecord } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { useOfficeOperation } from "./useOfficeData.ts";
import { employmentTypeLabels, readPersonnelCreate, readPersonnelUpdate } from "./personnelModel.ts";
import { PersonnelIdentityFields } from "./PersonnelIdentityFields.tsx";
import { departmentOptions } from "../organization/organizationModel.ts";
import { useAttachmentDraft } from "../../ui/useAttachmentDraft.ts";
import { AttachmentDraftFields } from "../../ui/AttachmentDraftFields.tsx";

export function PersonnelFormDialog({ client, user, departments, record, onClose, onSaved }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; departments: PersonnelDepartmentRecord[]; record?: PersonnelRecord;
  onClose: () => void; onSaved: (record: PersonnelRecord) => void;
}) {
  const operation = useOfficeOperation();
  const attachments = useAttachmentDraft(record);
  const [requestKey] = useState(createRequestKey);
  const [hireDate, setHireDate] = useState(record?.hireDate ?? user.businessDate);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    void operation.run(signal => attachments.save(JSON.stringify(readPersonnelUpdate(form, 0)), signal,
      (current, signal) => current
        ? client.updatePersonnel({ id: current.employee.id, body: readPersonnelUpdate(form, current.versionNumber) }, { signal })
        : client.createPersonnel({ body: readPersonnelCreate(form, requestKey) }, { signal }),
      (current, entry, signal) => {
        const body = new FormData(); body.set("file", entry.file); body.set("expectedVersion", String(current.versionNumber));
        return entry.kind === "document" ? client.uploadPersonnelAttachment({ id: current.employee.id, body }, { signal })
          : client.uploadPersonnelImage({ id: current.employee.id, kind: entry.kind as "Avatar" | "IdentityFront" | "IdentityBack", body }, { signal });
      }), onSaved);
  }
  const canCorrectRegistration = !record || record.canCorrectRegistration;
  return <OfficeDialog title={record ? `编辑档案 · ${record.employee.fullName}` : "入职登记"} onClose={onClose} {...operation} protectChanges>
    <form className="personnel-form" onSubmit={submit} autoComplete="off">
      <fieldset className="office-form-grid" disabled={operation.busy}>
        <legend>基本信息</legend>
        {canCorrectRegistration && <OfficeField label="工号（系统内唯一）"><input name="employeeNumber" required maxLength={40} defaultValue={record?.employee.employeeNumber} autoFocus={!record} /></OfficeField>}
        <OfficeField label="姓名"><input name="fullName" required maxLength={100} defaultValue={record?.profile.fullName ?? ""} autoFocus={Boolean(record)} /></OfficeField>
        {canCorrectRegistration && <>
          <OfficeField label="所属部门"><select name="departmentId" required defaultValue={record?.employee.departmentId ?? user.departmentId ?? ""}>
            <option value="">请选择部门</option>{departmentOptions(departments).filter((item) => item.isActive).map((item) => <option value={item.code} key={item.code}>{item.label}</option>)}
          </select></OfficeField>
          <OfficeField label="岗位"><input name="jobTitle" required maxLength={120} defaultValue={record?.employee.jobTitle} /></OfficeField>
          <OfficeField label="入职日期"><input type="date" name="hireDate" required min="1900-01-01" max={user.businessDate} value={hireDate} onChange={(event) => setHireDate(event.target.value)} /></OfficeField>
          <label className="checkbox-field"><input name="onProbation" type="checkbox" defaultChecked={!record || record.employee.status === "Probation"} />入职时处于试用期</label>
        </>}
        {record && <p className="office-field-wide office-muted">{canCorrectRegistration ? "该档案尚未用于业务，可修正工号、入职日期及初始任职信息，保存后留下修正记录。" : `${record.employee.employeeNumber} · ${record.employee.departmentName} · ${record.employee.jobTitle}。部门与岗位通过调岗流程变更。`}</p>}
        <OfficeField label="用工类型"><select name="employmentType" defaultValue={record?.employmentType ?? "FullTime"}>
          {Object.entries(employmentTypeLabels).map(([key, label]) => <option value={key} key={key}>{label}</option>)}
        </select></OfficeField>
        <OfficeField label="试用截止日期（选填）"><input type="date" name="probationEndsOn" min={hireDate} defaultValue={record?.probationEndsOn ?? ""} /></OfficeField>
        <OfficeField label="合同截止日期（选填）"><input type="date" name="contractEndsOn" min={hireDate} defaultValue={record?.contractEndsOn ?? ""} /></OfficeField>
      </fieldset>
      <fieldset className="office-form-grid" disabled={operation.busy}><legend>工作联系方式 · 公司通讯录可见</legend>
        <OfficeField label="工作邮箱"><input type="email" name="workEmail" maxLength={254} defaultValue={record?.profile.workEmail ?? ""} /></OfficeField>
        <OfficeField label="工作电话"><input type="tel" name="workPhone" maxLength={50} defaultValue={record?.profile.workPhone ?? ""} /></OfficeField>
        <OfficeField label="工作地点" wide><input name="workLocation" maxLength={120} defaultValue={record?.profile.workLocation ?? ""} /></OfficeField>
      </fieldset>
      <details className="personnel-private-fields"><summary>个人资料与人事备注（限人事档案权限）</summary>
        <PersonnelPrivateFields profile={record?.profile} busy={operation.busy} />
      </details>
      <fieldset className="office-form-grid" disabled={operation.busy}><legend>照片与文档</legend>
        <AttachmentDraftFields draft={attachments} busy={operation.busy} kind="Avatar" label="选择人员头像" />
        <details className="personnel-private-fields office-field-wide"><summary>身份证照片（限人事档案权限）</summary>
          <AttachmentDraftFields draft={attachments} busy={operation.busy} kind="IdentityFront" label="选择身份证人像面（正面）" />
          <AttachmentDraftFields draft={attachments} busy={operation.busy} kind="IdentityBack" label="选择身份证国徽面（反面）" />
        </details>
        <AttachmentDraftFields draft={attachments} busy={operation.busy} label="选择档案图片或文档" />
      </fieldset>
      {operation.error && attachments.saved && <p role="status">档案已保存，待保存文件仍保留。再次保存会继续处理剩余文件。</p>}
      <OfficeSubmit busy={operation.busy} label={record ? "保存档案" : "登记入职"} />
    </form>
  </OfficeDialog>;
}

function PersonnelPrivateFields({ profile, busy }: { profile?: PersonnelProfile; busy: boolean }) {
  return <fieldset className="office-form-grid" disabled={busy}>
    <PersonnelIdentityFields profile={profile} />
    <OfficeField label="个人电话"><input type="tel" name="personalPhone" maxLength={50} defaultValue={profile?.personalPhone ?? ""} /></OfficeField>
    <OfficeField label="紧急联系人"><input name="emergencyContact" maxLength={100} defaultValue={profile?.emergencyContact ?? ""} /></OfficeField>
    <OfficeField label="紧急联系电话"><input type="tel" name="emergencyPhone" maxLength={50} defaultValue={profile?.emergencyPhone ?? ""} /></OfficeField>
    <OfficeField label="人事备注" wide><textarea name="notes" rows={3} maxLength={1000} defaultValue={profile?.notes ?? ""} /></OfficeField>
  </fieldset>;
}
