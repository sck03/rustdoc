import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useRouteQuery } from "../../../ui/useRouteQuery.ts";
import { readRouteId } from "../../../ui/routeQueryState.ts";
import { useDirectoryLocation } from "../../../ui/useDirectoryLocation.ts";
import type { ApiCrmContactDto, ApiCrmCustomerDto, ApiCrmFollowUpDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { errorFeedback, requestErrorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../../ui/OperationFeedback.tsx";
import { usePermission } from "../../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../../app/permissionCatalog.ts";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import { usePagedDirectoryQuery } from "../../../ui/usePagedDirectoryQuery.ts";
import { useConfirmUnsavedChanges, useUnsavedChangesGuard } from "../../../ui/unsavedChangesGuard.tsx";
import { isAbortError, useAbortableOperation } from "../../../ui/useAbortableOperation.ts";
import { businessDateTimeLocalInputToIso } from "../../../ui/businessTime.ts";
export type CustomerFollowUpPageProps = {
  businessTimeZone: string;
  client: ExportDocManagerApiClient;
};

type CustomerTaskView = "followups" | "followup-editor" | "directory" | "profile" | "contacts" | "customer-followups" | "import";

function readCustomerView(value: string | null): CustomerTaskView {
  return value === "followup-editor" || value === "followups" || value === "contacts" || value === "customer-followups" || value === "profile" || value === "import" ? value : "directory";
}

export function optionalNumber(value: FormDataEntryValue | null) {
  const parsed = Number(String(value ?? ""));
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined;
}

export function useCustomerFollowUpWorkspace({ businessTimeZone, client }: CustomerFollowUpPageProps) {
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

  async function reloadCustomers(preferred?: ApiCrmCustomerDto, signal?: AbortSignal) {
    if (preferred) {
      signal?.throwIfAborted();
      setCustomers(current => [preferred, ...current.filter(item => item.id !== preferred.id)]);
      setCustomerId(preferred.id);
      await Promise.all([queryClient.invalidateQueries({ queryKey: queryKeys.crmCustomersRoot() }), queryClient.invalidateQueries({ queryKey: queryKeys.crmDashboard() })]);
      return;
    }
    await runAbortableOperation(async (signal) => {
      const page = await client.queryCrmCustomers(
        { keyword: customerKeyword.trim(), status: "", pageNumber: 1, pageSize: 100 },
        { signal },
      );
      setCustomers(page.items);
      setCustomerId(0);
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

  async function reloadContacts(preferred?: ApiCrmContactDto, signal?: AbortSignal) {
    if (!customerId) {
      setContacts([]);
      return;
    }
    if (preferred) setContacts(current => [preferred, ...current.filter(item => item.id !== preferred.id)]);
    const load = async (requestSignal: AbortSignal) => {
      const page = await client.queryCrmContacts({ customerId, pageNumber: 1, pageSize: 100 }, { signal: requestSignal });
      requestSignal.throwIfAborted();
      setContacts(preferred && !page.items.some(item => item.id === preferred.id) ? [preferred, ...page.items] : page.items.map(item => item.id === preferred?.id ? preferred : item));
    };
    await (signal ? load(signal) : runAbortableOperation(load));
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
  return {
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
  };
}
