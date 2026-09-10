import { useState } from "react";
import { MapPin, Package, Plus, RefreshCw } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient, OfficeSupplyRecord } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { officeAccess } from "./officeModel.ts";
import { OfficeStockDialog, OfficeSupplyApplication, OfficeSupplyEditor } from "./OfficeSupplyDialogs.tsx";
import { OfficeHistoryDialog } from "./OfficeHistoryDialog.tsx";
import { OfficeRequestsPanel } from "./OfficeRequestsPanel.tsx";
import { OfficePager, OfficeQueryState, OfficeTabs } from "./OfficeUi.tsx";
import { useOfficeDirectory, useOfficeOperation, useOfficeView } from "./useOfficeData.ts";
import { RecordDeleteDialog } from "./RecordDeleteDialog.tsx";
import "../../styles/routes/office.css";

type SupplyDialog = { kind: "edit"; supply?: OfficeSupplyRecord } | { kind: "apply" | "restock" | "stocktake" | "history" | "delete"; supply: OfficeSupplyRecord };

export function OfficeSuppliesPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const { records, setRecords } = useOfficeView();
  if (!user.companyScope) return <PageState tone="permission" title="请先设置所属公司" description="请管理员在账号与权限中为当前账号选择所属公司。" />;
  return <section className="work-surface office-workspace" aria-label="物品领用工作区">
    <OfficeTabs records={records} onChange={setRecords} resourcesLabel={user.capabilities.usesOfficeRegister ? "物品与登记" : undefined}
      recordsLabel={user.capabilities.usesOfficeRegister ? "领用与归还记录" : "领用记录与审批"} />
    {records ? <OfficeRequestsPanel client={client} user={user} kind="supplies" /> : <OfficeSupplyDirectory client={client} user={user} />}
  </section>;
}

function OfficeSupplyDirectory({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const access = officeAccess(user, "supplies");
  const model = useOfficeDirectory<OfficeSupplyRecord>(user, "supplies", (input, signal) => client.listOfficeSupplies(input, { signal }));
  const { paging, query, keyword, search, lowStockOnly, includeInactive } = model;
  const [dialog, setDialog] = useState<SupplyDialog | null>(null);
  const close = () => setDialog(null);
  const deletion = useOfficeOperation();
  return <>
    <div className="office-toolbar"><form className="office-search" onSubmit={(event) => { event.preventDefault(); model.commitSearch(); }}>
      <input aria-label="搜索办公物品" placeholder="物品名称或存放位置" maxLength={120} value={keyword} onChange={(event) => model.changeKeyword(event.target.value)} /><button className="command-button secondary" type="submit">搜索</button></form>
      <label className="checkbox-field"><input type="checkbox" checked={lowStockOnly} onChange={(event) => model.changeLowStock(event.target.checked)} />仅低库存</label>
      {access.allows("manage") && <label className="checkbox-field"><input type="checkbox" checked={includeInactive} onChange={(event) => model.changeInactive(event.target.checked)} />含停用</label>}
      <button className="icon-button" type="button" aria-label="刷新办公物品" disabled={query.isFetching} onClick={model.refresh}><RefreshCw size={17} aria-hidden="true" /></button>
      {access.allows("manage") && <button className="command-button" type="button" onClick={() => setDialog({ kind: "edit" })}><Plus size={17} aria-hidden="true" />添加物品</button>}
    </div>
    <OfficeQueryState query={query} emptyTitle={search || lowStockOnly ? "没有符合条件的物品" : "尚未添加物品，请联系行政管理员"} />
    {!query.isError && <div className="office-resource-grid">{query.data?.items.map((supply) => <article key={supply.id} className="office-resource-card">
      <div className="office-card-heading"><Package size={21} aria-hidden="true" /><h2>{supply.name}</h2><span className="office-badge" data-state={!supply.isActive ? "Cancelled" : supply.lowStock ? "Pending" : "Available"}>{!supply.isActive ? "已停用" : supply.lowStock ? "低库存" : supply.isReturnable ? "可借用" : "消耗品"}</span></div>
      <p className="office-stock-number">可用 <strong>{supply.availableQuantity}</strong> {supply.unit}</p>
      <p className="office-muted">在库 {supply.stockQuantity} · 已预留 {supply.reservedQuantity} · 最低可用 {supply.minimumStock}</p>
      <p className="office-card-detail"><MapPin size={16} aria-hidden="true" />{supply.location || "领取位置未填写"}</p>
      <p className="office-muted office-card-description">{supply.description || (supply.isReturnable ? "借用后需要按期归还" : "按需领用，交接时确认发放")}</p>
      <footer className="office-card-actions">
        {supply.isActive && access.allows("create") && <button className="command-button" type="button" onClick={() => setDialog({ kind: "apply", supply })}>{user.capabilities.usesOfficeRegister ? "登记" : "申请"}{supply.isReturnable ? "借用" : "领用"}</button>}
        {access.allows("restock") && <button className="command-button secondary" type="button" onClick={() => setDialog({ kind: "restock", supply })}>补充库存</button>}
        {(access.allows("restock") || access.allows("manage")) && <details className="office-secondary-actions">
          <summary>更多操作</summary><div>
            {access.allows("restock") && <button className="command-button secondary" type="button" onClick={() => setDialog({ kind: "history", supply })}>库存流水</button>}
            {access.allows("manage") && <><button className="command-button secondary" type="button" onClick={() => setDialog({ kind: "stocktake", supply })}>盘点</button>
              <button className="command-button secondary" type="button" onClick={() => setDialog({ kind: "edit", supply })}>编辑</button>
              <button className="command-button secondary" type="button" onClick={() => setDialog({ kind: "delete", supply })}>删除</button></>}
          </div>
        </details>}
      </footer>
    </article>)}</div>}
    <OfficePager page={query.data} paging={paging} busy={query.isFetching} />
    {dialog?.kind === "edit" && <OfficeSupplyEditor client={client} supply={dialog.supply} onClose={close} />}
    {dialog?.kind === "apply" && <OfficeSupplyApplication client={client} supply={dialog.supply} user={user} onClose={close} />}
    {(dialog?.kind === "restock" || dialog?.kind === "stocktake") && <OfficeStockDialog client={client} supply={dialog.supply} stocktake={dialog.kind === "stocktake"} onClose={close} />}
    {dialog?.kind === "history" && <OfficeHistoryDialog client={client} user={user} kind="stock" id={dialog.supply.id} title={`${dialog.supply.name} · 库存流水`} onClose={close} />}
    {dialog?.kind === "delete" && <RecordDeleteDialog name={dialog.supply.name} version={dialog.supply.versionNumber} operation={deletion}
      description="仅可删除没有库存、领用记录及库存流水的物品。已有历史的物品可在编辑窗口中停用。"
      onDelete={(body, signal) => client.deleteOfficeSupply({ id: dialog.supply.id, body }, { signal })} onClose={close} onDeleted={close} />}
  </>;
}
