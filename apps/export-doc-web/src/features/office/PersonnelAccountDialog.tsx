import { useState, type FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelAccountRecord, PersonnelRecord } from "../../api/index.ts";
import { OfficeDialog, OfficePager, OfficeQueryState, OfficeSubmit } from "./OfficeUi.tsx";
import { usePersonnelAccountOptions, usePersonnelOperation } from "./usePersonnelData.ts";
export function PersonnelAccountDialog({ client, user, record, onClose }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; record: PersonnelRecord; onClose: () => void;
}) {
  const operation = usePersonnelOperation();
  const model = usePersonnelAccountOptions(client, user, record.employee.id);
  const [selected, setSelected] = useState<PersonnelAccountRecord | null>(null);
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected) return;
    void operation.run((signal) => client.linkPersonnelAccount({ id: record.employee.id, body: { expectedVersion: record.versionNumber,
      userId: selected.id, expectedAccountVersion: selected.versionNumber } }, { signal }), onClose);
  }
  return <OfficeDialog title={`关联账号 · ${record.employee.fullName}`} onClose={onClose} {...operation} protectChanges>
    <p className="office-muted">{record.employee.status === "Departed" ? "管理员可纠正离职人员的账号关联，关联账号将保持停用。" : "选择本公司本人的启用普通账号。关联后姓名和部门由人员档案维护，离职时同步停用账号。"}更换关联账号或调整账号部门前须先交接未结申请、审批及办理职责。</p>
    <form className="office-search" onSubmit={(event) => { event.preventDefault(); setSelected(null); model.search(); }}>
      <input aria-label="搜索可关联账号" placeholder="账号或姓名" value={model.keyword} onChange={(event) => model.setKeyword(event.target.value)} maxLength={100} disabled={operation.busy} />
      <button className="command-button secondary" type="submit" disabled={operation.busy}>搜索</button>
    </form>
    <OfficeQueryState query={model.query} emptyTitle="没有可关联账号，请先在账号与权限中创建本公司普通账号" />
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}><legend>选择账号</legend>
      <ul className="personnel-account-list office-field-wide">{!model.query.isError && model.query.data?.items.map((item) => <li key={item.id}><label>
        <input type="radio" name="account" value={item.id} checked={selected?.id === item.id} onChange={() => setSelected(item)} />
        <span>{item.username} · {item.fullName || "未填写姓名"} · {item.isActive ? "启用" : "停用"}</span>
      </label></li>)}</ul>
    </fieldset>
      {selected && <p>已选：{selected.username}。将关联到 {record.employee.employeeNumber} / {record.employee.fullName}。</p>}
      <OfficeSubmit busy={operation.busy} disabled={!selected || model.query.isError} label="确认关联" />
    </form>
    <OfficePager page={model.query.data} paging={model.paging} busy={model.query.isFetching || operation.busy} />
  </OfficeDialog>;
}
