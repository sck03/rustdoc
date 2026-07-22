import { useEffect, useState, type FormEvent } from "react";
import type { ApiSupplierContactDto, ApiSupplierDto, ApiSupplierImportPreviewDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { SupplierProductLinksPanel } from "./SupplierProductLinksPanel.tsx";
import { SupplierAssessmentsPanel } from "./SupplierAssessmentsPanel.tsx";
import { SupplierAssessmentOverview } from "./SupplierAssessmentOverview.tsx";
import { TaskViewTabs } from "../../ui/TaskViewTabs.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { PermissionNotice } from "../../ui/PageState.tsx";

type SupplierTaskView = "overview" | "directory" | "profile" | "contacts" | "products" | "assessments" | "import";

export function SupplierDirectoryPage({ client }: { client: ExportDocManagerApiClient }) {
  const supplierPermission = useModulePermission("sales.suppliers");
  const requestConfirmation = useConfirmation();
  const [suppliers, setSuppliers] = useState<ApiSupplierDto[]>([]);
  const [contacts, setContacts] = useState<ApiSupplierContactDto[]>([]);
  const [supplierId, setSupplierId] = useState(0);
  const [contactId, setContactId] = useState(0);
  const [newSupplier, setNewSupplier] = useState(false);
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword, setKeyword] = useState("");
  const [status, setStatus] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [page, setPage] = useState<Awaited<ReturnType<ExportDocManagerApiClient["querySuppliers"]>> | null>(null);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [importPreview, setImportPreview] = useState<ApiSupplierImportPreviewDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [view, setView] = useState<SupplierTaskView>("overview");
  const [contactView, setContactView] = useState<"directory" | "editor">("directory");
  const selectedSupplier = suppliers.find((item) => item.id === supplierId);
  const selectedContact = contacts.find((item) => item.id === contactId);

  async function loadSuppliers(preferredId?: number) {
    const rows = await client.listSuppliers();
    setSuppliers(rows);
    setSupplierId(preferredId && rows.some((item) => item.id === preferredId) ? preferredId : rows[0]?.id ?? 0);
  }

  useEffect(() => { void loadSuppliers().catch((error) => setFeedback(errorFeedback(readApiError(error)))); }, [client]);
  useEffect(() => {
    if (!supplierId) { setContacts([]); return; }
    setContactView("directory");
    void client.listSupplierContacts({ supplierId }).then((rows) => {
      setContacts(rows); setContactId((current) => rows.some((item) => item.id === current) ? current : rows[0]?.id ?? 0);
    }).catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, supplierId]);
  useEffect(() => {
    void client.querySuppliers({ keyword, status, pageNumber, pageSize: 20 }).then(setPage).catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, keyword, status, pageNumber, suppliers]);

  async function saveSupplier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!supplierPermission.canOperate) return;
    const form = new FormData(event.currentTarget);
    const id = newSupplier ? 0 : selectedSupplier?.id ?? 0;
    const body = { id, name: text(form, "name"), countryRegion: text(form, "countryRegion"), category: text(form, "category"),
      website: text(form, "website"), status: text(form, "supplierStatus") || "合作中", mainProducts: text(form, "mainProducts"), notes: text(form, "notes"),
      expectedVersion: id > 0 ? selectedSupplier?.versionNumber ?? 0 : 0 };
    try {
      const saved = id ? await client.updateSupplier({ id, body }) : await client.createSupplier({ body });
      await loadSuppliers(saved.id); setNewSupplier(false); setFeedback(successFeedback(id ? "供应商已更新。" : "供应商已建立。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function deleteSupplier() {
    if (!supplierPermission.canManage || !selectedSupplier || !await requestConfirmation({ title: "删除供应商", description: `确定删除供应商“${selectedSupplier.name}”吗？`, details: ["该供应商的联系人将一并删除。", "已生成的历史业务记录不会被改写。"], confirmLabel: "确认删除", tone: "danger" })) return;
    try { await client.deleteSupplier({ id: selectedSupplier.id }); await loadSuppliers(); setView("directory"); setFeedback(successFeedback("供应商已删除。")); }
    catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function saveContact(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!supplierPermission.canOperate || !supplierId) return;
    const form = new FormData(event.currentTarget); const id = selectedContact?.id ?? 0;
    const body = { id, supplierCompanyId: supplierId, name: text(form, "contactName"), title: text(form, "title"), email: text(form, "email"),
      phone: text(form, "phone"), instantMessaging: text(form, "instantMessaging"), isPrimary: form.get("isPrimary") === "on",
      expectedVersion: id > 0 ? selectedContact?.versionNumber ?? 0 : 0 };
    try {
      const saved = id ? await client.updateSupplierContact({ supplierId, id, body }) : await client.createSupplierContact({ supplierId, body });
      const rows = await client.listSupplierContacts({ supplierId }); setContacts(rows); setContactId(saved.id); setContactView("editor"); setFeedback(successFeedback(id ? "联系人已更新。" : "联系人已添加。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function deleteContact() {
    if (!supplierPermission.canManage || !selectedContact || !await requestConfirmation({ title: "删除供应商联系人", description: `确定删除联系人“${selectedContact.name}”吗？`, confirmLabel: "确认删除", tone: "danger" })) return;
    try {
      await client.deleteSupplierContact({ supplierId, id: selectedContact.id });
      const rows = await client.listSupplierContacts({ supplierId });
      setContacts(rows); setContactId(rows[0]?.id ?? 0); setContactView("directory"); setFeedback(successFeedback("联系人已删除。"));
    }
    catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function previewImport(file?: File) {
    if (!supplierPermission.canOperate || !file) return; setBusy(true);
    try { setImportPreview(await client.previewSupplierImport({ fileName: file.name, body: file })); setFeedback(null); }
    catch (error) { setFeedback(errorFeedback(readApiError(error))); setImportPreview(null); }
    finally { setBusy(false); }
  }

  async function confirmImport() {
    if (!supplierPermission.canOperate || !importPreview?.validRows) return; setBusy(true);
    try {
      const result = await client.importSuppliers({ body: { rows: importPreview.rows } });
      setFeedback(successFeedback(`已导入 ${result.createdSuppliers} 家供应商、${result.createdContacts} 位联系人，跳过 ${result.skippedRows} 行。`));
      setImportPreview(null); await loadSuppliers();
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); } finally { setBusy(false); }
  }

  async function updateBatchStatus(targetStatus: string) {
    if (!supplierPermission.canOperate) return;
    if (!selectedIds.length) { setFeedback(warningFeedback("请先勾选供应商。")); return; }
    try {
      const result = await client.updateSupplierBatchStatus({ body: { ids: selectedIds, status: targetStatus } });
      setSelectedIds([]); await loadSuppliers(supplierId); setFeedback(successFeedback(`已更新 ${result.affectedCount} 家供应商为“${result.status}”。`));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function exportRows() {
    try {
      const blob = await client.exportSuppliers({ keyword, status });
      const url = URL.createObjectURL(blob); const anchor = document.createElement("a");
      anchor.href = url; anchor.download = `suppliers-${new Date().toISOString().slice(0, 10)}.xlsx`; anchor.click();
      URL.revokeObjectURL(url); setFeedback(successFeedback("供应商 Excel 已生成。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return <section className="work-surface">
    <div className="section-heading-row"><div><h2>供应商与联系人</h2><p>独立维护常用供应商资料，不与客户 CRM 或单证客户混用。</p></div></div>
    <OperationFeedback feedback={feedback} />
    {!supplierPermission.canOperate ? <PermissionNotice>当前权限模板仅允许查看供应商、联系人、供货关系和评价；新增、导入、修改与状态调整已禁用。</PermissionNotice> : null}
    <TaskViewTabs value={view} label="供应商工作区" onChange={setView} items={[
      { id: "overview", label: "采购概览" }, { id: "directory", label: "供应商目录" }, { id: "profile", label: newSupplier ? "新建供应商" : "供应商资料", disabled: newSupplier && !supplierPermission.canOperate },
      { id: "contacts", label: "供应商联系人", disabled: !selectedSupplier || newSupplier },
      { id: "products", label: "供应产品", disabled: !selectedSupplier || newSupplier },
      { id: "assessments", label: "供应商评价", disabled: !selectedSupplier || newSupplier },
      { id: "import", label: "导入导出" },
    ]} />
    {view === "overview" ? <SupplierAssessmentOverview client={client} onOpenSupplier={(id) => { setSupplierId(id); setNewSupplier(false); setView("assessments"); }} /> : null}
    {view === "import" ? <section className="form-section"><div className="section-header"><h3>导入、导出与批量维护</h3><span>CSV/XLSX 最多 5000 行、10 MB</span></div>
      <div className="form-actions">
        {supplierPermission.canOperate ? <label className="secondary-button">选择导入文件<input type="file" hidden accept=".csv,.xlsx,.xlsm" onChange={(event) => void previewImport(event.target.files?.[0])} /></label> : null}
        {supplierPermission.canOperate ? <button className="primary-button" type="button" disabled={busy || !importPreview?.validRows} onClick={() => void confirmImport()}>确认导入有效行</button> : null}
        <button className="secondary-button" type="button" onClick={() => void exportRows()}>导出当前筛选</button>
        {supplierPermission.canOperate ? <select aria-label="批量状态" onChange={(event) => { if (event.target.value) void updateBatchStatus(event.target.value); event.target.value = ""; }} defaultValue="">
          <option value="">批量修改状态...</option><option>合作中</option><option>考察中</option><option>暂停</option><option>停用</option>
        </select> : null}
      </div>
      {importPreview ? <><p>共 {importPreview.totalRows} 行，有效 {importPreview.validRows} 行，重复 {importPreview.duplicateRows} 行。</p>
        <ResponsiveTableFrame label="供应商导入预览" mobileLayout="scroll"><table className="data-table"><thead><tr><th>行</th><th>供应商</th><th>分类</th><th>联系人</th><th>结果</th></tr></thead><tbody>
          {importPreview.rows.slice(0, 30).map((row) => <tr key={row.rowNumber}><td>{row.rowNumber}</td><td>{row.name || "-"}</td><td>{row.category || "-"}</td><td>{row.contactName || "-"}</td><td>{row.error || (row.isDuplicate ? "重复，跳过" : "可导入")}</td></tr>)}
        </tbody></table></ResponsiveTableFrame></> : null}
    </section> : null}
    {view === "directory" ? <section className="form-section"><div className="section-header"><div><h3>供应商目录</h3><p className="section-description">查找供货单位并进入资料、联系人或供应产品。</p></div><div className="section-header-actions"><span>共 {page?.totalCount ?? 0} 家</span>{supplierPermission.canOperate ? <button className="primary-button" type="button" onClick={() => { setNewSupplier(true); setView("profile"); }}>新建供应商</button> : null}</div></div>
      <form className="toolbar" onSubmit={(event) => { event.preventDefault(); setKeyword(keywordInput.trim()); setPageNumber(1); }}>
        <input value={keywordInput} onChange={(event) => setKeywordInput(event.target.value)} placeholder="搜索名称、分类、产品或国家" />
        <select value={status} onChange={(event) => { setStatus(event.target.value); setPageNumber(1); }}><option value="">全部状态</option><option>合作中</option><option>考察中</option><option>暂停</option><option>停用</option></select>
        <button className="secondary-button">搜索</button>
      </form>
      <ResponsiveTableFrame label="供应商目录" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr>{supplierPermission.canOperate ? <th><input type="checkbox" aria-label="选择本页" checked={(page?.items.length ?? 0) > 0 && page!.items.every((item) => selectedIds.includes(item.id))} onChange={(event) => setSelectedIds(event.target.checked ? Array.from(new Set([...selectedIds, ...(page?.items.map((item) => item.id) ?? [])])) : selectedIds.filter((id) => !page?.items.some((item) => item.id === id)))} /></th> : null}<th>供应商</th><th data-table-priority="secondary">分类</th><th data-table-priority="secondary">主要产品</th><th>状态</th><th /></tr></thead><tbody>
        {(page?.items ?? []).map((item) => <tr key={item.id}>{supplierPermission.canOperate ? <td><input type="checkbox" aria-label={`选择供应商 ${item.name}`} checked={selectedIds.includes(item.id)} onChange={(event) => setSelectedIds((current) => event.target.checked ? [...current, item.id] : current.filter((id) => id !== item.id))} /></td> : null}<td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.category || "-"}</td><td data-table-priority="secondary">{item.mainProducts || "-"}</td><td><BusinessStatusBadge value={item.status} /></td><td><button className="secondary-button" type="button" onClick={() => { setSupplierId(item.id); setNewSupplier(false); setView("profile"); }}>打开</button></td></tr>)}
        {!page?.items.length ? <tr><td className="empty-cell" colSpan={supplierPermission.canOperate ? 6 : 5}><div className="empty-cell-content"><strong>暂无供应商</strong><span>{supplierPermission.canOperate ? "先建立供应商资料，再按需添加联系人和关联供应产品。" : "当前没有可查看的供应商。"}</span>{supplierPermission.canOperate ? <div className="form-actions"><button className="primary-button" type="button" onClick={() => { setNewSupplier(true); setView("profile"); }}>建立第一家供应商</button><button className="secondary-button" type="button" onClick={() => setView("import")}>从文件导入</button></div> : null}</div></td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
      <div className="form-actions"><button className="secondary-button" disabled={!page?.hasPreviousPage} onClick={() => setPageNumber((v) => v - 1)}>上一页</button><span>第 {page?.pageNumber ?? 1} / {Math.max(page?.totalPages ?? 1, 1)} 页</span><button className="secondary-button" disabled={!page?.hasNextPage} onClick={() => setPageNumber((v) => v + 1)}>下一页</button></div>
    </section> : null}
    {view === "profile" ? <form className="form-grid" key={newSupplier ? "new" : selectedSupplier?.id ?? "empty"} onSubmit={saveSupplier}>
        <div className="section-heading-row"><h3>{newSupplier ? "新建供应商" : supplierPermission.canOperate ? "供应商资料" : "查看供应商"}</h3>{supplierPermission.canOperate ? <button className="secondary-button" type="button" onClick={() => setNewSupplier(true)}>新建</button> : null}</div>
        {!newSupplier ? <label>选择供应商<select value={supplierId} onChange={(e) => setSupplierId(Number(e.target.value))}>{suppliers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label> : null}
        <fieldset className="permission-fieldset form-field-wide" disabled={!supplierPermission.canOperate}>
        <label>名称<input name="name" required defaultValue={newSupplier ? "" : selectedSupplier?.name} /></label><label>国家/地区<input name="countryRegion" defaultValue={newSupplier ? "" : selectedSupplier?.countryRegion} /></label>
        <label>分类<input name="category" defaultValue={newSupplier ? "" : selectedSupplier?.category} /></label><label>网站<input name="website" defaultValue={newSupplier ? "" : selectedSupplier?.website} /></label>
        <label>状态<select name="supplierStatus" defaultValue={newSupplier ? "合作中" : selectedSupplier?.status}><option>合作中</option><option>考察中</option><option>暂停</option><option>停用</option></select></label>
        <label className="form-field-wide">主要产品<input name="mainProducts" defaultValue={newSupplier ? "" : selectedSupplier?.mainProducts} /></label><label className="form-field-wide">备注<textarea name="notes" defaultValue={newSupplier ? "" : selectedSupplier?.notes} /></label>
        </fieldset>
        <div className="form-actions">{supplierPermission.canOperate ? <button className="primary-button">保存供应商</button> : null}{!newSupplier && selectedSupplier ? <button className="secondary-button" type="button" onClick={() => setView("contacts")}>{supplierPermission.canOperate ? "管理联系人" : "查看联系人"}</button> : null}{!newSupplier && selectedSupplier && supplierPermission.canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteSupplier()}>删除</button> : null}</div>
      </form> : null}
    {view === "contacts" && selectedSupplier && !newSupplier ? <section className="form-section supplier-contact-workspace">
      <div className="section-header"><div><h3>{contactView === "directory" ? "供应商联系人目录" : selectedContact ? "编辑供应商联系人" : "新增供应商联系人"}</h3><p className="section-description">联系人只归属 {selectedSupplier.name}，不写入客户 CRM。</p></div><span>{contacts.length} 位</span></div>
      {contactView === "directory" ? <>
        <div className="section-header-actions supplier-contact-directory-actions"><button className="secondary-button" type="button" onClick={() => setView("profile")}>返回供应商资料</button>{supplierPermission.canOperate ? <button className="primary-button" type="button" onClick={() => { setContactId(0); setContactView("editor"); }}>新增联系人</button> : null}</div>
        <ResponsiveTableFrame label="供应商联系人" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>联系人</th><th data-table-priority="secondary">职位</th><th>邮箱</th><th data-table-priority="secondary">电话</th><th>类型</th><th /></tr></thead><tbody>
          {contacts.map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.title || "-"}</td><td><TablePrimaryText value={item.email} /></td><td data-table-priority="secondary">{item.phone || "-"}</td><td>{item.isPrimary ? <BusinessStatusBadge value="主要联系人" /> : "普通联系人"}</td><td><button className="secondary-button" type="button" onClick={() => { setContactId(item.id); setContactView("editor"); }}>{supplierPermission.canOperate ? "编辑" : "查看"}</button></td></tr>)}
          {!contacts.length ? <tr><td className="empty-cell" colSpan={6}><div className="empty-cell-content"><strong>尚未建立供应商联系人</strong><span>{supplierPermission.canOperate ? "需要记录询价、交期或付款沟通对象时，再添加联系人。" : "当前供应商还没有联系人。"}</span>{supplierPermission.canOperate ? <button className="primary-button" type="button" onClick={() => { setContactId(0); setContactView("editor"); }}>添加第一位联系人</button> : null}</div></td></tr> : null}
        </tbody></table></ResponsiveTableFrame>
      </> : <form className="form-grid" key={selectedContact?.id ?? `new-${supplierId}`} onSubmit={saveContact}>
        <div className="section-heading-row"><h4>{selectedContact ? "编辑联系人资料" : "新增联系人资料"}</h4><button className="secondary-button" type="button" onClick={() => setContactView("directory")}>返回联系人目录</button></div>
        <div className="form-field-wide context-strip"><strong>{selectedSupplier.name}</strong><span>联系人只归属当前供应商，不写入客户 CRM。</span></div>
        <fieldset className="permission-fieldset form-field-wide" disabled={!supplierPermission.canOperate}>
        <label>姓名<input name="contactName" required defaultValue={selectedContact?.name} /></label><label>职位<input name="title" defaultValue={selectedContact?.title} /></label><label>邮箱<input name="email" type="email" defaultValue={selectedContact?.email} /></label>
        <label>电话<input name="phone" defaultValue={selectedContact?.phone} /></label><label>即时通讯<input name="instantMessaging" defaultValue={selectedContact?.instantMessaging} /></label><label className="checkbox-field"><input name="isPrimary" type="checkbox" defaultChecked={selectedContact?.isPrimary ?? contacts.length === 0} />主要联系人</label>
        </fieldset>
        <div className="form-actions">{supplierPermission.canOperate ? <button className="primary-button" disabled={!supplierId}>保存联系人</button> : null}{selectedContact && supplierPermission.canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteContact()}>删除</button> : null}</div>
      </form>}
    </section> : null}
    {view === "products" && selectedSupplier && !newSupplier ? <SupplierProductLinksPanel client={client} supplierId={selectedSupplier.id} supplierName={selectedSupplier.name} canOperate={supplierPermission.canOperate} canManage={supplierPermission.canManage} /> : null}
    {view === "assessments" && selectedSupplier && !newSupplier ? <SupplierAssessmentsPanel client={client} supplierId={selectedSupplier.id} supplierName={selectedSupplier.name} canOperate={supplierPermission.canOperate} canManage={supplierPermission.canManage} /> : null}
  </section>;
}

function text(form: FormData, name: string) { return String(form.get(name) ?? "").trim(); }
