import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { ApiCrmCustomerDto, ApiProductDto, ApiSalesOpportunityDto, ApiSalesOpportunityHistoryDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { TaskViewTabs } from "../../ui/TaskViewTabs.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { FormGuidance, PermissionNotice } from "../../ui/PageState.tsx";

const stages = ["线索", "需求确认", "已报价", "谈判中", "已成交", "已失单"];

export function SalesOpportunityPage({ client }: { client: ExportDocManagerApiClient }) {
  const opportunityPermission = useModulePermission("sales.opportunities");
  const requestConfirmation = useConfirmation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [page, setPage] = useState<Awaited<ReturnType<ExportDocManagerApiClient["querySalesOpportunities"]>> | null>(null);
  const [selected, setSelected] = useState<ApiSalesOpportunityDto | null>(null);
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [stage, setStage] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [customers, setCustomers] = useState<ApiCrmCustomerDto[]>([]);
  const [products, setProducts] = useState<ApiProductDto[]>([]);
  const [customerKeyword, setCustomerKeyword] = useState("");
  const [productKeyword, setProductKeyword] = useState("");
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [revision, setRevision] = useState(0);
  const [history, setHistory] = useState<ApiSalesOpportunityHistoryDto[]>([]);
  const [view, setView] = useState<"directory" | "editor" | "history">(readOpportunityView(searchParams.get("view")));
  const customerOptions = useMemo(() => selected && !customers.some((item) => item.id === selected.crmCustomerId)
    ? [{ id: selected.crmCustomerId, name: selected.customerName } as ApiCrmCustomerDto, ...customers] : customers, [customers, selected]);
  const productOptions = useMemo(() => selected?.productId && !products.some((item) => item.id === selected.productId)
    ? [{ id: selected.productId, productCode: selected.productCode ?? "", nameCN: selected.productName ?? "", nameEN: "" } as ApiProductDto, ...products] : products, [products, selected]);

  function changeView(nextView: "directory" | "editor" | "history") {
    setView(nextView);
    if (nextView === "history") return;
    setSearchParams(nextView === "directory" ? {} : { view: nextView }, { replace: true });
  }

  useEffect(() => {
    const requestedView = readOpportunityView(searchParams.get("view"));
    setView((current) => current === requestedView ? current : requestedView);
  }, [searchParams]);

  async function loadPage(query = { keyword, stage, pageNumber }) {
    const result = await client.querySalesOpportunities({ ...query, pageSize: 20 });
    setPage(result);
  }

  async function searchCustomers(searchKeyword = customerKeyword) {
    const result = await client.queryCrmCustomers({ keyword: searchKeyword.trim(), status: "", pageNumber: 1, pageSize: 50 });
    setCustomers(result.items);
  }

  async function searchProducts(searchKeyword = productKeyword) {
    setProducts((await client.listProducts({ keyword: searchKeyword.trim() })).slice(0, 50));
  }

  useEffect(() => { void loadPage().catch((error) => setFeedback(errorFeedback(readApiError(error)))); }, [client, keyword, stage, pageNumber, revision]);
  useEffect(() => { void Promise.all([searchCustomers(""), searchProducts("")]).catch((error) => setFeedback(errorFeedback(readApiError(error)))); }, [client]);
  useEffect(() => {
    if (!selected) { setHistory([]); return; }
    void client.listSalesOpportunityHistory({ id: selected.id }).then(setHistory).catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, selected]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!opportunityPermission.canOperate) return;
    const form = new FormData(event.currentTarget); const id = selected?.id ?? 0;
    const expectedCloseDate = String(form.get("expectedCloseDate") ?? "");
    const body = {
      id, crmCustomerId: Number(form.get("crmCustomerId") ?? 0), productId: Number(form.get("productId") ?? 0) || undefined,
      title: String(form.get("title") ?? "").trim(), stage: String(form.get("stage") ?? "线索"),
      quotationNo: String(form.get("quotationNo") ?? "").trim(), estimatedAmount: Number(form.get("estimatedAmount") ?? 0),
      currency: String(form.get("currency") ?? "USD").trim().toUpperCase(), probabilityPercent: Number(form.get("probabilityPercent") ?? 0),
      expectedCloseAt: expectedCloseDate ? new Date(`${expectedCloseDate}T00:00:00`).toISOString() : undefined,
      nextAction: String(form.get("nextAction") ?? "").trim(), notes: String(form.get("notes") ?? "").trim(),
      changeNote: String(form.get("changeNote") ?? "").trim(),
      expectedVersion: id > 0 ? selected?.versionNumber ?? 0 : 0,
    };
    try {
      const saved = id ? await client.updateSalesOpportunity({ id, body }) : await client.createSalesOpportunity({ body });
      setSelected(saved);
      if (keyword || stage || pageNumber !== 1) {
        setKeywordInput(""); setKeyword(""); setStage(""); setPageNumber(1);
        await loadPage({ keyword: "", stage: "", pageNumber: 1 });
      } else {
        await loadPage();
      }
      setFeedback(successFeedback(id ? "商机已更新并按规则追加历史版本。" : "商机已建立并生成版本 1。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function remove() {
    if (!opportunityPermission.canManage || !selected || !await requestConfirmation({ title: "删除商机", description: `确定删除商机“${selected.title}”吗？`, details: ["客户和产品资料将保留。"], confirmLabel: "确认删除", tone: "danger" })) return;
    try { await client.deleteSalesOpportunity({ id: selected.id }); setSelected(null); changeView("directory"); setRevision((value) => value + 1); setFeedback(successFeedback("商机已删除，客户和产品保持不变。")); }
    catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return <section className="work-surface">
    <div className="section-heading-row"><div><h2>商机与报价跟踪</h2><p>记录销售阶段和最近报价信息，不替代正式报价文件、发票或单证。</p></div></div>
    <OperationFeedback feedback={feedback} />
    {!opportunityPermission.canOperate ? <PermissionNotice>当前权限模板仅允许查看商机；新建、修改和阶段更新已禁用。</PermissionNotice> : null}
    <TaskViewTabs value={view} label="商机工作区" onChange={changeView} items={[
      { id: "directory", label: "商机目录" }, { id: "editor", label: selected ? opportunityPermission.canOperate ? "编辑商机" : "查看商机" : "新建商机", disabled: !selected && !opportunityPermission.canOperate },
      { id: "history", label: "版本历史", disabled: !selected },
    ]} />
    {view === "directory" ? <section className="form-section"><div className="section-header"><div><h3>商机目录</h3><p className="section-description">按客户、阶段和报价编号查找销售机会。</p></div><div className="section-header-actions"><span>共 {page?.totalCount ?? 0} 项</span>{opportunityPermission.canOperate ? <button className="primary-button" type="button" onClick={() => { setSelected(null); changeView("editor"); }}>新建商机</button> : null}</div></div>
      <form className="toolbar" onSubmit={(event) => { event.preventDefault(); setKeyword(keywordInput.trim()); setPageNumber(1); }}>
        <input value={keywordInput} onChange={(event) => setKeywordInput(event.target.value)} placeholder="搜索商机、客户、产品或报价编号" />
        <select value={stage} onChange={(event) => { setStage(event.target.value); setPageNumber(1); }}><option value="">全部阶段</option>{stages.map((item) => <option key={item}>{item}</option>)}</select>
        <button className="secondary-button">搜索</button>
      </form>
      <ResponsiveTableFrame label="销售商机列表" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>商机</th><th>客户</th><th data-table-priority="secondary">产品</th><th>金额</th><th data-table-priority="secondary">概率</th><th>阶段</th><th /></tr></thead><tbody>
        {(page?.items ?? []).map((item) => <tr key={item.id}><td><TablePrimaryText value={item.title} secondary={item.quotationNo || "未填报价编号"} /></td><td><TablePrimaryText value={item.customerName} /></td><td data-table-priority="secondary"><TablePrimaryText value={item.productCode || item.productName} /></td><td>{item.currency} {item.estimatedAmount.toFixed(2)}</td><td data-table-priority="secondary">{item.probabilityPercent}%</td><td><BusinessStatusBadge value={item.stage} /></td><td><button className="secondary-button" type="button" onClick={() => { setSelected(item); changeView("editor"); }}>{opportunityPermission.canOperate ? "编辑" : "查看"}</button></td></tr>)}
        {!page?.items.length ? <tr><td className="empty-cell" colSpan={7}><div className="empty-cell-content"><strong>暂无商机记录</strong><span>{opportunityPermission.canOperate ? "从一位销售客户开始，记录阶段、预计金额和下一步动作。" : "当前没有可查看的商机记录。"}</span>{opportunityPermission.canOperate ? <button className="primary-button" type="button" onClick={() => { setSelected(null); changeView("editor"); }}>建立第一条商机</button> : null}</div></td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
      <div className="form-actions"><button className="secondary-button" disabled={!page?.hasPreviousPage} onClick={() => setPageNumber((value) => value - 1)}>上一页</button><span>第 {page?.pageNumber ?? 1} / {Math.max(page?.totalPages ?? 1, 1)} 页</span><button className="secondary-button" disabled={!page?.hasNextPage} onClick={() => setPageNumber((value) => value + 1)}>下一页</button></div>
    </section> : null}
    {view === "editor" ? <form className="form-grid" key={selected?.id ?? "new"} onSubmit={save}>
      <div className="section-header"><h3>{selected ? opportunityPermission.canOperate ? "编辑商机" : "查看商机" : "新建商机"}</h3><span>轻量销售跟踪</span></div>
      {!customers.length ? <FormGuidance className="form-field-wide" title="先建立一位销售客户" description="商机必须归属 CRM 客户，不会写入原单证客户资料。" action={opportunityPermission.canOperate ? <button className="primary-button" type="button" onClick={() => navigate("/crm/follow-ups?view=profile")}>建立客户资料</button> : undefined} /> : null}
      <fieldset className="permission-fieldset form-field-wide" disabled={!opportunityPermission.canOperate}>
      <fieldset className="form-section-block">
        <legend>基本信息</legend>
        <label className="form-field-wide">CRM 客户<div className="toolbar"><input value={customerKeyword} onChange={(event) => setCustomerKeyword(event.target.value)} placeholder="搜索客户" /><button className="secondary-button" type="button" onClick={() => void searchCustomers()}>查找</button></div><select name="crmCustomerId" required defaultValue={selected?.crmCustomerId ?? ""}><option value="">请选择客户</option>{customerOptions.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="form-field-wide">商机名称<input name="title" required maxLength={200} defaultValue={selected?.title} placeholder="例如：2026 秋季服装订单" /></label>
        <label>当前阶段<select name="stage" defaultValue={selected?.stage ?? "线索"}>{stages.map((item) => <option key={item}>{item}</option>)}</select></label>
      </fieldset>
      <fieldset className="form-section-block">
        <legend>报价与预计</legend>
        <label>报价跟踪编号<input name="quotationNo" maxLength={100} defaultValue={selected?.quotationNo} placeholder="可暂不填写" /></label>
        <label>预计金额<input name="estimatedAmount" type="number" min="0" step="0.0001" defaultValue={selected?.estimatedAmount ?? 0} /></label>
        <label>币种<input name="currency" maxLength={3} defaultValue={selected?.currency ?? "USD"} /></label>
        <label>成交概率（%）<input name="probabilityPercent" type="number" min="0" max="100" defaultValue={selected?.probabilityPercent ?? 0} /></label>
        <label>预计成交日<input name="expectedCloseDate" type="date" defaultValue={selected?.expectedCloseAt?.slice(0, 10)} /></label>
        <label className="form-field-wide">关联产品（可选）<div className="toolbar"><input value={productKeyword} onChange={(event) => setProductKeyword(event.target.value)} placeholder="搜索产品货号或名称" /><button className="secondary-button" type="button" onClick={() => void searchProducts()}>查找</button></div><select name="productId" defaultValue={selected?.productId ?? 0}><option value={0}>不关联产品</option>{productOptions.map((item) => <option key={item.id} value={item.id}>{item.productCode || "无货号"} · {item.nameCN || item.nameEN || "未命名"}</option>)}</select></label>
      </fieldset>
      <fieldset className="form-section-block">
        <legend>下一步</legend>
        <label className="form-field-wide">下一步动作<input name="nextAction" maxLength={500} defaultValue={selected?.nextAction} placeholder="例如：周五发送新版报价并确认交期" /></label>
      </fieldset>
      <details className="optional-form-details form-field-wide">
        <summary>补充记录（可选）</summary>
        <div className="optional-form-grid"><label className="form-field-wide">长期备注<textarea name="notes" maxLength={2000} defaultValue={selected?.notes} /></label><label className="form-field-wide">本次变更说明<textarea name="changeNote" maxLength={1000} placeholder="阶段、报价或沟通背景；保存后只追加到历史，不覆盖长期备注" /></label></div>
      </details>
      </fieldset>
      <div className="form-actions">{opportunityPermission.canOperate ? <button className="primary-button">保存商机</button> : null}{selected ? <button className="secondary-button" type="button" onClick={() => changeView("history")}>查看版本历史</button> : null}{selected && opportunityPermission.canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void remove()}>删除商机</button> : null}</div>
    </form> : null}
    {selected && view === "history" ? <section className="form-section"><div className="section-header"><h3>阶段与报价版本历史</h3><span>{history.length} 个版本</span></div>
      <ResponsiveTableFrame label="商机版本历史" mobileLayout="scroll"><table className="data-table"><thead><tr><th>版本</th><th>类型</th><th>阶段</th><th>报价编号</th><th>金额</th><th>概率</th><th>变更说明</th><th>操作信息</th></tr></thead><tbody>
        {history.map((item) => <tr key={item.id}><td>V{item.versionNumber}</td><td>{item.changeType}</td><td><BusinessStatusBadge value={item.stage} /></td><td>{item.quotationNo || "-"}</td><td>{item.currency} {item.estimatedAmount.toFixed(2)}</td><td>{item.probabilityPercent}%</td><td>{item.changeNote || "-"}</td><td>{item.changedBy || "-"}<br /><small>{new Date(item.createdAt).toLocaleString("zh-CN", { hour12: false })}</small></td></tr>)}
        {!history.length ? <tr><td className="empty-cell" colSpan={8}>暂无版本历史。</td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
    </section> : null}
  </section>;
}

function readOpportunityView(value: string | null): "directory" | "editor" | "history" {
  return value === "editor" ? "editor" : "directory";
}
