import { useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { ArrowLeft,Edit3,Eye,Save,Trash2 } from "lucide-react";
import { FormEvent,useEffect,useMemo,useRef,useState } from "react";
import { useLocation,useNavigate,useParams } from "react-router-dom";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import {
normalizeText,
numberValue,
readApiError,
readRouteSuccessMessage
} from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ServerDraftUpdateNotice, useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { ConcurrencyConflictNotice, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import {
hasCustomOptionValue,
loadCustomOptionMap,
masterDataCustomOptionTypes
} from "../custom-options/customOptionModel.ts";

import {
productHsCodeLookupPageSize,
productUnitLookupTargets,
type MasterDataEntityConfig,
type MasterDataFieldDefinition,
type MasterDataRecord,
type ProductAssistanceField,
type ProductUnitSourceField,
type ProductUnitTargetField
} from "./masterDataTypes.ts";

import {
applyHsCodeToProductRecord,
autoPopulateProductUnitFields,
autoPopulateSingleProductUnitField,
buildMasterDataDisplayName,
buildProductHsCodeLookup,
buildProductInputAssistance,
buildProductUnitAssistance,
isProductUnitField,
isProductUnitTargetField,
normalizeHsCodeKey,
readString
} from "./masterDataModel.ts";


import { MasterDataField } from "./MasterDataField.tsx";
import { useMasterDataPersistence } from "./useMasterDataPersistence.ts";
import { HsCodeObsoleteAction } from "./HsCodeObsoleteAction.tsx";

const masterDataCustomOptionTypeSet = new Set<string>(masterDataCustomOptionTypes);

export function MasterDataEditorPage({
  client,
  config,
  mode,
  canOperate,
  canManage,
}: {
  client: ExportDocManagerApiClient;
  config: MasterDataEntityConfig;
  mode: "new" | "edit";
  canOperate: boolean;
  canManage: boolean;
}) {
  const { recordKey } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const [record, setRecord] = useState<MasterDataRecord | null>(() => (mode === "new" ? config.emptyRecord() : null));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(routeSuccessMessage);
  const [productUnitMessage, setProductUnitMessage] = useState<string | null>(null);
  const [autoFilledProductUnits, setAutoFilledProductUnits] = useState<Partial<Record<ProductUnitTargetField, string>>>({});
  const [productHsCodeKeyword, setProductHsCodeKeyword] = useState("");
  const [persistedRecord, setPersistedRecord] = useState<MasterDataRecord | null>(null);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [hasConcurrencyConflict, setHasConcurrencyConflict] = useState(false);
  const queryClient = useQueryClient();
  const customOptionTypes = useMemo(
    () =>
      Array.from(
        new Set(
          config.sections
            .flatMap((section) => section.fields)
            .map((field) => field.customOptionType)
            .filter(
              (optionType): optionType is string =>
                typeof optionType === "string" && masterDataCustomOptionTypeSet.has(optionType),
            ),
        ),
      ),
    [config],
  );

  const isNew = mode === "new";
  const effectiveRecordKey = recordKey ?? "";
  const isRecordKeyValid = isNew || effectiveRecordKey.trim().length > 0;
  const resourceScope = `${config.key}:${isNew ? "new" : effectiveRecordKey}`;
  const runAbortableOperation = useAbortableOperation(resourceScope);
  const runHsLookup = useAbortableOperation(resourceScope, { cancelPrevious: true });
  const latestRecord = useRef(record); latestRecord.current = record;
  const persistedRecordSnapshot = useMemo(() => persistedRecord
    ? buildMasterDataSnapshot(config, persistedRecord, numberValue(persistedRecord.id)) : null, [config, persistedRecord]);

  const detailQuery = useQuery({
    queryKey: queryKeys.masterDataRecord(config.key, effectiveRecordKey),
    queryFn: ({ signal }) => config.get(client, effectiveRecordKey, signal),
    enabled: !isNew && isRecordKeyValid,
  });

  const customOptionsQuery = useQuery({
    queryKey: queryKeys.customOptionsGroup(`master-data-${config.key}`),
    queryFn: ({ signal }) => loadCustomOptionMap(client, customOptionTypes, signal),
    enabled: customOptionTypes.length > 0,
    staleTime: 5 * 60 * 1000,
  });

  const productUnitProductsQuery = useQuery({
    queryKey: queryKeys.masterDataList("products", 1, productHsCodeLookupPageSize, ""),
    queryFn: ({ signal }) => client.listProducts({ pageNumber: 1, pageSize: productHsCodeLookupPageSize }, { signal }),
    enabled: config.key === "products",
    staleTime: 5 * 60 * 1000,
  });

  const productUnitUnitsQuery = useQuery({
    queryKey: queryKeys.masterDataRoot("units"),
    queryFn: ({ signal }) => client.listUnits({}, { signal }),
    enabled: config.key === "products",
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (config.key !== "products") return undefined;
    const keyword = normalizeHsCodeKey(record ? readString(record, "hsCode") : "");
    const timer = window.setTimeout(() => setProductHsCodeKeyword(keyword), 250);
    return () => window.clearTimeout(timer);
  }, [config.key, record]);

  const productHsCodesQuery = useQuery({
    queryKey: queryKeys.masterDataList("hs-codes", 1, 20, productHsCodeKeyword),
    queryFn: ({ signal }) => client.listHsCodes({ pageNumber: 1, pageSize: 20, keyword: productHsCodeKeyword }, { signal }),
    enabled: config.key === "products" && productHsCodeKeyword.length >= 4,
    staleTime: 60 * 1000,
  });

  useEffect(() => {
    if (isNew) {
      const nextRecord = config.emptyRecord();
      setRecord(nextRecord);
      setPersistedRecord(nextRecord);
      setMessage(null);
      setProductUnitMessage(null);
      setAutoFilledProductUnits({});
      setSuccessMessage(null);
      return;
    }

    if (!isRecordKeyValid) {
      setRecord(null);
      setPersistedRecord(null);
      setMessage(`${config.label} ID 无效。`);
      setProductUnitMessage(null);
      setAutoFilledProductUnits({});
      setSuccessMessage(null);
    }
  }, [config, isNew, isRecordKeyValid]);

  useEffect(() => {
    if (!isNew && detailQuery.isError) {
      setMessage(readApiError(detailQuery.error));
      setSuccessMessage(null);
    }
  }, [detailQuery.error, detailQuery.isError, isNew]);

  const { saveMutation, deleteMutation, sealUploadMutation, isSaving } = useMasterDataPersistence({
    client, config, record, isNew, recordKey: effectiveRecordKey,
    onSaved: (saved, draft) => { setRecord(draft); setPersistedRecord(saved); },
    onDeleteError: () => setDeleteDialogOpen(false),
    setMessage, setSuccessMessage, setHasConcurrencyConflict,
  });

  const saveCustomOptionMutation = useMutation({
    mutationFn: ({ optionType, value }: { optionType: string; value: string }) =>
      runAbortableOperation((signal) => client.saveCustomOption({
        optionType,
        body: { value },
      }, { signal })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
    },
  });

  const isBusy = detailQuery.isFetching || isSaving;
  const customOptions = customOptionsQuery.data ?? {};
  const productInputAssistance = useMemo(
    () => buildProductInputAssistance(productUnitProductsQuery.data?.items ?? [], productHsCodesQuery.data?.items ?? []),
    [productHsCodesQuery.data?.items, productUnitProductsQuery.data],
  );
  const productHsCodeLookup = useMemo(
    () => buildProductHsCodeLookup(productHsCodesQuery.data?.items ?? []),
    [productHsCodesQuery.data?.items],
  );
  const productUnitAssistance = useMemo(
    () => buildProductUnitAssistance(productUnitProductsQuery.data?.items ?? [], productUnitUnitsQuery.data ?? []),
    [productUnitProductsQuery.data, productUnitUnitsQuery.data],
  );
  const productUnitLookupMessage =
    config.key === "products" &&
    (productUnitProductsQuery.isError || productUnitUnitsQuery.isError || productHsCodesQuery.isError)
      ? readApiError(productUnitProductsQuery.error ?? productUnitUnitsQuery.error ?? productHsCodesQuery.error)
      : null;
  const title = isNew ? config.newLabel : record ? readString(record, config.primaryField) || config.editLabel : config.editLabel;
  const currentRecordSnapshot = useMemo(
    () => (record ? buildMasterDataSnapshot(config, record, isNew ? 0 : numberValue(record.id)) : null),
    [config, isNew, record],
  );
  const hasUnsavedRecordChanges = Boolean(
    canOperate &&
    record &&
      persistedRecordSnapshot &&
      currentRecordSnapshot &&
      currentRecordSnapshot !== persistedRecordSnapshot,
  );
  const sealUploadBlocked = isNew || hasUnsavedRecordChanges;
  const serverDraftSync = useServerDraftSync({
    resourceKey: isNew ? `${config.key}:new` : `${config.key}:${effectiveRecordKey}`,
    incomingValue: isNew ? null : detailQuery.data,
    persistedValue: persistedRecord,
    isDirty: hasUnsavedRecordChanges,
    fingerprint: (serverRecord) => buildMasterDataSnapshot(config, serverRecord, numberValue(serverRecord.id)),
    applyIncoming: (serverRecord) => {
      const continuation = location.state?.masterDataSaveContinuation;
      const continued = continuation?.entityKey === config.key && continuation.record?.id === serverRecord.id
        && continuation.record?.rowVersion === serverRecord.rowVersion ? continuation.record as MasterDataRecord : null;
      setRecord(continued ?? serverRecord);
      setPersistedRecord(serverRecord);
      if (continued) navigate(location.pathname + location.search, { replace: true, state: { successMessage: routeSuccessMessage } });
      setMessage(null);
      setProductUnitMessage(null);
      setAutoFilledProductUnits({});
      if (routeSuccessMessage && !successMessage) {
        setSuccessMessage(routeSuccessMessage);
      }
    },
  });
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedRecordChanges,
    message: `当前${config.label}有未保存的修改。`,
  });

  function patchRecord(name: string, value: string | number) {
    if (!canOperate) {
      return;
    }

    setRecord((current) => current
      ? config.applyFieldChange?.(current, name, value) ?? { ...current, [name]: value }
      : current);
    if (isProductUnitTargetField(name)) {
      setAutoFilledProductUnits((current) => {
        const currentAutoValue = current[name];
        if (!currentAutoValue || normalizeText(String(value)) === currentAutoValue) {
          return current;
        }

        const next = { ...current };
        delete next[name];
        return next;
      });
    }

    if (isProductUnitField(name)) {
      setProductUnitMessage(null);
    }

    setSuccessMessage(null);
  }

  function saveCurrentMasterDataDraft() {
    if (!canOperate || !record || isBusy || (!isNew && !isRecordKeyValid)) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    const preparedRecord = config.key === "products"
      ? autoPopulateProductUnitFields(record, productUnitAssistance, autoFilledProductUnits).record
      : record;
    saveMutation.mutate({ submitted: record, body: config.normalizeRecord(preparedRecord, isNew ? 0 : numberValue(record.id)) });
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveCurrentMasterDataDraft();
  }

  useEffect(() => {
    function handleDocumentKeyDown(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        saveCurrentMasterDataDraft();
      }
    }

    window.addEventListener("keydown", handleDocumentKeyDown);
    return () => window.removeEventListener("keydown", handleDocumentKeyDown);
  });

  function commitProductUnitField(sourceField: ProductUnitSourceField) {
    if (!canOperate || config.key !== "products" || !record) {
      return;
    }

    const result = autoPopulateSingleProductUnitField(
      record,
      sourceField,
      productUnitAssistance,
      autoFilledProductUnits,
    );

    if (!result.changed) {
      if (result.candidateCount > 1) {
        const target = productUnitLookupTargets[sourceField];
        setProductUnitMessage(`${readString(record, sourceField)} 有多个${target.targetLabel}候选，请在${target.targetLabel}字段中选择。`);
      }
      return;
    }

    setRecord(result.record);
    setAutoFilledProductUnits((current) => ({ ...current, [result.targetField]: result.autoFilledValue }));
    setProductUnitMessage(`已回填${result.targetLabel}：${result.autoFilledValue}`);
    setSuccessMessage(null);
  }

  async function commitProductAssistanceField(field: ProductAssistanceField, value: string) {
    if (!canOperate || config.key !== "products" || field !== "hsCode" || !record) {
      return;
    }

    const normalizedValue = normalizeHsCodeKey(value);
    let hsCode = productHsCodeLookup.get(normalizedValue);
    if (!hsCode && normalizedValue.length >= 4) {
      try {
        const result = await runHsLookup((signal) => client.listHsCodes(
          { pageNumber: 1, pageSize: 20, keyword: normalizedValue },
          { signal },
        ));
        hsCode = result.items.find(item => normalizeHsCodeKey(item.code || item.normalizedCode) === normalizedValue && item.status === "Active" && !item.requiresAnnualReview);
      } catch (error) {
        if (isAbortError(error)) return;
        if (normalizeHsCodeKey(readString(latestRecord.current ?? { id: 0 }, "hsCode")) !== normalizedValue) return;
        setProductUnitMessage(readApiError(error));
        return;
      }
    }
    if (normalizeHsCodeKey(readString(latestRecord.current ?? { id: 0 }, "hsCode")) !== normalizedValue) return;
    if (!hsCode) {
      setProductUnitMessage("未在已验证的年度税则中找到完全一致的有效 HS 编码。");
      return;
    }

    const current = latestRecord.current;
    if (!current || normalizeHsCodeKey(readString(current, "hsCode")) !== normalizedValue) return;
    const result = applyHsCodeToProductRecord(current, hsCode);
    if (!result.changed) {
      return;
    }

    setRecord(result.record);
    setProductUnitMessage(`已套用本地 HS 编码 ${hsCode.code || hsCode.normalizedCode}，回填：${result.appliedLabels.join("、")}`);
    setSuccessMessage(null);
  }

  function commitMasterDataCustomOption(optionType: string, value: string) {
    if (!canOperate) {
      return;
    }

    const normalizedValue = normalizeText(value);
    if (!normalizedValue || hasCustomOptionValue(customOptions, optionType, normalizedValue)) {
      return;
    }

    saveCustomOptionMutation.mutate({ optionType, value: normalizedValue });
  }

  function uploadMasterDataSeal(field: MasterDataFieldDefinition, file: File) {
    if (!canOperate || config.key !== "exporters" || field.pathPicker !== "exporterSealImage") return;
    if (isNew || numberValue(record?.id) <= 0) {
      setMessage("请先保存出口商基础资料，再上传印章图片。");
      setSuccessMessage(null);
      return;
    }
    if (hasUnsavedRecordChanges) {
      setMessage("当前出口商还有未保存的修改，请先保存后再上传印章图片。");
      setSuccessMessage(null);
      return;
    }

    const fieldName = field.name === "docSealPath" ? "docSealPath" : "customsSealPath";
    sealUploadMutation.mutate({ fieldName, file });
  }

  function handleDelete() {
    if (!canManage || isNew || !record || deleteMutation.isPending) {
      return;
    }

    setDeleteDialogOpen(true);
  }

  async function loadLatestRecord() {
    if (isNew || detailQuery.isFetching) return;
    setHasConcurrencyConflict(false);
    setMessage(null);
    setSuccessMessage(null);
    await detailQuery.refetch();
  }

  async function handleBackToMasterDataList() {
    if (await confirmDiscardChanges(`返回${config.label}列表`)) {
      navigate(`/master-data/${config.key}`);
    }
  }

  return (
    <section className="editor-surface" aria-label={isNew ? config.newLabel : config.editLabel}>
      <div className="editor-toolbar">
        <button className="command-button secondary" type="button" onClick={handleBackToMasterDataList}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回列表</span>
        </button>
        <div className="editor-title">
          {canOperate ? <Edit3 size={18} aria-hidden="true" /> : <Eye size={18} aria-hidden="true" />}
          <span>{title}</span>
        </div>
        {!isNew && isRecordKeyValid && canManage ? (
          <button
            className="command-button secondary danger"
            type="button"
            disabled={isBusy || !record}
            onClick={handleDelete}
          >
            <Trash2 size={17} aria-hidden="true" />
            <span>删除</span>
          </button>
        ) : null}
        {config.key === "hs-codes" && !isNew && canManage && record && <HsCodeObsoleteAction client={client} records={[record]}
          disabled={isBusy || hasUnsavedRecordChanges} onChanged={async () => {
            await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("hs-codes") });
          }} />}
      </div>

      {message && hasConcurrencyConflict ? <ConcurrencyConflictNotice message={message} isBusy={detailQuery.isFetching} onReload={() => void loadLatestRecord()} /> : null}
      {serverDraftSync.hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel={config.label}
        onKeepLocal={serverDraftSync.keepLocalDraft}
        onLoadServer={serverDraftSync.loadServerVersion}
      /> : null}
      {message && !hasConcurrencyConflict ? <InlineNotice tone="error" title="操作未完成">{message}</InlineNotice> : null}
      {productUnitLookupMessage ? <InlineNotice tone="warning" title="单位资料未能完整加载">{productUnitLookupMessage}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {productUnitMessage ? <InlineNotice tone="info">{productUnitMessage}</InlineNotice> : null}
      {!canOperate ? (
        <PermissionNotice>当前主数据记录为只读；字段修改、候选项新增和保存已禁用。</PermissionNotice>
      ) : null}

      {!record && isBusy ? <PageState tone="loading" title="正在加载主数据" description="正在读取记录详情和可选参考资料。" /> : null}

      {record ? (
        <form className="entity-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <fieldset className="permission-fieldset" disabled={!canOperate}>
          {config.sections.map((section, sectionIndex) => (
            <section className="form-section" aria-label={section.title} key={section.title}>
              <div className="section-header">
                <h2>{section.title}</h2>
                {sectionIndex === 0 && canOperate ? (
                  <button className="command-button" type="submit" disabled={isBusy}>
                    <Save size={17} aria-hidden="true" />
                    <span>保存</span>
                  </button>
                ) : null}
              </div>
              <div className="field-grid">
                {section.fields.filter((field) => !field.visibleWhen || field.visibleWhen(record)).map((field) => (
                  <MasterDataField
                    field={field}
                    isEdit={!isNew}
                    key={field.name}
                    record={record}
                    customOptions={customOptions}
                    productInputAssistance={productInputAssistance}
                    productUnitAssistance={productUnitAssistance}
                    onChange={(value) => patchRecord(field.name, value)}
                    onCommitCustomOption={commitMasterDataCustomOption}
                    onCommitProductAssistance={commitProductAssistanceField}
                    onCommitProductUnit={commitProductUnitField}
                    onUploadPath={(file) => uploadMasterDataSeal(field, file)}
                    onPathError={(error) => {
                      setMessage(readApiError(error));
                      setSuccessMessage(null);
                    }}
                    pathActionDisabled={isBusy || sealUploadBlocked}
                    pathActionTitle={sealUploadBlocked
                      ? isNew
                        ? "请先保存出口商后上传印章"
                        : "请先保存当前修改后上传印章"
                      : undefined}
                  />
                ))}
              </div>
            </section>
          ))}
          </fieldset>
        </form>
      ) : null}
      {deleteDialogOpen && record ? (
        <ConfirmationDialog
          title={`删除${config.label}`}
          description={`确定删除“${buildMasterDataDisplayName(config, record) || `#${effectiveRecordKey}`}”吗？`}
          details={config.key === "hs-codes" ? ["只删除目录记录，历史单据、商品、申报实例及替代关系保留。", "使用读取时版本，记录变化后拒绝删除；年度淘汰可先确认作废再按保留期清理。"] : ["删除后无法在列表中继续查看。", "如该资料已被业务单据引用，系统会拒绝删除并说明原因。"]}
          confirmLabel="确认删除"
          isBusy={deleteMutation.isPending}
          onCancel={() => setDeleteDialogOpen(false)}
          onConfirm={() => {
            setMessage(null);
            setSuccessMessage(null);
            deleteMutation.mutate();
          }}
        />
      ) : null}
    </section>
  );
}

function buildMasterDataSnapshot(config: MasterDataEntityConfig, record: MasterDataRecord, id: number) {
  return JSON.stringify(config.normalizeRecord(record, id));
}
