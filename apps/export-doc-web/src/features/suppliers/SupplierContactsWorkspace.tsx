import type { ApiSupplierDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useSupplierContacts } from "./useSupplierContacts.ts";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";
import { OperationFeedback, requestErrorFeedback } from "../../ui/OperationFeedback.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";

export function SupplierContactsWorkspace({ client, selectedSupplier, canCreateContact, canEditContact, canSetPrimaryContact, canDeleteContact, changeView }: {
  client: ExportDocManagerApiClient; selectedSupplier: ApiSupplierDto;
  canCreateContact: boolean; canEditContact: boolean; canSetPrimaryContact: boolean; canDeleteContact: boolean;
  changeView: (view: "profile") => Promise<boolean>;
}) {
  const supplierId = selectedSupplier.id;
  const { query, draft: contactDraft, selection, feedback, command, pageNumber: contactPageNumber, pageSize: contactPageSize,
    setPageNumber: setContactPageNumber, setPageSize: setContactPageSize, select, save, loadServer } = useSupplierContacts(client, supplierId,
      { create: canCreateContact, edit: canEditContact, setPrimary: canSetPrimaryContact, delete: canDeleteContact });
  const contactView = selection === undefined ? "directory" : "editor";
  const selectedContact = contactDraft.record;
  const contacts = query.data?.items ?? [];
  const contactTotalCount = query.data?.totalCount ?? 0, contactTotalPages = query.data?.totalPages ?? 1;
  return <section className="form-section supplier-contact-workspace" {...getTaskViewPanelProps("supplier-directory-workspace", "contacts")}>
    <OperationFeedback feedback={feedback ?? (query.isError ? requestErrorFeedback(query.error) : null)} />
    {contactDraft.hasPendingServerVersion && <ServerDraftUpdateNotice entityLabel="联系人" onKeepLocal={contactDraft.keepLocalDraft} onLoadServer={() => void loadServer()} />}
      <div className="section-header"><div><h3>{contactView === "directory" ? "供应商联系人目录" : selectedContact ? "编辑供应商联系人" : "新增供应商联系人"}</h3></div><span>{contactTotalCount} 位</span></div>
      {contactView === "directory" ? <>
        <div className="section-header-actions supplier-contact-directory-actions"><button className="secondary-button" type="button" onClick={() => void changeView("profile")}>返回供应商资料</button>{canCreateContact ? <button className="primary-button" type="button" onClick={() => void select(null)}>新增联系人</button> : null}</div>
        <ResponsiveTableFrame label="供应商联系人" mobileLayout="scroll" busy={query.isFetching}><table className="data-table responsive-data-table"><thead><tr><th>联系人</th><th data-table-priority="secondary">职位</th><th>邮箱</th><th data-table-priority="secondary">电话</th><th>类型</th><th /></tr></thead><tbody>
          {contacts.map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.title || "-"}</td><td><TablePrimaryText value={item.email} /></td><td data-table-priority="secondary">{item.phone || "-"}</td><td>{item.isPrimary ? <BusinessStatusBadge value="主要联系人" /> : "普通联系人"}</td><td><button className="secondary-button" type="button" onClick={() => void select(item)}>{canEditContact ? "编辑" : "查看"}</button></td></tr>)}
          {!query.isFetching && !query.isError && !contacts.length ? <tr><td className="empty-cell" colSpan={6}><div className="empty-cell-content"><strong>尚未建立供应商联系人</strong><span>{canCreateContact ? "需要记录询价、交期或付款沟通对象时，再添加联系人。" : "当前供应商还没有联系人。"}</span>{canCreateContact ? <button className="primary-button" type="button" onClick={() => void select(null)}>添加第一位联系人</button> : null}</div></td></tr> : null}
        </tbody></table></ResponsiveTableFrame>
        <Pagination pageNumber={contactPageNumber} totalPages={contactTotalPages} totalCount={contactTotalCount} pageSize={contactPageSize} pageSizeOptions={[20, 50, 100]} isBusy={query.isFetching} onPageChange={setContactPageNumber} onPageSizeChange={(value) => { setContactPageSize(value); setContactPageNumber(1); }} />
      </> : <form className="form-grid" onSubmit={save}>
        <div className="section-heading-row"><h4>{selectedContact ? "编辑联系人资料" : "新增联系人资料"}</h4><button className="secondary-button" type="button" onClick={() => void select(undefined)}>返回联系人目录</button></div>
        <div className="form-field-wide context-strip"><strong>{selectedSupplier.name}</strong></div>
        <fieldset className="permission-fieldset form-field-wide" disabled={command.isPending || (selectedContact ? !canEditContact : !canCreateContact)}>
        <label>姓名<input name="contactName" required {...contactDraft.field("name")} /></label><label>职位<input name="title" {...contactDraft.field("title")} /></label><label>邮箱<input name="email" type="email" {...contactDraft.field("email")} /></label>
        <label>电话<input name="phone" {...contactDraft.field("phone")} /></label><label>即时通讯<input name="instantMessaging" {...contactDraft.field("instantMessaging")} /></label>{selectedContact ? <label>联系人角色<input value={selectedContact.isPrimary ? "主要联系人" : "普通联系人"} readOnly /></label> : null}
        </fieldset>
        <div className="form-actions">{(selectedContact ? canEditContact : canCreateContact) ? <button className="primary-button" type="submit" disabled={!supplierId || command.isPending}>保存联系人</button> : null}{selectedContact && !selectedContact.isPrimary && canSetPrimaryContact ? <button className="secondary-button" type="button" disabled={command.isPending} onClick={() => void command.mutate("primary")}>设为主要联系人</button> : null}{selectedContact && canDeleteContact ? <button className="secondary-button danger-button" type="button" disabled={command.isPending} onClick={() => void command.mutate("delete")}>删除</button> : null}</div>
      </form>}
    </section>;
}
