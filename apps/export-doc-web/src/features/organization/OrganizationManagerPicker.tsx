import type { ExportDocManagerApiClient, OrganizationManagerRecord } from "../../api/index.ts";
import { OfficeDialog, OfficePager, OfficeQueryState } from "../office/OfficeUi.tsx";
import { useOrganizationManagers } from "./useOrganizationDirectory.ts";

export function OrganizationManagerPicker({ client, companyCode, onSelect, onClose }: {
  client: ExportDocManagerApiClient; companyCode: string; onSelect: (person: OrganizationManagerRecord) => void; onClose: () => void;
}) {
  const model = useOrganizationManagers(client, companyCode);
  return <OfficeDialog title="选择部门负责人" onClose={onClose}>
    <p className="office-muted">从本公司在职人员中选择；尚未建档的人员请先登记人员档案。</p>
    <form className="office-search" onSubmit={(event) => { event.preventDefault(); model.search(); }}>
      <input aria-label="按姓名或工号搜索负责人" maxLength={100} value={model.keyword} onChange={(event) => model.setKeyword(event.target.value)} autoFocus />
      <button type="submit" className="command-button secondary">搜索</button>
    </form>
    <OfficeQueryState query={model.query} emptyTitle="没有符合条件的在职人员" />
    {!model.query.isError && <ul className="organization-managers">{model.query.data?.items.map((person) => <li key={person.id}>
      <button type="button" className="command-button secondary" onClick={() => onSelect(person)}><strong>{person.fullName}</strong><span>{person.employeeNumber} · {person.departmentName}</span></button>
    </li>)}</ul>}
    <OfficePager page={model.query.data} paging={model.paging} busy={model.query.isFetching} />
  </OfficeDialog>;
}
