import { type FormEvent, useEffect, useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiInvoiceListItemDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { useModulePermission, usePermission } from "../../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../../app/permissionCatalog.ts";
import { useWorkspaceDeviceProfile } from "../../../app/workspaceDevice.ts";
import { useServerPageNumber } from "../../../ui/useServerPageNumber.ts";
import { readApiError, readRouteSuccessMessage } from "../../../ui/formUtils.ts";
import { loadListViewState, normalizeListPageSize, saveListViewState } from "../../../ui/listViewState.ts";
import { readDefaultExportDirectory } from "../../settings/settingsPaths.ts";
import { useInvoiceListSingleWindowOperations } from "../useInvoiceListSingleWindowOperations.ts";
import { useInvoiceListActions } from "./useInvoiceListActions.ts";

const invoiceListViewStateStorageKey = "export-doc-manager.invoice-list-view-state.v1";

export function useInvoiceListWorkspace({ client }: { client: ExportDocManagerApiClient }) {
  const invoicePermission = useModulePermission("document.invoices");
  const excelPermission = useModulePermission("document.excel");
  const singleWindowPermission = useModulePermission("document.single-window");
  const jobPermission = useModulePermission("document.jobs");
  const reportPermission = useModulePermission("document.reports");
  const invoicePackageExportPermission = usePermission(permissionResources.invoiceOutput, permissionActions.exportZip);
  const workspaceDeviceProfile = useWorkspaceDeviceProfile();
  const workspaceDeviceMode = workspaceDeviceProfile.mode;
  const workspaceDeviceCapabilities = workspaceDeviceProfile.capabilities;
  const canAuthorizeBatchExport = invoicePackageExportPermission.allowed && jobPermission.canOperate && reportPermission.canView;
  const canBatchExport = canAuthorizeBatchExport && workspaceDeviceCapabilities.canImportExport;
  const [batchInvoices, setBatchInvoices] = useState<ApiInvoiceListItemDto[]>([]);
  const [initialListViewState] = useState(() => loadListViewState(invoiceListViewStateStorageKey));
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const navigate = useNavigate();
  const location = useLocation();
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const queryClient = useQueryClient();
  const invoicesQuery = useQuery({
    queryKey: queryKeys.invoices(pageNumber, pageSize, committedKeyword.trim()),
    queryFn: ({ signal }) => client.listInvoices({ pageNumber, pageSize, keyword: committedKeyword.trim() || undefined, sortColumn: "InvoiceDate", ascending: false }, { signal }),
    placeholderData: keepPreviousData,
  });
  const settingsQuery = useQuery({ queryKey: queryKeys.settings(), queryFn: ({ signal }) => client.getSettings({ signal }), staleTime: 5 * 60 * 1000 });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  const actions = useInvoiceListActions({ client, queryClient, navigate, defaultExportDirectory, canOperate: invoicePermission.canOperate, canImportExcel: excelPermission.canOperate, canExport: invoicePackageExportPermission.allowed });
  const singleWindow = useInvoiceListSingleWindowOperations({ client, queryClient, defaultExportDirectory, canView: singleWindowPermission.canView, canOperate: singleWindowPermission.canOperate, canExportBookingSheet: excelPermission.canOperate });
  useServerPageNumber(invoicesQuery, pageNumber, setPageNumber);
  useEffect(() => { saveListViewState(invoiceListViewStateStorageKey, { keyword: committedKeyword, pageSize }); }, [committedKeyword, pageSize]);

  function handleSearch(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const next = keyword.trim(); setKeyword(next); setCommittedKeyword(next); setPageNumber(1); }
  function handleResetSearch() { setKeyword(""); setCommittedKeyword(""); setPageNumber(1); }
  function handlePageSizeChange(value: number) { setPageSize(normalizeListPageSize(value)); setPageNumber(1); }

  return { ...actions, invoicePermission, excelPermission, singleWindowPermission, batchInvoices, setBatchInvoices,
    invoicePackageExportPermission, workspaceDeviceMode, workspaceDeviceCapabilities, canAuthorizeBatchExport, canBatchExport,
    keyword, setKeyword, committedKeyword, pageNumber, setPageNumber, pageSize, navigate, routeSuccessMessage, invoicesQuery,
    singleWindow, handleSearch, handleResetSearch, handlePageSizeChange, openSingleWindowPanel: singleWindow.open,
    invoices: invoicesQuery.data ?? null, message: invoicesQuery.isError ? readApiError(invoicesQuery.error) : actions.actionMessage,
    isBusy: invoicesQuery.isFetching || actions.actionsBusy || singleWindow.isBusy };
}
