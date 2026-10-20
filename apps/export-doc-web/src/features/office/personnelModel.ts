import type { ApiUserDto, EmploymentStatus, EmploymentType, OaPersonnelChange, PersonnelCreateRequest, PersonnelProfile, PersonnelRecord, PersonnelUpdateRequest } from "../../api/index.ts";

export type PersonnelWorkflow = Exclude<OaPersonnelChange["action"], "Hire">;

export const employmentStatusLabels: Record<EmploymentStatus, string> = { Probation: "试用期", Active: "正式在职", Departed: "已离职" };
export const employmentTypeLabels: Record<EmploymentType, string> = { FullTime: "全职", PartTime: "兼职", Intern: "实习", Contractor: "合同用工" };
export const personnelHistoryLabels: Record<string, string> = {
  Hire: "入职登记", Edit: "档案更新", Image: "照片与证件更新", LinkAccount: "关联账号", UnlinkAccount: "解除账号绑定", Confirm: "转正", Transfer: "调岗", Depart: "离职归档", Rehire: "返聘入职",
};

export function canViewPersonnelDetails(user: ApiUserDto) {
  return (user.capabilities.permissions ?? []).some((grant) => grant.resourceKey === "office.people" && grant.action === "view-details"
    && ["own", "department", "company", "all"].includes(grant.dataScope));
}

const value = (form: FormData, key: string) => String(form.get(key) ?? "").trim();
export function readPersonnelProfile(form: FormData): PersonnelProfile {
  return { fullName: value(form, "fullName"), workEmail: value(form, "workEmail"), workPhone: value(form, "workPhone"),
    workLocation: value(form, "workLocation"), personalPhone: value(form, "personalPhone"), emergencyContact: value(form, "emergencyContact"),
    emergencyPhone: value(form, "emergencyPhone"), notes: value(form, "notes"),
    identityNumber: value(form, "identityNumber").toUpperCase(), identityAuthority: value(form, "identityAuthority"),
    registeredAddress: value(form, "registeredAddress"), identityValidFrom: value(form, "identityValidFrom") || null,
    identityValidUntil: form.has("identityLongTerm") ? null : value(form, "identityValidUntil") || null, identityLongTerm: form.has("identityLongTerm") };
}

export function readPersonnelUpdate(form: FormData, version: number): PersonnelUpdateRequest {
  const type = value(form, "employmentType");
  if (!(type in employmentTypeLabels)) throw new Error("请选择有效的用工类型。");
  return { expectedVersion: version, profile: readPersonnelProfile(form), employmentType: type as EmploymentType,
    probationEndsOn: value(form, "probationEndsOn") || null, contractEndsOn: value(form, "contractEndsOn") || null,
    correctionNote: value(form, "correctionNote"),
    registration: form.has("employeeNumber") ? { employeeNumber: value(form, "employeeNumber"), departmentId: value(form, "departmentId"),
      jobTitle: value(form, "jobTitle"), hireDate: value(form, "hireDate"), onProbation: form.has("onProbation") } : null };
}

export function readPersonnelCreate(form: FormData, key: string): PersonnelCreateRequest {
  const fields = readPersonnelUpdate(form, 0);
  return { requestKey: key, ...fields.registration!, employmentType: fields.employmentType,
    probationEndsOn: fields.probationEndsOn, contractEndsOn: fields.contractEndsOn, profile: fields.profile };
}

export function personnelReminders(record: PersonnelRecord, businessDate: string) {
  if (record.employee.status === "Departed") return [];
  const date = new Date(`${businessDate}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + 30);
  const soon = date.toISOString().slice(0, 10);
  return [
    { label: "试用期", day: record.employee.status === "Probation" ? record.probationEndsOn : null },
    { label: "合同", day: record.contractEndsOn },
    { label: "身份证", day: record.profile.identityLongTerm ? null : record.profile.identityValidUntil },
  ].filter((entry) => entry.day && entry.day <= soon).map((entry) => `${entry.label}${entry.day! < businessDate ? "已到期" : "即将到期"}：${entry.day}`);
}

export function personnelWorkflows(record: PersonnelRecord): PersonnelWorkflow[] {
  if (!record.canTransition) return [];
  if (record.employee.status === "Departed") return ["Rehire"];
  return record.employee.status === "Probation" ? ["Confirm", "Transfer", "Depart"] : ["Transfer", "Depart"];
}
