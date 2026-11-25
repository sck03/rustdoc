import { useQueryClient } from "@tanstack/react-query";
import { largeDocumentQueryOptions } from "../../api/queryRetention.ts";
import { useSupplierProfile } from "./useSupplierProfile.ts";
import { SupplierContactsWorkspace } from "./SupplierContactsWorkspace.tsx";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useScopedConfirmation } from "../../ui/useScopedConfirmation.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { FilterBar } from "../../ui/primitives/Field.tsx";
import { useEffect, useMemo } from "react";
import { useFileImportWorkspace } from "../../ui/useFileImportWorkspace.ts";
import { DirectoryImportPreview } from "../../ui/DirectoryImportPreview.tsx";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { currentLocalDateInputValue, readApiError } from "../../ui/formUtils.ts";
import { SupplierProductLinksPanel } from "./SupplierProductLinksPanel.tsx";
import { SupplierAssessmentsPanel } from "./SupplierAssessmentsPanel.tsx";
import { SupplierAssessmentOverview } from "./SupplierAssessmentOverview.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, successFeedback } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteChoice, readRouteId } from "../../ui/routeQueryState.ts";
import { useDirectoryLocation } from "../../ui/useDirectoryLocation.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
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
  const cache = useQueryClient();
  const { params, update: updateRoute } = useRouteQuery();
  const supplierId = readRouteId(params.get("supplierId")) ?? 0;
  const newSupplier = params.get("new") === "true";
  const setNewSupplier = (value: boolean) => updateRoute({ new: value });
  const { keywordInput, setKeywordInput, keyword, setKeyword, status, setStatus, pageNumber, setPageNumber, pageSize, setPageSize } = useDirectoryLocation();
  const view = readRouteChoice<SupplierTaskView>(params.get("view"), ["overview", "directory", "profile", "contacts", "products", "assessments", "import"], "directory");
  const setView = (value: SupplierTaskView) => updateRoute({ view: value }, false);
  const discard = useConfirmUnsavedChanges();
  const confirmationScope = useMemo(() => ({ client, supplierId, newSupplier, view }), [client, supplierId, newSupplier, view]);
  const confirmDiscardChanges = useScopedConfirmation(confirmationScope, discard);
  const { query: supplierQuery, draft: supplierDraft, command: supplierCommand, feedback, setFeedback, save: saveSupplier } = useSupplierProfile({
    client, supplierId, newSupplier, view,
    access: { create: canCreateSupplier, edit: canEditSupplier, admit: canAdmitSupplier, deactivate: canDeactivateSupplier, delete: canDeleteSupplier },
    confirmDiscard: confirmDiscardChanges,
    onSelect: (id, deleted) => updateRoute({ supplierId: id || null, new: false, ...(deleted ? { view: "directory" } : {}) }),
  });
  const { preview: importPreview, busy, importing, feedback: importFeedback, select: previewImport, commit: confirmImport } = useFileImportWorkspace({
    owner: client, enabled: canImportSupplier && view === "import",
    preview: (file, signal) => client.previewSupplierImport({ fileName: file.name, body: file }, { signal }),
    commit: (value, signal) => client.importSuppliers({ body: { previewId: value.previewId } }, { signal }),
    describe: result => `已导入 ${result.createdSuppliers} 家供应商、${result.createdContacts} 位联系人，跳过 ${result.skippedRows} 行。`,
    onImported: async () => { await cache.invalidateQueries({ queryKey: ["suppliers"] }); },
  });
  const selectedSupplier = supplierDraft.record;
  useUnsavedChangesGuard({ isDirty: supplierDraft.isDirty, message: "当前供应商资料有未保存的修改。" });
  useEffect(() => { supplierDraft.reset(); }, [view]);

  async function changeView(nextView: SupplierTaskView) {
    if (nextView === view) return true;
    if (!await confirmDiscardChanges("切换供应商工作区")) return false;
    supplierDraft.reset();
    if (view === "profile" && newSupplier) setNewSupplier(false);
    setView(nextView);
    return true;
  }

  async function beginNewSupplier() {
    if (!await confirmDiscardChanges("新建供应商")) return;
    supplierDraft.reset();
    updateRoute({ supplierId: null, new: true, view: "profile" }, false);
  }

  async function openSupplier(id: number, nextView = "profile") {
    if (!await confirmDiscardChanges("打开供应商")) return;
    supplierDraft.reset();
    updateRoute({ supplierId: id, new: false, view: nextView }, false);
  }

  const pageQuery = usePagedDirectoryQuery(
    ["suppliers", keyword, status, pageNumber, pageSize],
    (signal) => client.querySuppliers({ keyword, status, pageNumber, pageSize }, { signal }),
    view === "directory", largeDocumentQueryOptions,
  );
  const page = pageQuery.data ?? null;

  useServerPageNumber(pageQuery, pageNumber, setPageNumber, pageSize);
  const exportCommand = useScopedMutation<void>(JSON.stringify([view, keyword, status]), client, {
    mutationFn: async (_, signal, { checkCurrent }) => {
      if (!canExportSupplier) return;
      const blob = await client.exportSuppliers({ keyword, status }, { signal });
      checkCurrent(); downloadBlob(blob, `suppliers-${currentLocalDateInputValue()}.xlsx`);
    },
    onSuccess: () => setFeedback(successFeedback("供应商 Excel 已生成。")),
    onError: error => setFeedback(errorFeedback(readApiError(error))),
  });

  return <ListWorkspace >
    <OperationFeedback feedback={view === "import" ? importFeedback : feedback ?? (supplierQuery.isError ? errorFeedback(readApiError(supplierQuery.error)) : null)} />
    {view === "profile" && supplierDraft.hasPendingServerVersion ? <ServerDraftUpdateNotice
      entityLabel="供应商"
      onKeepLocal={supplierDraft.keepLocalDraft}
      onLoadServer={() => { void (async () => {
        if (!await confirmDiscardChanges("载入服务器版本")) return;
        supplierDraft.loadServerVersion();
      })(); }}
    /> : null}
    {!canCreateSupplier && !canEditSupplier && !canCreateContact && !canEditContact && !canEditProductLink && !canCreateAssessment && !canEditAssessment
      ? <PermissionNotice>当前岗位只有供应商业务查看权限；档案、联系人、供货关系、评价、导入和导出分别授权。</PermissionNotice>
      : null}
    {view === "directory" || view === "overview" ? <TaskViewTabs idPrefix={supplierTabsId} value={view} label="供应商工作区" onChange={changeView} items={[
      { id: "directory", label: "供应商目录" }, ...(canViewAssessments ? [{ id: "overview" as const, label: "采购概览" }] : []),
    ]} /> : <PageHeader title={newSupplier ? "新建供应商" : view === "import" ? "导入供应商" : selectedSupplier?.name ?? "供应商详情"}
      actions={<button className="secondary-button" type="button" onClick={() => void changeView("directory")}>返回供应商目录</button>} />}
    {supplierId > 0 && !newSupplier && !["directory", "overview", "import"].includes(view) && <TaskViewTabs idPrefix={supplierTabsId} value={view} label="供应商详情" onChange={changeView} items={[
      { id: "profile", label: "基本资料" }, ...(canViewContacts ? [{ id: "contacts" as const, label: "联系人" }] : []),
      ...(canViewProducts ? [{ id: "products" as const, label: "供应产品" }] : []), ...(canViewAssessments ? [{ id: "assessments" as const, label: "评价" }] : []),
    ]} />}
    {!newSupplier && ["profile", "contacts", "products", "assessments"].includes(view) && !selectedSupplier && <PageState tone={feedback ? "error" : supplierId ? "loading" : "empty"} title={supplierId ? "正在定位供应商资料" : "请从目录选择供应商"} />}
    {view === "overview" && canViewAssessments ? <div {...getTaskViewPanelProps(supplierTabsId, "overview")}><SupplierAssessmentOverview client={client} onOpenSupplier={id => openSupplier(id, "assessments")} /></div> : null}
    {view === "import" && canImportSupplier ? <section className="form-section" aria-label="导入供应商"><div className="section-header"><h3>导入供应商</h3><span>CSV/XLSX 最多 5000 行、10 MB</span></div>
      <div className="form-actions">
        {canImportSupplier ? <label className="secondary-button">选择导入文件<input type="file" hidden disabled={importing} accept=".csv,.xlsx,.xlsm" onChange={(event) => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; previewImport(file); }} /></label> : null}
        {canImportSupplier ? <button className="primary-button" type="button" disabled={busy || !importPreview?.validRows} onClick={() => void confirmImport()}>确认导入有效行</button> : null}
      </div>
      {importPreview ? <><p>共 {importPreview.totalRows} 行，有效 {importPreview.validRows} 行，重复 {importPreview.duplicateRows} 行。新供应商以考察中状态导入。</p>
        <DirectoryImportPreview key={importPreview.previewId} rows={importPreview.rows} supplier busy={busy} /></> : null}
    </section> : null}
    {view === "directory" ? <section className="form-section" {...getTaskViewPanelProps(supplierTabsId, "directory")}>
      <PageHeader title="供应商目录" count={page?.totalCount} unit="家" actions={<>
        {canImportSupplier && <button className="secondary-button" type="button" onClick={() => void changeView("import")}>导入供应商</button>}
        {canExportSupplier && <button className="secondary-button" type="button" disabled={exportCommand.isPending} onClick={() => exportCommand.mutate()}>导出当前筛选</button>}
        {canCreateSupplier && <button className="primary-button" type="button" onClick={() => void beginNewSupplier()}>新建供应商</button>}
      </>}>
      <FilterBar className="list-workspace-filters" label="筛选供应商目录" onSubmit={(event) => { event.preventDefault(); setKeyword(keywordInput.trim()); setPageNumber(1); }}>
        <input aria-label="搜索供应商目录" value={keywordInput} onChange={(event) => setKeywordInput(event.target.value)} placeholder="搜索名称、分类、产品或国家" />
        <select aria-label="供应商状态" value={status} onChange={(event) => { setStatus(event.target.value); setPageNumber(1); }}><option value="">全部状态</option><option>合作中</option><option>考察中</option><option>暂停</option><option>停用</option></select>
        <button className="secondary-button" type="submit">搜索</button>
      </FilterBar>
      </PageHeader>
      {pageQuery.isError ? <OperationFeedback feedback={errorFeedback(readApiError(pageQuery.error))} /> : null}
      <ResponsiveTableFrame label="供应商目录" mobileLayout="scroll" busy={pageQuery.isFetching}><table className="data-table responsive-data-table"><thead><tr><th>供应商</th><th data-table-priority="secondary">分类</th><th data-table-priority="secondary">主要产品</th><th>状态</th><th /></tr></thead><tbody>
        {(page?.items ?? []).map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.category || "-"}</td><td data-table-priority="secondary">{item.mainProducts || "-"}</td><td><BusinessStatusBadge value={item.status} /></td><td><button className="secondary-button" type="button" onClick={() => void openSupplier(item.id)}>打开</button></td></tr>)}
        {!pageQuery.isFetching && !pageQuery.isError && !page?.items.length ? <tr><td className="empty-cell" colSpan={5}><div className="empty-cell-content"><strong>暂无供应商</strong><span>{canCreateSupplier ? "先建立供应商资料，再按需添加联系人和关联供应产品。" : "当前没有可查看的供应商。"}</span>{canCreateSupplier ? <div className="form-actions"><button className="secondary-button" type="button" onClick={() => void beginNewSupplier()}>建立第一家供应商</button>{canImportSupplier ? <button className="secondary-button" type="button" onClick={() => void changeView("import")}>从文件导入</button> : null}</div> : null}</div></td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
      <Pagination pageNumber={pageNumber} totalPages={page?.totalPages ?? 1} totalCount={page?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,30,50,100]} isBusy={pageQuery.isFetching} onPageChange={setPageNumber} onPageSizeChange={(value) => { setPageSize(value); setPageNumber(1); }} />
    </section> : null}
    {view === "profile" && (newSupplier || selectedSupplier) ? <form className="form-grid" onSubmit={saveSupplier} {...(newSupplier ? { "aria-label": "新建供应商" } : getTaskViewPanelProps(supplierTabsId, "profile"))}>
        <div className="section-heading-row"><h3>{newSupplier ? "新建供应商" : canEditSupplier ? "供应商资料" : "查看供应商"}</h3></div>
        <fieldset className="permission-fieldset form-field-wide" disabled={supplierCommand.isEditingLocked || (newSupplier ? !canCreateSupplier : !canEditSupplier)}>
        <label>名称<input name="name" required {...supplierDraft.field("name")} /></label><label>国家/地区<input name="countryRegion" {...supplierDraft.field("countryRegion")} /></label>
        <label>分类<input name="category" {...supplierDraft.field("category")} /></label><label>网站<input name="website" {...supplierDraft.field("website")} /></label>
        {!newSupplier ? <label>当前状态<input value={selectedSupplier?.status ?? ""} readOnly /></label> : null}
        <label className="form-field-wide">主要产品<input name="mainProducts" {...supplierDraft.field("mainProducts")} /></label><label className="form-field-wide">备注<textarea name="notes" {...supplierDraft.field("notes")} /></label>
        </fieldset>
        <div className="form-actions">{(newSupplier ? canCreateSupplier : canEditSupplier) ? <button className="primary-button" type="submit" disabled={supplierCommand.isPending}>保存供应商</button> : null}{!newSupplier && selectedSupplier?.status === "考察中" && canAdmitSupplier ? <button className="secondary-button" type="button" disabled={supplierCommand.isPending} onClick={() => void supplierCommand.mutate("admit")}>确认准入</button> : null}{!newSupplier && selectedSupplier && selectedSupplier.status !== "停用" && canDeactivateSupplier ? <button className="secondary-button" type="button" disabled={supplierCommand.isPending} onClick={() => void supplierCommand.mutate("deactivate")}>停用供应商</button> : null}{!newSupplier && selectedSupplier && (selectedSupplier.status === "停用" || selectedSupplier.status === "暂停") && canDeactivateSupplier ? <button className="secondary-button" type="button" disabled={supplierCommand.isPending} onClick={() => void supplierCommand.mutate("restore")}>恢复考察</button> : null}{!newSupplier && selectedSupplier && canViewContacts ? <button className="secondary-button" type="button" onClick={() => void changeView("contacts")}>{canCreateContact || canEditContact ? "管理联系人" : "查看联系人"}</button> : null}{!newSupplier && selectedSupplier && canDeleteSupplier ? <button className="secondary-button danger-button" type="button" disabled={supplierCommand.isPending} onClick={() => void supplierCommand.mutate("delete")}>删除</button> : null}</div>
      </form> : null}
    {view === "contacts" && canViewContacts && selectedSupplier && !newSupplier && <SupplierContactsWorkspace
      key={selectedSupplier.id} client={client} selectedSupplier={selectedSupplier} canCreateContact={canCreateContact}
      canEditContact={canEditContact} canSetPrimaryContact={canSetPrimaryContact} canDeleteContact={canDeleteContact} changeView={changeView} />}
    {view === "products" && canViewProducts && selectedSupplier && !newSupplier ? <div {...getTaskViewPanelProps(supplierTabsId, "products")}><SupplierProductLinksPanel key={selectedSupplier.id} client={client} supplierId={selectedSupplier.id} supplierName={selectedSupplier.name} canEdit={canEditProductLink} canDeactivate={canDeactivateProductLink} canDelete={canDeleteProductLink} /></div> : null}
    {view === "assessments" && canViewAssessments && selectedSupplier && !newSupplier ? <div {...getTaskViewPanelProps(supplierTabsId, "assessments")}><SupplierAssessmentsPanel key={selectedSupplier.id} businessDate={businessDate} client={client} supplierId={selectedSupplier.id} supplierName={selectedSupplier.name} canCreate={canCreateAssessment} canEdit={canEditAssessment} canApprove={canApproveAssessment} canDelete={canDeleteAssessment} /></div> : null}
  </ListWorkspace>;
}
