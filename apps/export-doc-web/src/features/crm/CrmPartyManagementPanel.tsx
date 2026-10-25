import { useEffect, useState, type FormEvent } from "react";
import type { ApiCrmContactDto, ApiCrmCustomerDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { useVersionedRecordDraft } from "../../ui/useVersionedRecordDraft.ts";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { readApiError } from "../../ui/formUtils.ts";

type Props = {
  section: "profile" | "contacts";
  client: ExportDocManagerApiClient;
  customers: ApiCrmCustomerDto[];
  contacts: ApiCrmContactDto[];
  customerId: number;
  onSelectCustomer: (id: number) => void;
  onReloadCustomers: (preferred?: ApiCrmCustomerDto, signal?: AbortSignal) => Promise<void>;
  onReloadContacts: (preferred?: ApiCrmContactDto, signal?: AbortSignal) => Promise<void>;
  onFeedback: (feedback: OperationFeedbackState) => void;
  canCreateCustomer: boolean;
  canEditCustomer: boolean;
  canDeactivateCustomer: boolean;
  canDeleteCustomer: boolean;
  canCreateContact: boolean;
  canEditContact: boolean;
  canSetPrimaryContact: boolean;
  canDeleteContact: boolean;
};

export function CrmPartyManagementPanel(props: Props) {
  const requestConfirmation = useConfirmation();
  const runAbortableOperation = useAbortableOperation();
  const {
    client, customers, contacts, customerId, onReloadCustomers, onReloadContacts, onFeedback,
    canCreateCustomer, canEditCustomer, canDeactivateCustomer, canDeleteCustomer, canCreateContact, canEditContact,
    canSetPrimaryContact, canDeleteContact,
  } = props;
  const isNewCustomer = customerId === 0;
  const [contactId, setContactId] = useState(0);
  const customerDraft = useVersionedRecordDraft(`customer:${customerId}`, customers.find((item) => item.id === customerId), ["name", "countryRegion", "website", "source", "notes"]);
  const selectedCustomer = customerDraft.record;
  const activeCustomerId = isNewCustomer ? 0 : customerId;
  const contactDraft = useVersionedRecordDraft(`customer:${customerId}:contact:${contactId}`, activeCustomerId ? contacts.find((item) => item.id === contactId) : undefined, ["name", "title", "email", "phone", "instantMessaging"]);
  const customerDraftDirty = customerDraft.isDirty, contactDraftDirty = contactDraft.isDirty;
  const customerSave = useScopedSave(`customer:${customerId}:${props.section}`, client);
  const contactSave = useScopedSave(`customer:${customerId}:contact:${contactId}:${props.section}`, client);
  const selectedContact = contactDraft.record;
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: customerDraftDirty || contactDraftDirty,
    message: "当前客户或联系人资料有未保存的修改。",
  });
  useEffect(() => { customerDraft.reset(); contactDraft.reset(); }, [props.section]);

  useEffect(() => {
    if (contactDraftDirty) return;
    setContactId((current) => contacts.some((item) => item.id === current) ? current : contacts[0]?.id ?? 0);
  }, [customerId, contacts]);

  async function confirmAndResetDrafts(actionLabel: string) {
    if (!(customerDraftDirty || contactDraftDirty)) return true;
    if (!await confirmDiscardChanges(actionLabel)) return false;
    customerDraft.reset();
    contactDraft.reset();
    return true;
  }

  async function beginNewContact() {
    if (!await confirmAndResetDrafts("新建联系人")) return;
    setContactId(0);
  }

  async function selectContact(nextContactId: number) {
    if (nextContactId === contactId) return;
    if (!await confirmAndResetDrafts("切换联系人")) return;
    setContactId(nextContactId);
  }

  async function saveCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isNewCustomer ? !canCreateCustomer : !canEditCustomer) return;
    const submitted = { ...customerDraft.values };
    const id = isNewCustomer ? 0 : selectedCustomer?.id ?? 0;
    try {
      const body = {
        id,
        ...submitted,
        linkedDocumentCustomerId: selectedCustomer?.linkedDocumentCustomerId,
        expectedVersion: id > 0 ? selectedCustomer?.versionNumber ?? 0 : 0,
      };
      await customerSave.run(async ({ signal, checkCurrent, continueInScope }) => {
        const saved = id > 0 ? await client.updateCrmCustomer({ id, body }, { signal }) : await client.createCrmCustomer({ body }, { signal });
        checkCurrent(); continueInScope(`customer:${saved.id}:${props.section}`);
        customerDraft.acceptSaved(saved, submitted, `customer:${saved.id}`);
        onFeedback(successFeedback(id > 0 ? "CRM 客户已更新。" : "CRM 客户已建立；单证客户资料未被修改。"));
        try { await onReloadCustomers(saved, signal); }
        catch (error) { checkCurrent(); onFeedback(warningFeedback(`客户已保存，但目录刷新失败：${readApiError(error)}`)); }
      });
    } catch (error) {
      if (!isAbortError(error)) onFeedback(requestErrorFeedback(error));
    }
  }

  async function changeCustomerAvailability(restore: boolean) {
    if (!canDeactivateCustomer || !selectedCustomer) return;
    const action = restore ? "恢复" : "停用";
    if (!await requestConfirmation({
      title: `${action} CRM 客户`,
      description: `确定${action}客户“${selectedCustomer.name}”吗？`,
      details: restore ? ["恢复后客户状态将变为跟进中。"] : ["停用不会删除跟进或商机历史。"],
      confirmLabel: `确认${action}`,
      tone: restore ? undefined : "danger",
    }) || !await confirmDiscardChanges(`${action}客户`)) return;
    try {
      const saved = await runAbortableOperation((signal) => restore
        ? client.restoreCrmCustomer(
          { id: selectedCustomer.id, body: { expectedVersion: selectedCustomer.versionNumber } },
          { signal },
        )
        : client.deactivateCrmCustomer(
          { id: selectedCustomer.id, body: { expectedVersion: selectedCustomer.versionNumber } },
          { signal },
        ));
      await onReloadCustomers(saved);
      customerDraft.reset();
      onFeedback(successFeedback(restore ? "CRM 客户已恢复为跟进中。" : "CRM 客户已停用，历史记录保持不变。"));
    } catch (error) {
      if (!isAbortError(error)) onFeedback(requestErrorFeedback(error));
    }
  }

  async function deleteCustomer() {
    if (!canDeleteCustomer || !selectedCustomer || !await requestConfirmation({ title: "删除 CRM 客户", description: `确定删除 CRM 客户“${selectedCustomer.name}”吗？`, details: ["存在业务引用时服务端会拒绝删除。"], confirmLabel: "确认删除", tone: "danger" }) || !await confirmDiscardChanges("删除客户")) return;
    try {
      await runAbortableOperation((signal) => client.deleteCrmCustomer(
        { id: selectedCustomer.id, expectedVersion: selectedCustomer.versionNumber },
        { signal },
      ));
      await onReloadCustomers();
      customerDraft.reset();
      contactDraft.reset();
      onFeedback(successFeedback("CRM 客户已删除。"));
    } catch (error) {
      if (!isAbortError(error)) onFeedback(requestErrorFeedback(error));
    }
  }

  async function saveContact(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (selectedContact ? !canEditContact : !canCreateContact) return;
    if (!activeCustomerId) return;
    const submitted = { ...contactDraft.values };
    const id = selectedContact?.id ?? 0;
    const body = {
      id,
      crmCustomerId: activeCustomerId,
      ...submitted,
      expectedVersion: id > 0 ? selectedContact?.versionNumber ?? 0 : 0,
    };
    try {
      await contactSave.run(async ({ signal, checkCurrent, continueInScope }) => {
        const saved = id > 0 ? await client.updateCrmContact({ customerId: activeCustomerId, id, body }, { signal }) : await client.createCrmContact({ customerId: activeCustomerId, body }, { signal });
        checkCurrent(); continueInScope(`customer:${customerId}:contact:${saved.id}:${props.section}`);
        contactDraft.acceptSaved(saved, submitted, `customer:${customerId}:contact:${saved.id}`); setContactId(saved.id);
        onFeedback(successFeedback(id > 0 ? "联系人已更新。" : "联系人已添加。"));
        try { await onReloadContacts(saved, signal); }
        catch (error) { checkCurrent(); onFeedback(warningFeedback(`联系人已保存，但目录刷新失败：${readApiError(error)}`)); }
      });
    } catch (error) {
      if (!isAbortError(error)) onFeedback(requestErrorFeedback(error));
    }
  }

  async function setPrimaryContact() {
    if (!canSetPrimaryContact || !selectedContact || selectedContact.isPrimary) return;
    if (!await confirmDiscardChanges("设为主要联系人")) return;
    try {
      const saved = await runAbortableOperation((signal) => client.setPrimaryCrmContact({
        customerId: activeCustomerId,
        id: selectedContact.id,
        body: { expectedVersion: selectedContact.versionNumber },
      }, { signal }));
      await onReloadContacts();
      contactDraft.reset();
      setContactId(saved.id);
      onFeedback(successFeedback("主要联系人已切换。"));
    } catch (error) {
      if (!isAbortError(error)) onFeedback(requestErrorFeedback(error));
    }
  }

  async function deleteContact() {
    if (!canDeleteContact || !selectedContact || !await requestConfirmation({ title: "删除客户联系人", description: `确定删除联系人“${selectedContact.name}”吗？`, details: ["历史跟进记录仍会保留。"], confirmLabel: "确认删除", tone: "danger" }) || !await confirmDiscardChanges("删除联系人")) return;
    try {
      await runAbortableOperation((signal) => client.deleteCrmContact(
        { customerId: activeCustomerId, id: selectedContact.id, expectedVersion: selectedContact.versionNumber },
        { signal },
      ));
      setContactId(0);
      await onReloadContacts();
      contactDraft.reset();
      onFeedback(successFeedback("联系人已删除，历史跟进仍保留。"));
    } catch (error) {
      if (!isAbortError(error)) onFeedback(requestErrorFeedback(error));
    }
  }

  return (
    <div className="record-detail-content">
      {(props.section === "profile" ? customerDraft : contactDraft).hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel={props.section === "profile" ? "客户" : "联系人"}
        onKeepLocal={(props.section === "profile" ? customerDraft : contactDraft).keepLocalDraft}
        onLoadServer={() => { void (async () => {
          if (!await confirmDiscardChanges("载入服务器版本")) return;
          (props.section === "profile" ? customerDraft : contactDraft).loadServerVersion();
          customerDraft.reset();
          contactDraft.reset();
        })(); }}
      /> : null}
      {props.section === "profile" && <form className="form-grid" onSubmit={saveCustomer}>
        <div className="section-heading-row">
          <h3>{isNewCustomer ? "新建销售客户" : "客户资料"}</h3>
        </div>
        <fieldset className="permission-fieldset form-field-wide" disabled={isNewCustomer ? !canCreateCustomer : !canEditCustomer}>
        <label>客户名称<input name="name" required maxLength={200} {...customerDraft.field("name")} /></label>
        <label>国家/地区<input name="countryRegion" maxLength={100} {...customerDraft.field("countryRegion")} /></label>
        <label>网站<input name="website" maxLength={300} {...customerDraft.field("website")} /></label>
        {!isNewCustomer ? <label>当前状态<input value={selectedCustomer?.status ?? ""} readOnly /></label> : null}
        <label>来源<input name="source" maxLength={50} {...customerDraft.field("source")} /></label>
        <label className="form-field-wide">备注<textarea name="notes" maxLength={1000} {...customerDraft.field("notes")} /></label>
        </fieldset>
        <div className="form-actions">
          {(isNewCustomer ? canCreateCustomer : canEditCustomer) ? <button className="primary-button" type="submit" disabled={customerSave.isSaving}>{isNewCustomer ? "建立客户" : "保存客户"}</button> : null}
          {!isNewCustomer && selectedCustomer && canDeactivateCustomer && selectedCustomer.status !== "暂停" && selectedCustomer.status !== "已流失" ? <button className="secondary-button" type="button" disabled={customerSave.isSaving} onClick={() => void changeCustomerAvailability(false)}>停用客户</button> : null}
          {!isNewCustomer && selectedCustomer && canDeactivateCustomer && (selectedCustomer.status === "暂停" || selectedCustomer.status === "已流失") ? <button className="secondary-button" type="button" disabled={customerSave.isSaving} onClick={() => void changeCustomerAvailability(true)}>恢复客户</button> : null}
          {!isNewCustomer && selectedCustomer && canDeleteCustomer ? <button className="secondary-button danger-button" type="button" disabled={customerSave.isSaving} onClick={() => void deleteCustomer()}>删除客户</button> : null}
        </div>
      </form>}

      {props.section === "contacts" && <form className="form-grid" onSubmit={saveContact}>
        <div className="section-heading-row">
          <h3>{selectedContact ? "联系人资料" : "添加联系人"}</h3>
          {canCreateContact ? <button className="secondary-button" type="button" disabled={!activeCustomerId} onClick={() => void beginNewContact()}>新建</button> : null}
        </div>
        {isNewCustomer ? <div className="context-strip form-field-wide"><strong>先保存新客户</strong><span>保存后即可为该客户添加联系人。</span></div> : null}
        <label>选择联系人<select value={contactId} disabled={!activeCustomerId} onChange={(event) => void selectContact(Number(event.target.value))}>
          <option value={0}>新联系人</option>
          {contacts.map((item) => <option key={item.id} value={item.id}>{item.name}{item.isPrimary ? " · 主要" : ""}</option>)}
        </select></label>
        <fieldset className="permission-fieldset form-field-wide" disabled={(selectedContact ? !canEditContact : !canCreateContact) || !activeCustomerId}>
        <label>姓名<input name="contactName" required maxLength={100} {...contactDraft.field("name")} /></label>
        <label>职位<input name="title" maxLength={100} {...contactDraft.field("title")} /></label>
        <label>邮箱<input name="email" type="email" maxLength={200} {...contactDraft.field("email")} /></label>
        <label>电话<input name="phone" maxLength={100} {...contactDraft.field("phone")} /></label>
        <label>即时通讯<input name="instantMessaging" maxLength={100} {...contactDraft.field("instantMessaging")} /></label>
        {selectedContact ? <label>联系人角色<input value={selectedContact.isPrimary ? "主要联系人" : "普通联系人"} readOnly /></label> : null}
        </fieldset>
        <div className="form-actions">
          {(selectedContact ? canEditContact : canCreateContact) ? <button className="primary-button" type="submit" disabled={!activeCustomerId || contactSave.isSaving}>{selectedContact ? "保存联系人" : "添加联系人"}</button> : null}
          {selectedContact && !selectedContact.isPrimary && canSetPrimaryContact ? <button className="secondary-button" type="button" disabled={contactSave.isSaving} onClick={() => void setPrimaryContact()}>设为主要联系人</button> : null}
          {selectedContact && canDeleteContact ? <button className="secondary-button danger-button" type="button" disabled={contactSave.isSaving} onClick={() => void deleteContact()}>删除联系人</button> : null}
        </div>
      </form>}
    </div>
  );
}
