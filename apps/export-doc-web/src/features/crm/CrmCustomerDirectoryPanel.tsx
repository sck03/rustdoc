import { useState, type FormEvent } from "react";
import type { ApiCrmCustomerDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { usePagedDirectoryQuery } from "../../ui/usePagedDirectoryQuery.ts";

export function CrmCustomerDirectoryPanel({ client, canOperate, onCreateCustomer, onSelectCustomer }: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  onCreateCustomer: () => void;
  onSelectCustomer: (customer: ApiCrmCustomerDto) => void;
}) {
  const [inputKeyword, setInputKeyword] = useState("");
  const [keyword, setKeyword] = useState("");
  const [status, setStatus] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [revision, setRevision] = useState(0);

  const pageQuery = usePagedDirectoryQuery(
    ["crm-customers", keyword, status, pageNumber, pageSize, revision],
    (signal) => client.queryCrmCustomers({ keyword, status, pageNumber, pageSize }, { signal }),
  );
  const page = pageQuery.data ?? null;

  function search(event: FormEvent) {
    event.preventDefault();
    setKeyword(inputKeyword.trim());
    setPageNumber(1);
  }

  async function updateBatchStatus(targetStatus: string) {
    if (!canOperate) return;
    if (!selectedIds.length) { setFeedback(warningFeedback("请先勾选 CRM 客户。")); return; }
    try {
      const result = await client.updateCrmCustomerBatchStatus({ body: { ids: selectedIds, status: targetStatus } });
      setSelectedIds([]); setRevision((value) => value + 1);
      setFeedback(successFeedback(`已更新 ${result.affectedCount} 家客户为“${result.status}”。`));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function exportRows() {
    try {
      const blob = await client.exportCrmCustomers({ keyword, status });
      const url = URL.createObjectURL(blob); const anchor = document.createElement("a");
      anchor.href = url; anchor.download = `crm-customers-${new Date().toISOString().slice(0, 10)}.xlsx`; anchor.click();
      URL.revokeObjectURL(url); setFeedback(successFeedback("CRM 客户与主要联系人 Excel 已生成。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return <section className="form-section">
    <div className="section-header"><div><h3>客户目录</h3><p className="section-description">查找销售客户并进入联系人资料。</p></div><div className="section-header-actions"><span>共 {page?.totalCount ?? 0} 家</span>{canOperate ? <button className="primary-button" type="button" onClick={onCreateCustomer}>新建客户</button> : null}</div></div>
    <form className="toolbar" onSubmit={search}>
      <input value={inputKeyword} onChange={(event) => setInputKeyword(event.target.value)} placeholder="搜索名称、国家、网站、来源或备注" />
      <select value={status} onChange={(event) => { setStatus(event.target.value); setPageNumber(1); }}>
        <option value="">全部状态</option><option>潜在客户</option><option>跟进中</option><option>已成交</option><option>暂停</option><option>已流失</option>
      </select>
      <button className="secondary-button" type="submit">搜索</button>
      <button className="secondary-button" type="button" onClick={() => void exportRows()}>导出当前筛选</button>
      {canOperate ? <select aria-label="批量客户状态" defaultValue="" onChange={(event) => { if (event.target.value) void updateBatchStatus(event.target.value); event.target.value = ""; }}>
        <option value="">批量修改状态...</option><option>潜在客户</option><option>跟进中</option><option>已成交</option><option>暂停</option><option>已流失</option>
      </select> : null}
    </form>
    <OperationFeedback feedback={feedback} />
    {pageQuery.isError ? <OperationFeedback feedback={errorFeedback(readApiError(pageQuery.error))} /> : null}
    <ResponsiveTableFrame label="CRM 客户目录" mobileLayout="scroll" busy={pageQuery.isFetching}><table className="data-table responsive-data-table"><thead><tr>{canOperate ? <th><input type="checkbox" aria-label="选择本页 CRM 客户" checked={(page?.items.length ?? 0) > 0 && page!.items.every((item) => selectedIds.includes(item.id))} onChange={(event) => setSelectedIds(event.target.checked ? Array.from(new Set([...selectedIds, ...(page?.items.map((item) => item.id) ?? [])])) : selectedIds.filter((id) => !page?.items.some((item) => item.id === id)))} /></th> : null}<th>客户</th><th data-table-priority="secondary">国家/地区</th><th>状态</th><th data-table-priority="secondary">来源</th><th /></tr></thead>
      <tbody>{(page?.items ?? []).map((item) => <tr key={item.id}>{canOperate ? <td><input type="checkbox" aria-label={`选择客户 ${item.name}`} checked={selectedIds.includes(item.id)} onChange={(event) => setSelectedIds((current) => event.target.checked ? Array.from(new Set([...current, item.id])) : current.filter((id) => id !== item.id))} /></td> : null}<td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.countryRegion || "-"}</td><td><BusinessStatusBadge value={item.status} /></td><td data-table-priority="secondary">{item.source || "-"}</td><td><button className="secondary-button" type="button" onClick={() => onSelectCustomer(item)}>打开</button></td></tr>)}
        {!pageQuery.isFetching && !pageQuery.isError && !page?.items.length ? <tr><td className="empty-cell" colSpan={canOperate ? 6 : 5}><div className="empty-cell-content"><strong>暂无销售客户</strong><span>{canOperate ? "先建立客户和主要联系人，之后即可记录跟进与商机。" : "当前没有可查看的销售客户。"}</span>{canOperate ? <button className="primary-button" type="button" onClick={onCreateCustomer}>建立第一位客户</button> : null}</div></td></tr> : null}
      </tbody>
    </table></ResponsiveTableFrame>
    <ListPaginationControls pageNumber={pageNumber} totalPages={page?.totalPages ?? 1} totalCount={page?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={pageQuery.isFetching} onPageChange={setPageNumber} onPageSizeChange={(value) => { setPageSize(value); setPageNumber(1); }} />
  </section>;
}
