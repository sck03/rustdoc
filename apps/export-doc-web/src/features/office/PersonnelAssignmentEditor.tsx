import { useQuery } from "@tanstack/react-query";
import type { ApiOrganizationCompanyDto, ExportDocManagerApiClient, PersonnelAssignment, PersonnelDepartmentRecord } from "../../api/index.ts";
import { OfficeField } from "./OfficeUi.tsx";
import { JobTitleInput } from "./PersonnelJobTitleInput.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function PersonnelAssignmentFields({ client, assignment, company, companies, departments, primaryDepartment, patch }: {
  client: ExportDocManagerApiClient; assignment: PersonnelAssignment; company: string; companies: ApiOrganizationCompanyDto[];
  departments: PersonnelDepartmentRecord[]; primaryDepartment: string; patch: (value: Partial<PersonnelAssignment>) => void;
}) {
  const target = assignment.companyCode || company;
  const options = useQuery({ queryKey: ["office", "people", "appointment-options", target], enabled: target !== company,
    queryFn: ({ signal }) => client.getPersonnelOptions({ companyCode: target }, { signal }) });
  const available = target === company ? departments : options.data?.departments ?? [];
  return <>
    <OfficeField label="任职单位"><select value={target} onChange={event => patch({ companyCode: event.target.value, departmentId: "", jobTitle: "", id: undefined, managerEmployeeId: null, managerAssignmentId: null, managerName: "", functionalManagerEmployeeIds: [], functionalManagerNames: [], functionalManagerAssignmentIds: [] })}>
      {!companies.some(c => c.code === target) && <option value={target}>{target}</option>}
      {companies.map(c => <option key={c.code} value={c.code}>{c.name}</option>)}
    </select></OfficeField>
    <OfficeField label="任职部门"><select required value={assignment.departmentId} onChange={event => patch({ departmentId: event.target.value })}>
      <option value="">选择任职部门</option>{available.filter(d => d.isActive && (target !== company || d.code !== primaryDepartment)).map(d => <option key={d.code} value={d.code}>{d.name}</option>)}
    </select></OfficeField>
    <OfficeField label="任职职位"><JobTitleInput name={`assignment-${target}-${assignment.departmentId}`} positions={options.data?.positions} value={assignment.jobTitle} onChange={event => patch({ jobTitle: event.target.value })} /></OfficeField>
    <OfficeField label="任职开始日期"><input type="date" value={assignment.startsOn ?? ""} max={assignment.endsOn || undefined} onChange={event => patch({ startsOn: event.target.value })} /></OfficeField>
    <OfficeField label="任职结束日期（选填）"><input type="date" value={assignment.endsOn ?? ""} min={assignment.startsOn || undefined} onChange={event => patch({ endsOn: event.target.value })} /></OfficeField>
    {options.isError && <p role="alert">任职选项加载失败：{readApiError(options.error)} <button type="button" className="text-button" onClick={() => void options.refetch()}>重试</button></p>}
  </>;
}
