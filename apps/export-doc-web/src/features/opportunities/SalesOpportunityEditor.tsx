import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "../../ui/Button.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { opportunityClosed } from "./salesOpportunityModel.ts";
import type { SalesOpportunityWorkspace } from "./useSalesOpportunityWorkspace.ts";

export function SalesOpportunityEditor({ model }: { model: SalesOpportunityWorkspace }) {
  const { client, record, draft, permissions } = model;
  const [customerSearch, setCustomerSearch] = useState("");
  const [productSearch, setProductSearch] = useState("");
  const [customerKeyword, setCustomerKeyword] = useState("");
  const [productKeyword, setProductKeyword] = useState("");
  const queryScope = useMemo(createRequestKey, [client]);
  const [chosenCustomer, setChosenCustomer] = useState<{ id: number; name: string } | null>(record ? { id: record.crmCustomerId, name: record.customerName } : null);
  const [chosenProduct, setChosenProduct] = useState<{ id: number; name: string } | null>(record?.productId ? { id: record.productId, name: record.productCode || record.productName } : null);
  const customers = useQuery({ queryKey: ["sales-opportunity-options", queryScope, "customers", customerKeyword], gcTime: 0,
    queryFn: ({ signal }) => client.queryCrmCustomers({ keyword: customerKeyword, pageNumber: 1, pageSize: 100 }, { signal }) });
  const products = useQuery({ queryKey: ["sales-opportunity-options", queryScope, "products", productKeyword], gcTime: 0,
    queryFn: ({ signal }) => client.listProducts({ keyword: productKeyword, pageNumber: 1, pageSize: 100 }, { signal }) });
  const customerItems = withChosen((customers.data?.items ?? []).map(item => ({ id: item.id, name: item.name })), chosenCustomer);
  const productItems = withChosen((products.data?.items ?? []).map(item => ({ id: item.id, name: `${item.productCode || "无货号"} · ${item.nameCN || item.nameEN}` })), chosenProduct);
  const editable = !opportunityClosed(record) && (record ? permissions.edit : permissions.create);
  const quoteEditable = record ? permissions.editQuote : permissions.createQuote;
  return <form className="form-grid" aria-label={record ? "编辑商机" : "新建商机"} onSubmit={event => { event.preventDefault(); model.save(); }}>
    {(customers.isError || products.isError) && <InlineNotice tone="error">{readApiError(customers.error ?? products.error)}</InlineNotice>}
    {customers.isSuccess && !customerItems.length && !customerKeyword && <InlineNotice tone="info"
      action={permissions.createCustomer ? <Button disabled={model.busy} onClick={model.openCustomer}>建立客户资料</Button> : undefined}>尚无可选客户，请先建立销售客户资料。</InlineNotice>}
    {draft.hasPendingServerVersion && <InlineNotice tone="warning" action={<Button disabled={model.busy} onClick={model.reload}>重新加载</Button>}>服务器上的商机已更新，请核对后重新加载。</InlineNotice>}
    <fieldset className="permission-fieldset form-field-wide" disabled={!editable || model.editLocked}>
      <fieldset className="form-section-block"><legend>基本信息</legend>
        <label className="form-field-wide">CRM 客户
          <div className="toolbar"><input aria-label="搜索商机客户" value={customerSearch} onChange={event => setCustomerSearch(event.target.value)} placeholder="搜索客户" />
            <Button onClick={() => { const next = customerSearch.trim(); if (next === customerKeyword) void customers.refetch(); else setCustomerKeyword(next); }}>查找</Button></div>
          <select name="crmCustomerId" required value={draft.values.crmCustomerId} onChange={event => {
            draft.change("crmCustomerId", event.target.value); setChosenCustomer(customerItems.find(item => item.id === Number(event.target.value)) ?? null);
          }}><option value="">请选择客户</option>{customerItems.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
          {(customers.data?.totalCount ?? 0) > 100 && <small>匹配 {customers.data?.totalCount} 家，请输入名称缩小范围。</small>}
        </label>
        <label className="form-field-wide">商机名称<input name="title" required maxLength={200} {...draft.field("title")} /></label>
        <label>当前阶段<input value={record?.isArchived ? "已归档" : record?.stage ?? "线索"} readOnly /></label>
      </fieldset>
      <fieldset className="form-section-block" disabled={!quoteEditable}><legend>报价与预计</legend>
        <label>报价跟踪编号<input name="quotationNo" maxLength={100} {...draft.field("quotationNo")} /></label>
        <label>预计金额<input name="estimatedAmount" type="number" min="0" step="0.0001" {...draft.field("estimatedAmount")} /></label>
        <label>币种<input name="currency" maxLength={3} placeholder="USD" {...draft.field("currency")} /></label>
        <label>成交概率（%）<input name="probabilityPercent" type="number" min="0" max="100" {...draft.field("probabilityPercent")} /></label>
        <label>预计成交日<input name="expectedCloseDate" type="date" {...draft.field("expectedCloseDate")} /></label>
        <label className="form-field-wide">关联产品（可选）
          <div className="toolbar"><input aria-label="搜索商机产品" value={productSearch} onChange={event => setProductSearch(event.target.value)} placeholder="搜索产品货号或名称" />
            <Button onClick={() => { const next = productSearch.trim(); if (next === productKeyword) void products.refetch(); else setProductKeyword(next); }}>查找</Button></div>
          <select name="productId" value={draft.values.productId || "0"} onChange={event => {
            draft.change("productId", event.target.value); setChosenProduct(productItems.find(item => item.id === Number(event.target.value)) ?? null);
          }}><option value="0">不关联产品</option>{productItems.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
          {(products.data?.totalCount ?? 0) > 100 && <small>匹配 {products.data?.totalCount} 项，请输入货号或名称缩小范围。</small>}
        </label>
      </fieldset>
      <fieldset className="form-section-block"><legend>下一步</legend>
        <label className="form-field-wide">下一步动作<input name="nextAction" maxLength={500} {...draft.field("nextAction")} /></label>
      </fieldset>
      <details className="optional-form-details form-field-wide"><summary>补充记录</summary><div className="optional-form-grid">
        <label className="form-field-wide">长期备注<textarea name="notes" maxLength={2000} {...draft.field("notes")} /></label>
        <label className="form-field-wide">本次变更说明<textarea name="changeNote" maxLength={1000} {...draft.field("changeNote")} /></label>
      </div></details>
    </fieldset>
    {record && !record.isArchived && permissions.transition && <fieldset className="form-section-block form-field-wide"><legend>阶段流转</legend>
      <label className="form-field-wide">流转说明（可选）<input maxLength={1000} disabled={model.busy} value={model.transitionNote} onChange={event => model.setTransitionNote(event.target.value)} /></label>
      <div className="form-actions">{record.allowedNextStages.map(stage => <Button key={stage} disabled={model.busy} onClick={() => model.transition(stage)}>流转到{stage}</Button>)}</div>
    </fieldset>}
    <div className="form-actions">
      {editable && <Button type="submit" variant="primary" disabled={model.busy}>{model.busy ? "正在处理…" : "保存商机"}</Button>}
      {record && <Button disabled={model.busy} onClick={model.reload}>重新加载</Button>}
      {record && !record.isArchived && permissions.archive && <Button disabled={model.busy} onClick={model.archive}>归档商机</Button>}
    </div>
  </form>;
}

function withChosen(items: { id: number; name: string }[], chosen: { id: number; name: string } | null) {
  return chosen && !items.some(item => item.id === chosen.id) ? [chosen, ...items] : items;
}
