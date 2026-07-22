import { useEffect, useState, type FormEvent } from "react";
import type { ApiCrmContactDto, ApiCrmCustomerDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { requestErrorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";

type Props = {
  client: ExportDocManagerApiClient;
  customers: ApiCrmCustomerDto[];
  contacts: ApiCrmContactDto[];
  customerId: number;
  onSelectCustomer: (id: number) => void;
  onReloadCustomers: (preferred?: ApiCrmCustomerDto) => Promise<void>;
  onReloadContacts: () => Promise<void>;
  onFeedback: (feedback: OperationFeedbackState) => void;
  canOperate: boolean;
  canManage: boolean;
};

export function CrmPartyManagementPanel(props: Props) {
  const requestConfirmation = useConfirmation();
  const { client, customers, contacts, customerId, onSelectCustomer, onReloadCustomers, onReloadContacts, onFeedback, canOperate, canManage } = props;
  const selectedCustomer = customers.find((item) => item.id === customerId);
  const [isNewCustomer, setIsNewCustomer] = useState(false);
  const [contactId, setContactId] = useState(0);
  const selectedContact = contacts.find((item) => item.id === contactId);

  useEffect(() => {
    setIsNewCustomer(false);
    setContactId((current) => contacts.some((item) => item.id === current) ? current : contacts[0]?.id ?? 0);
  }, [customerId, contacts]);

  async function saveCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canOperate) return;
    const form = new FormData(event.currentTarget);
    const id = isNewCustomer ? 0 : selectedCustomer?.id ?? 0;
    try {
      const body = {
        id,
        name: String(form.get("name") ?? ""),
        countryRegion: String(form.get("countryRegion") ?? ""),
        website: String(form.get("website") ?? ""),
        status: String(form.get("status") ?? "潜在客户"),
        source: String(form.get("source") ?? ""),
        notes: String(form.get("notes") ?? ""),
        linkedDocumentCustomerId: selectedCustomer?.linkedDocumentCustomerId,
        expectedVersion: id > 0 ? selectedCustomer?.versionNumber ?? 0 : 0,
      };
      const saved = id > 0
        ? await client.updateCrmCustomer({ id, body })
        : await client.createCrmCustomer({ body });
      await onReloadCustomers(saved);
      setIsNewCustomer(false);
      onFeedback(successFeedback(id > 0 ? "CRM 客户已更新。" : "CRM 客户已建立；单证客户资料未被修改。"));
    } catch (error) { onFeedback(requestErrorFeedback(error)); }
  }

  async function deleteCustomer() {
    if (!canManage || !selectedCustomer || !await requestConfirmation({ title: "删除 CRM 客户", description: `确定删除 CRM 客户“${selectedCustomer.name}”吗？`, details: ["存在业务引用时服务端会拒绝删除。"], confirmLabel: "确认删除", tone: "danger" })) return;
    try {
      await client.deleteCrmCustomer({ id: selectedCustomer.id });
      await onReloadCustomers();
      onFeedback(successFeedback("CRM 客户已删除。"));
    } catch (error) { onFeedback(requestErrorFeedback(error)); }
  }

  async function saveContact(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canOperate) return;
    if (!customerId) return;
    const form = new FormData(event.currentTarget);
    const id = selectedContact?.id ?? 0;
    const body = {
      id,
      crmCustomerId: customerId,
      name: String(form.get("contactName") ?? ""),
      title: String(form.get("title") ?? ""),
      email: String(form.get("email") ?? ""),
      phone: String(form.get("phone") ?? ""),
      instantMessaging: String(form.get("instantMessaging") ?? ""),
      isPrimary: form.get("isPrimary") === "on",
      expectedVersion: id > 0 ? selectedContact?.versionNumber ?? 0 : 0,
    };
    try {
      const saved = id > 0
        ? await client.updateCrmContact({ customerId, id, body })
        : await client.createCrmContact({ customerId, body });
      await onReloadContacts();
      setContactId(saved.id);
      onFeedback(successFeedback(id > 0 ? "联系人已更新。" : "联系人已添加。"));
    } catch (error) { onFeedback(requestErrorFeedback(error)); }
  }

  async function deleteContact() {
    if (!canManage || !selectedContact || !await requestConfirmation({ title: "删除客户联系人", description: `确定删除联系人“${selectedContact.name}”吗？`, details: ["历史跟进记录仍会保留。"], confirmLabel: "确认删除", tone: "danger" })) return;
    try {
      await client.deleteCrmContact({ customerId, id: selectedContact.id });
      setContactId(0);
      await onReloadContacts();
      onFeedback(successFeedback("联系人已删除，历史跟进仍保留。"));
    } catch (error) { onFeedback(requestErrorFeedback(error)); }
  }

  return (
    <div className="two-column-layout">
      <form className="form-grid" key={isNewCustomer ? "new" : selectedCustomer?.id ?? "empty"} onSubmit={saveCustomer}>
        <div className="section-heading-row">
          <h3>{isNewCustomer ? "新建销售客户" : "客户资料"}</h3>
          {canOperate ? <button className="secondary-button" type="button" onClick={() => setIsNewCustomer(true)}>新建</button> : null}
        </div>
        {!isNewCustomer ? (
          <label>选择客户<select value={customerId} onChange={(event) => onSelectCustomer(Number(event.target.value))}>
            {customers.length === 0 ? <option value={0}>暂无销售客户</option> : null}
            {customers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select></label>
        ) : null}
        <fieldset className="permission-fieldset form-field-wide" disabled={!canOperate}>
        <label>客户名称<input name="name" required maxLength={200} defaultValue={isNewCustomer ? "" : selectedCustomer?.name} /></label>
        <label>国家/地区<input name="countryRegion" maxLength={100} defaultValue={isNewCustomer ? "" : selectedCustomer?.countryRegion} /></label>
        <label>网站<input name="website" maxLength={300} defaultValue={isNewCustomer ? "" : selectedCustomer?.website} /></label>
        <label>状态<select name="status" defaultValue={isNewCustomer ? "潜在客户" : selectedCustomer?.status || "潜在客户"}>
          <option>潜在客户</option><option>跟进中</option><option>已成交</option><option>暂停</option><option>已流失</option>
        </select></label>
        <label>来源<input name="source" maxLength={50} defaultValue={isNewCustomer ? "" : selectedCustomer?.source} /></label>
        <label className="form-field-wide">备注<textarea name="notes" maxLength={1000} defaultValue={isNewCustomer ? "" : selectedCustomer?.notes} /></label>
        </fieldset>
        <div className="form-actions">
          {canOperate ? <button className="primary-button" type="submit">{isNewCustomer ? "建立客户" : "保存客户"}</button> : null}
          {!isNewCustomer && selectedCustomer && canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteCustomer()}>删除客户</button> : null}
        </div>
      </form>

      <form className="form-grid" key={selectedContact?.id ?? `new-${customerId}`} onSubmit={saveContact}>
        <div className="section-heading-row">
          <h3>{selectedContact ? "联系人资料" : "添加联系人"}</h3>
          {canOperate ? <button className="secondary-button" type="button" disabled={!customerId} onClick={() => setContactId(0)}>新建</button> : null}
        </div>
        <label>选择联系人<select value={contactId} disabled={!customerId} onChange={(event) => setContactId(Number(event.target.value))}>
          <option value={0}>新联系人</option>
          {contacts.map((item) => <option key={item.id} value={item.id}>{item.name}{item.isPrimary ? " · 主要" : ""}</option>)}
        </select></label>
        <fieldset className="permission-fieldset form-field-wide" disabled={!canOperate || !customerId}>
        <label>姓名<input name="contactName" required maxLength={100} defaultValue={selectedContact?.name} /></label>
        <label>职位<input name="title" maxLength={100} defaultValue={selectedContact?.title} /></label>
        <label>邮箱<input name="email" type="email" maxLength={200} defaultValue={selectedContact?.email} /></label>
        <label>电话<input name="phone" maxLength={100} defaultValue={selectedContact?.phone} /></label>
        <label>即时通讯<input name="instantMessaging" maxLength={100} defaultValue={selectedContact?.instantMessaging} /></label>
        <label className="checkbox-field"><input name="isPrimary" type="checkbox" defaultChecked={selectedContact?.isPrimary ?? contacts.length === 0} />主要联系人</label>
        </fieldset>
        <div className="form-actions">
          {canOperate ? <button className="primary-button" type="submit" disabled={!customerId}>{selectedContact ? "保存联系人" : "添加联系人"}</button> : null}
          {selectedContact && canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteContact()}>删除联系人</button> : null}
        </div>
      </form>
    </div>
  );
}
