import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { numberValue, readApiError, readRouteSuccessMessage } from "../../../ui/formUtils.ts";
import { useServerPageNumber } from "../../../ui/useServerPageNumber.ts";
import { loadListViewState, normalizeListPageSize, saveListViewState } from "../../../ui/listViewState.ts";
import { type MasterDataEntityConfig, type MasterDataEntityKey, type MasterDataRecord } from "../masterDataTypes.ts";
import { hsCodeVersionReference } from "../hsCodeMaintenance.ts";
import { useScopedSave } from "../../../ui/useScopedSave.ts";
import { isAbortError } from "../../../ui/useAbortableOperation.ts";
function buildMasterDataListViewStateStorageKey(entityKey: MasterDataEntityKey) {
  return `export-doc-manager.master-data.${entityKey}.list-view-state.v1`;
}

export function useMasterDataListWorkspace({ client, config, canManage }: {
  client: ExportDocManagerApiClient;
  config: MasterDataEntityConfig;
  canOperate: boolean;
  canManage: boolean;
  canViewMasterData: boolean;
}) {
  const listViewStateStorageKey = useMemo(() => buildMasterDataListViewStateStorageKey(config.key), [config.key]);
  const [initialListViewState] = useState(() => loadListViewState(listViewStateStorageKey));
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const [listSuccessMessage, setListSuccessMessage] = useState<string | null>(null);
  const [selectedRecords, setSelectedRecords] = useState<Map<number, MasterDataRecord>>(() => new Map());
  const selectedRecordIds = new Set(selectedRecords.keys());
  const [pendingDeleteRecord, setPendingDeleteRecord] = useState<MasterDataRecord | null>(null);
  const [batchDeleteDialogOpen, setBatchDeleteDialogOpen] = useState(false);
  const skipNextListViewStateSaveRef = useRef(false);
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const deletion = useScopedSave(config.key, client);
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const successMessage = listSuccessMessage ?? routeSuccessMessage;

  useEffect(() => {
    const nextViewState = loadListViewState(listViewStateStorageKey);
    skipNextListViewStateSaveRef.current = true;
    setKeyword(nextViewState.keyword);
    setCommittedKeyword(nextViewState.keyword);
    setPageNumber(1);
    setPageSize(nextViewState.pageSize);
    setListSuccessMessage(null);
    setSelectedRecords(new Map());
    setPendingDeleteRecord(null);
    setBatchDeleteDialogOpen(false);
  }, [config.key, listViewStateStorageKey]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const nextKeyword = keyword.trim();
      if (nextKeyword !== committedKeyword) {
        setCommittedKeyword(nextKeyword);
        setPageNumber(1);
        setListSuccessMessage(null);
        setSelectedRecords(new Map());
      }
    }, 350);

    return () => window.clearTimeout(timer);
  }, [committedKeyword, keyword]);

  const masterDataQuery = useQuery({
    queryKey: queryKeys.masterDataList(config.key, pageNumber, pageSize, committedKeyword.trim()),
    queryFn: ({ signal }) =>
      config.list(client, {
        keyword: committedKeyword.trim(),
        pageNumber,
        pageSize,
      }, signal),
    placeholderData: (previous, query) => query?.queryKey[1] === config.key ? previous : undefined,
  });

  useEffect(() => {
    setSelectedRecords(new Map());
  }, [committedKeyword, pageNumber, pageSize]);

  useEffect(() => {
    if (skipNextListViewStateSaveRef.current) {
      skipNextListViewStateSaveRef.current = false;
      return;
    }

    saveListViewState(listViewStateStorageKey, {
      keyword: committedKeyword,
      pageSize,
    });
  }, [committedKeyword, listViewStateStorageKey, pageSize]);

  useServerPageNumber(masterDataQuery, pageNumber, setPageNumber);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCommittedKeyword(keyword.trim());
    setPageNumber(1);
    setListSuccessMessage(null);
  }

  function handlePageSizeChange(nextPageSize: number) {
    setPageSize(normalizeListPageSize(nextPageSize));
    setPageNumber(1);
    setListSuccessMessage(null);
    setSelectedRecords(new Map());
  }

  function removeRecords(records: MasterDataRecord[], batch: boolean) {
    return deletion.run(async ({ signal, checkCurrent }) => {
      if (!canManage || masterDataQuery.isFetching || masterDataQuery.isPlaceholderData) return;
      const response = batch
        ? await client.deleteHsCodesBatch({ body: { ids: records.map(row => row.id), expectedVersions: records.map(hsCodeVersionReference) } }, { signal })
        : await config.delete(client, records[0], config.routeId(records[0]), signal);
      checkCurrent();
      setPendingDeleteRecord(null);
      setBatchDeleteDialogOpen(false);
      setSelectedRecords(new Map());
      const message = response.message || `${config.label}已删除。`;
      setListSuccessMessage(message);
      for (const record of records) queryClient.removeQueries({ queryKey: queryKeys.masterDataRecord(config.key, config.routeId(record)) });
      try {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot(config.key) }, { throwOnError: true }),
          ...(config.key === "customers" || config.key === "exporters"
            ? [queryClient.invalidateQueries({ queryKey: queryKeys.invoiceParties() }, { throwOnError: true })] : []),
        ]);
      } catch (error) {
        checkCurrent();
        setListSuccessMessage(`${message}列表刷新失败：${readApiError(error)}`);
      }
    });
  }
  const deleteMutation = useMutation({
    mutationFn: (record: MasterDataRecord) => removeRecords([record], false),
    onError: error => {
      if (isAbortError(error)) return;
      setPendingDeleteRecord(null);
      setListSuccessMessage(null);
    },
  });

  const batchDeleteHsCodesMutation = useMutation({
    mutationFn: (records: MasterDataRecord[]) => removeRecords(records, true),
    onError: error => {
      if (isAbortError(error)) return;
      setBatchDeleteDialogOpen(false);
      setListSuccessMessage(null);
    },
  });

  function handleDelete(record: MasterDataRecord) {
    if (!canManage || isBusy) {
      return;
    }

    setPendingDeleteRecord(record);
  }

  function toggleSelectedRecord(record: MasterDataRecord, selected: boolean) {
    if (isBusy) return;
    const id = numberValue(record.id);
    if (id <= 0) {
      return;
    }

    setSelectedRecords((current) => {
      const next = new Map(current);
      if (selected) {
        next.set(id, record);
      } else {
        next.delete(id);
      }

      return next;
    });
  }

  function toggleAllCurrentPageRecords(selected: boolean) {
    if (isBusy) return;
    const currentRows = page?.items ?? [];
    setSelectedRecords((current) => {
      const next = new Map(current);
      for (const record of currentRows) {
        const id = numberValue(record.id);
        if (id <= 0) {
          continue;
        }

        if (selected) {
          next.set(id, record);
        } else {
          next.delete(id);
        }
      }

      return next;
    });
  }

  function handleBatchDeleteHsCodes() {
    if (!canManage || isBusy || selectedRecordIds.size === 0) {
      return;
    }

    setBatchDeleteDialogOpen(true);
  }

  const page = masterDataQuery.data ?? null;
  const message = batchDeleteHsCodesMutation.isError && !isAbortError(batchDeleteHsCodesMutation.error)
    ? readApiError(batchDeleteHsCodesMutation.error)
    : deleteMutation.isError && !isAbortError(deleteMutation.error)
    ? readApiError(deleteMutation.error)
    : masterDataQuery.isError
      ? readApiError(masterDataQuery.error)
      : null;
  const isBusy = masterDataQuery.isFetching || masterDataQuery.isPlaceholderData || deletion.isSaving;
  return {
    keyword, setKeyword, pageNumber, setPageNumber, pageSize,
    setListSuccessMessage, selectedRecords, setSelectedRecords, selectedRecordIds, pendingDeleteRecord,
    setPendingDeleteRecord, batchDeleteDialogOpen, setBatchDeleteDialogOpen, navigate, queryClient,
    successMessage, masterDataQuery, handleSearch, handlePageSizeChange, deleteMutation,
    batchDeleteHsCodesMutation, handleDelete, toggleSelectedRecord, toggleAllCurrentPageRecords, handleBatchDeleteHsCodes,
    page, message, isBusy,
  };
}
