import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Plus, RefreshCw, Search } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useBusinessAttachments } from "./useBusinessAttachments.ts";
import { attachmentVersionLabel, fileSizeLabel } from "./attachmentModel.ts";
import { BusinessAttachmentUploadForm } from "./BusinessAttachmentUploadForm.tsx";
import { BusinessAttachmentDetails } from "./BusinessAttachmentDetails.tsx";
import { BusinessAttachmentPreview } from "./BusinessAttachmentPreview.tsx";
import { BusinessAttachmentCategoryManager } from "./BusinessAttachmentCategoryManager.tsx";
import "../../styles/routes/business-records.css";

export function BusinessAttachmentsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const route = useParams();
  const invoiceId = route.invoiceId && /^[1-9]\d*$/.test(route.invoiceId) ? Number(route.invoiceId) : undefined;
  if (route.invoiceId && (!invoiceId || !Number.isSafeInteger(invoiceId) || invoiceId > 2147483647)) return <PageState tone="error" title="发票编号无效" />;
  return <BusinessAttachmentWorkspace key={invoiceId ?? "all"} client={client} user={user} invoiceId={invoiceId} />;
}

function BusinessAttachmentWorkspace({ client, user, invoiceId }: { client: ExportDocManagerApiClient; user: ApiUserDto; invoiceId?: number }) {
  const model = useBusinessAttachments(client, invoiceId, user.id);
  const { uploadMode, preview } = model;
  const [managingCategories, setManagingCategories] = useState(false);
  const [editingMetadata, setEditingMetadata] = useState(false);
  const catalog = model.categories.data;
  const categories = catalog?.items ?? [];
  const editorOpen = uploadMode !== null || editingMetadata || managingCategories;
  const data = model.query.data;
  const selected = model.details.data;
  const uploadAttachment = uploadMode === "revision" ? selected?.attachment : undefined;
  const uploadInvoiceId = uploadMode === "new" ? invoiceId : uploadAttachment?.invoiceId;
  return <section className="work-surface business-records" aria-label="业务资料归档">
    <header className="business-records-heading"><div><h2>{invoiceId ? `${model.invoice.data?.invoiceNo ?? "单据"} · 业务资料` : "业务资料归档"}</h2>
      <p>保留客户原始资料、确认文件和实际交付文件，新版本不会覆盖旧文件。</p></div>
      <div className="business-records-actions">{invoiceId && <Link className="command-button secondary" to={`/invoices/${invoiceId}`}>返回单据</Link>}
        {catalog?.canManage && <button type="button" className="command-button secondary" disabled={model.busy || editorOpen} onClick={() => setManagingCategories(true)}>管理分类</button>}
        {data?.canUpload && <button type="button" className="command-button" disabled={model.busy || editorOpen || !catalog} onClick={() => model.setUploadMode("new")}><Plus size={16} aria-hidden="true" />上传资料</button>}</div></header>
    {data?.usedBytes != null && <p className="business-records-muted">本单已使用 {fileSizeLabel(data.usedBytes)} / {fileSizeLabel(data.invoiceBytesLimit)}，包含所有历史版本与停用资料。资料随数据库备份保存。</p>}
    {model.error && <InlineNotice tone="error" title="操作未完成">{model.error}</InlineNotice>}
    {model.message && <InlineNotice tone="success">{model.message}</InlineNotice>}
    {model.categories.isError && <InlineNotice tone="error" title="分类加载失败" action={<button className="command-button secondary" type="button" onClick={() => void model.categories.refetch()}>重新加载分类</button>}>{readApiError(model.categories.error)}</InlineNotice>}
    {managingCategories && catalog?.canManage && <BusinessAttachmentCategoryManager items={categories} busy={model.busy}
      onSave={model.saveCategory} onDelete={model.removeCategory} onClose={() => setManagingCategories(false)} />}
    {uploadMode && data && uploadInvoiceId !== undefined && <BusinessAttachmentUploadForm key={uploadMode}
      invoiceId={uploadInvoiceId} attachment={uploadAttachment} categories={categories} maximumBytes={data.fileBytesLimit} busy={model.busy}
      onUpload={model.upload} onClose={() => model.setUploadMode(null)} />}
    <div className="business-records-content" hidden={model.selectedId !== null}>
    <form className="toolbar business-records-toolbar" role="search" onSubmit={(event) => { event.preventDefault(); model.commitSearch(); }}>
      <div className="search-form"><Search size={17} aria-hidden="true" />
        <input aria-label="搜索业务资料" placeholder="客户、单据号、PO、款号、资料名或文件名" value={model.keyword} maxLength={100} onChange={(event) => model.changeKeyword(event.target.value)} /></div>
      <div className="toolbar-actions">
        <button className="command-button secondary" type="submit">搜索</button>
        <label className="inline-check"><input type="checkbox" checked={model.includeArchived} onChange={(event) => model.changeArchived(event.target.checked)} />含停用资料</label>
        <button className="icon-button" type="button" aria-label="刷新业务资料" title="刷新业务资料" disabled={model.query.isFetching} onClick={model.refresh}><RefreshCw size={16} aria-hidden="true" /></button>
      </div>
    </form>
    {model.query.isPending ? <PageState tone="loading" title="正在读取业务资料" /> : model.query.isError ?
      <PageState tone="error" title="资料加载失败" description={readApiError(model.query.error)} /> : <>
        {!data?.page.items.length ? <PageState title="没有符合条件的业务资料" description={invoiceId ? "可从本单上传客户资料或实际交付文件。" : "请打开对应发票，从“业务资料”入口上传。"} /> :
        <ul className="business-records-list">{data.page.items.map((item) => <li key={item.id} className="business-records-card">
          <div><span className="business-records-muted">{item.invoiceNo} · {item.invoiceType} · {item.categoryName}</span>
            <h3>{item.title}</h3><p>{item.customerName}{item.poNumber ? ` · PO ${item.poNumber}` : ""}{item.styleNo ? ` · 款号 ${item.styleNo}` : ""}</p>
            <p className="business-records-muted">{attachmentVersionLabel(item)}</p></div>
          <button type="button" className="command-button secondary" disabled={model.busy || editorOpen} onClick={() => model.setSelectedId(item.id)}>查看版本</button>
        </li>)}</ul>}
        <ListPaginationControls pageNumber={model.pageNumber} pageSize={model.pageSize} totalCount={data?.page.totalCount ?? 0} totalPages={data?.page.totalPages ?? 0}
          pageSizeOptions={[20, 50, 100]} isBusy={model.query.isFetching} onPageChange={model.setPageNumber} onPageSizeChange={model.changePageSize} />
      </>}
    </div>
    {model.selectedId !== null && (model.details.isPending ? <PageState tone="loading" title="正在读取版本" /> : model.details.isError ?
      <PageState tone="error" title="版本读取失败" description={readApiError(model.details.error)} /> : selected && <BusinessAttachmentDetails key={selected.attachment.id}
        details={selected} categories={categories} editing={editingMetadata} onEditingChange={setEditingMetadata}
        busy={model.busy || uploadMode !== null || managingCategories || !catalog} timeZone={user.businessTimeZone} onClose={() => model.setSelectedId(null)}
        onEdit={model.editMetadata} onDelete={model.remove} onReplace={() => model.setUploadMode("revision")} onRead={(version, show) => void model.read(version, show)} onUpdate={model.update} />)}
    {preview && <BusinessAttachmentPreview blob={preview.blob} name={preview.name} onClose={() => model.setPreview(null)} />}
  </section>;
}
