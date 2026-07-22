import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileCheck2 } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ApiCustomsCooAttachmentDto,
  ApiCustomsCooDocumentDto,
  ApiCustomsCooEditorOptionsResponse,
  ApiCustomsCooItemDto,
  ApiCustomsCooNonpartyCorpDto,
  ApiCustomsCooOptionDto,
  ApiSingleWindowIssuingAuthorityOptionDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectCustomsCooAttachmentFiles } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { CustomsCooProducerProfileDialog } from "./CustomsCooProducerProfileDialog.tsx";
import { CustomsCooIdentitySections } from "./CustomsCooIdentitySections.tsx";
import { CustomsCooTradeSections } from "./CustomsCooTradeSections.tsx";
import { CustomsCooGoodsWorkspace } from "./CustomsCooGoodsWorkspace.tsx";
import { CooSummary, buildCustomsCooSectionNavItems } from "./CustomsCooSummary.tsx";
import { SingleWindowHandoffPanel } from "./SingleWindowHandoffPanel.tsx";
import { SingleWindowLockedFieldsDialog } from "./SingleWindowLockedFieldsDialog.tsx";
import { SingleWindowExportReviewPanel } from "./SingleWindowExportReviewPanel.tsx";
import { SingleWindowScopedClearControls } from "./SingleWindowScopedClearControls.tsx";
import { SingleWindowDocumentActionBar } from "./SingleWindowDocumentActionBar.tsx";
import { SingleWindowSectionNav } from "./SingleWindowSectionNav.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { useSingleWindowLockedFields } from "./useSingleWindowLockedFields.ts";
import { useCustomsCooProducerProfiles } from "./useCustomsCooProducerProfiles.ts";
import { useCustomsCooAuthoritySelection } from "./useCustomsCooAuthoritySelection.ts";
import {
  applyCooDefaultsForScope,
  applyCooDefaultsToEmptyFields,
  areEditorDocumentsEqual,
  clearCooManualOverrides,
  cloneEditorDocument,
  cooScopedClearOptionsByGroup,
} from "./singleWindowEditorTools.ts";
import {
  buildCooDocumentSnapshot,
  buildCooGoodsDescription,
  copyCooOriginAndEnterpriseFields,
  createAttachmentFromPath,
  createEmptyCooItem,
  createEmptyNonpartyCorp,
  formatScopedClearResultMessage,
  getCooGoodsDescriptionFailureMessage,
  isMeaningfulCooItem,
  isMeaningfulNonpartyCorp,
  normalizeAttachment,
  normalizeCooDocumentForSave,
  normalizeCooItem,
  normalizeNonpartyCorp,
  normalizeText,
  numberOrZero,
  toIssuingAuthorityOptions,
  type CooScopedClearRequest,
} from "./customsCooModel.ts";

const customsCooBusinessType = "CustomsCoo";
const cooScopedClearGroups = [
  { key: "证书基础", label: "证书基础" },
  { key: "申报与对象", label: "申报与对象" },
  { key: "运输与贸易", label: "运输与贸易" },
  { key: "补充与特殊项", label: "补充与特殊项" },
  { key: "更改与重发", label: "更改与重发" },
  { key: "商品明细", label: "商品明细" },
  { key: "附件", label: "附件" },
] as const;

const emptyCooOptionList: ApiCustomsCooOptionDto[] = [];
const emptyCooEditorOptions: ApiCustomsCooEditorOptionsResponse = {
  applyTypeOptions: emptyCooOptionList,
  certStatusOptions: emptyCooOptionList,
  certTypeOptions: emptyCooOptionList,
  producerSecretOptions: emptyCooOptionList,
  exhibitFlagOptions: emptyCooOptionList,
  thirdPartyInvoiceOptions: emptyCooOptionList,
  predictFlagOptions: emptyCooOptionList,
  promiseOptions: emptyCooOptionList,
  currencyOptions: emptyCooOptionList,
  cooTradeModeOptions: emptyCooOptionList,
  goodsItemFlagOptions: emptyCooOptionList,
  packTypeOptions: emptyCooOptionList,
  goodsTaxRateOptions: emptyCooOptionList,
  packUnitOptions: emptyCooOptionList,
  originCriteriaOptionSets: [],
  originCriteriaSubOptionSets: [],
  storagePolicy: "",
};

