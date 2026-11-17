import { useEffect, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useRouteQuery } from "../../../ui/useRouteQuery.ts";
import { readRouteId } from "../../../ui/routeQueryState.ts";
import { useDirectoryLocation } from "../../../ui/useDirectoryLocation.ts";
import type { ApiCrmCustomerDto, ApiCrmFollowUpDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { errorFeedback, requestErrorFeedback, type OperationFeedbackState } from "../../../ui/OperationFeedback.tsx";
import { usePermission } from "../../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../../app/permissionCatalog.ts";
import { usePagedDirectoryQuery } from "../../../ui/usePagedDirectoryQuery.ts";
import { useConfirmUnsavedChanges, useUnsavedChangesGuard } from "../../../ui/unsavedChangesGuard.tsx";
import { isAbortError, useAbortableOperation } from "../../../ui/useAbortableOperation.ts";
import { useFollowUpCommands } from "./useFollowUpCommands.ts";
import { useCrmContactsPage } from "./useCrmContactsPage.ts";
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
  const queryClient = useQueryClient();
  const { params: searchParams, update: updateRoute } = useRouteQuery();
  const focusedFollowUpId = readRouteId(searchParams.get("followUpId")) ?? undefined;
  const [customers, setCustomers] = useState<ApiCrmCustomerDto[]>([]);
  const customerId = readRouteId(searchParams.get("customerId")) ?? 0;
  const setCustomerId = (id: number) => updateRoute({ customerId: id || null, contactsPage: null });
  const [customerKeyword, setCustomerKeyword] = useState("");
  const includeCompleted = searchParams.get("completed") === "true";
  const setIncludeCompleted = (value: boolean) => updateRoute({ completed: value, followupPage: null });
  const { pageNumber: followUpPageNumber, setPageNumber: setFollowUpPageNumber, pageSize: followUpPageSize, setPageSize: setFollowUpPageSize } = useDirectoryLocation("followup");
  const [followUpRevision, setFollowUpRevision] = useState(0);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const editFollowUpId = readRouteId(searchParams.get("editFollowUpId"));
  const [editingRecord, setEditingRecord] = useState<ApiCrmFollowUpDto | null>(null);
  const editingFollowUp = editingRecord?.id === editFollowUpId ? editingRecord : null;
  const setEditingFollowUp = (item: ApiCrmFollowUpDto | null) => { setEditingRecord(item); updateRoute({ editFollowUpId: item?.id }); };
  const transferMode = searchParams.get("followUpAction") === "transfer";
  const setTransferMode = (value: boolean) => updateRoute({ followUpAction: value ? "transfer" : null });
  const view = readCustomerView(searchParams.get("view") ?? (focusedFollowUpId || !canViewCustomers ? "followups" : null));
  const readScope = useMemo(() => ({}), [client, view, customerId, canViewCustomers]);
  const runAbortableOperation = useAbortableOperation(readScope, { cancelPrevious: true });
  const contactsPage = useCrmContactsPage(client, customerId, canViewContacts && (view === "contacts" || view === "followup-editor"));
  const { contacts, reloadContacts } = contactsPage;
  const commands = useFollowUpCommands({
    client, view, customerId, recordId: editFollowUpId, record: editingFollowUp, transfer: transferMode, timeZone: businessTimeZone,
    permissions: { create: canCreateFollowUp, edit: canEditFollowUp, complete: canCompleteFollowUp, restore: canRestoreFollowUp, assign: canAssignFollowUp, delete: canDeleteFollowUp },
    accept: setEditingFollowUp, feedback: setFeedback, refresh,
    complete: () => {
      commands.draft.reset();
      setEditingRecord(null);
      updateRoute({ view: searchParams.get("returnView") === "customer-followups" ? "customer-followups" : "followups", editFollowUpId: null, followUpAction: null, returnView: null }, false);
    },
  });
  const { draft: followUpDraft } = commands;
  const followUpContactId = optionalNumber(followUpDraft.values.crmContactId) ?? "";
  const setFollowUpContactId = (id: number | "") => followUpDraft.change("crmContactId", String(id));
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  useUnsavedChangesGuard({
    isDirty: view === "followup-editor" && (followUpDraft.isDirty || transferMode && Boolean(editingFollowUp && customerId !== editingFollowUp.crmCustomerId)),
    message: "当前客户跟进有未保存的修改。",
  });

  function applyView(nextView: CustomerTaskView) {
    const returnView = searchParams.get("returnView");
    const target = nextView === "followups" && view === "followup-editor" && returnView === "customer-followups" ? "customer-followups" : nextView;
    updateRoute({ view: target, returnView: nextView === "followup-editor" && view === "customer-followups" ? view : null }, false);
  }

  async function changeView(nextView: CustomerTaskView) {
    if (nextView === view) return true;
    if (!await confirmDiscardChanges("切换客户工作区")) return false;
    followUpDraft.reset();
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

  async function refresh() {
    setFollowUpRevision((value) => value + 1);
  }

  async function reloadCustomers(preferred?: ApiCrmCustomerDto, signal?: AbortSignal) {
    if (preferred) {
      signal?.throwIfAborted();
      setCustomers(current => [preferred, ...current.filter(item => item.id !== preferred.id)]);
      setCustomerId(preferred.id);
      await Promise.all([queryClient.invalidateQueries({ queryKey: queryKeys.crmCustomersRoot() }, { throwOnError: true }), queryClient.invalidateQueries({ queryKey: queryKeys.crmDashboard() }, { throwOnError: true })]);
      return;
    }
    const load = async (requestSignal: AbortSignal) => {
      const page = await client.queryCrmCustomers(
        { keyword: customerKeyword.trim(), status: "", pageNumber: 1, pageSize: 100 },
        { signal: requestSignal },
      );
      requestSignal.throwIfAborted();
      setCustomers(page.items);
      setCustomerId(0);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.crmCustomersRoot() }, { throwOnError: true }),
        queryClient.invalidateQueries({ queryKey: queryKeys.crmDashboard() }, { throwOnError: true }),
      ]);
    };
    await (signal ? load(signal) : runAbortableOperation(load));
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

  return {
    canViewCustomers, canViewFollowUps, canViewContacts, canCreateCustomer, canEditCustomer,
    canDeactivateCustomer, canDeleteCustomer, canImportCustomer, canExportCustomer, canCreateContact,
    canEditContact, canSetPrimaryContact, canDeleteContact, canCreateFollowUp, canEditFollowUp,
    canCompleteFollowUp, canRestoreFollowUp, canAssignFollowUp, canDeleteFollowUp, updateRoute,
    focusedFollowUpId, customers, setCustomers, contacts, customerId,
    setCustomerId, customerKeyword, setCustomerKeyword, includeCompleted, setIncludeCompleted,
    followUpPageNumber, setFollowUpPageNumber, followUpPageSize, setFollowUpPageSize,
    feedback, setFeedback, editFollowUpId, editingFollowUp, setEditingFollowUp,
    transferMode, setTransferMode, followUpContactId, setFollowUpContactId, followUpDraft, contactsPage,
    view, changeView, returnToList, selectedCustomer, followUpQuery,
    followUpPage, rows, loading, reloadCustomers, searchCustomerOptions,
    reloadContacts, ...commands,
  };
}
