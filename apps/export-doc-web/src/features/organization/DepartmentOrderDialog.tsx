import { useState } from "react";
import type { ApiOrganizationDepartmentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { compareDepartments } from "./organizationModel.ts";
import { useOrganizationOperation } from "./useOrganizationDirectory.ts";

export function DepartmentOrderDialog({ client, department, departments, onClose }: {
  client: ExportDocManagerApiClient; department: ApiOrganizationDepartmentDto;
  departments: ApiOrganizationDepartmentDto[]; onClose: () => void;
}) {
  const [siblings] = useState(() => departments.filter(row => (row.parentCode ?? "") === (department.parentCode ?? "")).sort(compareDepartments));
  const initial = siblings.findIndex(row => row.id === department.id) + 1;
  const [position, setPosition] = useState(initial);
  const operation = useOrganizationOperation();
  return <OfficeDialog title={"调整顺序 · " + department.name} onClose={onClose} {...operation} protectChanges hasChanges={position !== initial}>
    <form onSubmit={event => {
      event.preventDefault();
      const ordered = siblings.filter(row => row.id !== department.id);
      ordered.splice(position - 1, 0, department);
      void operation.run(signal => client.reorderOrganizationDepartments({ body: {
        companyCode: department.companyCode, parentId: departments.find(row => row.code === department.parentCode)?.id,
        items: ordered.map(row => ({ id: row.id, expectedVersion: Number(row.versionNumber) })),
      } }, { signal }), onClose);
    }}>
      <fieldset disabled={operation.busy}>
        <OfficeField label={"同级位置（1–" + siblings.length + "）"}><input type="number" min={1} max={siblings.length} required value={position} onChange={event => setPosition(Number(event.target.value))} /></OfficeField>
        <div className="office-card-actions">
          <button type="button" disabled={position <= 1} onClick={() => setPosition(1)}>置顶</button>
          <button type="button" disabled={position <= 1} onClick={() => setPosition(value => value - 1)}>上移</button>
          <button type="button" disabled={position >= siblings.length} onClick={() => setPosition(value => value + 1)}>下移</button>
          <button type="button" disabled={position >= siblings.length} onClick={() => setPosition(siblings.length)}>置底</button>
        </div>
      </fieldset><OfficeSubmit busy={operation.busy} disabled={position === initial} label="保存顺序" />
    </form>
  </OfficeDialog>;
}
