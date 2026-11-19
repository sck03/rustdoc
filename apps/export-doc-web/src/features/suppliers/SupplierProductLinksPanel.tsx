import { useNavigate } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { useSupplierProductLinks } from "./useSupplierProductLinks.ts";
import { ExactDecimal } from "../../ui/decimalSum.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, requestErrorFeedback } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { FormGuidance } from "../../ui/PageState.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";

export function SupplierProductLinksPanel({ client, supplierId, supplierName, canEdit, canDeactivate, canDelete }: {
  client: ExportDocManagerApiClient; supplierId: number; supplierName: string;
  canEdit: boolean; canDeactivate: boolean; canDelete: boolean;
}) {
  const navigate = useNavigate();
  const workspace = useSupplierProductLinks(client, supplierId, { edit: canEdit, deactivate: canDeactivate, delete: canDelete });
  const { query, draft, view, feedback, options, optionsQuery, command, select, save, loadServer,
    pageNumber, pageSize, setPageNumber, setPageSize } = workspace;
  const selected = draft.record?.id ? draft.record : undefined;
  const links = query.data?.items ?? [];
  async function openProductDirectory() {
    if (command.isPending || !await workspace.discard("打开产品资料")) return;
    navigate("/master-data/products");
  }
  return <section className="form-section supplier-product-workspace">
    <div className="section-header"><h3>{view === "directory" ? "供应产品目录" : selected ? "编辑供货关系" : "新增供货关系"}</h3><span>{query.data?.totalCount ?? 0} 项</span></div>
    <OperationFeedback feedback={feedback ?? (query.isError ? requestErrorFeedback(query.error) : optionsQuery.isError ? requestErrorFeedback(optionsQuery.error) : null)} />
    {draft.hasPendingServerVersion && <ServerDraftUpdateNotice entityLabel="供货关系" onKeepLocal={draft.keepLocalDraft} onLoadServer={() => void loadServer()} />}
    {view === "directory" ? <>
      <div className="section-header-actions supplier-product-directory-actions">{canEdit && <button className="primary-button" type="button" onClick={() => void select(null)}>新增供货关系</button>}</div>
      <ResponsiveTableFrame label="供应商供货关系" mobileLayout="scroll" busy={query.isFetching}><table className="data-table"><thead><tr><th>产品</th><th>供应商货号</th><th>参考价</th><th>交期</th><th>状态</th><th /></tr></thead><tbody>
        {links.map(item => <tr key={item.id}><td><TablePrimaryText value={item.productNameCN || item.productNameEN || "未命名"} secondary={item.productCode || "无产品货号"} /></td><td><TablePrimaryText value={item.supplierProductCode} /></td><td>{item.currency} {ExactDecimal.from(item.referencePrice).toFixed(2)}</td><td>{item.leadTimeDays ? item.leadTimeDays + " 天" : "未设置"}</td><td><BusinessStatusBadge value={item.status} /></td><td><button className="secondary-button" type="button" onClick={() => void select(item)}>{canEdit ? "编辑" : "查看"}</button></td></tr>)}
        {!query.isFetching && !query.isError && !links.length && <tr><td className="empty-cell" colSpan={6}>尚未关联供应产品</td></tr>}
      </tbody></table></ResponsiveTableFrame>
      <Pagination pageNumber={pageNumber} totalPages={query.data?.totalPages ?? 1} totalCount={query.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20, 50, 100]} isBusy={query.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize} />
    </> : <form className="form-grid" onSubmit={save}>
      <div className="section-heading-row"><h4>{selected ? "编辑供货关系" : "新增供货关系"}</h4><button className="secondary-button" type="button" disabled={command.isPending} onClick={() => void select(selected ?? null, "directory")}>返回供应产品目录</button></div>
      <div className="form-field-wide context-strip"><strong>{supplierName}</strong></div>
      {!options.length && !optionsQuery.isFetching && <FormGuidance className="form-field-wide" title="先建立产品资料" description="供货关系必须关联现有产品。" action={canEdit ? <button className="secondary-button" type="button" onClick={() => void openProductDirectory()}>打开产品资料</button> : undefined} />}
      <fieldset className="permission-fieldset form-field-wide" disabled={!canEdit || command.isEditingLocked}>
        <label className="form-field-wide">查找产品<div className="toolbar"><input value={workspace.keywordInput} onChange={event => workspace.setKeywordInput(event.target.value)} placeholder="输入产品货号或名称" /><button className="secondary-button" type="button" onClick={workspace.search}>查找</button></div>{(optionsQuery.data?.totalCount ?? 0) > options.length && <small>匹配 {optionsQuery.data?.totalCount} 项，请缩小搜索范围。</small>}</label>
        <label className="form-field-wide">产品<select name="productId" required value={draft.values.productId === "0" ? "" : draft.values.productId} onChange={event => draft.change("productId", event.target.value)}><option value="">请选择产品</option>{options.map(item => <option key={item.id} value={item.id}>{item.productCode || "无货号"} · {item.nameCN || item.nameEN || "未命名"}</option>)}</select></label>
        <label>供应商货号<input name="supplierProductCode" maxLength={100} {...draft.field("supplierProductCode")} /></label>
        <label>参考价<input name="referencePrice" inputMode="decimal" required maxLength={60} {...draft.field("referencePrice")} /></label>
        <label>币种<input name="currency" maxLength={3} {...draft.field("currency")} /></label>
        <label>交期（天）<input name="leadTimeDays" type="number" min="0" max="3650" {...draft.field("leadTimeDays")} /></label>
        {selected && <label>供货状态<input value={selected.status} readOnly /></label>}
      </fieldset>
      <div className="form-actions">{canEdit && <button className="primary-button" type="submit" disabled={command.isPending || !options.length}>保存供货关系</button>}{selected && canDeactivate && <button className="secondary-button" type="button" disabled={command.isPending} onClick={() => command.mutate(["停用", "暂停"].includes(selected.status) ? "restore" : "deactivate")}>{["停用", "暂停"].includes(selected.status) ? "恢复供货关系" : "停用供货关系"}</button>}{selected && canDelete && <button className="secondary-button danger-button" type="button" disabled={command.isPending} onClick={() => command.mutate("delete")}>删除关联</button>}</div>
    </form>}
  </section>;
}