export function CustomsCooPage({ client }: { client: ExportDocManagerApiClient }) {
  const requestConfirmation = useConfirmation();
  const permission = useModulePermission("document.single-window");
  const { invoiceId } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const parsedInvoiceId = Number(invoiceId);
  const isInvoiceIdValid = Number.isInteger(parsedInvoiceId) && parsedInvoiceId > 0;
  const documentQueryKey = queryKeys.singleWindowCustomsCooDocument(parsedInvoiceId);
  const reviewQueryKey = queryKeys.singleWindowCustomsCooExportReview(parsedInvoiceId);

  const [document, setDocument] = useState<ApiCustomsCooDocumentDto | null>(null);
  const [undoDocument, setUndoDocument] = useState<ApiCustomsCooDocumentDto | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [persistedDocumentSnapshot, setPersistedDocumentSnapshot] = useState<string | null>(null);

  const documentQuery = useQuery({
    queryKey: documentQueryKey,
    queryFn: () => client.getCustomsCooDocument({ invoiceId: parsedInvoiceId }),
    enabled: isInvoiceIdValid,
  });

  const editorOptionsQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooEditorOptions(),
    queryFn: () => client.getCustomsCooEditorOptions(),
    enabled: isInvoiceIdValid,
    staleTime: 10 * 60 * 1000,
  });

  const issuingAuthoritiesQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooIssuingAuthorities(),
    queryFn: () => client.getCustomsCooIssuingAuthorities(),
    enabled: isInvoiceIdValid,
    staleTime: 10 * 60 * 1000,
  });

  const reviewQuery = useQuery({
    queryKey: reviewQueryKey,
    queryFn: () =>
      client.getSingleWindowExportReview({
        businessType: customsCooBusinessType,
        invoiceId: parsedInvoiceId,
      }),
    enabled: isInvoiceIdValid,
  });

  useEffect(() => {
    if (documentQuery.data) {
      setDocument(documentQuery.data);
      setPersistedDocumentSnapshot(buildCooDocumentSnapshot(documentQuery.data, parsedInvoiceId));
      setUndoDocument(null);
      setMessage(null);
      authoritySelection.reset();
    }
  }, [documentQuery.data, parsedInvoiceId]);

  const buildDefaultsMutation = useMutation({
    mutationFn: (_snapshot: ApiCustomsCooDocumentDto) => client.buildCustomsCooDefaults({ invoiceId: parsedInvoiceId }),
    onSuccess: (nextDocument, snapshot) => {
      setDocument(nextDocument);
      setUndoDocument(areEditorDocumentsEqual(snapshot, nextDocument) ? null : snapshot);
      setMessage(null);
      setSuccessMessage(
        areEditorDocumentsEqual(snapshot, nextDocument)
          ? "当前已经是按发票推导的建议值，无需恢复默认值。"
          : "已恢复为按当前发票推导的默认值，保存后写入草稿。",
      );
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const fillEmptyMutation = useMutation({
    mutationFn: (_snapshot: ApiCustomsCooDocumentDto) => client.buildCustomsCooDefaults({ invoiceId: parsedInvoiceId }),
    onSuccess: (defaults, snapshot) => {
      const result = applyCooDefaultsToEmptyFields(snapshot, defaults);
      setDocument(result.document);
      setUndoDocument(result.changedCount > 0 ? snapshot : null);
      setMessage(null);
      setSuccessMessage(
        result.changedCount > 0
          ? `已按当前发票回填 ${result.changedCount} 个空白项，保存后写入草稿。`
          : "当前可回填的空白项已经都补齐了。",
      );
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const scopedClearMutation = useMutation({
    mutationFn: async (request: CooScopedClearRequest) => ({
      defaults: await client.buildCustomsCooDefaults({ invoiceId: parsedInvoiceId }),
      request,
    }),
    onSuccess: ({ defaults, request }) => {
      const result = applyCooDefaultsForScope(
        request.snapshot,
        defaults,
        request.groupKey,
        request.categoryKey ?? "",
      );
      setDocument(result.document);
      setUndoDocument(result.changedCount > 0 ? request.snapshot : null);
      setMessage(null);
      setSuccessMessage(formatScopedClearResultMessage(request, result.changedCount));
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const repairReviewMutation = useMutation({
    mutationFn: async (request: { groupKeys: string[]; snapshot: ApiCustomsCooDocumentDto | null }) => {
      let savedBeforeRepair = false;
      if (request.snapshot) {
        await client.saveCustomsCooDocument({
          invoiceId: parsedInvoiceId,
          body: normalizeCooDocumentForSave(request.snapshot, parsedInvoiceId),
        });
        savedBeforeRepair = true;
      }

      const response = await client.repairSingleWindowExportReviewGroups({
        businessType: customsCooBusinessType,
        invoiceId: parsedInvoiceId,
        body: { groupKeys: request.groupKeys },
      });
      const repairedDocument = await client.getCustomsCooDocument({ invoiceId: parsedInvoiceId });
      return { response, repairedDocument, savedBeforeRepair };
    },
    onSuccess: async ({ response, repairedDocument, savedBeforeRepair }) => {
      setDocument(repairedDocument);
      setPersistedDocumentSnapshot(buildCooDocumentSnapshot(repairedDocument, parsedInvoiceId));
      setUndoDocument(null);
      queryClient.setQueryData(documentQueryKey, repairedDocument);
      queryClient.setQueryData(reviewQueryKey, response.review);
      setMessage(null);
      setSuccessMessage(
        `${savedBeforeRepair ? "已先保存当前草稿，" : ""}${
          response.message || `已自动修复 ${response.repairedGroupCount} 个预检分组。`
        }`,
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveMutation = useMutation({
    mutationFn: (body: ApiCustomsCooDocumentDto) =>
      client.saveCustomsCooDocument({
        invoiceId: parsedInvoiceId,
        body,
      }),
    onSuccess: async (response) => {
      setDocument(response.document);
      setPersistedDocumentSnapshot(buildCooDocumentSnapshot(response.document, parsedInvoiceId));
      setUndoDocument(null);
      queryClient.setQueryData(documentQueryKey, response.document);
      setMessage(null);
      setSuccessMessage(response.message || "海关原产地证草稿已保存。");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const lockedFieldsWorkspace = useSingleWindowLockedFields({
    document,
    isDocumentValid: isInvoiceIdValid,
    hasUnsavedChanges: Boolean(document && documentQuery.data && !areEditorDocumentsEqual(document, documentQuery.data)),
    saveDocument: async () => {
      const response = await client.saveCustomsCooDocument({
        invoiceId: parsedInvoiceId,
        body: normalizeCooDocumentForSave(document!, parsedInvoiceId),
      });
      return response.document;
    },
    loadLockedFields: () => client.getCustomsCooLockedFields({ invoiceId: parsedInvoiceId }),
    unlockFields: (fieldKeys) => client.unlockCustomsCooFields({ invoiceId: parsedInvoiceId, body: { fieldKeys } }),
    applyPersistedDocument: (nextDocument) => {
      setDocument(nextDocument);
      setPersistedDocumentSnapshot(buildCooDocumentSnapshot(nextDocument, parsedInvoiceId));
      setUndoDocument(null);
      queryClient.setQueryData(documentQueryKey, nextDocument);
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
      ]);
    },
    clearMessages: () => { setMessage(null); setSuccessMessage(null); },
    showError: (nextMessage) => { setMessage(nextMessage); setSuccessMessage(null); },
    showSuccess: (nextMessage) => { setMessage(null); setSuccessMessage(nextMessage); },
  });

  const producerProfiles = useCustomsCooProducerProfiles({ client, queryClient, document, setDocument: (next) => setDocument(next), setUndoDocument, setMessage, setSuccessMessage });

  const isBusy =
    documentQuery.isFetching ||
    reviewQuery.isFetching ||
    buildDefaultsMutation.isPending ||
    fillEmptyMutation.isPending ||
    scopedClearMutation.isPending ||
    repairReviewMutation.isPending ||
    saveMutation.isPending ||
    lockedFieldsWorkspace.isPending ||
    producerProfiles.isPending;
  const loadMessage = !isInvoiceIdValid
    ? "发票 ID 无效。"
    : documentQuery.isError
      ? readApiError(documentQuery.error)
      : null;
  const reviewMessage = reviewQuery.isError ? readApiError(reviewQuery.error) : null;
  const catalogMessage = editorOptionsQuery.isError
    ? readApiError(editorOptionsQuery.error)
    : issuingAuthoritiesQuery.isError
      ? readApiError(issuingAuthoritiesQuery.error)
      : null;
  const editorOptions = editorOptionsQuery.data ?? emptyCooEditorOptions;
  const issuingAuthorityOptions = issuingAuthoritiesQuery.data?.options ?? [];
  const authoritySelection = useCustomsCooAuthoritySelection(document, issuingAuthorityOptions, patchDocument);
  const issuingAuthorityCooOptions = useMemo(
    () => toIssuingAuthorityOptions(issuingAuthorityOptions),
    [issuingAuthorityOptions],
  );
  const currentDocumentSnapshot = useMemo(
    () => (document && isInvoiceIdValid ? buildCooDocumentSnapshot(document, parsedInvoiceId) : null),
    [document, isInvoiceIdValid, parsedInvoiceId],
  );
  const hasUnsavedDocumentChanges = Boolean(
    permission.canOperate &&
    document &&
      persistedDocumentSnapshot &&
      currentDocumentSnapshot &&
      currentDocumentSnapshot !== persistedDocumentSnapshot,
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedDocumentChanges,
    message: "当前海关原产地证草稿有未保存的修改。",
  });

  async function handleRepairReviewGroups(groupKeys: string[]) {
    if (!permission.canOperate || !document || !isInvoiceIdValid || groupKeys.length === 0) {
      return;
    }

    const snapshot = cloneEditorDocument(document);
    const shouldSaveCurrentDraft =
      documentQuery.data != null && !areEditorDocumentsEqual(snapshot, documentQuery.data);

    if (
      shouldSaveCurrentDraft &&
      !await requestConfirmation({ title: "保存并自动修复", description: "当前海关原产地证草稿有未保存修改，需要先保存当前草稿再执行自动修复。", confirmLabel: "保存并修复" })
    ) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    repairReviewMutation.mutate({
      groupKeys,
      snapshot: shouldSaveCurrentDraft ? snapshot : null,
    });
  }

  function patchDocument(next: Partial<ApiCustomsCooDocumentDto>) {
    setDocument((current) => (current ? { ...current, ...next } : current));
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function patchItem(index: number, next: Partial<ApiCustomsCooItemDto>) {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        items: current.items.map((item, itemIndex) => (itemIndex === index ? { ...item, ...next } : item)),
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function addItem() {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      const nextGNo = Math.max(0, ...current.items.map((item) => numberOrZero(item.gNo))) + 1;
      return {
        ...current,
        items: [...current.items, createEmptyCooItem(current.id, nextGNo, current.invNo)],
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function removeItem(index: number) {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        items: current.items.filter((_, itemIndex) => itemIndex !== index),
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function patchNonpartyCorp(index: number, next: Partial<ApiCustomsCooNonpartyCorpDto>) {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        nonpartyCorps: current.nonpartyCorps.map((corp, corpIndex) => (corpIndex === index ? { ...corp, ...next } : corp)),
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function addNonpartyCorp() {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      const nextSortNo = Math.max(0, ...current.nonpartyCorps.map((corp) => numberOrZero(corp.sortNo))) + 1;
      return {
        ...current,
        nonpartyCorps: [...current.nonpartyCorps, createEmptyNonpartyCorp(current.id, nextSortNo)],
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function removeNonpartyCorp(index: number) {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        nonpartyCorps: current.nonpartyCorps.filter((_, corpIndex) => corpIndex !== index),
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  async function addAttachmentsFromDialog() {
    try {
      const selectedFiles = await selectCustomsCooAttachmentFiles();
      if (!selectedFiles.length) {
        return;
      }

      setDocument((current) => {
        if (!current) {
          return current;
        }

        let nextSortOrder = Math.max(0, ...current.attachments.map((attachment) => numberOrZero(attachment.sortOrder))) + 1;
        const newAttachments = selectedFiles
          .map((filePath) => filePath.trim())
          .filter(Boolean)
          .map((filePath) => createAttachmentFromPath(current, filePath, nextSortOrder++));

        if (!newAttachments.length) {
          return current;
        }

        return {
          ...current,
          attachments: [...current.attachments, ...newAttachments],
        };
      });
      setUndoDocument(null);
      setMessage(null);
      setSuccessMessage(`已添加 ${selectedFiles.length} 个附件，保存后写入草稿。`);
    } catch (error) {
      setMessage(readDesktopError(error));
      setSuccessMessage(null);
    }
  }

  function patchAttachment(index: number, next: Partial<ApiCustomsCooAttachmentDto>) {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        attachments: current.attachments.map((attachment, attachmentIndex) =>
          attachmentIndex === index ? { ...attachment, ...next } : attachment,
        ),
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  function removeAttachment(index: number) {
    setDocument((current) => {
      if (!current) {
        return current;
      }

      return {
        ...current,
        attachments: current.attachments.filter((_, attachmentIndex) => attachmentIndex !== index),
      };
    });
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  async function handleRestoreDefaults() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "重新套用建议值", description: "系统将按当前发票重新生成建议值。", details: ["原来的手工覆盖内容会被替换。"], confirmLabel: "重新套用" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    buildDefaultsMutation.mutate(cloneEditorDocument(document));
  }

  function handleFillEmptyFields() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    fillEmptyMutation.mutate(cloneEditorDocument(document));
  }

  async function handleClearManualOverrides() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "清空手工覆盖", description: "确定清空手工补充的覆盖字段吗？", details: ["系统回写值会保留。", "保存后才会写入草稿。"], confirmLabel: "清空覆盖" })) {
      return;
    }

    const snapshot = cloneEditorDocument(document);
    const result = clearCooManualOverrides(snapshot);
    setDocument(result.document);
    setUndoDocument(result.changedCount > 0 ? snapshot : null);
    setMessage(null);
    setSuccessMessage(
      result.changedCount > 0
        ? "已清空手工补充的覆盖字段，系统回写值已保留，保存后写入草稿。"
        : "当前没有可清空的手工覆盖字段。",
    );
  }

  async function handleClearScopedGroup(groupKey: string) {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "恢复分组建议值", description: `确定把“${groupKey}”分组里的手工覆盖值恢复到当前发票建议值吗？`, confirmLabel: "确认恢复" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    scopedClearMutation.mutate({ snapshot: cloneEditorDocument(document), groupKey });
  }

  async function handleClearScopedCategory(groupKey: string, categoryKey: string, categoryLabel: string) {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "恢复分类建议值", description: `确定只恢复“${groupKey}”分组里的“${categoryLabel}”吗？`, confirmLabel: "确认恢复" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    scopedClearMutation.mutate({
      snapshot: cloneEditorDocument(document),
      groupKey,
      categoryKey,
      categoryLabel,
    });
  }

  function handleGenerateGoodsDescription(index: number) {
    if (!document || !document.items[index]) {
      return;
    }

    const item = document.items[index];
    const generated = buildCooGoodsDescription(item);
    if (!generated) {
      setMessage(getCooGoodsDescriptionFailureMessage(item));
      setSuccessMessage(null);
      return;
    }

    if (normalizeText(item.goodsDesc) === generated) {
      setMessage(null);
      setSuccessMessage(`第 ${index + 1} 行货物描述已是当前生成内容。`);
      return;
    }

    const snapshot = cloneEditorDocument(document);
    setDocument({
      ...document,
      items: document.items.map((currentItem, itemIndex) =>
        itemIndex === index ? { ...currentItem, goodsDesc: generated } : currentItem,
      ),
    });
    setUndoDocument(snapshot);
    setMessage(null);
    setSuccessMessage(`已生成第 ${index + 1} 行货物描述，保存后写入草稿。`);
  }

  function handleCopyOriginAndEnterpriseToFollowingRows(index: number) {
    if (!document || !document.items[index] || index >= document.items.length - 1) {
      return;
    }

    const source = document.items[index];
    let changedRows = 0;
    const nextItems = document.items.map((item, itemIndex) => {
      if (itemIndex <= index) {
        return item;
      }

      const { item: nextItem, changed } = copyCooOriginAndEnterpriseFields(source, item);
      if (changed) {
        changedRows++;
      }

      return nextItem;
    });

    if (changedRows === 0) {
      setMessage(null);
      setSuccessMessage("后续货项没有需要复制的原产标准或生产企业字段。");
      return;
    }

    setUndoDocument(cloneEditorDocument(document));
    setDocument({
      ...document,
      items: nextItems,
    });
    setMessage(null);
    setSuccessMessage(`已复制当前行原产标准和生产企业字段到后续 ${changedRows} 行，保存后写入草稿。`);
  }

  function handleUndoToolAction() {
    if (!undoDocument) {
      return;
    }

    setDocument(cloneEditorDocument(undoDocument));
    setUndoDocument(null);
    setMessage(null);
    setSuccessMessage("已撤销上一次工具动作，保存后写入草稿。");
  }

  async function handleBackToInvoice() {
    if (await confirmDiscardChanges("返回发票")) {
      navigate(isInvoiceIdValid ? `/invoices/${parsedInvoiceId}` : "/invoices");
    }
  }

  async function handleRefreshDocument() {
    if (await confirmDiscardChanges("刷新草稿")) {
      void documentQuery.refetch();
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!permission.canOperate || !document || !isInvoiceIdValid) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    saveMutation.mutate(normalizeCooDocumentForSave(document, parsedInvoiceId));
  }

  return (
    <section className="editor-surface customs-coo-surface" aria-label="海关原产地证草稿">
      <SingleWindowTabs activeKey="customs-coo" />

      <SingleWindowDocumentActionBar
        title={document ? document.invNo || document.invoiceNo || `发票 ${parsedInvoiceId}` : "海关原产地证草稿"}
        titleIcon={<FileCheck2 size={18} aria-hidden="true" />}
        formId="customs-coo-form"
        isBusy={isBusy}
        isDocumentReady={Boolean(document)}
        isInvoiceIdValid={isInvoiceIdValid}
        canOperate={permission.canOperate}
        canUndo={Boolean(undoDocument)}
        scopedClearControls={
          <SingleWindowScopedClearControls
            groups={cooScopedClearGroups}
            optionsByGroup={cooScopedClearOptionsByGroup}
            disabled={!permission.canOperate || isBusy || !document || !isInvoiceIdValid}
            onClearGroup={handleClearScopedGroup}
            onClearCategory={handleClearScopedCategory}
          />
        }
        onBack={handleBackToInvoice}
        onRefresh={handleRefreshDocument}
        onRestoreDefaults={handleRestoreDefaults}
        onFillEmptyFields={handleFillEmptyFields}
        onClearManualOverrides={handleClearManualOverrides}
        onOpenLockedFields={lockedFieldsWorkspace.open}
        onUndo={handleUndoToolAction}
        onBuildReview={() => void reviewQuery.refetch()}
      />

      {loadMessage || message || catalogMessage ? <InlineNotice tone="error" title="原产地证操作未完成">{loadMessage || message || catalogMessage}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!permission.canOperate ? <PermissionNotice>当前权限模板仅允许查看单一窗口草稿和预检结果；修改、修复、保存与交接操作已禁用。</PermissionNotice> : null}
      {!document && isBusy ? <PageState tone="loading" title="正在加载原产地证草稿" description="正在读取表头、商品明细、附件和预检状态。" /> : null}

      {document ? (
        <form id="customs-coo-form" className="entity-form customs-coo-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <SingleWindowSectionNav
            items={buildCustomsCooSectionNavItems(document)}
            ariaLabel="海关原产地证录入分区"
          />

          <section id="coo-section-status" className="form-section single-window-editor-section" aria-label="草稿状态">
            <div className="section-header">
              <h2>草稿状态</h2>
              <span className="section-count">{document.items.length} 行商品</span>
            </div>
            <CooSummary document={document} />
          </section>

          <fieldset className="permission-fieldset" disabled={!permission.canOperate}>

          <CustomsCooIdentitySections
            document={document}
            editorOptions={editorOptions}
            issuingAuthorityOptions={issuingAuthorityCooOptions}
            onPatch={patchDocument}
            onOrgCodeChange={authoritySelection.changeOrgCode}
            onFetchPlaceChange={authoritySelection.changeFetchPlace}
            onApplicationAddressChange={authoritySelection.changeApplicationAddress}
          />

          <CustomsCooTradeSections document={document} editorOptions={editorOptions} onPatch={patchDocument} />

          <CustomsCooGoodsWorkspace
            document={document}
            editorOptions={editorOptions}
            disabled={isBusy}
            savingProducerRowIndex={producerProfiles.savingRowIndex}
            onAddItem={addItem}
            onChangeItem={patchItem}
            onRemoveItem={removeItem}
            onGenerateGoodsDescription={handleGenerateGoodsDescription}
            onCopyOriginAndEnterpriseToFollowingRows={handleCopyOriginAndEnterpriseToFollowingRows}
            onOpenProducerProfile={producerProfiles.open}
            onSaveProducerProfile={producerProfiles.save}
            onAddNonpartyCorp={addNonpartyCorp}
            onChangeNonpartyCorp={patchNonpartyCorp}
            onRemoveNonpartyCorp={removeNonpartyCorp}
            onSelectAttachments={() => void addAttachmentsFromDialog()}
            onChangeAttachment={patchAttachment}
            onRemoveAttachment={removeAttachment}
            onAttachmentPathError={(value) => {
              setMessage(value);
              setSuccessMessage(null);
            }}
          />
          </fieldset>

          <section id="coo-section-review" className="form-section single-window-editor-section" aria-label="导出前预检">
            <div className="section-header">
              <h2>导出前预检</h2>
              <span className="section-count">
                {reviewQuery.data
                  ? `${reviewQuery.data.totalErrorCount} 错误 · ${reviewQuery.data.totalWarningCount} 警告`
                  : "未加载"}
              </span>
            </div>
            {reviewMessage ? <InlineNotice tone="warning" title="审查提示">{reviewMessage}</InlineNotice> : null}
            <SingleWindowExportReviewPanel
              review={reviewQuery.data ?? null}
              isBusy={reviewQuery.isFetching}
              isActionDisabled={!permission.canOperate || isBusy || !isInvoiceIdValid}
              repairBusy={repairReviewMutation.isPending}
              onRepairGroups={handleRepairReviewGroups}
            />
          </section>

          <SingleWindowHandoffPanel businessType="CustomsCoo" client={client} invoiceId={parsedInvoiceId} canOperate={permission.canOperate} />
        </form>
      ) : null}

      {lockedFieldsWorkspace.isOpen ? (
        <SingleWindowLockedFieldsDialog
          title="海关原产地证字段锁定"
          fields={lockedFieldsWorkspace.fields}
          selectedKeys={lockedFieldsWorkspace.selectedFieldKeys}
          isBusy={lockedFieldsWorkspace.isPending}
          onClose={lockedFieldsWorkspace.close}
          onToggleField={lockedFieldsWorkspace.toggleField}
          onToggleAll={lockedFieldsWorkspace.toggleAll}
          onUnlockSelected={lockedFieldsWorkspace.unlockSelected}
        />
      ) : null}
      {producerProfiles.currentProfile ? (
        <CustomsCooProducerProfileDialog
          client={client}
          currentProfile={producerProfiles.currentProfile}
          rowLabel={producerProfiles.rowLabel}
          onApply={producerProfiles.apply}
          onClose={producerProfiles.close}
        />
      ) : null}
    </section>
  );
}
