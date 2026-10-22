import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelOrganization, PersonnelAssignment, PersonnelDepartmentRecord, OrganizationPosition } from "../../api/index.ts";
import { OfficeField } from "./OfficeUi.tsx";
import { JobTitleInput } from "./PersonnelJobTitleInput.tsx";
import { OrganizationManagerPicker } from "../organization/OrganizationManagerPicker.tsx";

export const emptyOrganization = (): PersonnelOrganization => ({ managerEmployeeId: null, functionalManagerEmployeeIds: [], additionalAssignments: [] });
type Reporting = Pick<PersonnelOrganization, "managerEmployeeId" | "managerName" | "functionalManagerEmployeeIds" | "functionalManagerNames">;

export function PersonnelOrganizationEditor({ client, company, departments, primaryDepartment, value, onChange, positions }: {
  client: ExportDocManagerApiClient; company: string; departments: PersonnelDepartmentRecord[]; primaryDepartment: string;
  value: PersonnelOrganization; onChange: (value: PersonnelOrganization) => void; positions?: OrganizationPosition[];
}) {
  const patch = (index: number, update: Partial<PersonnelAssignment>) => onChange({ ...value, additionalAssignments: value.additionalAssignments.map((old, i) => i === index ? { ...old, ...update } : old) });
  return <details className="office-field-wide" onInvalidCapture={event => { event.currentTarget.open = true; }}><summary>兼职与汇报关系</summary>
    <p className="office-muted">主职与兼职共同构成部门权限范围；直属、职能汇报可用于审批路由。调整任职不会自动增加账号的操作权限。</p>
    <ReportingEditor client={client} company={company} label="主职汇报" value={value} onChange={next => onChange({ ...value, ...next })} />
    {value.additionalAssignments.map((assignment, index) => <fieldset className="office-resource-card" key={index}><legend>兼职 {index + 1}</legend><div className="office-form-grid">
      <OfficeField label="兼职部门"><select required value={assignment.departmentId} onChange={event => patch(index, { departmentId: event.target.value })}><option value="">选择兼职部门</option>{departments.filter(d => d.isActive && d.code !== primaryDepartment).map(d => <option key={d.code} value={d.code}>{d.name}</option>)}</select></OfficeField>
      <OfficeField label="兼职职位"><JobTitleInput name={`additionalJob${index}`} positions={positions} value={assignment.jobTitle} onChange={event => patch(index, { jobTitle: event.target.value })} /></OfficeField>
    </div><ReportingEditor client={client} company={company} label={`兼职 ${index + 1} 汇报`} value={assignment} onChange={next => patch(index, next)} />
      <button type="button" className="command-button secondary" onClick={() => onChange({ ...value, additionalAssignments: value.additionalAssignments.filter((_, i) => i !== index) })}>移除兼职</button>
    </fieldset>)}
    <button type="button" className="command-button secondary" disabled={value.additionalAssignments.length >= 20} onClick={() => onChange({ ...value, additionalAssignments: [...value.additionalAssignments, { departmentId: "", jobTitle: "普通员工", managerEmployeeId: null, functionalManagerEmployeeIds: [] }] })}>添加兼职部门</button>
  </details>;
}

function ReportingEditor({ client, company, label, value, onChange }: { client: ExportDocManagerApiClient; company: string; label: string; value: Reporting; onChange: (value: Reporting) => void }) {
  const [pick, setPick] = useState<"line" | "functional" | null>(null);
  return <section aria-label={label}><h4>{label}</h4><div className="office-card-actions"><span>直属负责人：{value.managerName || (value.managerEmployeeId ? `人员 #${value.managerEmployeeId}` : "未设置")}</span>
    <button type="button" className="command-button secondary" onClick={() => setPick("line")}>选择直属负责人</button>
    {value.managerEmployeeId && <button type="button" className="command-button secondary" onClick={() => onChange({ ...value, managerEmployeeId: null, managerName: "" })}>清除直属负责人</button>}</div>
    <ul>{value.functionalManagerEmployeeIds.map((id, index) => <li key={id}>职能负责人：{value.functionalManagerNames?.[index] || `人员 #${id}`} <button type="button" className="text-button" onClick={() => onChange({ ...value, functionalManagerEmployeeIds: value.functionalManagerEmployeeIds.filter(v => v !== id), functionalManagerNames: value.functionalManagerNames?.filter((_, i) => i !== index) })}>移除</button></li>)}</ul>
    <button type="button" className="command-button secondary" disabled={value.functionalManagerEmployeeIds.length >= 10} onClick={() => setPick("functional")}>添加职能负责人</button>
    {pick && <OrganizationManagerPicker client={client} companyCode={company} personnel onClose={() => setPick(null)} onSelect={person => {
      const name = `${person.fullName} · ${person.employeeNumber}`;
      if (pick === "line") onChange({ ...value, managerEmployeeId: person.id, managerName: name });
      else if (!value.functionalManagerEmployeeIds.includes(person.id)) onChange({ ...value, functionalManagerEmployeeIds: [...value.functionalManagerEmployeeIds, person.id], functionalManagerNames: [...(value.functionalManagerNames ?? value.functionalManagerEmployeeIds.map(id => `人员 #${id}`)), name] });
      setPick(null);
    }} />}
  </section>;
}

export function PersonnelOrganizationFacts({ value }: { value?: PersonnelOrganization }) {
  if (!value) return null;
  const reporting = (item: Reporting) => <p>直属负责人：{item.managerName || (item.managerEmployeeId ? `人员 #${item.managerEmployeeId}` : "未设置")}；职能负责人：{item.functionalManagerNames?.join("、") || item.functionalManagerEmployeeIds.map(id => `人员 #${id}`).join("、") || "未设置"}</p>;
  return <section className="personnel-fact-section" aria-label="任职与汇报关系"><h3>任职与汇报关系</h3>{reporting(value)}
    {value.additionalAssignments.map(assignment => <div key={assignment.departmentId}><strong>兼职部门 {assignment.departmentId} · {assignment.jobTitle}</strong>{reporting(assignment)}</div>)}
  </section>;
}
