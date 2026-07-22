import { useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { ArrowLeft,Edit3,Eye,Save,Trash2 } from "lucide-react";
import { FormEvent,useEffect,useMemo,useState } from "react";
import { useLocation,useNavigate,useParams } from "react-router-dom";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectExporterSealImageFile } from "../../desktop/desktopBridge.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import {
normalizeText,
numberValue,
isConcurrencyConflict,
readApiError,
readRouteSuccessMessage
} from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
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
  const [persistedRecordSnapshot, setPersistedRecordSnapshot] = useState<string | null>(null);
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

  const detailQuery = useQuery({
    queryKey: queryKeys.masterDataRecord(config.key, effectiveRecordKey),
    queryFn: () => config.get(client, effectiveRecordKey),
    enabled: !isNew && isRecordKeyValid,
  });

  const customOptionsQuery = useQuery({
    queryKey: queryKeys.customOptionsGroup(`master-data-${config.key}`),
    queryFn: () => loadCustomOptionMap(client, customOptionTypes),
    enabled: customOptionTypes.length > 0,
    staleTime: 5 * 60 * 1000,
  });

  const productUnitProductsQuery = useQuery({
    queryKey: queryKeys.masterDataList("products", 1, productHsCodeLookupPageSize, ""),
    queryFn: () => client.listProducts({}),
    enabled: config.key === "products",
    staleTime: 5 * 60 * 1000,
  });

  const productUnitUnitsQuery = useQuery({
    queryKey: queryKeys.masterDataList("units", 1, productHsCodeLookupPageSize, ""),
    queryFn: () => client.listUnits({}),
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
    queryFn: () => client.listHsCodes({ pageNumber: 1, pageSize: 20, keyword: productHsCodeKeyword }),
    enabled: config.key === "products" && productHsCodeKeyword.length >= 4,
    staleTime: 60 * 1000,
  });

  useEffect(() => {
    if (isNew) {
      const nextRecord = config.emptyRecord();
      setRecord(nextRecord);
      setPersistedRecordSnapshot(buildMasterDataSnapshot(config, nextRecord, 0));
      setMessage(null);
      setProductUnitMessage(null);
      setAutoFilledProductUnits({});
      setSuccessMessage(null);
      return;
    }

    if (!isRecordKeyValid) {
      setRecord(null);
      setPersistedRecordSnapshot(null);
      setMessage(`${config.label} ID 无效。`);
      setProductUnitMessage(null);
      setAutoFilledProductUnits({});
      setSuccessMessage(null);
    }
  }, [config, isNew, isRecordKeyValid]);

  useEffect(() => {
    if (!isNew && detailQuery.data) {
      setRecord(detailQuery.data);
      setPersistedRecordSnapshot(buildMasterDataSnapshot(config, detailQuery.data, numberValue(detailQuery.data.id)));
      setMessage(null);
      setProductUnitMessage(null);
      setAutoFilledProductUnits({});
      if (routeSuccessMessage && !successMessage) {
        setSuccessMessage(routeSuccessMessage);
      }
    }
  }, [detailQuery.data, isNew, routeSuccessMessage, successMessage]);

  useEffect(() => {
    if (!isNew && detailQuery.isError) {
      setMessage(readApiError(detailQuery.error));
      setSuccessMessage(null);
    }
  }, [detailQuery.error, detailQuery.isError, isNew]);

  const saveMutation = useMutation({
    mutationFn: (body: MasterDataRecord) =>
      isNew ? config.create(client, body) : config.update(client, effectiveRecordKey, body),
    onSuccess: async (saved) => {
      const nextMessage = isNew ? `${config.label}已创建。` : `${config.label}已保存。`;
      setRecord(saved);
      setPersistedRecordSnapshot(buildMasterDataSnapshot(config, saved, numberValue(saved.id)));
      setMessage(null);
      setSuccessMessage(nextMessage);
      setHasConcurrencyConflict(false);
      queryClient.setQueryData(queryKeys.masterDataRecord(config.key, config.routeId(saved)), saved);
      await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot(config.key) });
      if (isNew) {
        navigate(`/master-data/${config.key}/${config.routeId(saved)}`, {
          replace: true,
          state: { successMessage: nextMessage },
        });
      }
    },
    onError: (error) => {
      const errorMessage = readApiError(error);
      setMessage(errorMessage);
      setSuccessMessage(null);
      setHasConcurrencyConflict(
        !isNew &&
        isConcurrencyConflict(error),
      );
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => {
      if (!record) {
        throw new Error(`${config.label}未加载，无法删除。`);
      }

      return config.delete(client, record, effectiveRecordKey);
    },
    onSuccess: async (response) => {
      const nextMessage = response.message || `${config.label}已删除。`;
      setMessage(null);
      setSuccessMessage(null);
      queryClient.removeQueries({ queryKey: queryKeys.masterDataRecord(config.key, effectiveRecordKey) });
      if (record) {
        queryClient.removeQueries({ queryKey: queryKeys.masterDataRecord(config.key, config.routeId(record)) });
      }

      const invalidations = [queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot(config.key) })];
      if (config.key === "customers" || config.key === "exporters") {
        invalidations.push(queryClient.invalidateQueries({ queryKey: queryKeys.invoiceParties() }));
      }

      await Promise.all(invalidations);
      navigate(`/master-data/${config.key}`, {
        replace: true,
        state: { successMessage: nextMessage },
      });
    },
    onError: (error) => {
      setDeleteDialogOpen(false);
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveCustomOptionMutation = useMutation({
    mutationFn: ({ optionType, value }: { optionType: string; value: string }) =>
      client.saveCustomOption({
        optionType,
        body: { value },
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
    },
  });

  const isBusy = detailQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;
  const customOptions = customOptionsQuery.data ?? {};
  const productInputAssistance = useMemo(
    () => buildProductInputAssistance(productUnitProductsQuery.data ?? [], productHsCodesQuery.data?.items ?? []),
    [productHsCodesQuery.data?.items, productUnitProductsQuery.data],
  );
  const productHsCodeLookup = useMemo(
    () => buildProductHsCodeLookup(productHsCodesQuery.data?.items ?? []),
    [productHsCodesQuery.data?.items],
  );
  const productUnitAssistance = useMemo(
    () => buildProductUnitAssistance(productUnitProductsQuery.data ?? [], productUnitUnitsQuery.data ?? []),
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
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedRecordChanges,
    message: `当前${config.label}有未保存的修改。`,
  });

  function patchRecord(name: string, value: string | number) {
    if (!canOperate) {
      return;
    }

    setRecord((current) => (current ? { ...current, [name]: value } : current));
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
    saveMutation.mutate(config.normalizeRecord(preparedRecord, isNew ? 0 : numberValue(record.id)));
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
        const result = await client.listHsCodes({ pageNumber: 1, pageSize: 20, keyword: normalizedValue });
        hsCode = result.items.find(item => normalizeHsCodeKey(item.code || item.normalizedCode) === normalizedValue && item.status === "Active");
      } catch (error) {
        setProductUnitMessage(readApiError(error));
        return;
      }
    }
    if (!hsCode) {
      setProductUnitMessage("未在已验证的年度税则中找到完全一致的有效 HS 编码。");
      return;
    }

    const result = applyHsCodeToProductRecord(record, hsCode);
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

  async function selectMasterDataPath(field: MasterDataFieldDefinition) {
    if (!canOperate || field.pathPicker !== "exporterSealImage") {
      return;
    }

    try {
      const selectedPath = await selectExporterSealImageFile();
      if (selectedPath) {
        patchRecord(field.name, selectedPath);
      }
    } catch (error) {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    }
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
      </div>

      {message && hasConcurrencyConflict ? <ConcurrencyConflictNotice message={message} isBusy={detailQuery.isFetching} onReload={() => void loadLatestRecord()} /> : null}
      {message && !hasConcurrencyConflict ? <InlineNotice tone="error" title="操作未完成">{message}</InlineNotice> : null}
      {productUnitLookupMessage ? <InlineNotice tone="warning" title="单位资料未能完整加载">{productUnitLookupMessage}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {productUnitMessage ? <div className="info-alert">{productUnitMessage}</div> : null}
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
                {section.fields.map((field) => (
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
                    onSelectPath={() => void selectMasterDataPath(field)}
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
          details={["删除后无法在列表中继续查看。", "如该资料已被业务单据引用，系统会拒绝删除并说明原因。"]}
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
