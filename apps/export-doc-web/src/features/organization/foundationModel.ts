import type { ExportDocManagerApiClient, IdentityEmployment, IdentityPosition, IdentityPositionSave, IdentityProject, IdentityProjectSave, IdentityResponsibility, IdentityResponsibilitySave } from "../../api/index.ts";

export type FoundationKind = "positions" | "employments" | "responsibilities" | "projects";
export type FoundationRecord = IdentityPosition | IdentityEmployment | IdentityResponsibility | IdentityProject;
type FoundationPage = { items: FoundationRecord[]; totalCount: number; pageNumber: number; pageSize: number };
export type FoundationDraft = { kind: "positions"; body: IdentityPositionSave } | { kind: "responsibilities"; body: IdentityResponsibilitySave } | { kind: "projects"; body: IdentityProjectSave };
export const foundationLabels: Record<FoundationKind, string> = { positions: "岗位", employments: "雇佣期间", responsibilities: "业务职责", projects: "项目审批职责" };
export function foundationDraft(kind: Exclude<FoundationKind, "employments">, unitId: string, item?: FoundationRecord): FoundationDraft {
  const base = { id: item?.id ?? null, unitId, expectedVersion: item?.versionNumber ?? 0, note: "" };
  switch (kind) {
    case "positions": { const row = item as IdentityPosition | undefined; return { kind, body: { ...base, departmentId: row?.departmentId ?? null, code: row?.code ?? "", name: row?.name ?? "", isActive: row?.isActive ?? true } }; }
    case "responsibilities": { const row = item as IdentityResponsibility | undefined; return { kind, body: { ...base, departmentId: row?.departmentId ?? null, code: row?.code ?? "", name: row?.name ?? "", workflowKinds: row?.workflowKinds ?? [], assignments: row?.assignments ?? [], isActive: row?.isActive ?? true } }; }
    case "projects": { const row = item as IdentityProject | undefined; return { kind, body: { ...base, code: row?.code ?? "", name: row?.name ?? "", ownerAppointmentId: row?.ownerAppointmentId ?? "", members: row?.members ?? [], isActive: row?.isActive ?? true } }; }
  }
}
export function listFoundation(client: ExportDocManagerApiClient, kind: FoundationKind, unitId: string, pageNumber: number, signal: AbortSignal): Promise<FoundationPage> {
  const request = { unitId, pageNumber, pageSize: 20 };
  switch (kind) {
    case "positions": return client.listPositions(request, { signal });
    case "employments": return client.listEmployments(request, { signal });
    case "responsibilities": return client.listResponsibilities(request, { signal });
    case "projects": return client.listProjects(request, { signal });
  }
}
export function saveFoundation(client: ExportDocManagerApiClient, draft: FoundationDraft, signal: AbortSignal): Promise<FoundationRecord> {
  switch (draft.kind) {
    case "positions": return client.savePosition({ body: draft.body }, { signal });
    case "responsibilities": return client.saveResponsibility({ body: draft.body }, { signal });
    case "projects": return client.saveProject({ body: draft.body }, { signal });
  }
}
