import type { ApiOrganizationDepartmentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { identityQueryRoots } from "../../api/queryKeys.ts";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { moveDepartment } from "./organizationModel.ts";

export function useDepartmentOrdering(client: ExportDocManagerApiClient, companyCode: string, departments: ApiOrganizationDepartmentDto[]) {
  const operation = useOfficeOperation(identityQueryRoots, false, `department-order:${companyCode}`, client);
  async function move(department: ApiOrganizationDepartmentDto, direction: -1 | 1) {
    const ordered = moveDepartment(departments, department, direction);
    if (!ordered || operation.busy) return;
    await operation.run(signal => client.reorderOrganizationDepartments({ body: {
      companyCode,
      parentId: departments.find(row => row.code === department.parentCode)?.id,
      items: ordered.map(row => ({ id: row.id, expectedVersion: Number(row.versionNumber) })),
    } }, { signal }), () => {});
  }
  return { move, busy: operation.busy, error: operation.error };
}
