import { useRef } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { mergeSavedDraftFields } from "../../ui/recordDraftMerge.ts";
import { isConcurrencyConflict, numberValue, readApiError } from "../../ui/formUtils.ts";
import type { MasterDataEntityConfig, MasterDataRecord } from "./masterDataTypes.ts";

export function useMasterDataPersistence({ client, config, record, isNew, recordKey, onSaved, onDeleteError, setMessage, setSuccessMessage, setHasConcurrencyConflict }: {
  client: ExportDocManagerApiClient; config: MasterDataEntityConfig; record: MasterDataRecord | null;
  isNew: boolean; recordKey: string;
  onSaved: (saved: MasterDataRecord, draft: MasterDataRecord) => void;
  onDeleteError: () => void;
  setMessage: (value: string | null) => void; setSuccessMessage: (value: string | null) => void;
  setHasConcurrencyConflict: (value: boolean) => void;
}) {
  const queries = useQueryClient(), navigate = useNavigate();
  const scope = `${config.key}:${isNew ? "new" : recordKey}`;
  const boundary = useScopedSave(scope, client);
  const latest = useRef(record); latest.current = record;
  const editableFields = config.sections.flatMap(section => section.fields.map(field => field.name));
  const onError = (error: unknown) => {
    if (isAbortError(error)) return;
    setMessage(readApiError(error)); setSuccessMessage(null);
    setHasConcurrencyConflict(!isNew && isConcurrencyConflict(error));
  };
  const refresh = () => Promise.all([
    queries.invalidateQueries({ queryKey: queryKeys.masterDataRoot(config.key) }),
    ...(config.key === "customers" || config.key === "exporters"
      ? [queries.invalidateQueries({ queryKey: queryKeys.invoiceParties() })] : []),
  ]);
  function persist(submitted: MasterDataRecord, write: (signal: AbortSignal) => Promise<MasterDataRecord>, message: string) {
    return boundary.run(async ({ signal, checkCurrent, continueInScope }) => {
      const saved = await write(signal); checkCurrent();
      const draft = mergeSavedDraftFields(latest.current ?? submitted, submitted, saved, editableFields);
      onSaved(saved, draft);
      setMessage(null); setHasConcurrencyConflict(false); setSuccessMessage(message);
      queries.setQueryData(queryKeys.masterDataRecord(config.key, config.routeId(saved)), saved);
      if (isNew) {
        continueInScope(`${config.key}:${config.routeId(saved)}`);
        navigate(`/master-data/${config.key}/${config.routeId(saved)}`, { replace: true, state: {
          successMessage: message, masterDataSaveContinuation: { entityKey: config.key, record: draft },
        } });
      }
      try { await refresh(); }
      catch (error) { checkCurrent(); setMessage(`${config.label}已保存，但目录刷新失败：${readApiError(error)}`); }
    });
  }
  const saveMutation = useMutation({
    mutationFn: ({ submitted, body }: { submitted: MasterDataRecord; body: MasterDataRecord }) => persist(submitted,
      signal => isNew ? config.create(client, body, signal) : config.update(client, recordKey, body, signal),
      isNew ? `${config.label}已创建。` : `${config.label}已保存。`),
    onError,
  });
  const sealUploadMutation = useMutation({
    mutationFn: ({ fieldName, file }: { fieldName: "docSealPath" | "customsSealPath"; file: File }) => {
      if (!record) throw new Error("出口商未加载，无法上传印章。");
      return persist(record, async signal => await client.uploadExporterSeal({
        id: numberValue(record.id), sealType: fieldName === "docSealPath" ? "document" : "customs", fileName: file.name, body: file,
      }, { signal }) as unknown as MasterDataRecord, fieldName === "docSealPath" ? "单证章已上传并保存。" : "报关章已上传并保存。");
    },
    onError,
  });
  const deleteMutation = useMutation({
    mutationFn: () => boundary.run(async ({ signal, checkCurrent }) => {
      if (!record) throw new Error(`${config.label}未加载，无法删除。`);
      const response = await config.delete(client, record, recordKey, signal); checkCurrent();
      queries.removeQueries({ queryKey: queryKeys.masterDataRecord(config.key, recordKey) });
      queries.removeQueries({ queryKey: queryKeys.masterDataRecord(config.key, config.routeId(record)) });
      navigate(`/master-data/${config.key}`, { replace: true, state: { successMessage: response.message || `${config.label}已删除。` } });
      await refresh();
    }),
    onError: error => { if (!isAbortError(error)) onDeleteError(); onError(error); },
  });
  return { saveMutation, sealUploadMutation, deleteMutation, isSaving: boundary.isSaving };
}
