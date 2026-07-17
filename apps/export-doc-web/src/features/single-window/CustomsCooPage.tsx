import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileCheck2, Paperclip, Plus } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ApiCustomsCooAttachmentDto,
  ApiCustomsCooDocumentDto,
  ApiCustomsCooEditorOptionsResponse,
  ApiCustomsCooItemDto,
  ApiCustomsCooNonpartyCorpDto,
  ApiCustomsCooOptionDto,
  ApiCustomsCooProducerProfileInputDto,
  ApiSingleWindowIssuingAuthorityOptionDto,
  ApiSingleWindowLockedFieldDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectCustomsCooAttachmentFiles } from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { DateField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { CustomsCooProducerProfileDialog } from "./CustomsCooProducerProfileDialog.tsx";
import { CooDatalistField, CooSelectField } from "./CustomsCooFields.tsx";
import { CooItemsEditor } from "./CustomsCooItemsEditor.tsx";
import { CooAttachmentTable, CooNonpartyEditor } from "./CustomsCooTables.tsx";
import { CooSummary, buildCustomsCooSectionNavItems } from "./CustomsCooSummary.tsx";
import { SingleWindowHandoffPanel } from "./SingleWindowHandoffPanel.tsx";
import { SingleWindowLockedFieldsDialog } from "./SingleWindowLockedFieldsDialog.tsx";
import { SingleWindowExportReviewPanel } from "./SingleWindowExportReviewPanel.tsx";
import { SingleWindowScopedClearControls } from "./SingleWindowScopedClearControls.tsx";
import { SingleWindowDocumentActionBar } from "./SingleWindowDocumentActionBar.tsx";
import { SingleWindowSectionNav } from "./SingleWindowSectionNav.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import {
  applyCooDefaultsForScope,
  applyCooDefaultsToEmptyFields,
  areEditorDocumentsEqual,
  clearCooManualOverrides,
  cloneEditorDocument,
  cooScopedClearOptionsByGroup,
} from "./singleWindowEditorTools.ts";
import {
  applyProducerProfileToCooItem,
  buildCooDocumentSnapshot,
  buildCooGoodsDescription,
  buildProducerProfileInputFromCooItem,
  buildProducerProfileRowLabel,
  copyCooOriginAndEnterpriseFields,
  countProducerProfileChanges,
  createAttachmentFromPath,
  createEmptyCooItem,
  createEmptyNonpartyCorp,
  findIssuingAuthority,
  formatScopedClearResultMessage,
  getCooGoodsDescriptionFailureMessage,
  isMeaningfulCooItem,
  isMeaningfulNonpartyCorp,
  normalizeAttachment,
  normalizeAuthorityCompareText,
  normalizeCooDocumentForSave,
  normalizeCooItem,
  normalizeNonpartyCorp,
  normalizeText,
  numberOrZero,
  parseIssuingAuthorityCode,
  shouldShowCooHeaderField,
  shouldShowCooModificationFields,
  shouldShowCooNonpartyCorps,
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

type CooAuthorityAutoState = {
  fetchPlace: string;
  aplAdd: string;
};

export function CustomsCooPage({ client }: { client: ExportDocManagerApiClient }) {
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
  const [lockedFieldsOpen, setLockedFieldsOpen] = useState(false);
  const [lockedFields, setLockedFields] = useState<ApiSingleWindowLockedFieldDto[]>([]);
  const [selectedLockedFieldKeys, setSelectedLockedFieldKeys] = useState<Set<string>>(() => new Set());
  const [producerProfileRowIndex, setProducerProfileRowIndex] = useState<number | null>(null);
  const [savingProducerProfileRowIndex, setSavingProducerProfileRowIndex] = useState<number | null>(null);
  const [persistedDocumentSnapshot, setPersistedDocumentSnapshot] = useState<string | null>(null);
  const [authorityAutoState, setAuthorityAutoState] = useState<CooAuthorityAutoState>({ fetchPlace: "", aplAdd: "" });

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
      setAuthorityAutoState({ fetchPlace: "", aplAdd: "" });
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

  const lockedFieldsMutation = useMutation({
    mutationFn: async () => {
      if (!document || !isInvoiceIdValid) {
        return null;
      }

      let savedDocument: ApiCustomsCooDocumentDto | null = null;
      if (documentQuery.data && !areEditorDocumentsEqual(document, documentQuery.data)) {
        if (!window.confirm("当前草稿有未保存修改，先保存后再查看锁定字段吗？")) {
          return null;
        }

        const saveResponse = await client.saveCustomsCooDocument({
          invoiceId: parsedInvoiceId,
          body: normalizeCooDocumentForSave(document, parsedInvoiceId),
        });
        savedDocument = saveResponse.document;
      }

      const lockedFieldsResponse = await client.getCustomsCooLockedFields({ invoiceId: parsedInvoiceId });
      return { savedDocument, lockedFieldsResponse };
    },
    onSuccess: (result) => {
      if (!result) {
        return;
      }

      if (result.savedDocument) {
        setDocument(result.savedDocument);
        setPersistedDocumentSnapshot(buildCooDocumentSnapshot(result.savedDocument, parsedInvoiceId));
        setUndoDocument(null);
        queryClient.setQueryData(documentQueryKey, result.savedDocument);
        void Promise.all([
          queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
          queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
        ]);
      }

      setLockedFields(result.lockedFieldsResponse.fields);
      setSelectedLockedFieldKeys(new Set());
      setLockedFieldsOpen(true);
      setMessage(null);
      setSuccessMessage(null);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const unlockFieldsMutation = useMutation({
    mutationFn: (fieldKeys: string[]) =>
      client.unlockCustomsCooFields({
        invoiceId: parsedInvoiceId,
        body: { fieldKeys },
      }),
    onSuccess: (response) => {
      setDocument(response.document);
      setPersistedDocumentSnapshot(buildCooDocumentSnapshot(response.document, parsedInvoiceId));
      setUndoDocument(null);
      setLockedFields(response.lockedFields);
      setSelectedLockedFieldKeys(new Set());
      queryClient.setQueryData(documentQueryKey, response.document);
      setMessage(null);
      setSuccessMessage(response.message || "字段已恢复为当前建议值。");
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveProducerProfileMutation = useMutation({
    mutationFn: (request: { rowIndex: number; profile: ApiCustomsCooProducerProfileInputDto }) =>
      client.createCustomsCooProducerProfile({
        body: { profile: request.profile },
      }),
    onMutate: (request) => {
      setSavingProducerProfileRowIndex(request.rowIndex);
    },
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "当前生产企业已保存到资料库。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowCustomsCooProducerProfilesRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
    onSettled: () => {
      setSavingProducerProfileRowIndex(null);
    },
  });

  const isBusy =
    documentQuery.isFetching ||
    reviewQuery.isFetching ||
    buildDefaultsMutation.isPending ||
    fillEmptyMutation.isPending ||
    scopedClearMutation.isPending ||
    repairReviewMutation.isPending ||
    saveMutation.isPending ||
    lockedFieldsMutation.isPending ||
    unlockFieldsMutation.isPending ||
    saveProducerProfileMutation.isPending;
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

  function handleRepairReviewGroups(groupKeys: string[]) {
    if (!permission.canOperate || !document || !isInvoiceIdValid || groupKeys.length === 0) {
      return;
    }

    const snapshot = cloneEditorDocument(document);
    const shouldSaveCurrentDraft =
      documentQuery.data != null && !areEditorDocumentsEqual(snapshot, documentQuery.data);

    if (
      shouldSaveCurrentDraft &&
      !window.confirm("当前海关原产地证草稿有未保存修改，先保存当前草稿再执行自动修复吗？")
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

  function handleOrgCodeChange(value: string) {
    if (!document) {
      return;
    }

    const orgCode = parseIssuingAuthorityCode(value, issuingAuthorityOptions);
    const authority = findIssuingAuthority(orgCode, issuingAuthorityOptions);
    const nextDocument: Partial<ApiCustomsCooDocumentDto> = { orgCode };
    const nextAutoState: CooAuthorityAutoState = { ...authorityAutoState };

    if (authority) {
      if (
        !document.fetchPlace.trim() ||
        normalizeAuthorityCompareText(document.fetchPlace) === normalizeAuthorityCompareText(authorityAutoState.fetchPlace)
      ) {
        nextDocument.fetchPlace = authority.code;
        nextAutoState.fetchPlace = authority.code;
      }

      if (
        authority.applicationAddress &&
        (!document.aplAdd.trim() ||
          normalizeAuthorityCompareText(document.aplAdd) === normalizeAuthorityCompareText(authorityAutoState.aplAdd))
      ) {
        nextDocument.aplAdd = authority.applicationAddress;
        nextAutoState.aplAdd = authority.applicationAddress;
      }
    }

    setAuthorityAutoState(nextAutoState);
    patchDocument(nextDocument);
  }

  function handleFetchPlaceChange(value: string) {
    const fetchPlace = parseIssuingAuthorityCode(value, issuingAuthorityOptions);
    if (
      authorityAutoState.fetchPlace &&
      normalizeAuthorityCompareText(fetchPlace) !== normalizeAuthorityCompareText(authorityAutoState.fetchPlace)
    ) {
      setAuthorityAutoState((current) => ({ ...current, fetchPlace: "" }));
    }

    patchDocument({ fetchPlace });
  }

  function handleAplAddChange(value: string) {
    if (
      authorityAutoState.aplAdd &&
      normalizeAuthorityCompareText(value) !== normalizeAuthorityCompareText(authorityAutoState.aplAdd)
    ) {
      setAuthorityAutoState((current) => ({ ...current, aplAdd: "" }));
    }

    patchDocument({ aplAdd: value });
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

  function handleRestoreDefaults() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!window.confirm("这会按当前发票重新套用建议值，原来的手工覆盖内容会被替换。要继续吗？")) {
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

  function handleClearManualOverrides() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!window.confirm("这会清空手工补充的覆盖字段，但会保留系统回写值。要继续吗？")) {
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

  function handleClearScopedGroup(groupKey: string) {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!window.confirm(`这会把“${groupKey}”分组里的手工覆盖值恢复到当前发票建议值。要继续吗？`)) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    scopedClearMutation.mutate({ snapshot: cloneEditorDocument(document), groupKey });
  }

  function handleClearScopedCategory(groupKey: string, categoryKey: string, categoryLabel: string) {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!window.confirm(`这会只恢复“${groupKey}”分组里的“${categoryLabel}”到当前发票建议值。要继续吗？`)) {
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

  function handleOpenLockedFields() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    lockedFieldsMutation.mutate();
  }

  function toggleLockedField(key: string) {
    setSelectedLockedFieldKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  }

  function toggleAllLockedFields() {
    setSelectedLockedFieldKeys((current) =>
      current.size === lockedFields.length ? new Set() : new Set(lockedFields.map((field) => field.key)),
    );
  }

  function handleUnlockSelectedFields() {
    const fieldKeys = Array.from(selectedLockedFieldKeys);
    if (fieldKeys.length === 0 || !isInvoiceIdValid) {
      return;
    }

    unlockFieldsMutation.mutate(fieldKeys);
  }

  function handleOpenProducerProfiles(index: number) {
    if (!document || !document.items[index]) {
      return;
    }

    setProducerProfileRowIndex(index);
    setMessage(null);
    setSuccessMessage(null);
  }

  function handleApplyProducerProfile(profile: ApiCustomsCooProducerProfileInputDto) {
    if (!document || producerProfileRowIndex === null || !document.items[producerProfileRowIndex]) {
      return;
    }

    const snapshot = cloneEditorDocument(document);
    const currentItem = document.items[producerProfileRowIndex];
    const nextItem = applyProducerProfileToCooItem(currentItem, profile);
    const changedCount = countProducerProfileChanges(currentItem, nextItem);
    if (changedCount === 0) {
      setProducerProfileRowIndex(null);
      setMessage(null);
      setSuccessMessage("当前货项已经是这条生产企业资料。");
      return;
    }

    setDocument({
      ...document,
      items: document.items.map((item, index) => (index === producerProfileRowIndex ? nextItem : item)),
    });
    setUndoDocument(snapshot);
    setProducerProfileRowIndex(null);
    setMessage(null);
    setSuccessMessage(`已将生产企业资料回填到第 ${producerProfileRowIndex + 1} 行，保存后写入草稿。`);
  }

  function handleSaveProducerProfile(index: number) {
    if (!document || !document.items[index]) {
      return;
    }

    const profile = buildProducerProfileInputFromCooItem(document.items[index], document);
    if (!profile.ciqRegNo.trim() && !profile.prdcEtpsName.trim()) {
      setMessage("请先填写当前货项的生产企业代码或生产企业名称。");
      setSuccessMessage(null);
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    saveProducerProfileMutation.mutate({ rowIndex: index, profile });
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

  function handleBackToInvoice() {
    if (confirmDiscardChanges("返回发票")) {
      navigate(isInvoiceIdValid ? `/invoices/${parsedInvoiceId}` : "/invoices");
    }
  }

  function handleRefreshDocument() {
    if (confirmDiscardChanges("刷新草稿")) {
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
        onOpenLockedFields={handleOpenLockedFields}
        onUndo={handleUndoToolAction}
        onBuildReview={() => void reviewQuery.refetch()}
      />

      {loadMessage || message || catalogMessage ? <div className="alert">{loadMessage || message || catalogMessage}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}
      {!permission.canOperate ? <div className="permission-readonly-notice">当前权限模板仅允许查看单一窗口草稿和预检结果；修改、修复、保存与交接操作已禁用。</div> : null}
      {!document && isBusy ? <div className="loading-panel">加载中</div> : null}

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

          <section id="coo-section-basic" className="form-section single-window-editor-section" aria-label="证书基础">
            <div className="section-header">
              <h2>证书基础</h2>
            </div>
            <div className="field-grid">
              <CooSelectField label="申请类型" value={document.applyType} options={editorOptions.applyTypeOptions} onChange={(value) => patchDocument({ applyType: value })} />
              <CooSelectField label="证书类别" value={document.certStatus} options={editorOptions.certStatusOptions} onChange={(value) => patchDocument({ certStatus: value })} />
              <TextField label="原产地证编号" value={document.certNo} onChange={(value) => patchDocument({ certNo: value })} />
              <CooSelectField label="证书类型" value={document.certType} options={editorOptions.certTypeOptions} onChange={(value) => patchDocument({ certType: value })} />
              <TextField label="企业名称(中文)" value={document.etpsName} onChange={(value) => patchDocument({ etpsName: value })} />
              <TextField label="企业编号" value={document.entMgrNo} onChange={(value) => patchDocument({ entMgrNo: value })} />
              <TextField label="出口商代码" value={document.ciqRegNo} onChange={(value) => patchDocument({ ciqRegNo: value })} />
              <TextField label="录入企业代码" value={document.aplRegNo} onChange={(value) => patchDocument({ aplRegNo: value })} />
            </div>
          </section>

          <section id="coo-section-parties" className="form-section single-window-editor-section" aria-label="申报与对象">
            <div className="section-header">
              <h2>申报与对象</h2>
            </div>
            <div className="field-grid">
              <TextField label="申报员姓名" value={document.applName} onChange={(value) => patchDocument({ applName: value })} />
              <TextField label="申报员身份证号" value={document.applicant} onChange={(value) => patchDocument({ applicant: value })} />
              <TextField label="申报员电话" value={document.applTel} onChange={(value) => patchDocument({ applTel: value })} />
              <CooDatalistField label="签证机构代码(4位)" value={document.orgCode} options={issuingAuthorityCooOptions} onChange={handleOrgCodeChange} />
              <CooDatalistField label="领证机构代码(4位)" value={document.fetchPlace} options={issuingAuthorityCooOptions} onChange={handleFetchPlaceChange} />
              <CooDatalistField label="申请地址(机构所在地)" value={document.aplAdd} options={[]} onChange={handleAplAddChange} />
              <DateField label="发票日期" value={document.invDate} onChange={(value) => patchDocument({ invDate: value })} />
              <TextField label="发票号" value={document.invNo} onChange={(value) => patchDocument({ invNo: value })} />
              <DateField label="申请日期" value={document.aplDate} onChange={(value) => patchDocument({ aplDate: value })} />
              <TextField label="进口国/地区英文" value={document.destCountry} onChange={(value) => patchDocument({ destCountry: value })} />
              <TextField label="进口国代码" value={document.destCountryCode} onChange={(value) => patchDocument({ destCountryCode: value })} />
              <TextField label="进口国中文名" value={document.destCountryName} onChange={(value) => patchDocument({ destCountryName: value })} />
            </div>
            <div className="field-grid field-grid-wide">
              <TextAreaField label="出口商" value={document.exporter} onChange={(value) => patchDocument({ exporter: value })} />
              <TextAreaField label="收货人" value={document.consignee} onChange={(value) => patchDocument({ consignee: value })} />
            </div>
            {shouldShowCooHeaderField(document, "ExporterTel") ||
            shouldShowCooHeaderField(document, "ConsigneeTel") ||
            shouldShowCooHeaderField(document, "EtpsConcEr") ? (
              <div className="field-grid">
                {shouldShowCooHeaderField(document, "ExporterTel") ? <TextField label="出口商电话" value={document.exporterTel} onChange={(value) => patchDocument({ exporterTel: value })} /> : null}
                {shouldShowCooHeaderField(document, "ExporterFax") ? <TextField label="出口商传真" value={document.exporterFax} onChange={(value) => patchDocument({ exporterFax: value })} /> : null}
                {shouldShowCooHeaderField(document, "ExporterEmail") ? <TextField label="出口商邮箱" value={document.exporterEmail} onChange={(value) => patchDocument({ exporterEmail: value })} /> : null}
                {shouldShowCooHeaderField(document, "ConsigneeTel") ? <TextField label="进口商电话" value={document.consigneeTel} onChange={(value) => patchDocument({ consigneeTel: value })} /> : null}
                {shouldShowCooHeaderField(document, "ConsigneeFax") ? <TextField label="进口商传真" value={document.consigneeFax} onChange={(value) => patchDocument({ consigneeFax: value })} /> : null}
                {shouldShowCooHeaderField(document, "ConsigneeEmail") ? <TextField label="进口商邮箱" value={document.consigneeEmail} onChange={(value) => patchDocument({ consigneeEmail: value })} /> : null}
                {shouldShowCooHeaderField(document, "EtpsConcEr") ? <TextField label="企业联系人" value={document.etpsConcEr} onChange={(value) => patchDocument({ etpsConcEr: value })} /> : null}
                {shouldShowCooHeaderField(document, "EtpsTel") ? <TextField label="企业联系电话" value={document.etpsTel} onChange={(value) => patchDocument({ etpsTel: value })} /> : null}
              </div>
            ) : null}
          </section>

          <section id="coo-section-trade" className="form-section single-window-editor-section" aria-label="运输与贸易">
            <div className="section-header">
              <h2>运输与贸易</h2>
            </div>
            <div className="field-grid field-grid-wide">
              <TextAreaField label="特殊条款（商品描述）" value={document.goodsSpecClause} onChange={(value) => patchDocument({ goodsSpecClause: value })} />
              <TextAreaField label="唛头" value={document.mark} onChange={(value) => patchDocument({ mark: value })} />
            </div>
            <div className="field-grid">
              <TextField label="启运港" value={document.loadPort} onChange={(value) => patchDocument({ loadPort: value })} />
              <TextField label="卸货港" value={document.unloadPort} onChange={(value) => patchDocument({ unloadPort: value })} />
              <TextField label="运输方式" value={document.transMeans} onChange={(value) => patchDocument({ transMeans: value })} />
              <TextField label="船名/航次" value={document.transName} onChange={(value) => patchDocument({ transName: value })} />
              <TextField label="中转国代码" value={document.transCountryCode} onChange={(value) => patchDocument({ transCountryCode: value })} />
              <TextField label="中转国名称" value={document.transCountryName} onChange={(value) => patchDocument({ transCountryName: value })} />
              <TextField label="转运港" value={document.transPort} onChange={(value) => patchDocument({ transPort: value })} />
              <TextField label="目的港" value={document.destPort} onChange={(value) => patchDocument({ destPort: value })} />
              <DateField label="出运日期" value={document.intendExpDate} onChange={(value) => patchDocument({ intendExpDate: value })} />
              {shouldShowCooHeaderField(document, "PredictFlag") ? <CooSelectField label="预计离港标志" value={document.predictFlag} options={editorOptions.predictFlagOptions} onChange={(value) => patchDocument({ predictFlag: value })} /> : null}
              <DateField label="出口报关日期" value={document.expDeclDate} onChange={(value) => patchDocument({ expDeclDate: value })} />
              <CooSelectField label="贸易方式代码" value={document.tradeModeCode} options={editorOptions.cooTradeModeOptions} onChange={(value) => patchDocument({ tradeModeCode: value })} />
              <TextField label="FOB值" value={document.fobValue} onChange={(value) => patchDocument({ fobValue: value })} />
              <TextField label="总金额" value={document.totalAmt} onChange={(value) => patchDocument({ totalAmt: value })} />
              <TextField label="合同号" value={document.contractNo} onChange={(value) => patchDocument({ contractNo: value })} />
              <TextField label="信用证号" value={document.lcNo} onChange={(value) => patchDocument({ lcNo: value })} />
              <TextField label="价格条款" value={document.priceTerms} onChange={(value) => patchDocument({ priceTerms: value })} />
              <CooSelectField label="币制" value={document.curr} options={editorOptions.currencyOptions} onChange={(value) => patchDocument({ curr: value.toUpperCase() })} />
            </div>
            <div className="field-grid field-grid-wide">
              <TextAreaField label="运输细节" value={document.transDetails} onChange={(value) => patchDocument({ transDetails: value })} />
              <TextAreaField label="申请书备注" value={document.note} onChange={(value) => patchDocument({ note: value })} />
              <TextAreaField label="发票特殊条款" value={document.specInvTerms} onChange={(value) => patchDocument({ specInvTerms: value })} />
            </div>
          </section>

          <section id="coo-section-special" className="form-section single-window-editor-section" aria-label="补充与特殊项">
            <div className="section-header">
              <h2>补充与特殊项</h2>
            </div>
            <div className="field-grid field-grid-wide">
              {shouldShowCooHeaderField(document, "Remark") ? <TextAreaField label="证书备注" value={document.remark} onChange={(value) => patchDocument({ remark: value })} /> : null}
              {shouldShowCooHeaderField(document, "Producer") ? <TextAreaField label="证书货物生产商描述" value={document.producer} onChange={(value) => patchDocument({ producer: value })} /> : null}
              {shouldShowCooHeaderField(document, "PrcsAssembly") ? <TextAreaField label="加工装配工序" value={document.prcsAssembly} onChange={(value) => patchDocument({ prcsAssembly: value })} /> : null}
            </div>
            <div className="field-grid">
              <CooSelectField label="生产商保密" value={document.producerSertFlag} options={editorOptions.producerSecretOptions} onChange={(value) => patchDocument({ producerSertFlag: value })} />
              {shouldShowCooHeaderField(document, "ExhibitFlag") ? <CooSelectField label="是否展览证书" value={document.exhibitFlag} options={editorOptions.exhibitFlagOptions} onChange={(value) => patchDocument({ exhibitFlag: value })} /> : null}
              {shouldShowCooHeaderField(document, "ThirdPartyInvFlag") ? <CooSelectField label="第三方发票标志" value={document.thirdPartyInvFlag} options={editorOptions.thirdPartyInvoiceOptions} onChange={(value) => patchDocument({ thirdPartyInvFlag: value })} /> : null}
              {shouldShowCooHeaderField(document, "OriCountryCode") ? <TextField label="原产国代码" value={document.oriCountryCode} onChange={(value) => patchDocument({ oriCountryCode: value })} /> : null}
              {shouldShowCooHeaderField(document, "OriCountry") ? <TextField label="原产国名称" value={document.oriCountry} onChange={(value) => patchDocument({ oriCountry: value })} /> : null}
              <DateField label="签发有效日期" value={document.chkValidDate} onChange={(value) => patchDocument({ chkValidDate: value })} />
              <TextField label="报关单号" value={document.entryId} onChange={(value) => patchDocument({ entryId: value })} />
              <CooSelectField label="企业承诺代码" value={document.aplPromiseCode} options={editorOptions.promiseOptions} onChange={(value) => patchDocument({ aplPromiseCode: value })} />
            </div>
          </section>

          {shouldShowCooModificationFields(document) ? (
          <section id="coo-section-modification" className="form-section single-window-editor-section" aria-label="更改与重发">
            <div className="section-header">
              <h2>更改与重发</h2>
            </div>
            <div className="field-grid">
              <TextField label="原证书号" value={document.oldCertNo} onChange={(value) => patchDocument({ oldCertNo: value })} />
              <TextField label="更改栏目" value={document.modColm} onChange={(value) => patchDocument({ modColm: value })} />
              <DateField label="原证申请日期" value={document.oldDeclDate} onChange={(value) => patchDocument({ oldDeclDate: value })} />
              <DateField label="原证签发日期" value={document.oldIssueDate} onChange={(value) => patchDocument({ oldIssueDate: value })} />
            </div>
            <div className="field-grid field-grid-wide">
              <TextAreaField label="更改/重发原因" value={document.modReason} onChange={(value) => patchDocument({ modReason: value })} />
              <TextAreaField label="原有情况描述" value={document.oldSituDesc} onChange={(value) => patchDocument({ oldSituDesc: value })} />
              <TextAreaField label="更改情况描述" value={document.modSituDesc} onChange={(value) => patchDocument({ modSituDesc: value })} />
            </div>
          </section>
          ) : null}

          <section id="coo-section-items" className="form-section single-window-editor-section" aria-label="商品明细">
            <div className="section-header">
              <h2>商品明细</h2>
              <span className="section-count">{document.items.length} 行</span>
              <button className="icon-button" type="button" title="新增商品" onClick={addItem}>
                <Plus size={17} aria-hidden="true" />
              </button>
            </div>
            <CooItemsEditor
              items={document.items}
              certType={document.certType}
              editorOptions={editorOptions}
              disabled={isBusy}
              savingProducerRowIndex={savingProducerProfileRowIndex}
              onChangeItem={patchItem}
              onRemoveItem={removeItem}
              onGenerateGoodsDescription={handleGenerateGoodsDescription}
              onCopyOriginAndEnterpriseToFollowingRows={handleCopyOriginAndEnterpriseToFollowingRows}
              onOpenProducerProfile={handleOpenProducerProfiles}
              onSaveProducerProfile={handleSaveProducerProfile}
            />
          </section>

          {shouldShowCooNonpartyCorps(document) ? (
          <section id="coo-section-nonparty" className="form-section single-window-editor-section" aria-label="第三方企业">
            <div className="section-header">
              <h2>第三方企业</h2>
              <button className="icon-button" type="button" title="新增第三方企业" onClick={addNonpartyCorp}>
                <Plus size={17} aria-hidden="true" />
              </button>
            </div>
            <CooNonpartyEditor
              data={document.nonpartyCorps}
              onChangeCorp={patchNonpartyCorp}
              onRemoveCorp={removeNonpartyCorp}
            />
          </section>
          ) : null}

          <section id="coo-section-attachments" className="form-section single-window-editor-section" aria-label="附件">
            <div className="section-header">
              <h2>附件</h2>
              <button
                className="icon-button"
                type="button"
                title="选择附件"
                disabled={isBusy || !document}
                onClick={() => void addAttachmentsFromDialog()}
              >
                <Paperclip size={17} aria-hidden="true" />
              </button>
              <span className="section-count">{document.attachments.length} 条</span>
            </div>
            <CooAttachmentTable
              data={document.attachments}
              certTypeOptions={editorOptions.certTypeOptions}
              disabled={isBusy}
              onChangeAttachment={patchAttachment}
              onRemoveAttachment={removeAttachment}
              onPathError={(value) => {
                setMessage(value);
                setSuccessMessage(null);
              }}
            />
          </section>
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
            {reviewMessage ? <div className="alert">{reviewMessage}</div> : null}
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

      {lockedFieldsOpen ? (
        <SingleWindowLockedFieldsDialog
          title="海关原产地证字段锁定"
          fields={lockedFields}
          selectedKeys={selectedLockedFieldKeys}
          isBusy={unlockFieldsMutation.isPending}
          onClose={() => setLockedFieldsOpen(false)}
          onToggleField={toggleLockedField}
          onToggleAll={toggleAllLockedFields}
          onUnlockSelected={handleUnlockSelectedFields}
        />
      ) : null}
      {producerProfileRowIndex !== null && document?.items[producerProfileRowIndex] ? (
        <CustomsCooProducerProfileDialog
          client={client}
          currentProfile={buildProducerProfileInputFromCooItem(document.items[producerProfileRowIndex], document)}
          rowLabel={buildProducerProfileRowLabel(document.items[producerProfileRowIndex], producerProfileRowIndex)}
          onApply={handleApplyProducerProfile}
          onClose={() => setProducerProfileRowIndex(null)}
        />
      ) : null}
    </section>
  );
}
