import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { CrmPartyManagementPanel } from "./CrmPartyManagementPanel.tsx";
import { CrmCustomerDirectoryPanel } from "./CrmCustomerDirectoryPanel.tsx";
import { CrmCustomerImportPanel } from "./CrmCustomerImportPanel.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { OperationFeedback, errorFeedback } from "../../ui/OperationFeedback.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { FormGuidance, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { formatBusinessDateTime, isPastInstant, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { useCustomerFollowUpWorkspace, CustomerFollowUpPageProps, optionalNumber } from "./hooks/useCustomerFollowUpWorkspace.ts";

const crmTabsId = "crm-customer-workspace";

export function CustomerFollowUpPage({ businessTimeZone, client }: CustomerFollowUpPageProps) {
  const {
    canViewCustomers, canViewFollowUps, canViewContacts, canCreateCustomer, canEditCustomer,
    canDeactivateCustomer, canDeleteCustomer, canImportCustomer, canExportCustomer, canCreateContact,
    canEditContact, canSetPrimaryContact, canDeleteContact, canCreateFollowUp, canEditFollowUp,
    canCompleteFollowUp, canRestoreFollowUp, canAssignFollowUp, canDeleteFollowUp, updateRoute,
    focusedFollowUpId, customers, setCustomers, contacts, customerId,
    setCustomerId, customerKeyword, setCustomerKeyword, includeCompleted, setIncludeCompleted,
    followUpPageNumber, setFollowUpPageNumber, followUpPageSize, setFollowUpPageSize, saving,
    feedback, setFeedback, editFollowUpId, editingFollowUp, setEditingFollowUp,
    transferMode, setTransferMode, followUpContactId, setFollowUpContactId, setFollowUpDraftDirty,
    view, changeView, returnToList, selectedCustomer, followUpQuery,
    followUpPage, rows, loading, reloadCustomers, searchCustomerOptions,
    reloadContacts, handleCreate, toggleCompleted, transferFollowUp, deleteFollowUp,
  } = useCustomerFollowUpWorkspace({ businessTimeZone, client });

  return (
    <ListWorkspace >
      {["directory", "followups", "customer-followups"].includes(view) && <div className="section-heading-row">
        <p className="section-description">从客户目录进入资料与联系人，或集中查看需要办理的跟进事项。</p>
        {view === "followups" || view === "customer-followups" ? <label className="checkbox-field">
          <input
            type="checkbox"
            checked={includeCompleted}
            onChange={(event) => setIncludeCompleted(event.target.checked)}
          />
          显示已完成
        </label> : null}
      </div>}

      <OperationFeedback feedback={feedback} />
      {focusedFollowUpId && <InlineNotice tone="info">正在查看指定跟进事项。<button type="button" className="command-button secondary" onClick={() => updateRoute({ view: "followups", followUpId: null, followupPage: null })}>返回全部跟进</button></InlineNotice>}
      {!canCreateCustomer && !canEditCustomer && !canCreateContact && !canEditContact && !canCreateFollowUp && !canEditFollowUp
        ? <PermissionNotice>当前岗位只有查看权限；客户、联系人和跟进的具体动作由管理员逐项授权。</PermissionNotice>
        : null}

      {view === "directory" || view === "followups" ? <TaskViewTabs idPrefix={crmTabsId} value={view} label="客户业务工作区" onChange={changeView} items={[
        ...(canViewCustomers ? [{ id: "directory" as const, label: "客户目录" }] : []), ...(canViewFollowUps ? [{ id: "followups" as const, label: "跟进记录" }] : []),
      ]} /> : <PageHeader title={view === "import" ? "导入客户" : selectedCustomer?.name ?? (view === "followup-editor" ? "客户跟进" : customerId ? "客户详情" : "新建销售客户")}
        actions={<button type="button" className="secondary-button" onClick={() => void returnToList()}>{view === "followup-editor" ? "返回跟进记录" : "返回客户目录"}</button>} />}
      {customerId > 0 && ["profile", "contacts", "customer-followups"].includes(view) && <TaskViewTabs idPrefix={crmTabsId} value={view} label="客户详情" onChange={changeView} items={[
        { id: "profile", label: "客户资料" }, ...(canViewContacts ? [{ id: "contacts" as const, label: "联系人" }] : []), ...(canViewFollowUps ? [{ id: "customer-followups" as const, label: "该客户跟进" }] : []),
      ]} />}

      {(view === "profile" && canViewCustomers || view === "contacts" && canViewContacts) && (!customerId || selectedCustomer) ? <div {...(customerId ? getTaskViewPanelProps(crmTabsId, view) : { "aria-label": "新建销售客户" })}><CrmPartyManagementPanel
        section={view === "contacts" ? "contacts" : "profile"}
        client={client}
        customers={customers}
        contacts={contacts}
        customerId={customerId}
        onSelectCustomer={setCustomerId}
        onReloadCustomers={reloadCustomers}
        onReloadContacts={reloadContacts}
        onFeedback={setFeedback}
        canCreateCustomer={canCreateCustomer}
        canEditCustomer={canEditCustomer}
        canDeactivateCustomer={canDeactivateCustomer}
        canDeleteCustomer={canDeleteCustomer}
        canCreateContact={canCreateContact}
        canEditContact={canEditContact}
        canSetPrimaryContact={canSetPrimaryContact}
        canDeleteContact={canDeleteContact}
      /></div> : null}

      {view === "directory" && canViewCustomers ? <div {...getTaskViewPanelProps(crmTabsId, "directory")}><CrmCustomerDirectoryPanel
        client={client}
        canCreate={canCreateCustomer}
        canDeactivate={canDeactivateCustomer}
        canExport={canExportCustomer}
        canImport={canImportCustomer} onImport={() => void changeView("import")}
        onCreateCustomer={() => { setCustomerId(0); void changeView("profile"); }}
        onSelectCustomer={(customer) => { setCustomers((current) => [customer, ...current.filter((item) => item.id !== customer.id)]); setCustomerId(customer.id); changeView("profile"); }}
      /></div> : null}
      {view === "import" && canImportCustomer ? <div aria-label="导入客户"><CrmCustomerImportPanel client={client} canImport={canImportCustomer} onImported={() => reloadCustomers()} /></div> : null}

      {view === "followup-editor" && editFollowUpId && !editingFollowUp && <PageState tone={feedback ? "error" : "loading"} title={feedback ? "无法打开跟进记录" : "正在读取跟进记录"} />}
      {view === "followup-editor" && (!editFollowUpId || editingFollowUp) ? <form className="form-grid" key={`${transferMode ? "transfer" : "edit"}-${editingFollowUp?.id ?? "new"}-${editingFollowUp?.versionNumber ?? 0}`} onSubmit={transferMode ? transferFollowUp : handleCreate} aria-label="跟进记录编辑">
        <div className="section-heading-row"><h3>{transferMode ? "转移跟进" : editingFollowUp ? canEditFollowUp ? "编辑跟进" : "查看跟进" : "新增跟进"}</h3>
        </div>
        {!customers.length ? <FormGuidance className="form-field-wide" title="先建立一位销售客户" description="跟进记录必须归属客户。客户资料与原单证客户相互独立。" action={canCreateCustomer ? <button className="primary-button" type="button" onClick={() => changeView("profile")}>建立客户资料</button> : undefined} /> : null}
        <fieldset className="permission-fieldset form-field-wide" disabled={transferMode ? !canAssignFollowUp : editingFollowUp ? !canEditFollowUp : !canCreateFollowUp} onChangeCapture={(event) => {
          if (!(event.target instanceof Element) || !event.target.closest("[data-draft-ignore]")) setFollowUpDraftDirty(true);
        }}>
        <label>
          客户
          <div className="toolbar compact-search-toolbar" data-draft-ignore>
            <input aria-label="搜索跟进客户" value={customerKeyword} disabled={Boolean(editingFollowUp) && !transferMode} onChange={(event) => setCustomerKeyword(event.target.value)} placeholder="输入客户名称后查找" />
            <button className="secondary-button" type="button" disabled={Boolean(editingFollowUp) && !transferMode} onClick={() => void searchCustomerOptions()}>查找</button>
          </div>
          <select value={customerId} disabled={Boolean(editingFollowUp) && !transferMode} onChange={(event) => {
            setCustomerId(Number(event.target.value));
            setFollowUpContactId("");
          }}>
            <option value={0}>{customers.length ? "请选择销售客户" : "请先建立销售客户"}</option>
            {customers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>
          联系人
          <select
            name="crmContactId"
            value={followUpContactId}
            onChange={(event) => setFollowUpContactId(optionalNumber(event.target.value) ?? "")}
          >
            <option value="">不指定</option>
            {contacts.map((item) => <option key={item.id} value={item.id}>{item.name}{item.title ? ` · ${item.title}` : ""}</option>)}
          </select>
        </label>
        {!transferMode ? <><label>
          跟进方式
          <select name="type" defaultValue={editingFollowUp?.type ?? "邮件"}>
            <option>邮件</option>
            <option>电话</option>
            <option>即时通讯</option>
            <option>拜访</option>
            <option>其他</option>
          </select>
        </label>
        <label className="form-field-wide">
          跟进摘要
          <input name="summary" required maxLength={500} defaultValue={editingFollowUp?.summary} placeholder="例如：客户确认样品，等待价格调整" />
        </label>
        <label className="form-field-wide">
          下次动作
          <input name="nextAction" maxLength={300} defaultValue={editingFollowUp?.nextAction} placeholder="例如：周五发送新版报价" />
        </label>
        <label>
          下次跟进时间
          <input name="nextFollowUpAt" type="datetime-local" defaultValue={toBusinessDateTimeLocalInput(editingFollowUp?.nextFollowUpAt, businessTimeZone)} />
        </label></> : <FormGuidance className="form-field-wide" title="受审计的客户转移" description="转移只变更跟进归属和可选联系人，不会改写原跟进内容、完成状态或历史时间。" />}
        <div className="form-actions">
          {(transferMode ? canAssignFollowUp : editingFollowUp ? canEditFollowUp : canCreateFollowUp) ? <button className="primary-button" type="submit" disabled={saving || !selectedCustomer}>
            {saving ? "保存中..." : transferMode ? "确认转移" : editingFollowUp ? "更新跟进" : "保存跟进"}
          </button> : null}
        </div>
        </fieldset>
      </form> : null}

      {canViewFollowUps && (view === "followups" || view === "customer-followups" && customerId > 0) ? <section className="form-section" {...getTaskViewPanelProps(crmTabsId, view)}>
      <div className="section-header">
        <div><h3>跟进记录</h3><p className="section-description">集中查看沟通结果、下一步动作和待办提醒。</p></div>
        {canCreateFollowUp ? <button className="primary-button" type="button" onClick={() => {
          setFollowUpDraftDirty(false);
          setEditingFollowUp(null);
          setTransferMode(false);
          setFollowUpContactId("");
          void changeView("followup-editor");
        }}>记录新跟进</button> : null}
      </div>
      <ResponsiveTableFrame label="客户跟进记录" className="table-scroll-region" mobileLayout="scroll" busy={loading}>
        <table className="data-table responsive-data-table follow-up-data-table">
          <thead>
            <tr>
              <th>客户</th>
              <th data-table-priority="secondary">方式</th>
              <th data-table-priority="secondary">联系人</th>
              <th>跟进摘要</th>
              <th data-table-priority="secondary">下次动作</th>
              <th>提醒时间</th>
              <th>状态</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((item) => (
              <tr key={item.id}>
                <td><TablePrimaryText value={item.customerName} /></td>
                <td data-table-priority="secondary">{item.type}</td>
                <td data-table-priority="secondary"><TablePrimaryText value={item.contactName} /></td>
                <td><TablePrimaryText value={item.summary} /></td>
                <td data-table-priority="secondary"><TablePrimaryText value={item.nextAction} /></td>
                <td>{formatBusinessDateTime(item.nextFollowUpAt, businessTimeZone, "未设置")}</td>
                <td><BusinessStatusBadge value={item.isCompleted ? "已完成" : isPastInstant(item.nextFollowUpAt) ? "已逾期" : "待跟进"} /></td>
                <td>
                  <div className="table-row-actions">
                    <button className="secondary-button" type="button" onClick={() => {
                      setFollowUpDraftDirty(false);
                      setTransferMode(false);
                      setCustomerId(item.crmCustomerId);
                      setEditingFollowUp(item);
                      setFollowUpContactId(item.crmContactId ?? "");
                      void changeView("followup-editor");
                    }}>{canEditFollowUp ? "编辑" : "查看"}</button>
                    {canAssignFollowUp ? <button className="secondary-button" type="button" onClick={() => {
                      setFollowUpDraftDirty(false);
                      setTransferMode(true);
                      setCustomerId(item.crmCustomerId);
                      setEditingFollowUp(item);
                      setFollowUpContactId(item.crmContactId ?? "");
                      void changeView("followup-editor");
                    }}>转移</button> : null}
                    {(item.isCompleted ? canRestoreFollowUp : canCompleteFollowUp) ? <button className="secondary-button" type="button" onClick={() => void toggleCompleted(item)}>
                      {item.isCompleted ? "恢复" : "完成"}
                    </button> : null}
                    {canDeleteFollowUp ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteFollowUp(item)}>删除</button> : null}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!loading && !followUpQuery.isError && rows.length === 0 ? <PageState tone="empty" title="没有符合条件的跟进记录" description="可调整筛选，或记录一次邮件、电话或拜访结果。" /> : null}
        {loading ? <PageState tone="loading" title="正在加载客户跟进" description="正在读取沟通结果、下一步动作和提醒状态。" /> : null}
      </ResponsiveTableFrame>
      {followUpQuery.isError ? <OperationFeedback feedback={errorFeedback(readApiError(followUpQuery.error))} /> : null}
      <Pagination pageNumber={followUpPageNumber} totalPages={followUpPage?.totalPages ?? 1} totalCount={followUpPage?.totalCount ?? 0} pageSize={followUpPageSize} pageSizeOptions={[20, 30, 50, 100]} isBusy={loading} onPageChange={setFollowUpPageNumber} onPageSizeChange={(value) => { setFollowUpPageSize(value); setFollowUpPageNumber(1); }} />
      </section> : null}
    </ListWorkspace>
  );
}
