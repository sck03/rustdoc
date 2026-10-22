import { useEffect, type Dispatch, type SetStateAction } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequestSave, OaPersonnelChange } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { departmentOptions } from "../organization/organizationModel.ts";
import { employmentStatusLabels, employmentTypeLabels } from "../office/personnelModel.ts";
import { JobTitleInput } from "../office/PersonnelJobTitleInput.tsx";
import { PersonnelOrganizationEditor, emptyOrganization } from "../office/PersonnelOrganizationEditor.tsx";

import { personnelChangeLabels } from "./oaModel.ts";

export function PersonnelChangeFields({ client, user, draft, setDraft }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; draft: OaRequestSave; setDraft: Dispatch<SetStateAction<OaRequestSave>>;
}) {
  const change = draft.personnelChange!;
  const hire = change.action === "Hire";
  const target = useQuery({ queryKey: ["office", "people", "change-target", user.id, user.companyScope, draft.employeeId], enabled: !hire && Boolean(draft.employeeId),
    queryFn: ({ signal }) => client.getPersonnel({ id: draft.employeeId! }, { signal }) });
  const options = useQuery({ queryKey: ["office", "people", "change-options", user.id, user.companyScope, draft.employeeId], queryFn: ({ signal }) => client.getPersonnelOptions({ employeeId: draft.employeeId ?? undefined }, { signal }) });
  const patch = (next: Partial<OaPersonnelChange>) => setDraft(current => ({ ...current, personnelChange: { ...current.personnelChange!, ...next } }));
  const registration = change.registration;
  const person = target.data;
  useEffect(() => {
    if (person && !change.expectedPersonnelVersion) setDraft(current => ({ ...current, personnelChange: { ...current.personnelChange!,
      expectedPersonnelVersion: person.versionNumber, departmentId: person.employee.departmentId, jobTitle: person.employee.jobTitle,
      organization: current.personnelChange?.action === "Rehire" ? emptyOrganization() : person.organization ?? emptyOrganization() } }));
  }, [person, change.expectedPersonnelVersion, setDraft]);
  const error = options.error ?? target.error;
  const department = <select required value={hire ? registration?.departmentId ?? "" : change.departmentId ?? ""}
    onChange={event => hire ? patch({ registration: { ...registration!, departmentId: event.target.value } }) : patch({ departmentId: event.target.value })}>
    <option value="">请选择部门</option>{departmentOptions(options.data?.departments ?? []).filter(item => item.isActive).map(item => <option key={item.code} value={item.code}>{item.label}</option>)}
  </select>;
  return <>
    <p className="office-field-wide">变动类型：{personnelChangeLabels[change.action]}。最终批准前，现有人员与账号保持原状态；申请人及被变动人员不能审批本申请。</p>
    {error && <InlineNotice tone="error" action={<button type="button" onClick={() => { void options.refetch(); if (!hire) void target.refetch(); }}>重新读取</button>}>{readApiError(error)}</InlineNotice>}
    {hire ? <>
      <OfficeField label="工号（系统内唯一）"><input required maxLength={50} value={registration?.employeeNumber ?? ""} onChange={event => patch({ registration: { ...registration!, employeeNumber: event.target.value } })} /></OfficeField>
      <OfficeField label="姓名"><input required maxLength={100} value={registration?.profile.fullName ?? ""} onChange={event => patch({ registration: { ...registration!, profile: { ...registration!.profile, fullName: event.target.value } } })} /></OfficeField>
      <OfficeField label="所属部门">{department}</OfficeField>
      <OfficeField label="职位"><JobTitleInput positions={options.data?.positions} value={registration?.jobTitle ?? ""} onChange={event => patch({ registration: { ...registration!, jobTitle: event.target.value } })} /></OfficeField>
      <OfficeField label="入职日期"><input type="date" required max={user.businessDate} value={registration?.hireDate ?? user.businessDate} onChange={event => patch({ registration: { ...registration!, hireDate: event.target.value } })} /></OfficeField>
      <OfficeField label="用工类型"><select value={registration?.employmentType ?? "FullTime"} onChange={event => patch({ registration: { ...registration!, employmentType: event.target.value as NonNullable<typeof registration>["employmentType"] } })}>{Object.entries(employmentTypeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></OfficeField>
      <label className="checkbox-field"><input type="checkbox" checked={registration?.onProbation ?? true} onChange={event => patch({ registration: { ...registration!, onProbation: event.target.checked } })} />入职时处于试用期</label>
      <PersonnelOrganizationEditor client={client} company={user.companyScope} departments={options.data?.departments ?? []} primaryDepartment={registration?.departmentId ?? ""} value={registration?.organization ?? emptyOrganization()} onChange={organization => patch({ registration: { ...registration!, organization } })} positions={options.data?.positions} />
      <p className="office-field-wide office-muted">批准后建立档案，再维护工作联系方式、私密资料、照片与合同，并按需开通关联账号。审批附件保留在本申请内。</p>
    </> : <>
      {person ? <p className="office-field-wide">变动人员：{person.employee.fullName} · 工号 {person.employee.employeeNumber} · {person.employee.departmentName} · {person.employee.jobTitle} · {employmentStatusLabels[person.employee.status]}</p>
        : <p className="office-field-wide">{target.isFetching ? "正在读取人员档案…" : <>请从<Link to="/office/people">人员档案</Link>选择人员并发起变动。</>}</p>}
      {person && change.expectedPersonnelVersion !== person.versionNumber && <InlineNotice tone="warning" action={<button type="button" onClick={() => patch({ expectedPersonnelVersion: person.versionNumber })}>已核对，采用最新档案版本</button>}>人员档案已变化，请核对当前任职与申请内容后更新版本。</InlineNotice>}
      <OfficeField label="生效日期"><input type="date" required min={person?.lastEffectiveDate} max={user.businessDate} value={change.effectiveDate ?? user.businessDate} onChange={event => patch({ effectiveDate: event.target.value })} /></OfficeField>
      {["Transfer", "Rehire"].includes(change.action) && <><OfficeField label="新部门">{department}</OfficeField><OfficeField label="新职位"><JobTitleInput positions={options.data?.positions} value={change.jobTitle ?? ""} onChange={event => patch({ jobTitle: event.target.value })} /></OfficeField></>}
      {change.action === "Rehire" && <label className="checkbox-field"><input type="checkbox" checked={change.onProbation ?? true} onChange={event => patch({ onProbation: event.target.checked })} />返聘后进入试用期，账号由管理员复核后启用</label>}
      {person && ["Transfer", "Rehire"].includes(change.action) && <PersonnelOrganizationEditor client={client} company={person.companyScope ?? user.companyScope} departments={options.data?.departments ?? []} primaryDepartment={change.departmentId ?? ""} value={change.organization ?? emptyOrganization()} onChange={organization => patch({ organization })} positions={options.data?.positions} />}
    </>}
  </>;
}
