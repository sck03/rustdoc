import { useEffect, useState, type FormEvent } from "react";
import type { ApiSupplierContactDto, ApiSupplierDto, ApiSupplierImportPreviewDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { currentLocalDateInputValue, readApiError } from "../../ui/formUtils.ts";
import { SupplierProductLinksPanel } from "./SupplierProductLinksPanel.tsx";
import { SupplierAssessmentsPanel } from "./SupplierAssessmentsPanel.tsx";
import { SupplierAssessmentOverview } from "./SupplierAssessmentOverview.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, requestErrorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteChoice, readRouteId } from "../../ui/routeQueryState.ts";
import { useDirectoryLocation } from "../../ui/useDirectoryLocation.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { usePagedDirectoryQuery } from "../../ui/usePagedDirectoryQuery.ts";
import { useConfirmUnsavedChanges, useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";

type SupplierTaskView = "overview" | "directory" | "profile" | "contacts" | "products" | "assessments" | "import";
const supplierTabsId = "supplier-directory-workspace";

export function SupplierDirectoryPage({ businessDate, client }: { businessDate: string; client: ExportDocManagerApiClient }) {
  const canViewContacts = usePermission(permissionResources.supplierContacts, permissionActions.view).allowed;
  const canViewProducts = usePermission(permissionResources.supplierProductLinks, permissionActions.view).allowed;
  const canViewAssessments = usePermission(permissionResources.supplierAssessments, permissionActions.view).allowed;
  const canCreateSupplier = usePermission(permissionResources.suppliers, permissionActions.create).allowed;
  const canEditSupplier = usePermission(permissionResources.suppliers, permissionActions.edit).allowed;
  const canAdmitSupplier = usePermission(permissionResources.suppliers, permissionActions.admit).allowed;
  const canDeactivateSupplier = usePermission(permissionResources.suppliers, permissionActions.deactivate).allowed;
  const canDeleteSupplier = usePermission(permissionResources.suppliers, permissionActions.delete).allowed;
  const canImportSupplier = usePermission(permissionResources.suppliers, permissionActions.import).allowed;
  const canExportSupplier = usePermission(permissionResources.suppliers, permissionActions.export).allowed;
  const canCreateContact = usePermission(permissionResources.supplierContacts, permissionActions.create).allowed;
  const canEditContact = usePermission(permissionResources.supplierContacts, permissionActions.edit).allowed;
  const canSetPrimaryContact = usePermission(permissionResources.supplierContacts, permissionActions.setPrimary).allowed;
  const canDeleteContact = usePermission(permissionResources.supplierContacts, permissionActions.delete).allowed;
  const canEditProductLink = usePermission(permissionResources.supplierProductLinks, permissionActions.edit).allowed;
  const canDeactivateProductLink = usePermission(permissionResources.supplierProductLinks, permissionActions.deactivate).allowed;
  const canDeleteProductLink = usePermission(permissionResources.supplierProductLinks, permissionActions.delete).allowed;
  const canCreateAssessment = usePermission(permissionResources.supplierAssessments, permissionActions.create).allowed;
  const canEditAssessment = usePermission(permissionResources.supplierAssessments, permissionActions.edit).allowed;
  const canApproveAssessment = usePermission(permissionResources.supplierAssessments, permissionActions.approve).allowed;
  const canDeleteAssessment = usePermission(permissionResources.supplierAssessments, permissionActions.delete).allowed;
  const requestConfirmation = useConfirmation();
  const [suppliers, setSuppliers] = useState<ApiSupplierDto[]>([]);
  const [contacts, setContacts] = useState<ApiSupplierContactDto[]>([]);
  const { params, update: updateRoute } = useRouteQuery();
  const supplierId = readRouteId(params.get("supplierId")) ?? 0;
  const setSupplierId = (id: number) => updateRoute({ supplierId: id || null });
  const [contactId, setContactId] = useState(0);
  const newSupplier = params.get("new") === "true";
  const setNewSupplier = (value: boolean) => updateRoute({ new: value });
  const { keywordInput, setKeywordInput, keyword, setKeyword, status, setStatus, pageNumber, setPageNumber, pageSize, setPageSize } = useDirectoryLocation();
  const [revision, setRevision] = useState(0);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [contactPageNumber, setContactPageNumber] = useState(1);
  const [contactPageSize, setContactPageSize] = useState(20);
  const [contactTotalCount, setContactTotalCount] = useState(0);
  const [contactTotalPages, setContactTotalPages] = useState(1);
  const [importPreview, setImportPreview] = useState<ApiSupplierImportPreviewDto | null>(null);
  const [busy, setBusy] = useState(false);
  const view = readRouteChoice<SupplierTaskView>(params.get("view"), ["overview", "directory", "profile", "contacts", "products", "assessments", "import"], "directory");
  const setView = (value: SupplierTaskView) => updateRoute({ view: value }, false);
  const [contactView, setContactView] = useState<"directory" | "editor">("directory");
  const [supplierDraftDirty, setSupplierDraftDirty] = useState(false);
  const [contactDraftDirty, setContactDraftDirty] = useState(false);
  const selectedSupplier = suppliers.find((item) => item.id === supplierId);
  const selectedContact = contacts.find((item) => item.id === contactId);
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  useUnsavedChangesGuard({
    isDirty: supplierDraftDirty || contactDraftDirty,
    message: "当前供应商或联系人资料有未保存的修改。",
  });
  useEffect(() => {
    setSupplierDraftDirty(false);
    setContactDraftDirty(false);
  }, [supplierId, view]);

  async function changeView(nextView: SupplierTaskView) {
    if (nextView === view) return true;
    if (!await confirmDiscardChanges("切换供应商工作区")) return false;
    setSupplierDraftDirty(false);
    setContactDraftDirty(false);
    if (view === "profile" && newSupplier) setNewSupplier(false);
    setView(nextView);
    return true;
  }

  async function beginNewSupplier() {
    if (!await confirmDiscardChanges("新建供应商")) return;
    setSupplierDraftDirty(false);
    setContactDraftDirty(false);
    setSupplierId(0);
    setNewSupplier(true);
    setView("profile");
  }

  async function changeContactView(nextView: "directory" | "editor") {
    if (nextView === contactView) return true;
    if (!await confirmDiscardChanges("切换联系人视图")) return false;
    setContactDraftDirty(false);
    setContactView(nextView);
    return true;
  }

  async function selectContact(nextContactId: number) {
    if (nextContactId === contactId && contactView === "editor") return;
    if (!await confirmDiscardChanges("切换联系人")) return;
    setContactDraftDirty(false);
    setContactId(nextContactId);
    setContactView("editor");
  }

  async function loadSupplierOptions(preferred?: ApiSupplierDto) {
    setSuppliers(preferred ? [preferred] : []);
    setSupplierId(preferred?.id ?? 0);
  }

  async function openSupplier(item: ApiSupplierDto) {
    setSuppliers((current) => [item, ...current.filter((supplier) => supplier.id !== item.id)]);
    setSupplierId(item.id);
    setContactPageNumber(1);
    setNewSupplier(false);
    setView("profile");
  }

  const pageQuery = usePagedDirectoryQuery(
    ["suppliers", keyword, status, pageNumber, pageSize, revision],
    (signal) => client.querySuppliers({ keyword, status, pageNumber, pageSize }, { signal }),
  );
  const page = pageQuery.data ?? null;

  useEffect(() => {
    if (!supplierId || newSupplier || selectedSupplier) return;
    const controller = new AbortController();
    void client.getSupplier({ id: supplierId }, { signal: controller.signal })
      .then((item) => { if (!controller.signal.aborted) setSuppliers([item]); })
      .catch((error) => { if (!controller.signal.aborted) setFeedback(errorFeedback(readApiError(error))); });
    return () => controller.abort();
  }, [client, supplierId, newSupplier, selectedSupplier]);
  useEffect(() => {
    if (!supplierId || !canViewContacts) {
      setContacts([]);
      setContactTotalCount(0);
      setContactTotalPages(1);
      return;
    }
    const controller = new AbortController();
    setContacts([]);
    setContactView("directory");
    void client.querySupplierContacts(
      { supplierId, pageNumber: contactPageNumber, pageSize: contactPageSize },
      { signal: controller.signal },
    ).then((page) => {
      if (controller.signal.aborted) return;
      setContacts(page.items);
      setContactTotalCount(page.totalCount);
      setContactTotalPages(page.totalPages);
      setContactId((current) => page.items.some((item) => item.id === current) ? current : page.items[0]?.id ?? 0);
    }).catch((error: unknown) => { if (!controller.signal.aborted) setFeedback(errorFeedback(readApiError(error))); });
    return () => controller.abort();
  }, [client, contactPageNumber, contactPageSize, supplierId, canViewContacts]);

  async function reloadContacts(preferredId = 0) {
    if (!supplierId) return;
    const page = await client.querySupplierContacts({ supplierId, pageNumber: 1, pageSize: contactPageSize });
    setContactPageNumber(1);
    setContacts(page.items);
    setContactTotalCount(page.totalCount);
    setContactTotalPages(page.totalPages);
    setContactId(preferredId && page.items.some((item) => item.id === preferredId)
      ? preferredId
      : page.items[0]?.id ?? 0);
  }

  async function saveSupplier(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (newSupplier ? !canCreateSupplier : !canEditSupplier) return;
    const form = new FormData(event.currentTarget);
    const id = newSupplier ? 0 : selectedSupplier?.id ?? 0;
    const body = { id, name: text(form, "name"), countryRegion: text(form, "countryRegion"), category: text(form, "category"),
      website: text(form, "website"),
      mainProducts: text(form, "mainProducts"), notes: text(form, "notes"),
      expectedVersion: id > 0 ? selectedSupplier?.versionNumber ?? 0 : 0 };
    try {
      const saved = id ? await client.updateSupplier({ id, body }) : await client.createSupplier({ body });
      await loadSupplierOptions(saved); setSupplierDraftDirty(false); setContactDraftDirty(false); setRevision((value) => value + 1); setNewSupplier(false); setFeedback(successFeedback(id ? "供应商已更新。" : "供应商已建立。"));
    } catch (error) { setFeedback(requestErrorFeedback(error)); }
  }

  async function changeSupplierStatus(action: "admit" | "deactivate" | "restore") {
    if (!selectedSupplier) return;
    const labels = {
      admit: ["准入供应商", "确认该供应商已经完成准入审核并进入合作中状态吗？", "供应商已准入。"],
      deactivate: ["停用供应商", "停用会保留联系人、供货关系和评价历史，确定继续吗？", "供应商已停用。"],
      restore: ["恢复供应商", "恢复后供应商将回到考察中状态，确定继续吗？", "供应商已恢复考察。"],
    } as const;
    const [title, description, successMessage] = labels[action];
    if (!await requestConfirmation({
      title,
      description,
      confirmLabel: title,
      tone: action === "deactivate" ? "danger" : undefined,
    })) return;
    try {
      const request = { id: selectedSupplier.id, body: { expectedVersion: selectedSupplier.versionNumber } };
      const saved = action === "admit"
        ? await client.admitSupplier(request)
        : action === "restore"
          ? await client.restoreSupplier(request)
          : await client.deactivateSupplier(request);
      await loadSupplierOptions(saved);
      setRevision((value) => value + 1);
      setFeedback(successFeedback(successMessage));
    } catch (error) {
      setFeedback(requestErrorFeedback(error));
    }
  }

  async function deleteSupplier() {
    if (!canDeleteSupplier || !selectedSupplier || !await requestConfirmation({ title: "删除供应商", description: `确定删除供应商“${selectedSupplier.name}”吗？`, details: ["只有没有供货关系和评价的供应商才能删除。", "联系人会随供应商一并删除；有业务历史时请使用“停用供应商”。"], confirmLabel: "确认删除", tone: "danger" }) || !await confirmDiscardChanges("删除供应商")) return;
    try { const response = await client.deleteSupplier({ id: selectedSupplier.id, expectedVersion: selectedSupplier.versionNumber }); setSupplierDraftDirty(false); setContactDraftDirty(false); await loadSupplierOptions(); setRevision((value) => value + 1); setView("directory"); setFeedback(successFeedback(response.message)); }
    catch (error) { setFeedback(requestErrorFeedback(error)); }
  }

  async function saveContact(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if ((selectedContact ? !canEditContact : !canCreateContact) || !supplierId) return;
    const form = new FormData(event.currentTarget); const id = selectedContact?.id ?? 0;
    const body = { id, supplierCompanyId: supplierId, name: text(form, "contactName"), title: text(form, "title"), email: text(form, "email"),
      phone: text(form, "phone"), instantMessaging: text(form, "instantMessaging"),
      expectedVersion: id > 0 ? selectedContact?.versionNumber ?? 0 : 0 };
    try {
      const saved = id ? await client.updateSupplierContact({ supplierId, id, body }) : await client.createSupplierContact({ supplierId, body });
      await reloadContacts(saved.id); setContactDraftDirty(false); setContactView("editor"); setFeedback(successFeedback(id ? "联系人已更新。" : "联系人已添加。"));
    } catch (error) { setFeedback(requestErrorFeedback(error)); }
  }

  async function setPrimaryContact() {
    if (!canSetPrimaryContact || !selectedContact) return;
    try {
      const saved = await client.setPrimarySupplierContact({
        supplierId,
        id: selectedContact.id,
        body: { expectedVersion: selectedContact.versionNumber },
      });
      await reloadContacts(saved.id);
      setFeedback(successFeedback("主要联系人已切换。"));
    } catch (error) {
      setFeedback(requestErrorFeedback(error));
    }
  }

  async function deleteContact() {
    if (!canDeleteContact || !selectedContact || !await requestConfirmation({ title: "删除供应商联系人", description: `确定删除联系人“${selectedContact.name}”吗？`, confirmLabel: "确认删除", tone: "danger" }) || !await confirmDiscardChanges("删除联系人")) return;
    try {
      await client.deleteSupplierContact({ supplierId, id: selectedContact.id, expectedVersion: selectedContact.versionNumber });
      await reloadContacts(); setContactDraftDirty(false); setContactView("directory"); setFeedback(successFeedback("联系人已删除。"));
    }
    catch (error) { setFeedback(requestErrorFeedback(error)); }
  }

  async function previewImport(file?: File) {
    if (!canImportSupplier || !file) return; setBusy(true);
    try { setImportPreview(await client.previewSupplierImport({ fileName: file.name, body: file })); setFeedback(null); }
    catch (error) { setFeedback(errorFeedback(readApiError(error))); setImportPreview(null); }
    finally { setBusy(false); }
  }

  async function confirmImport() {
    if (!canImportSupplier || !importPreview?.validRows) return; setBusy(true);
    try {
      const result = await client.importSuppliers({ body: { previewId: importPreview.previewId } });
      setFeedback(successFeedback(`已导入 ${result.createdSuppliers} 家供应商、${result.createdContacts} 位联系人，跳过 ${result.skippedRows} 行。`));
      setImportPreview(null); await loadSupplierOptions(); setRevision((value) => value + 1);
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); } finally { setBusy(false); }
  }

  async function exportRows() {
    if (!canExportSupplier) return;
    try {
      const blob = await client.exportSuppliers({ keyword, status });
      downloadBlob(blob, `suppliers-${currentLocalDateInputValue()}.xlsx`);
      setFeedback(successFeedback("供应商 Excel 已生成。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return <section className="work-surface">
    {view === "directory" && <p className="section-description">查找供应商，打开资料后维护联系人、供应产品和评价。</p>}
    <OperationFeedback feedback={feedback} />
    {!canCreateSupplier && !canEditSupplier && !canCreateContact && !canEditContact && !canEditProductLink && !canCreateAssessment && !canEditAssessment
      ? <PermissionNotice>当前岗位只有供应商业务查看权限；档案、联系人、供货关系、评价、导入和导出分别授权。</PermissionNotice>
      : null}
    {view === "directory" || view === "overview" ? <TaskViewTabs idPrefix={supplierTabsId} value={view} label="供应商工作区" onChange={changeView} items={[
      { id: "directory", label: "供应商目录" }, ...(canViewAssessments ? [{ id: "overview" as const, label: "采购概览" }] : []),
    ]} /> : <div className="record-context-heading"><button className="secondary-button" type="button" onClick={() => void changeView("directory")}>返回供应商目录</button>
      <strong>{newSupplier ? "新建供应商" : view === "import" ? "导入供应商" : selectedSupplier?.name ?? "供应商详情"}</strong></div>}
    {supplierId > 0 && !newSupplier && !["directory", "overview", "import"].includes(view) && <TaskViewTabs idPrefix={supplierTabsId} value={view} label="供应商详情" onChange={changeView} items={[
      { id: "profile", label: "基本资料" }, ...(canViewContacts ? [{ id: "contacts" as const, label: "联系人" }] : []),
      ...(canViewProducts ? [{ id: "products" as const, label: "供应产品" }] : []), ...(canViewAssessments ? [{ id: "assessments" as const, label: "评价" }] : []),
    ]} />}
    {!newSupplier && ["profile", "contacts", "products", "assessments"].includes(view) && !selectedSupplier && <PageState tone={feedback ? "error" : supplierId ? "loading" : "empty"} title={supplierId ? "正在定位供应商资料" : "请从目录选择供应商"} />}
    {view === "overview" && canViewAssessments ? <div {...getTaskViewPanelProps(supplierTabsId, "overview")}><SupplierAssessmentOverview client={client} onOpenSupplier={async (id) => {
      try {
        const item = await client.getSupplier({ id });
        setSuppliers((current) => [item, ...current.filter((supplier) => supplier.id !== item.id)]);
        setSupplierId(item.id); setNewSupplier(false); setView("assessments");
      } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
    }} /></div> : null}
    {view === "import" && canImportSupplier ? <section className="form-section" aria-label="导入供应商"><div className="section-header"><h3>导入供应商</h3><span>CSV/XLSX 最多 5000 行、10 MB</span></div>
      <div className="form-actions">
        {canImportSupplier ? <label className="secondary-button">选择导入文件<input type="file" hidden accept=".csv,.xlsx,.xlsm" onChange={(event) => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; void previewImport(file); }} /></label> : null}
        {canImportSupplier ? <button className="primary-button" type="button" disabled={busy || !importPreview?.validRows} onClick={() => void confirmImport()}>确认导入有效行</button> : null}
      </div>
      {importPreview ? <><p>共 {importPreview.totalRows} 行，有效 {importPreview.validRows} 行，重复 {importPreview.duplicateRows} 行。提交将使用服务端预检快照并重新校验；下表最多展示前 30 行。</p>
        <ResponsiveTableFrame label="供应商导入预览" mobileLayout="scroll"><table className="data-table"><thead><tr><th>行</th><th>供应商</th><th>分类</th><th>联系人</th><th>结果</th></tr></thead><tbody>
          {importPreview.rows.slice(0, 30).map((row) => <tr key={row.rowNumber}><td>{row.rowNumber}</td><td>{row.name || "-"}</td><td>{row.category || "-"}</td><td>{row.contactName || "-"}</td><td>{row.error || (row.isDuplicate ? "重复，跳过" : "可导入")}</td></tr>)}
        </tbody></table></ResponsiveTableFrame></> : null}
    </section> : null}
    {view === "directory" ? <section className="form-section" {...getTaskViewPanelProps(supplierTabsId, "directory")}><div className="section-header"><div><h3>供应商目录</h3><p className="section-description">查找供货单位并进入资料、联系人或供应产品。</p></div><div className="section-header-actions"><span>共 {page?.totalCount ?? 0} 家</span>{canCreateSupplier ? <button className="primary-button" type="button" onClick={() => void beginNewSupplier()}>新建供应商</button> : null}</div></div>
      <form className="toolbar" onSubmit={(event) => { event.preventDefault(); setKeyword(keywordInput.trim()); setPageNumber(1); }}>
        <input aria-label="搜索供应商目录" value={keywordInput} onChange={(event) => setKeywordInput(event.target.value)} placeholder="搜索名称、分类、产品或国家" />
        <select aria-label="供应商状态" value={status} onChange={(event) => { setStatus(event.target.value); setPageNumber(1); }}><option value="">全部状态</option><option>合作中</option><option>考察中</option><option>暂停</option><option>停用</option></select>
        <button className="secondary-button" type="submit">搜索</button>
        {canImportSupplier && <button className="secondary-button" type="button" onClick={() => void changeView("import")}>导入供应商</button>}
        {canExportSupplier && <button className="secondary-button" type="button" onClick={() => void exportRows()}>导出当前筛选</button>}
      </form>
      {pageQuery.isError ? <OperationFeedback feedback={errorFeedback(readApiError(pageQuery.error))} /> : null}
      <ResponsiveTableFrame label="供应商目录" mobileLayout="scroll" busy={pageQuery.isFetching}><table className="data-table responsive-data-table"><thead><tr><th>供应商</th><th data-table-priority="secondary">分类</th><th data-table-priority="secondary">主要产品</th><th>状态</th><th /></tr></thead><tbody>
        {(page?.items ?? []).map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.category || "-"}</td><td data-table-priority="secondary">{item.mainProducts || "-"}</td><td><BusinessStatusBadge value={item.status} /></td><td><button className="secondary-button" type="button" onClick={() => void openSupplier(item)}>打开</button></td></tr>)}
        {!pageQuery.isFetching && !pageQuery.isError && !page?.items.length ? <tr><td className="empty-cell" colSpan={5}><div className="empty-cell-content"><strong>暂无供应商</strong><span>{canCreateSupplier ? "先建立供应商资料，再按需添加联系人和关联供应产品。" : "当前没有可查看的供应商。"}</span>{canCreateSupplier ? <div className="form-actions"><button className="primary-button" type="button" onClick={() => void beginNewSupplier()}>建立第一家供应商</button>{canImportSupplier ? <button className="secondary-button" type="button" onClick={() => void changeView("import")}>从文件导入</button> : null}</div> : null}</div></td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
      <ListPaginationControls pageNumber={pageNumber} totalPages={page?.totalPages ?? 1} totalCount={page?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={pageQuery.isFetching} onPageChange={setPageNumber} onPageSizeChange={(value) => { setPageSize(value); setPageNumber(1); }} />
    </section> : null}
    {view === "profile" && (newSupplier || selectedSupplier) ? <form className="form-grid" key={newSupplier ? "new" : `${selectedSupplier?.id ?? "empty"}-${selectedSupplier?.versionNumber ?? 0}`} onSubmit={saveSupplier} {...(newSupplier ? { "aria-label": "新建供应商" } : getTaskViewPanelProps(supplierTabsId, "profile"))}>
        <div className="section-heading-row"><h3>{newSupplier ? "新建供应商" : canEditSupplier ? "供应商资料" : "查看供应商"}</h3></div>
        <fieldset className="permission-fieldset form-field-wide" disabled={newSupplier ? !canCreateSupplier : !canEditSupplier} onChangeCapture={() => setSupplierDraftDirty(true)}>
        <label>名称<input name="name" required defaultValue={newSupplier ? "" : selectedSupplier?.name} /></label><label>国家/地区<input name="countryRegion" defaultValue={newSupplier ? "" : selectedSupplier?.countryRegion} /></label>
        <label>分类<input name="category" defaultValue={newSupplier ? "" : selectedSupplier?.category} /></label><label>网站<input name="website" defaultValue={newSupplier ? "" : selectedSupplier?.website} /></label>
        {!newSupplier ? <label>当前状态<input value={selectedSupplier?.status ?? ""} readOnly /></label> : null}
        <label className="form-field-wide">主要产品<input name="mainProducts" defaultValue={newSupplier ? "" : selectedSupplier?.mainProducts} /></label><label className="form-field-wide">备注<textarea name="notes" defaultValue={newSupplier ? "" : selectedSupplier?.notes} /></label>
        </fieldset>
        <div className="form-actions">{(newSupplier ? canCreateSupplier : canEditSupplier) ? <button className="primary-button" type="submit">保存供应商</button> : null}{!newSupplier && selectedSupplier?.status === "考察中" && canAdmitSupplier ? <button className="secondary-button" type="button" onClick={() => void changeSupplierStatus("admit")}>确认准入</button> : null}{!newSupplier && selectedSupplier && selectedSupplier.status !== "停用" && canDeactivateSupplier ? <button className="secondary-button" type="button" onClick={() => void changeSupplierStatus("deactivate")}>停用供应商</button> : null}{!newSupplier && selectedSupplier && (selectedSupplier.status === "停用" || selectedSupplier.status === "暂停") && canDeactivateSupplier ? <button className="secondary-button" type="button" onClick={() => void changeSupplierStatus("restore")}>恢复考察</button> : null}{!newSupplier && selectedSupplier && canViewContacts ? <button className="secondary-button" type="button" onClick={() => void changeView("contacts")}>{canCreateContact || canEditContact ? "管理联系人" : "查看联系人"}</button> : null}{!newSupplier && selectedSupplier && canDeleteSupplier ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteSupplier()}>删除</button> : null}</div>
      </form> : null}
    {view === "contacts" && canViewContacts && selectedSupplier && !newSupplier ? <section className="form-section supplier-contact-workspace" {...getTaskViewPanelProps(supplierTabsId, "contacts")}>
      <div className="section-header"><div><h3>{contactView === "directory" ? "供应商联系人目录" : selectedContact ? "编辑供应商联系人" : "新增供应商联系人"}</h3><p className="section-description">联系人只归属 {selectedSupplier.name}，不写入客户 CRM。</p></div><span>{contactTotalCount} 位</span></div>
      {contactView === "directory" ? <>
        <div className="section-header-actions supplier-contact-directory-actions"><button className="secondary-button" type="button" onClick={() => void changeView("profile")}>返回供应商资料</button>{canCreateContact ? <button className="primary-button" type="button" onClick={() => void selectContact(0)}>新增联系人</button> : null}</div>
        <ResponsiveTableFrame label="供应商联系人" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>联系人</th><th data-table-priority="secondary">职位</th><th>邮箱</th><th data-table-priority="secondary">电话</th><th>类型</th><th /></tr></thead><tbody>
          {contacts.map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.title || "-"}</td><td><TablePrimaryText value={item.email} /></td><td data-table-priority="secondary">{item.phone || "-"}</td><td>{item.isPrimary ? <BusinessStatusBadge value="主要联系人" /> : "普通联系人"}</td><td><button className="secondary-button" type="button" onClick={() => void selectContact(item.id)}>{canEditContact ? "编辑" : "查看"}</button></td></tr>)}
          {!contacts.length ? <tr><td className="empty-cell" colSpan={6}><div className="empty-cell-content"><strong>尚未建立供应商联系人</strong><span>{canCreateContact ? "需要记录询价、交期或付款沟通对象时，再添加联系人。" : "当前供应商还没有联系人。"}</span>{canCreateContact ? <button className="primary-button" type="button" onClick={() => void selectContact(0)}>添加第一位联系人</button> : null}</div></td></tr> : null}
        </tbody></table></ResponsiveTableFrame>
        <ListPaginationControls pageNumber={contactPageNumber} totalPages={contactTotalPages} totalCount={contactTotalCount} pageSize={contactPageSize} pageSizeOptions={[20, 50, 100]} isBusy={false} onPageChange={setContactPageNumber} onPageSizeChange={(value) => { setContactPageSize(value); setContactPageNumber(1); }} />
      </> : <form className="form-grid" key={`${selectedContact?.id ?? "new"}-${selectedContact?.versionNumber ?? 0}-${supplierId}`} onSubmit={saveContact}>
        <div className="section-heading-row"><h4>{selectedContact ? "编辑联系人资料" : "新增联系人资料"}</h4><button className="secondary-button" type="button" onClick={() => void changeContactView("directory")}>返回联系人目录</button></div>
        <div className="form-field-wide context-strip"><strong>{selectedSupplier.name}</strong><span>联系人只归属当前供应商，不写入客户 CRM。</span></div>
        <fieldset className="permission-fieldset form-field-wide" disabled={selectedContact ? !canEditContact : !canCreateContact} onChangeCapture={() => setContactDraftDirty(true)}>
        <label>姓名<input name="contactName" required defaultValue={selectedContact?.name} /></label><label>职位<input name="title" defaultValue={selectedContact?.title} /></label><label>邮箱<input name="email" type="email" defaultValue={selectedContact?.email} /></label>
        <label>电话<input name="phone" defaultValue={selectedContact?.phone} /></label><label>即时通讯<input name="instantMessaging" defaultValue={selectedContact?.instantMessaging} /></label>{selectedContact ? <label>联系人角色<input value={selectedContact.isPrimary ? "主要联系人" : "普通联系人"} readOnly /></label> : null}
        </fieldset>
        <div className="form-actions">{(selectedContact ? canEditContact : canCreateContact) ? <button className="primary-button" type="submit" disabled={!supplierId}>保存联系人</button> : null}{selectedContact && !selectedContact.isPrimary && canSetPrimaryContact ? <button className="secondary-button" type="button" onClick={() => void setPrimaryContact()}>设为主要联系人</button> : null}{selectedContact && canDeleteContact ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteContact()}>删除</button> : null}</div>
      </form>}
    </section> : null}
    {view === "products" && canViewProducts && selectedSupplier && !newSupplier ? <div {...getTaskViewPanelProps(supplierTabsId, "products")}><SupplierProductLinksPanel client={client} supplierId={selectedSupplier.id} supplierName={selectedSupplier.name} canEdit={canEditProductLink} canDeactivate={canDeactivateProductLink} canDelete={canDeleteProductLink} /></div> : null}
    {view === "assessments" && canViewAssessments && selectedSupplier && !newSupplier ? <div {...getTaskViewPanelProps(supplierTabsId, "assessments")}><SupplierAssessmentsPanel businessDate={businessDate} client={client} supplierId={selectedSupplier.id} supplierName={selectedSupplier.name} canCreate={canCreateAssessment} canEdit={canEditAssessment} canApprove={canApproveAssessment} canDelete={canDeleteAssessment} /></div> : null}
  </section>;
}

function text(form: FormData, name: string) { return String(form.get(name) ?? "").trim(); }
