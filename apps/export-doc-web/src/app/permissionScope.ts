import type { ApiPermissionScopeRestrictionDto } from "../api/index.ts";

export type PermissionSubject = { id: number; companyScope: string; departmentId: string; effectiveDepartmentIds?: string[]; employee?: { organization?: { additionalAssignments: { departmentId: string }[] } } | null };
export type PermissionRecord = { ownerUserId?: number | null; companyScope: string; departmentId: string; projectId?: string | null };

export function hasDepartment(subject: Pick<PermissionSubject, "departmentId" | "employee" | "effectiveDepartmentIds">, department: string) {
  return subject.effectiveDepartmentIds ? subject.effectiveDepartmentIds.includes(department)
    : subject.departmentId === department || subject.employee?.organization?.additionalAssignments.some(assignment => assignment.departmentId === department) === true;
}

export function isRecordInPermissionScope(scope: string, subject: PermissionSubject | undefined, record: PermissionRecord | null, administrator = false) {
  if (!record || !subject || !scope) return false;
  if (administrator || scope === "all") return true;
  if (record.companyScope !== subject.companyScope) return false;
  if (scope === "company") return true;
  if (scope === "department") return hasDepartment(subject, record.departmentId);
  return scope === "own" && record.ownerUserId === subject.id;
}

/** Restrictions intersect every grant, including all-company and administrator scopes. */
export function isRecordWithinRestrictions(
  rules: readonly ApiPermissionScopeRestrictionDto[] | undefined,
  resourceKey: string, action: string, subject: PermissionSubject | undefined,
  record: PermissionRecord | null, participant = false,
) {
  if (!subject || !record) return false;
  return (rules ?? []).every(rule => {
    if (rule.resourceKey !== resourceKey || rule.action !== action) return true;
    switch (rule.mode) {
      case "Unrestricted": return true;
      case "Units": return rule.unitCodes.includes(record.companyScope);
      case "Departments": return rule.departments.some(member => member.companyScope === record.companyScope && member.departmentId === record.departmentId);
      case "Projects": return Boolean(record.projectId && rule.projectIds.includes(record.projectId));
      case "Participation": return participant || record.ownerUserId === subject.id;
      default: return false;
    }
  });
}
