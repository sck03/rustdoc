import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiSupplierProductLinkDto, ApiSupplierProductOptionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";

export function SupplierProductLinksPanel({
  client,
  supplierId,
  supplierName,
  canOperate,
  canManage,
}: {
  client: ExportDocManagerApiClient;
  supplierId: number;
  supplierName: string;
  canOperate: boolean;
  canManage: boolean;
}) {
  const navigate = useNavigate();
  const [links, setLinks] = useState<ApiSupplierProductLinkDto[]>([]);
  const [options, setOptions] = useState<ApiSupplierProductOptionDto[]>([]);
  const [selectedId, setSelectedId] = useState(0);
  const [keyword, setKeyword] = useState("");
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [view, setView] = useState<"directory" | "editor">("directory");
  const selected = links.find((item) => item.id === selectedId);
  const productOptions = useMemo(() => {
    if (!selected || options.some((item) => item.id === selected.productId)) return options;
    return [{ id: selected.productId, productCode: selected.productCode, nameCN: selected.productNameCN, nameEN: selected.productNameEN }, ...options];
  }, [options, selected]);

  async function loadLinks(preferredId?: number) {
    if (!supplierId) { setLinks([]); setSelectedId(0); return; }
    const rows = await client.listSupplierProductLinks({ supplierId });
    setLinks(rows);
    setSelectedId(preferredId && rows.some((item) => item.id === preferredId) ? preferredId : 0);
  }

  async function searchProducts(searchKeyword = keyword) {
    setOptions(await client.searchSupplierProductOptions({ keyword: searchKeyword.trim() }));
  }

  useEffect(() => {
    setFeedback(null);
    setView("directory");
    void Promise.all([loadLinks(), searchProducts("")]).catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, supplierId]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canOperate || !supplierId) return;
    const form = new FormData(event.currentTarget);
    const id = selected?.id ?? 0;
    const body = {
      id,
      supplierCompanyId: supplierId,
      productId: Number(form.get("productId") ?? 0),
      supplierProductCode: String(form.get("supplierProductCode") ?? "").trim(),
      referencePrice: Number(form.get("referencePrice") ?? 0),
      currency: String(form.get("currency") ?? "CNY").trim().toUpperCase(),
      leadTimeDays: Number(form.get("leadTimeDays") ?? 0),
      status: String(form.get("linkStatus") ?? "供货中"),
    };
    try {
      const saved = id
        ? await client.updateSupplierProductLink({ supplierId, id, body })
        : await client.createSupplierProductLink({ supplierId, body });
      await loadLinks(saved.id);
      setView("editor");
      setFeedback(successFeedback(id ? "供货关系已更新。" : "供货关系已建立。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function remove() {
    if (!canManage || !selected || !window.confirm(`删除“${selected.productCode || selected.productNameCN}”的供货关系？`)) return;
    try {
      await client.deleteSupplierProductLink({ supplierId, id: selected.id });
      await loadLinks();
      setView("directory");
      setFeedback(successFeedback("供货关系已删除，产品资料保持不变。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return <section className="form-section supplier-product-workspace">
    <div className="section-header"><div><h3>{view === "directory" ? "供应产品目录" : selected ? "编辑供货关系" : "新增供货关系"}</h3><p>为 {supplierName || "当前供应商"} 维护独立供货关系，不修改产品主数据。</p></div><span>{links.length} 项</span></div>
    <OperationFeedback feedback={feedback} />
    {view === "directory" ? <>
      <div className="section-header-actions supplier-product-directory-actions"><span>每条关系只记录当前供应商的报价、货号和交期。</span>{canOperate ? <button className="primary-button" type="button" onClick={() => { setSelectedId(0); setView("editor"); }}>新增供货关系</button> : null}</div>
      <div className="table-frame"><table className="data-table"><thead><tr><th>产品</th><th>供应商货号</th><th>参考价</th><th>交期</th><th>状态</th><th /></tr></thead><tbody>
        {links.map((item) => <tr key={item.id}><td><TablePrimaryText value={item.productNameCN || item.productNameEN || "未命名"} secondary={item.productCode || "无产品货号"} /></td><td><TablePrimaryText value={item.supplierProductCode} /></td><td>{item.currency} {item.referencePrice.toFixed(2)}</td><td>{item.leadTimeDays ? `${item.leadTimeDays} 天` : "未设置"}</td><td><BusinessStatusBadge value={item.status} /></td><td><button className="secondary-button" type="button" onClick={() => { setSelectedId(item.id); setView("editor"); }}>{canOperate ? "编辑" : "查看"}</button></td></tr>)}
        {!links.length ? <tr><td className="empty-cell" colSpan={6}><div className="empty-cell-content"><strong>尚未关联供应产品</strong><span>{canOperate ? "需要记录供应商货号、参考价或交期时，再建立供货关系。" : "当前供应商还没有供货关系。"}</span>{canOperate ? <button className="primary-button" type="button" onClick={() => { setSelectedId(0); setView("editor"); }}>新增第一条供货关系</button> : null}</div></td></tr> : null}
      </tbody></table></div>
    </> : null}
    {view === "editor" ? <form className="form-grid" key={selected?.id ?? `new-${supplierId}`} onSubmit={save}>
      <div className="section-heading-row"><h4>{selected ? "编辑供货关系" : "新增供货关系"}</h4><button className="secondary-button" type="button" onClick={() => setView("directory")}>返回供应产品目录</button></div>
      <div className="form-field-wide context-strip"><strong>{supplierName}</strong><span>这里只建立供应商与现有产品的关系，不会修改产品主数据。</span></div>
      {!productOptions.length ? <div className="empty-guidance form-field-wide"><strong>先建立产品资料</strong><span>供货关系必须关联现有产品。建立产品后返回此处即可继续。</span>{canOperate ? <button className="secondary-button" type="button" onClick={() => navigate("/master-data/products")}>打开产品资料</button> : null}</div> : null}
      <fieldset className="permission-fieldset form-field-wide" disabled={!canOperate}>
      <label className="form-field-wide">查找产品<div className="toolbar"><input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="输入产品货号或名称" /><button className="secondary-button" type="button" onClick={() => void searchProducts()}>查找</button></div></label>
      <label className="form-field-wide">产品<select name="productId" required defaultValue={selected?.productId ?? ""}><option value="">请选择产品</option>{productOptions.map((item) => <option key={item.id} value={item.id}>{item.productCode || "无货号"} · {item.nameCN || item.nameEN || "未命名"}</option>)}</select></label>
      <label>供应商货号<input name="supplierProductCode" defaultValue={selected?.supplierProductCode} /></label>
      <label>参考价<input name="referencePrice" type="number" min="0" step="0.0001" defaultValue={selected?.referencePrice ?? 0} /></label>
      <label>币种<input name="currency" maxLength={3} defaultValue={selected?.currency ?? "CNY"} /></label>
      <label>交期（天）<input name="leadTimeDays" type="number" min="0" max="3650" defaultValue={selected?.leadTimeDays ?? 0} /></label>
      <label>供货状态<select name="linkStatus" defaultValue={selected?.status ?? "供货中"}><option>供货中</option><option>备选</option><option>暂停</option><option>停用</option></select></label>
      </fieldset>
      <div className="form-actions">{canOperate ? <button className="primary-button" disabled={!supplierId || !productOptions.length}>保存供货关系</button> : null}{selected && canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void remove()}>删除关联</button> : null}</div>
    </form> : null}
  </section>;
}
