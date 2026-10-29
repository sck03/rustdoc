import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelOrganization, PersonnelAssignment, PersonnelDepartmentRecord, OrganizationPosition } from "../../api/index.ts";
import { useQuery } from "@tanstack/react-query";
import { PersonnelAssignmentFields } from "./PersonnelAssignmentEditor.tsx";
import { OrganizationManagerPicker } from "../organization/OrganizationManagerPicker.tsx";

export const emptyOrganization = (): PersonnelOrganization => ({ managerEmployeeId: null, functionalManagerEmployeeIds: [], additionalAssignments: [] });
type Reporting = Pick<PersonnelOrganization, "managerEmployeeId" | "managerName" | "managerAssignmentId" | "functionalManagerEmployeeIds" | "functionalManagerNames" | "functionalManagerAssignmentIds">;

export function PersonnelOrganizationEditor({ client, company, departments, primaryDepartment, value, onChange }: {
  client: ExportDocManagerApiClient; company: string; departments: PersonnelDepartmentRecord[]; primaryDepartment: string;
  value: PersonnelOrganization; onChange: (value: PersonnelOrganization) => void; positions?: OrganizationPosition[];
}) {
  const patch = (index: number, update: Partial<PersonnelAssignment>) => onChange({ ...value, additionalAssignments: value.additionalAssignments.map((old, i) => i === index ? { ...old, ...update } : old) });
  const options = useQuery({ queryKey: ["office", "people", "appointment-options", company], queryFn: ({ signal }) => client.getPersonnelOptions({ companyCode: company }, { signal }) });
  return <details className="office-field-wide" onInvalidCapture={event => { event.currentTarget.open = true; }}><summary>兼职与汇报关系</summary>
    <p className="office-muted">主职与兼职共同构成部门权限范围；直属、职能汇报可用于审批路由。调整任职不会自动增加账号的操作权限。</p>
    <ReportingEditor client={client} company={company} label="主职汇报" value={value} onChange={next => onChange({ ...value, ...next })} />
    {value.additionalAssignments.map((assignment, index) => assignment.companyCode && assignment.companyCode !== company && !options.data?.companies?.some(unit => unit.code === assignment.companyCode) ? <section className="office-resource-card" key={assignment.id ?? index}>
      <h4>其他单位任职（只读）</h4><p>{assignment.companyCode} · {assignment.departmentName || assignment.departmentId} · {assignment.jobTitle}</p>
      <p>任职日期：{assignment.startsOn || "长期"} 至 {assignment.endsOn || "长期"}；直属负责人：{assignment.managerName || "未设置"}</p><p className="office-muted">由对应单位的任职管理员维护。保存本页会保留此任职。</p>
    </section> : <fieldset className="office-resource-card" key={assignment.id ?? index}><legend>兼职 {index + 1}</legend><div className="office-form-grid">
      <PersonnelAssignmentFields client={client} assignment={assignment} company={company} companies={options.data?.companies ?? []} departments={departments} primaryDepartment={primaryDepartment} patch={update => patch(index, update)} />
    </div><ReportingEditor client={client} company={assignment.companyCode || company} label={`兼职 ${index + 1} 汇报`} value={assignment} onChange={next => patch(index, next)} />
      <button type="button" className="command-button secondary" onClick={() => onChange({ ...value, additionalAssignments: value.additionalAssignments.filter((_, i) => i !== index) })}>移除兼职</button>
    </fieldset>)}
    <button type="button" className="command-button secondary" disabled={value.additionalAssignments.length >= 20} onClick={() => onChange({ ...value, additionalAssignments: [...value.additionalAssignments, { companyCode: company, startsOn: "", endsOn: "", departmentId: "", jobTitle: "普通员工", managerEmployeeId: null, functionalManagerEmployeeIds: [] }] })}>添加任职</button>
  </details>;
}

export function ReportingEditor({ client, company, label, value, onChange, personnel = true }: { client: ExportDocManagerApiClient; company: string; label: string; value: Reporting; onChange: (value: Reporting) => void; personnel?: boolean }) {
  const [pick, setPick] = useState<"line" | "functional" | null>(null);
  return <section aria-label={label}><h4>{label}</h4><div className="office-card-actions"><span>直属负责人：{value.managerName || (value.managerEmployeeId ? `人员 #${value.managerEmployeeId}` : "未设置")}</span>
    <button type="button" className="command-button secondary" onClick={() => setPick("line")}>选择直属负责人</button>
    {value.managerEmployeeId && <button type="button" className="command-button secondary" onClick={() => onChange({ ...value, managerEmployeeId: null, managerAssignmentId: null, managerName: "" })}>清除直属负责人</button>}</div>
    <ul>{value.functionalManagerEmployeeIds.map((id, index) => <li key={id}>职能负责人：{value.functionalManagerNames?.[index] || `人员 #${id}`} <button type="button" className="text-button" onClick={() => onChange({ ...value, functionalManagerEmployeeIds: value.functionalManagerEmployeeIds.filter(v => v !== id), functionalManagerNames: value.functionalManagerNames?.filter((_, i) => i !== index), functionalManagerAssignmentIds: value.functionalManagerAssignmentIds?.filter((_, i) => i !== index) })}>移除</button></li>)}</ul>
    <button type="button" className="command-button secondary" disabled={value.functionalManagerEmployeeIds.length >= 10} onClick={() => setPick("functional")}>添加职能负责人</button>
    {pick && <OrganizationManagerPicker client={client} companyCode={company} personnel={personnel} onClose={() => setPick(null)} onSelect={person => {
      const name = `${person.fullName} · ${person.employeeNumber}`;
      if (pick === "line") onChange({ ...value, managerEmployeeId: person.id, managerAssignmentId: person.appointmentId, managerName: name });
      else if (!value.functionalManagerEmployeeIds.includes(person.id)) onChange({ ...value, functionalManagerEmployeeIds: [...value.functionalManagerEmployeeIds, person.id], functionalManagerAssignmentIds: [...(value.functionalManagerAssignmentIds ?? []), person.appointmentId], functionalManagerNames: [...(value.functionalManagerNames ?? value.functionalManagerEmployeeIds.map(id => `人员 #${id}`)), name] });
      setPick(null);
    }} />}
  </section>;
}

export function PersonnelOrganizationFacts({ value }: { value?: PersonnelOrganization }) {
  if (!value) return null;
  const reporting = (item: Reporting) => <p>直属负责人：{item.managerName || (item.managerEmployeeId ? `人员 #${item.managerEmployeeId}` : "未设置")}；职能负责人：{item.functionalManagerNames?.join("、") || item.functionalManagerEmployeeIds.map(id => `人员 #${id}`).join("、") || "未设置"}</p>;
  return <section className="personnel-fact-section" aria-label="任职与汇报关系"><h3>任职与汇报关系</h3>{reporting(value)}
    {value.additionalAssignments.length ? value.additionalAssignments.map(assignment => <div key={`${assignment.companyCode}:${assignment.departmentId}`}><strong>{assignment.companyCode} · {assignment.departmentName || assignment.departmentId} · {assignment.jobTitle}</strong><p>任职日期：{assignment.startsOn || "长期"} 至 {assignment.endsOn || "长期"}</p>{reporting(assignment)}</div>) : <p>兼职部门：未登记</p>}
  </section>;
}
