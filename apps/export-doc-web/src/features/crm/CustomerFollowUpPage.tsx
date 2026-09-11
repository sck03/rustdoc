import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteId } from "../../ui/routeQueryState.ts";
import { useDirectoryLocation } from "../../ui/useDirectoryLocation.ts";
import type {
  ApiCrmContactDto,
  ApiCrmCustomerDto,
  ApiCrmFollowUpDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { CrmPartyManagementPanel } from "./CrmPartyManagementPanel.tsx";
import { CrmCustomerDirectoryPanel } from "./CrmCustomerDirectoryPanel.tsx";
import { CrmCustomerImportPanel } from "./CrmCustomerImportPanel.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { OperationFeedback, errorFeedback, requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { FormGuidance, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { usePagedDirectoryQuery } from "../../ui/usePagedDirectoryQuery.ts";
import { useConfirmUnsavedChanges, useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { businessDateTimeLocalInputToIso, formatBusinessDateTime, isPastInstant, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";

type CustomerFollowUpPageProps = {
  businessTimeZone: string;
  client: ExportDocManagerApiClient;
};

type CustomerTaskView = "followups" | "followup-editor" | "directory" | "profile" | "contacts" | "customer-followups" | "import";
const crmTabsId = "crm-customer-workspace";

export function CustomerFollowUpPage({ businessTimeZone, client }: CustomerFollowUpPageProps) {
  const canViewCustomers = usePermission(permissionResources.crmCustomers, permissionActions.view).allowed;
  const canViewFollowUps = usePermission(permissionResources.crmFollowUps, permissionActions.view).allowed;
  const canViewContacts = usePermission(permissionResources.crmContacts, permissionActions.view).allowed;
  const canCreateCustomer = usePermission(permissionResources.crmCustomers, permissionActions.create).allowed;
  const canEditCustomer = usePermission(permissionResources.crmCustomers, permissionActions.edit).allowed;
  const canDeactivateCustomer = usePermission(permissionResources.crmCustomers, permissionActions.deactivate).allowed;
  const canDeleteCustomer = usePermission(permissionResources.crmCustomers, permissionActions.delete).allowed;
  const canImportCustomer = usePermission(permissionResources.crmCustomers, permissionActions.import).allowed;
  const canExportCustomer = usePermission(permissionResources.crmCustomers, permissionActions.export).allowed;
  const canCreateContact = usePermission(permissionResources.crmContacts, permissionActions.create).allowed;
  const canEditContact = usePermission(permissionResources.crmContacts, permissionActions.edit).allowed;
  const canSetPrimaryContact = usePermission(permissionResources.crmContacts, permissionActions.setPrimary).allowed;
  const canDeleteContact = usePermission(permissionResources.crmContacts, permissionActions.delete).allowed;
  const canCreateFollowUp = usePermission(permissionResources.crmFollowUps, permissionActions.create).allowed;
  const canEditFollowUp = usePermission(permissionResources.crmFollowUps, permissionActions.edit).allowed;
  const canCompleteFollowUp = usePermission(permissionResources.crmFollowUps, permissionActions.complete).allowed;
  const canRestoreFollowUp = usePermission(permissionResources.crmFollowUps, permissionActions.restore).allowed;
  const canAssignFollowUp = usePermission(permissionResources.crmFollowUps, permissionActions.assign).allowed;
  const canDeleteFollowUp = usePermission(permissionResources.crmFollowUps, permissionActions.delete).allowed;
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const runAbortableOperation = useAbortableOperation();
  const { params: searchParams, update: updateRoute } = useRouteQuery();
  const focusedFollowUpId = readRouteId(searchParams.get("followUpId")) ?? undefined;
  const [customers, setCustomers] = useState<ApiCrmCustomerDto[]>([]);
  const [contacts, setContacts] = useState<ApiCrmContactDto[]>([]);
  const customerId = readRouteId(searchParams.get("customerId")) ?? 0;
  const setCustomerId = (id: number) => updateRoute({ customerId: id || null });
  const [customerKeyword, setCustomerKeyword] = useState("");
  const includeCompleted = searchParams.get("completed") === "true";
  const setIncludeCompleted = (value: boolean) => updateRoute({ completed: value, followupPage: null });
  const { pageNumber: followUpPageNumber, setPageNumber: setFollowUpPageNumber, pageSize: followUpPageSize, setPageSize: setFollowUpPageSize } = useDirectoryLocation("followup");
  const [followUpRevision, setFollowUpRevision] = useState(0);
  const [saving, setSaving] = useState(false);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const editFollowUpId = readRouteId(searchParams.get("editFollowUpId"));
  const [editingRecord, setEditingRecord] = useState<ApiCrmFollowUpDto | null>(null);
  const editingFollowUp = editingRecord?.id === editFollowUpId ? editingRecord : null;
  const setEditingFollowUp = (item: ApiCrmFollowUpDto | null) => { setEditingRecord(item); updateRoute({ editFollowUpId: item?.id }); };
  const transferMode = searchParams.get("followUpAction") === "transfer";
  const setTransferMode = (value: boolean) => updateRoute({ followUpAction: value ? "transfer" : null });
  const [followUpContactId, setFollowUpContactId] = useState<number | "">("");
  const [followUpDraftDirty, setFollowUpDraftDirty] = useState(false);
  const view = readCustomerView(searchParams.get("view") ?? (focusedFollowUpId || !canViewCustomers ? "followups" : null));
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  useUnsavedChangesGuard({
    isDirty: followUpDraftDirty,
    message: "当前客户跟进有未保存的修改。",
  });
  useEffect(() => setFollowUpDraftDirty(false), [view, editFollowUpId]);

  function applyView(nextView: CustomerTaskView) {
    const returnView = searchParams.get("returnView");
    const target = nextView === "followups" && view === "followup-editor" && returnView === "customer-followups" ? "customer-followups" : nextView;
    updateRoute({ view: target, returnView: nextView === "followup-editor" && view === "customer-followups" ? view : null }, false);
  }

  async function changeView(nextView: CustomerTaskView) {
    if (nextView === view) return true;
    if (!await confirmDiscardChanges("切换客户工作区")) return false;
    setFollowUpDraftDirty(false);
    applyView(nextView);
    return true;
  }

  async function returnToList() {
    if (view !== "followup-editor") { await changeView(canViewCustomers ? "directory" : "followups"); return; }
    if (!await changeView("followups")) return;
    setEditingFollowUp(null);
    setTransferMode(false);
    setFollowUpContactId("");
  }

  const selectedCustomer = useMemo(
    () => customers.find((item) => item.id === customerId),
    [customerId, customers],
  );

  const followUpQuery = usePagedDirectoryQuery(
    ["crm-follow-ups", focusedFollowUpId, view === "customer-followups" ? customerId : null, includeCompleted, followUpPageNumber, followUpPageSize, followUpRevision],
    (signal) => client.queryCrmFollowUps({ crmCustomerId: view === "customer-followups" ? customerId : undefined, followUpId: focusedFollowUpId, includeCompleted, pageNumber: followUpPageNumber, pageSize: followUpPageSize }, { signal }),
    canViewFollowUps && (view === "followups" || view === "customer-followups" && customerId > 0),
  );
  const followUpPage = followUpQuery.data ?? null;
  const rows = followUpPage?.items ?? [];
  const loading = followUpQuery.isFetching;

  useEffect(() => {
    if (view !== "followup-editor" || !editFollowUpId || editingRecord?.id === editFollowUpId || !canViewFollowUps) return;
    const controller = new AbortController();
    void client.queryCrmFollowUps({ followUpId: editFollowUpId, includeCompleted: true, pageNumber: 1, pageSize: 1 }, { signal: controller.signal }).then((page) => {
      if (controller.signal.aborted) return;
      const item = page.items.find((record) => record.id === editFollowUpId);
      if (!item) throw new Error("跟进记录不存在或无权访问。");
      setEditingRecord(item);
      updateRoute({ customerId: item.crmCustomerId });
      setFollowUpContactId(item.crmContactId ?? "");
    }).catch((error) => { if (!controller.signal.aborted) setFeedback(requestErrorFeedback(error)); });
    return () => controller.abort();
  }, [client, view, editFollowUpId, editingRecord?.id, canViewFollowUps, updateRoute]);

  useEffect(() => {
    if (!canViewCustomers || view !== "followup-editor") return;
    const controller = new AbortController();
    void client.queryCrmCustomers({ keyword: "", status: "", pageNumber: 1, pageSize: 100 }, { signal: controller.signal })
      .then((customerPage) => {
        if (controller.signal.aborted) return;
        const customerRows = customerPage.items;
        setCustomers((current) => [...current.filter((item) => !customerRows.some((row) => row.id === item.id)), ...customerRows]);
      })
      .catch((error) => {
        if (!controller.signal.aborted) setFeedback(errorFeedback(readApiError(error)));
      })
    return () => controller.abort();
  }, [client, canViewCustomers, view]);

  useEffect(() => {
    if (!customerId || !canViewCustomers || selectedCustomer) return;
    const controller = new AbortController();
    void client.getCrmCustomer({ id: customerId }, { signal: controller.signal }).then((item) => {
      if (!controller.signal.aborted) setCustomers((current) => [item, ...current.filter((row) => row.id !== item.id)]);
    }).catch((error) => { if (!controller.signal.aborted) setFeedback(requestErrorFeedback(error)); });
    return () => controller.abort();
  }, [client, customerId, canViewCustomers, selectedCustomer]);

  useEffect(() => {
    if (!customerId || !canViewContacts) {
      setContacts([]);
      return;
    }
    const controller = new AbortController();
    setContacts([]);
    void client.queryCrmContacts(
      { customerId, pageNumber: 1, pageSize: 100 },
      { signal: controller.signal },
    ).then((page) => { if (!controller.signal.aborted) setContacts(page.items); }).catch((error: unknown) => {
      if (!controller.signal.aborted) setFeedback(errorFeedback(readApiError(error)));
    });
    return () => controller.abort();
  }, [client, customerId, canViewContacts]);

  async function refresh() {
    setFollowUpRevision((value) => value + 1);
  }

  async function reloadCustomers(preferred?: ApiCrmCustomerDto) {
    await runAbortableOperation(async (signal) => {
      const page = await client.queryCrmCustomers(
        { keyword: customerKeyword.trim(), status: "", pageNumber: 1, pageSize: 100 },
        { signal },
      );
      const nextCustomers = preferred && !page.items.some((item) => item.id === preferred.id)
        ? [preferred, ...page.items]
        : page.items;
      setCustomers(nextCustomers);
      const nextId = preferred && nextCustomers.some((item) => item.id === preferred.id)
        ? preferred.id
        : 0;
      setCustomerId(nextId);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.crmCustomersRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.crmDashboard() }),
      ]);
    });
  }

  async function searchCustomerOptions() {
    try {
      const page = await runAbortableOperation((signal) => client.queryCrmCustomers(
        { keyword: customerKeyword.trim(), status: "", pageNumber: 1, pageSize: 100 },
        { signal },
      ));
      const current = customers.find((item) => item.id === customerId);
      setCustomers(current && !page.items.some((item) => item.id === current.id) ? [current, ...page.items] : page.items);
      if (!customerId && page.items.length > 0) setCustomerId(page.items[0].id);
    } catch (error) {
      if (isAbortError(error)) return;
      setFeedback(requestErrorFeedback(error));
    }
  }

  async function reloadContacts() {
    if (!customerId) {
      setContacts([]);
      return;
    }
    await runAbortableOperation(async (signal) => {
      const page = await client.queryCrmContacts(
        { customerId, pageNumber: 1, pageSize: 100 },
        { signal },
      );
      setContacts(page.items);
    });
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (editingFollowUp ? !canEditFollowUp : !canCreateFollowUp) return;
    const formElement = event.currentTarget;
    if (!customerId) {
      setFeedback(warningFeedback("请先在客户目录中建立并选择销售客户。"));
      return;
    }

    const form = new FormData(formElement);
    setSaving(true);
    setFeedback(null);
    try {
      const body = {
          id: 0,
          crmCustomerId: customerId,
          crmContactId: optionalNumber(form.get("crmContactId")),
          type: String(form.get("type") ?? "其他"),
          summary: String(form.get("summary") ?? ""),
          nextAction: String(form.get("nextAction") ?? ""),
          nextFollowUpAt: businessDateTimeLocalInputToIso(form.get("nextFollowUpAt"), businessTimeZone),
          expectedVersion: editingFollowUp?.versionNumber ?? 0,
        };
      await runAbortableOperation(async (signal) => {
        if (editingFollowUp) {
          await client.updateCrmFollowUp(
            { id: editingFollowUp.id, body: { ...body, id: editingFollowUp.id, followedUpAt: editingFollowUp.followedUpAt } },
            { signal },
          );
        } else {
          await client.createCrmFollowUp({ body }, { signal });
        }
      });
      formElement.reset();
      setFollowUpContactId("");
      setFollowUpDraftDirty(false);
      setEditingFollowUp(null);
      setTransferMode(false);
      setFeedback(successFeedback(editingFollowUp ? "客户跟进已更新。" : "客户跟进已保存。"));
      await refresh();
      applyView("followups");
    } catch (error) {
      if (isAbortError(error)) return;
      setFeedback(requestErrorFeedback(error));
    } finally {
      setSaving(false);
    }
  }

  async function toggleCompleted(item: ApiCrmFollowUpDto) {
    if (item.isCompleted ? !canRestoreFollowUp : !canCompleteFollowUp) return;
    try {
      await runAbortableOperation((signal) => item.isCompleted
        ? client.restoreCrmFollowUp(
          { id: item.id, body: { expectedVersion: item.versionNumber } },
          { signal },
        )
        : client.completeCrmFollowUp(
          { id: item.id, body: { expectedVersion: item.versionNumber } },
          { signal },
        ));
      await refresh();
      setFeedback(successFeedback(item.isCompleted ? "跟进记录已恢复为待跟进。" : "跟进记录已标记完成。"));
    } catch (error) {
      if (isAbortError(error)) return;
      setFeedback(requestErrorFeedback(error));
    }
  }

  async function transferFollowUp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingFollowUp || !canAssignFollowUp || !selectedCustomer) return;
    if (editingFollowUp.crmCustomerId === selectedCustomer.id) {
      setFeedback(warningFeedback("请选择不同的目标客户。"));
      return;
    }
    if (!await requestConfirmation({
      title: "转移跟进记录",
      description: `确定把“${editingFollowUp.customerName}”的跟进转移到“${selectedCustomer.name}”吗？此操作会写入安全审计。`,
      confirmLabel: "确认转移",
      tone: "warning",
    })) return;

    setSaving(true);
    setFeedback(null);
    try {
      await runAbortableOperation((signal) => client.transferCrmFollowUp({
        id: editingFollowUp.id,
        body: {
          crmCustomerId: selectedCustomer.id,
          crmContactId: followUpContactId === "" ? null : followUpContactId,
          expectedVersion: editingFollowUp.versionNumber,
        },
      }, { signal }));
      setFollowUpDraftDirty(false);
      setEditingFollowUp(null);
      setTransferMode(false);
      setFollowUpContactId("");
      setFeedback(successFeedback("跟进记录已转移。"));
      await refresh();
      applyView("followups");
    } catch (error) {
      if (!isAbortError(error)) setFeedback(requestErrorFeedback(error));
    } finally {
      setSaving(false);
    }
  }

  async function deleteFollowUp(item: ApiCrmFollowUpDto) {
    if (!canDeleteFollowUp || !await requestConfirmation({ title: "删除跟进记录", description: `确定删除“${item.customerName}”的这条跟进记录吗？`, confirmLabel: "确认删除", tone: "danger" })) return;
    try {
      await runAbortableOperation((signal) => client.deleteCrmFollowUp(
        { id: item.id, expectedVersion: item.versionNumber },
        { signal },
      ));
      await refresh();
      setFeedback(successFeedback("跟进记录已删除。"));
    } catch (error) {
      if (!isAbortError(error)) setFeedback(requestErrorFeedback(error));
    }
  }

  return (
    <section className="work-surface">
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
      ]} /> : <div className="record-context-heading"><button type="button" className="secondary-button" onClick={() => void returnToList()}>{view === "followup-editor" ? "返回跟进记录" : "返回客户目录"}</button>
        <strong>{view === "import" ? "导入客户" : selectedCustomer?.name ?? (view === "followup-editor" ? "客户跟进" : customerId ? "客户详情" : "新建销售客户")}</strong></div>}
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
      <ListPaginationControls pageNumber={followUpPageNumber} totalPages={followUpPage?.totalPages ?? 1} totalCount={followUpPage?.totalCount ?? 0} pageSize={followUpPageSize} pageSizeOptions={[20, 30, 50, 100]} isBusy={loading} onPageChange={setFollowUpPageNumber} onPageSizeChange={(value) => { setFollowUpPageSize(value); setFollowUpPageNumber(1); }} />
      </section> : null}
    </section>
  );
}

function readCustomerView(value: string | null): CustomerTaskView {
  return value === "followup-editor" || value === "followups" || value === "contacts" || value === "customer-followups" || value === "profile" || value === "import" ? value : "directory";
}

function optionalNumber(value: FormDataEntryValue | null) {
  const parsed = Number(String(value ?? ""));
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined;
}
