import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import "../../styles/routes/reports.css";
import { useLocation } from "react-router-dom";
import { ApiReportTemplatePreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useWorkspaceDeviceProfile } from "../../app/workspaceDevice.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  isDesktopBridgeAvailable,
} from "../../desktop/desktopBridge.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import {
  getReportDesignerPreviewSampleProfiles,
  type ReportDesignerPreviewSampleProfile,
} from "../report-designer/reportDesignerPreviewSamples.ts";
import { ReportTemplatePreviewWorkspace } from "./ReportTemplatePreviewWorkspace.tsx";
import { ReportTemplateWorkspaceHeader } from "./ReportTemplateWorkspaceHeader.tsx";
import { useReportTemplateSelectionSync } from "./useReportTemplateSelectionSync.ts";
import { deriveReportTemplateFeedback, deriveReportTemplateWorkspaceState } from "./reportTemplateWorkspaceState.ts";
import { ReportTemplateDesignWorkspace } from "./ReportTemplateDesignWorkspace.tsx";
import { useReportTemplateSelectionActions } from "./useReportTemplateSelectionActions.ts";
import { ReportTemplateFeedback } from "./ReportTemplateFeedback.tsx";
import { reportTemplateShareScopeLabel } from "./ReportTemplateUserPanel.tsx";
import { ReportTemplateManagementWorkspace } from "./ReportTemplateManagementWorkspace.tsx";
import { ReportTemplateManagementView } from "./ReportTemplateManagementView.tsx";
import { useReportTemplateWorkspaceNavigation } from "./useReportTemplateWorkspaceNavigation.ts";
import { useReportTemplateWorkspaceQueries } from "./useReportTemplateWorkspaceQueries.ts";
import { useReportTemplateSaveMutations } from "./useReportTemplateSaveMutations.ts";
import { useUserReportTemplateLifecycleMutations } from "./useUserReportTemplateLifecycleMutations.ts";
import { useDefaultReportTemplateLifecycleMutations } from "./useDefaultReportTemplateLifecycleMutations.ts";
import { useReportTemplatePackageWorkspace } from "./useReportTemplatePackageWorkspace.ts";
import { useReportTemplatePreviewMutations } from "./useReportTemplatePreviewMutations.ts";
import {
  buildNewTemplateFileName,
  buildUserTemplateKey,
  fileNameFromPath,
  matchesTemplatePath,
  readPreferredPreviewSampleProfile,
  readPreviewSourceIdFromSearch,
  readReportTypeFromSearch,
  readSearchFromHash,
  readTemplateFileNameFromSearch,
  readUserTemplateIdFromSearch,
  reportTypeOptions,
  type DesignerMode,
  type ReportTypeOption,
  type TemplatePreviewMode,
  type TemplateWorkspaceMode,
} from "./reportTemplateDesignerModel.ts";
import { useReportTemplateEditingActions } from "./useReportTemplateEditingActions.ts";
import { readDefaultReportTemplatePath } from "./reportTemplateSelectionModel.ts";
import { readReportTemplateReturnTarget } from "./reportTemplateReturnNavigation.ts";
import { useReportExportDefaults } from "./useReportExportDefaults.ts";
export function ReportTemplateWorkspacePage({
  client,
  canManageTemplates,
  canDesignTemplates,
  canManageSettings,
  view = "designer",
}: {
  client: ExportDocManagerApiClient;
  canManageTemplates: boolean;
  canDesignTemplates: boolean;
  canManageSettings: boolean;
  view?: "designer" | "management";
}) {
  const workspaceDeviceProfile = useWorkspaceDeviceProfile();
  const workspaceDeviceMode = workspaceDeviceProfile.mode;
  const workspaceDeviceCapabilities = workspaceDeviceProfile.capabilities;
  const isLimitedReportView = !workspaceDeviceCapabilities.canUseDenseWorkbench;
  const requestConfirmation = useConfirmation();
  const invoiceOutputPermission = useModulePermission("document.invoice-reports");
  const paymentOutputPermission = useModulePermission("document.payment-reports");
  const location = useLocation();
  const returnTarget = readReportTemplateReturnTarget(location.state);
  const routeSearch = location.search || readSearchFromHash();
  const requestedReportType = useMemo(() => readReportTypeFromSearch(routeSearch), [routeSearch]);
  const initialReportType: ReportTypeOption =
    requestedReportType === "PaymentVoucher" && paymentOutputPermission.canView
      ? "PaymentVoucher"
      : requestedReportType === "ExportDocument" && invoiceOutputPermission.canView
        ? "ExportDocument"
        : invoiceOutputPermission.canView
          ? "ExportDocument"
          : "PaymentVoucher";
  const requestedTemplateFileName = useMemo(() => readTemplateFileNameFromSearch(routeSearch), [routeSearch]);
  const requestedUserTemplateId = useMemo(() => readUserTemplateIdFromSearch(routeSearch), [routeSearch]);
  const requestedPreviewSourceId = useMemo(
    () => readPreviewSourceIdFromSearch(routeSearch, requestedReportType ?? initialReportType),
    [initialReportType, requestedReportType, routeSearch],
  );
  const [reportType, setReportType] = useState<ReportTypeOption>(() => initialReportType);
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [selectedUserTemplateId, setSelectedUserTemplateId] = useState(() => requestedUserTemplateId);
  const [content, setContent] = useState("");
  const [contentTemplatePath, setContentTemplatePath] = useState("");
  const [loadedContent, setLoadedContent] = useState("");
  const [workspaceMode, setWorkspaceMode] = useState<TemplateWorkspaceMode>(() =>
    isLimitedReportView ? "preview" : "design",
  );
  const [designerMode, setDesignerMode] = useState<DesignerMode>("new");
  const [designerDraftContent, setDesignerDraftContent] = useState("");
  const [templatePreviewMode, setTemplatePreviewMode] = useState<TemplatePreviewMode>("sample");
  const [templatePreviewSampleProfile, setTemplatePreviewSampleProfile] = useState<ReportDesignerPreviewSampleProfile>(() =>
    readPreferredPreviewSampleProfile(initialReportType),
  );
  const [preview, setPreview] = useState<ApiReportTemplatePreviewResponse | null>(null);
  const [previewInvoiceId, setPreviewInvoiceId] = useState(() =>
    initialReportType === "PaymentVoucher" ? 0 : requestedPreviewSourceId,
  );
  const [previewPaymentId, setPreviewPaymentId] = useState(() =>
    initialReportType === "PaymentVoucher" ? requestedPreviewSourceId : 0,
  );
  const [newTemplateFileName, setNewTemplateFileName] = useState(() => buildNewTemplateFileName(initialReportType));
  const [newTemplateDisplayName, setNewTemplateDisplayName] = useState("");
  const [newUserTemplateName, setNewUserTemplateName] = useState("");
  const [newUserTemplateShareScope, setNewUserTemplateShareScope] = useState("Private");
  const [renameTemplateFileName, setRenameTemplateFileName] = useState("");
  const [currentTemplateDisplayName, setCurrentTemplateDisplayName] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error" | null>(null);
  const desktopAvailable = isDesktopBridgeAvailable();
  const availableReportTypeOptions = useMemo(
    () => reportTypeOptions.filter((option) =>
      option.value === "PaymentVoucher" ? paymentOutputPermission.canView : invoiceOutputPermission.canView,
    ),
    [invoiceOutputPermission.canView, paymentOutputPermission.canView],
  );
  const canUseCurrentReportType =
    reportType === "PaymentVoucher" ? paymentOutputPermission.canView : invoiceOutputPermission.canView;

  const {
    templatesQuery,
    userTemplatesQuery,
    userTemplateVersionsQuery,
    fieldCatalogQuery,
    previewInvoicesQuery,
    previewPaymentsQuery,
    settingsQuery,
    templateContentQuery,
  } = useReportTemplateWorkspaceQueries({
    client,
    reportType,
    enabled: canUseCurrentReportType,
    includeDesignerData: view === "designer",
    selectedUserTemplateId,
    selectedTemplatePath,
  });
  const currentUserTemplate = useMemo(
    () => userTemplatesQuery.data?.find((template) => template.id === selectedUserTemplateId) ?? null,
    [selectedUserTemplateId, userTemplatesQuery.data],
  );
  const templates = templatesQuery.data ?? [];
  const userTemplates = userTemplatesQuery.data ?? [];
  const currentTemplate = useMemo(
    () => templates.find((template) => template.templatePath === selectedTemplatePath) ?? null,
    [selectedTemplatePath, templates],
  );
  const defaultTemplatePath = readDefaultReportTemplatePath(settingsQuery.data?.settings, reportType);
  const persistedDisplayName = currentUserTemplate?.name || currentTemplate?.displayName || "";
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  const packageWorkspace = useReportTemplatePackageWorkspace({
    client,
    reportType,
    selectedTemplatePath,
    defaultExportDirectory,
    requestConfirmation,
    clearPreview: () => setPreview(null),
    showMessage: (nextMessage, nextType) => { setMessage(nextMessage); setMessageType(nextType); },
  });

  const previewSampleProfiles = useMemo(() => getReportDesignerPreviewSampleProfiles(reportType), [reportType]);

  const applyLoadedContent = useCallback((templatePath: string, nextContent: string) => {
    setContent(nextContent);
    setContentTemplatePath(templatePath);
    setLoadedContent(nextContent);
  }, []);
  const showFeedback = useCallback((nextMessage: string, nextType: "success" | "error") => {
    setMessage(nextMessage);
    setMessageType(nextType);
  }, []);
  const exportDefaults = useReportExportDefaults({
    client,
    response: settingsQuery.data,
    refetch: settingsQuery.refetch,
    onFeedback: showFeedback,
  });

  const handleSelectionChanged = useCallback(() => {
    setContent("");
    setContentTemplatePath("");
    setLoadedContent("");
    setDesignerDraftContent("");
    setPreview(null);
  }, []);

  const handleUserTemplateLoaded = useCallback((selected: (typeof userTemplates)[number]) => {
    const syntheticPath = buildUserTemplateKey(selected.id);
    setSelectedTemplatePath(syntheticPath);
    applyLoadedContent(syntheticPath, selected.contentHtml);
    setRenameTemplateFileName(selected.name);
    setCurrentTemplateDisplayName(selected.name);
    setDesignerDraftContent("");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, [applyLoadedContent]);

  const handleDefaultTemplateLoaded = useCallback((template: NonNullable<typeof templateContentQuery.data>) => {
    applyLoadedContent(template.templatePath, template.content);
    setDesignerDraftContent("");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, [applyLoadedContent]);

  const handleDefaultMetadataLoaded = useCallback((fileName: string, displayName: string) => {
    setRenameTemplateFileName(fileName);
    setCurrentTemplateDisplayName(displayName);
  }, []);

  useReportTemplateSelectionSync({
    requestedReportType,
    availableReportTypeOptions,
    reportType,
    setReportType,
    previewSampleProfiles,
    previewSampleProfile: templatePreviewSampleProfile,
    setPreviewSampleProfile: setTemplatePreviewSampleProfile,
    requestedPreviewSourceId,
    previewInvoiceIds: (previewInvoicesQuery.data?.items ?? []).map((invoice) => invoice.id),
    previewPaymentIds: (previewPaymentsQuery.data?.items ?? []).map((payment) => payment.id),
    setPreviewInvoiceId,
    setPreviewPaymentId,
    templates,
    templatesLoaded: templatesQuery.isSuccess,
    requestedTemplateFileName,
    configuredTemplatePath: defaultTemplatePath,
    requestedUserTemplateId,
    selectedTemplatePath,
    setSelectedTemplatePath,
    selectedUserTemplateId,
    setSelectedUserTemplateId,
    userTemplates,
    userTemplatesLoaded: userTemplatesQuery.isSuccess,
    templateContent: templateContentQuery.data ?? null,
    onSelectionChanged: handleSelectionChanged,
    onUserTemplateLoaded: handleUserTemplateLoaded,
    onDefaultTemplateLoaded: handleDefaultTemplateLoaded,
    onDefaultMetadataLoaded: handleDefaultMetadataLoaded,
  });

  const clearSelectionFeedback = useCallback(() => {
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, []);

  useEffect(() => {
    if (view === "designer" && isLimitedReportView) {
      setWorkspaceMode("preview");
    }
  }, [isLimitedReportView, view]);
  const hasAppliedTemplateChanges = content !== loadedContent;
  const hasUnappliedDesignerChanges =
    designerMode === "new" && Boolean(designerDraftContent.trim()) && designerDraftContent !== content;
  const hasUnsavedTemplateChanges = Boolean(
    selectedTemplatePath && (hasAppliedTemplateChanges || hasUnappliedDesignerChanges),
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedTemplateChanges,
    message: "当前报表模板有未保存的修改。",
  });
  const {
    clearLoadedTemplateContent,
    handleReportTypeChange,
    handleTemplateChange,
    handleUserTemplateChange,
    handlePreviewSourceChange,
  } = useReportTemplateSelectionActions({
    reportType,
    setReportType,
    setSelectedUserTemplateId,
    setSelectedTemplatePath,
    setContent,
    setContentTemplatePath,
    setLoadedContent,
    setDesignerDraftContent,
    setNewTemplateFileName,
    setNewTemplateDisplayName,
    setNewUserTemplateName,
    setNewUserTemplateShareScope,
    setRenameTemplateFileName,
    setTemplatePreviewSampleProfile,
    setPreviewInvoiceId,
    setPreviewPaymentId,
    clearFeedback: clearSelectionFeedback,
    confirmDiscardChanges,
  });

  const { saveDefaultTemplateMutation: saveMutation, saveUserTemplateMutation } = useReportTemplateSaveMutations({
    client,
    reportType,
    selectedTemplatePath,
    selectedUserTemplateId,
    userTemplates: userTemplatesQuery.data ?? [],
    content,
    userTemplateName: currentTemplateDisplayName,
    onDefaultTemplateSaved: (saved) => {
      applyLoadedContent(saved.templatePath, saved.content);
      showFeedback("模板已保存。", "success");
    },
    onUserTemplateSaved: (saved) => {
      const syntheticPath = buildUserTemplateKey(saved.id);
      applyLoadedContent(syntheticPath, saved.contentHtml);
      setRenameTemplateFileName(saved.name);
      setCurrentTemplateDisplayName(saved.name);
      showFeedback(saved.isShared ? "模板已保存并保持团队共享。" : "我的模板已保存。", "success");
    },
    onError: (error) => showFeedback(readApiError(error), "error"),
  });

  const {
    createUserTemplateMutation,
    deleteUserTemplateMutation,
    restoreUserTemplateVersionMutation,
    updateUserTemplateStatusMutation,
  } = useUserReportTemplateLifecycleMutations({
    client,
    reportType,
    selectedTemplatePath,
    selectedUserTemplateId,
    userTemplates: userTemplatesQuery.data ?? [],
    currentUserTemplate,
    content,
    newTemplateName: newUserTemplateName,
    newTemplateShareScope: newUserTemplateShareScope,
    onCreated: (created) => {
      setSelectedUserTemplateId(created.id);
      setSelectedTemplatePath(buildUserTemplateKey(created.id));
      applyLoadedContent(buildUserTemplateKey(created.id), created.contentHtml);
      setRenameTemplateFileName(created.name);
      setCurrentTemplateDisplayName(created.name);
      setNewUserTemplateName("");
      setNewUserTemplateShareScope("Private");
      showFeedback(created.isShared ? "团队共享模板已创建。" : "我的私有模板已创建。", "success");
    },
    onDeleted: async () => {
      setSelectedUserTemplateId(0);
      setSelectedTemplatePath("");
      clearLoadedTemplateContent();
      setRenameTemplateFileName("");
      setCurrentTemplateDisplayName("");
      showFeedback("我的模板已删除。", "success");
      await templatesQuery.refetch();
    },
    onRestored: (saved) => {
      applyLoadedContent(buildUserTemplateKey(saved.id), saved.contentHtml);
      setRenameTemplateFileName(saved.name);
      setCurrentTemplateDisplayName(saved.name);
      showFeedback(`已恢复到版本 ${saved.versionNumber}，请检查后继续编辑。`, "success");
    },
    onStatusUpdated: (saved) => {
      applyLoadedContent(buildUserTemplateKey(saved.id), saved.contentHtml);
      setCurrentTemplateDisplayName(saved.name);
      showFeedback(!saved.isActive ? "模板已停用，不再用于预览和正式输出。" : `共享范围已更新：${reportTemplateShareScopeLabel(saved.shareScope)}`, "success");
    },
    onError: (error) => showFeedback(readApiError(error), "error"),
  });

  const {
    createTemplateMutation,
    renameTemplateMutation,
    updateDisplayNameMutation,
    setDefaultTemplateMutation,
    deleteTemplateMutation,
  } =
    useDefaultReportTemplateLifecycleMutations({
      client,
      reportType,
      selectedTemplatePath,
      newTemplateFileName,
      newTemplateDisplayName,
      currentTemplateDisplayName,
      renameTemplateFileName,
      onCreated: (created) => {
        setSelectedTemplatePath(created.templatePath);
        applyLoadedContent(created.templatePath, created.content);
        setRenameTemplateFileName(fileNameFromPath(created.templatePath));
        setCurrentTemplateDisplayName(created.displayName);
        setNewTemplateFileName(buildNewTemplateFileName(reportType));
        setNewTemplateDisplayName("");
        setPreview(null);
        showFeedback("模板已新建。", "success");
      },
      onRenamed: (renamed) => {
        setSelectedTemplatePath(renamed.templatePath);
        applyLoadedContent(renamed.templatePath, renamed.content);
        setRenameTemplateFileName(fileNameFromPath(renamed.templatePath));
        setCurrentTemplateDisplayName(renamed.displayName);
        setPreview(null);
        showFeedback("模板文件名已更新。", "success");
      },
      onDisplayNameUpdated: (updated) => {
        setCurrentTemplateDisplayName(updated.displayName);
        showFeedback("模板显示名称已更新，文件名保持不变。", "success");
      },
      onDefaultSet: (nextMessage) => showFeedback(nextMessage, "success"),
      onDeleted: () => {
        setSelectedTemplatePath("");
        setContent("");
        setContentTemplatePath("");
        setLoadedContent("");
        setRenameTemplateFileName("");
        setCurrentTemplateDisplayName("");
        setPreview(null);
        showFeedback("模板已删除。", "success");
      },
      onError: (error) => showFeedback(readApiError(error), "error"),
    });

  const { samplePreviewMutation, invoicePreviewMutation, paymentPreviewMutation } = useReportTemplatePreviewMutations({
    client,
    reportType,
    selectedTemplatePath,
    content,
    withSeal: reportType === "ExportDocument" && (currentTemplate?.withSealDefault ?? true),
    previewInvoiceId,
    previewPaymentId,
    onPreviewed: (response) => {
      setPreview(response);
      setWorkspaceMode("preview");
      setMessage(null);
      setMessageType(null);
    },
    onError: (error) => {
      setPreview(null);
      setMessage(readApiError(error));
      setMessageType("error");
    },
  });

  const {
    isUserTemplate,
    previewDocumentOptions,
    selectedPreviewSourceValue,
    selectedPreviewSourceLabel,
    previewContent,
    isLocalSamplePreview,
    renderedPreviewHtml,
    selectedTemplateContentActive,
    isBusy,
    hasUnappliedDesignerChanges: workspaceHasUnappliedDesignerChanges,
    hasUnsavedChanges,
    canRenderTemplatePreview,
    canCreateTemplate,
    canCreateUserTemplate,
    canRenameTemplate,
    canDeleteTemplate,
    canExportPackage,
    canExportPackageByPath,
    canDownloadPackage,
    canImportPackage,
    canImportPackageByPath,
    canUploadPackage,
    canSave,
    canFormatSource,
  } = deriveReportTemplateWorkspaceState({
    reportType,
    designerMode,
    designerDraftContent,
    content,
    loadedContent,
    contentTemplatePath,
    selectedTemplatePath,
    selectedContentTemplatePath: templateContentQuery.data?.templatePath ?? "",
    currentUserTemplate,
    templatePreviewMode,
    templatePreviewSampleProfile,
    previewHtml: preview?.html ?? "",
    previewInvoices: previewInvoicesQuery.data?.items ?? [],
    previewPayments: previewPaymentsQuery.data?.items ?? [],
    previewInvoiceId,
    previewPaymentId,
    busyFlags: [
      templatesQuery.isFetching,
      userTemplatesQuery.isFetching,
      settingsQuery.isFetching,
      templateContentQuery.isFetching,
      createTemplateMutation.isPending,
      renameTemplateMutation.isPending,
      updateDisplayNameMutation.isPending,
      setDefaultTemplateMutation.isPending,
      deleteTemplateMutation.isPending,
      packageWorkspace.exportPackageMutation.isPending,
      packageWorkspace.downloadPackageMutation.isPending,
      packageWorkspace.importPackageMutation.isPending,
      packageWorkspace.uploadPackageMutation.isPending,
      saveMutation.isPending,
      saveUserTemplateMutation.isPending,
      createUserTemplateMutation.isPending,
      deleteUserTemplateMutation.isPending,
      restoreUserTemplateVersionMutation.isPending,
      updateUserTemplateStatusMutation.isPending,
      exportDefaults.isBusy,
      samplePreviewMutation.isPending,
      invoicePreviewMutation.isPending,
      paymentPreviewMutation.isPending,
    ],
    canManageTemplates,
    canDesignTemplates,
    newTemplateFileName,
    newUserTemplateName,
    renameTemplateFileName,
    desktopAvailable,
    packageExportPath: packageWorkspace.exportPath,
    packageImportPath: packageWorkspace.importPath,
    templateContentFetching: templateContentQuery.isFetching,
  });
  const { effectiveMessage, effectiveMessageType } = deriveReportTemplateFeedback({
    reportType,
    templateListError: templatesQuery.error,
    userTemplateListError: userTemplatesQuery.error,
    templateContentError: templateContentQuery.error,
    previewInvoiceError: previewInvoicesQuery.error,
    previewPaymentError: previewPaymentsQuery.error,
    message,
    messageType,
  });
  const canUpdateDisplayName = Boolean(
    selectedTemplatePath &&
    currentTemplateDisplayName.trim() &&
    currentTemplateDisplayName.trim() !== persistedDisplayName &&
    !isBusy &&
    (isUserTemplate ? currentUserTemplate?.canEdit && canDesignTemplates : canManageTemplates),
  );
  const canSetDefault = Boolean(
    selectedTemplatePath &&
    canManageTemplates &&
    !isBusy &&
    !matchesTemplatePath(selectedTemplatePath, defaultTemplatePath),
  );
  const {
    handleDesignerModeChange,
    handleFormatSource,
    handleRenderTemplatePreview,
    handleSaveNewReportDesignerContent,
    handleTemplatePreviewModeChange,
    handleTemplatePreviewSampleProfileChange,
  } = useReportTemplateEditingActions({
    canDesignTemplates,
    canFormatSource,
    canManageTemplates,
    canRenderTemplatePreview,
    content,
    currentUserTemplateCanEdit: currentUserTemplate?.canEdit === true,
    designerDraftContent,
    designerMode,
    isLimitedReportView,
    isLocalSamplePreview,
    isUserTemplate,
    reportType,
    selectedTemplateContentActive,
    selectedTemplatePath,
    templatePreviewMode,
    workspaceHasUnappliedDesignerChanges,
    renderInvoicePreview: () => invoicePreviewMutation.mutate(),
    renderPaymentPreview: () => paymentPreviewMutation.mutate(),
    renderSamplePreview: () => samplePreviewMutation.mutate(previewContent),
    saveDefaultTemplateContent: (nextContent) => saveMutation.mutate(nextContent),
    saveUserTemplateContent: (nextContent) => saveUserTemplateMutation.mutate(nextContent),
    setContent,
    setContentTemplatePath,
    setDesignerMode,
    setMessage,
    setMessageType,
    setPreview,
    setTemplatePreviewMode,
    setTemplatePreviewSampleProfile,
    setWorkspaceMode,
  });

  function handleCreateTemplate() {
    if (canCreateTemplate) {
      createTemplateMutation.mutate();
    }
  }

  function handleCreateUserTemplate() {
    if (canCreateUserTemplate) {
      createUserTemplateMutation.mutate();
    }
  }
  async function handleToggleUserTemplateActive() {
    if (!currentUserTemplate) {
      return;
    }

    const action = currentUserTemplate.isActive ? "停用" : "重新启用";
    if (await requestConfirmation({
      title: `${action}模板`,
      description: `确定${action}“${currentUserTemplate.name}”吗？`,
      confirmLabel: `确认${action}`,
    })) {
      updateUserTemplateStatusMutation.mutate({ isActive: !currentUserTemplate.isActive });
    }
  }
  async function handleRestoreUserTemplateVersion(versionNumber: number) {
    if (await requestConfirmation({
      title: `恢复到 V${versionNumber}`,
      description: `确定恢复到 V${versionNumber} 吗？`,
      details: ["当前未保存修改将被替换。", "现有历史版本仍会保留。"],
      confirmLabel: "确认恢复",
    })) {
      restoreUserTemplateVersionMutation.mutate(versionNumber);
    }
  }
  async function handleRenameTemplate() {
    if (!canRenameTemplate || isUserTemplate) {
      return;
    }

    if (hasUnsavedChanges && !await requestConfirmation({ title: "修改模板文件名", description: "当前模板有未保存修改，确定继续修改文件名吗？", details: ["文件名修改后，默认模板和导出设置中的引用会同步更新。"], confirmLabel: "继续修改" })) {
      return;
    }

    renameTemplateMutation.mutate();
  }
  function handleUpdateDisplayName() {
    if (!canUpdateDisplayName) {
      return;
    }

    if (isUserTemplate) {
      saveUserTemplateMutation.mutate(undefined);
    } else {
      updateDisplayNameMutation.mutate();
    }
  }

  function handleSetDefaultTemplate() {
    if (canSetDefault) {
      setDefaultTemplateMutation.mutate();
    }
  }

  function handleExportSettingsChange(path: string[], value: unknown) {
    exportDefaults.onChange(path, value);
    setMessage(null);
    setMessageType(null);
  }

  function handleSaveExportSettings() {
    exportDefaults.onSave();
  }

  async function handleDeleteTemplate() {
    if (!canDeleteTemplate) {
      return;
    }

    if (!await requestConfirmation({ title: "删除报表模板", description: "确定删除当前模板吗？", details: hasUnsavedChanges ? ["当前模板有未保存修改，这些修改将丢失。"] : undefined, confirmLabel: "确认删除", tone: "danger" })) {
      return;
    }

    if (currentUserTemplate) {
      deleteUserTemplateMutation.mutate(currentUserTemplate.id);
    } else {
      deleteTemplateMutation.mutate();
    }
  }

  function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave) {
      return;
    }
    if (workspaceHasUnappliedDesignerChanges) {
      void handleSaveNewReportDesignerContent(previewContent);
    } else if (isUserTemplate) {
      saveUserTemplateMutation.mutate(undefined);
    } else {
      saveMutation.mutate(undefined);
    }
  }

  const { handleRefreshTemplates, handleBackToManagement, handleReturnToBusiness, handleOpenDesigner } =
    useReportTemplateWorkspaceNavigation({
      reportType,
      selectedTemplatePath,
      selectedUserTemplateId,
      locationState: location.state,
      returnTarget,
      confirmDiscardChanges,
      requestConfirmation,
      exportDefaultsDirty: exportDefaults.isDirty,
      refetchTemplates: async () => {
        await Promise.all([templatesQuery.refetch(), userTemplatesQuery.refetch()]);
      },
    });

  const selectionPanelProps = {
    reportType,
    reportTypeOptions: availableReportTypeOptions,
    templates,
    userTemplates,
    selectedTemplatePath,
    selectedUserTemplateId,
    defaultTemplatePath,
    isBusy,
    canSetDefault,
    onReportTypeChange: handleReportTypeChange,
    onTemplateChange: handleTemplateChange,
    onUserTemplateChange: handleUserTemplateChange,
    onSetDefault: handleSetDefaultTemplate,
  };

  const currentTemplateName = persistedDisplayName || (selectedTemplatePath ? fileNameFromPath(selectedTemplatePath) : "未选择模板");
  const managementWorkspace = (
    <ReportTemplateManagementWorkspace
       reportType={reportType}
       selectionPanel={selectionPanelProps}
       exportDefaultsPanel={{
         settings: exportDefaults.settings,
         canManageSettings,
         isBusy: isBusy || exportDefaults.isBusy,
         isDirty: exportDefaults.isDirty,
         onChange: handleExportSettingsChange,
         onSave: handleSaveExportSettings,
         templates,
       }}
      userPanel={canDesignTemplates ? {
        currentTemplate: currentUserTemplate,
        versions: userTemplateVersionsQuery.data ?? [],
        versionsLoading: userTemplateVersionsQuery.isFetching,
        newTemplateName: newUserTemplateName,
        newTemplateShareScope: newUserTemplateShareScope,
        isBusy,
        canCreate: canCreateUserTemplate,
        isUserTemplate,
        onNewTemplateNameChange: setNewUserTemplateName,
        onNewTemplateShareScopeChange: setNewUserTemplateShareScope,
        onCreate: handleCreateUserTemplate,
        onShareScopeChange: (shareScope) => updateUserTemplateStatusMutation.mutate({ shareScope }),
        onToggleActive: handleToggleUserTemplateActive,
        onRestoreVersion: handleRestoreUserTemplateVersion,
      } : null}
      adminPanel={{
        currentTemplateLabel: currentTemplateName,
        newTemplateFileName,
        newTemplateDisplayName,
        currentTemplateDisplayName,
        renameTemplateFileName,
        isUserTemplate,
        canManageTemplates,
        canCreate: canCreateTemplate,
        canUpdateDisplayName,
        canRenameFile: !isUserTemplate && canRenameTemplate,
        canDelete: canDeleteTemplate,
        canEditDisplayName: isUserTemplate
          ? Boolean(currentUserTemplate?.canEdit) && canDesignTemplates && !isBusy
          : canManageTemplates && Boolean(selectedTemplatePath) && !isBusy,
        canEditFileName: !isUserTemplate && canManageTemplates && Boolean(selectedTemplatePath) && !isBusy,
        isBusy,
        onNewTemplateFileNameChange: setNewTemplateFileName,
        onNewTemplateDisplayNameChange: setNewTemplateDisplayName,
        onCurrentTemplateDisplayNameChange: setCurrentTemplateDisplayName,
        onRenameTemplateFileNameChange: setRenameTemplateFileName,
        onCreate: handleCreateTemplate,
        onUpdateDisplayName: handleUpdateDisplayName,
        onRenameFile: handleRenameTemplate,
        onDelete: handleDeleteTemplate,
      }}
      packagePanel={{
        desktopAvailable: packageWorkspace.desktopAvailable,
        canManageTemplates,
        isBusy,
        importStrategy: packageWorkspace.importStrategy,
        exportPath: packageWorkspace.exportPath,
        importPath: packageWorkspace.importPath,
        uploadInputRef: packageWorkspace.uploadInputRef,
        canExport: canExportPackage,
        canExportByPath: canExportPackageByPath,
        canDownload: canDownloadPackage,
        canImport: canImportPackage,
        canImportByPath: canImportPackageByPath,
        canUpload: canUploadPackage,
        onImportStrategyChange: packageWorkspace.setImportStrategy,
        onExport: () => packageWorkspace.exportPackage(canExportPackage),
        onExportByPath: () => packageWorkspace.exportByPath(canExportPackageByPath),
        onDownload: () => packageWorkspace.downloadPackage(canDownloadPackage),
        onImport: () => packageWorkspace.importPackage(canImportPackage, hasUnsavedChanges),
        onImportByPath: () => packageWorkspace.importByPath(canImportPackageByPath, hasUnsavedChanges),
        onUpload: () => packageWorkspace.chooseUpload(canUploadPackage),
        onUploadFileChange: (event) => packageWorkspace.uploadFile(event, canUploadPackage, hasUnsavedChanges),
        onExportPathChange: packageWorkspace.setExportPath,
        onImportPathChange: packageWorkspace.setImportPath,
        onChooseExportPath: packageWorkspace.chooseExportPath,
        onChooseImportPath: packageWorkspace.chooseImportPath,
      }}
    />
  );

  if (view === "management") {
    return (
      <ReportTemplateManagementView
        currentTemplateName={currentTemplateName}
        returnTarget={returnTarget}
        workspaceDeviceMode={workspaceDeviceMode}
        isBusy={isBusy}
        canOpenDesigner={Boolean(selectedTemplatePath) && selectedTemplateContentActive && !isBusy}
        message={effectiveMessage}
        messageType={effectiveMessageType}
        managementWorkspace={managementWorkspace}
        onRefresh={() => void handleRefreshTemplates()}
        onOpenDesigner={handleOpenDesigner}
        onReturn={() => void handleReturnToBusiness()}
      />
    );
  }

  return (
    <section className="editor-surface report-template-surface" aria-label="报表模板设计">
      <form className="report-template-layout" onSubmit={handleSave} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
        <ReportTemplateWorkspaceHeader
          title={currentUserTemplate?.name || currentTemplate?.displayName || "报表模板"}
          designerMode={designerMode}
          workspaceMode={workspaceMode}
          canPreview={canRenderTemplatePreview}
          canSave={canSave}
          designDisabled={isLimitedReportView}
          onBackToManagement={() => void handleBackToManagement()}
          onDesignerModeChange={handleDesignerModeChange}
          onPreview={handleRenderTemplatePreview}
        />

        <WorkspaceDeviceNotice
          mode={workspaceDeviceMode}
          phone="当前设备提供模板预览；返回模板管理可切换模板，完整设计请使用桌面端。"
          tablet={workspaceDeviceCapabilities.canUseAdvancedTools
            ? "可预览并使用高级 HTML；完整可视化设计仍建议使用更宽屏幕。"
            : "当前设备提供模板预览；连接鼠标或触控板后可使用高级 HTML。"}
        />

        <ReportTemplateFeedback message={effectiveMessage} type={effectiveMessageType} />

        {workspaceMode === "design" ? (
          <div className={`report-template-grid report-template-grid-design report-template-grid-${designerMode}`}>
            <ReportTemplateDesignWorkspace
              designerMode={designerMode}
              reportType={reportType}
              displayName={currentUserTemplate?.name ?? currentTemplate?.displayName ?? ""}
              content={content}
              fieldCatalog={fieldCatalogQuery.data}
              canFormatSource={canFormatSource}
              sourceDisabled={
                !selectedTemplatePath ||
                templateContentQuery.isFetching ||
                (isUserTemplate ? !currentUserTemplate?.canEdit : !canManageTemplates)
              }
              onDesignerDraftContentChange={setDesignerDraftContent}
              onFormatSource={handleFormatSource}
              onSourceContentChange={(nextContent) => {
                setContent(nextContent);
                setContentTemplatePath(selectedTemplatePath);
                setPreview(null);
                setMessage(null);
                setMessageType(null);
              }}
            />
          </div>
        ) : (
          <ReportTemplatePreviewWorkspace
            mode={templatePreviewMode}
            sampleProfile={templatePreviewSampleProfile}
            sampleProfiles={previewSampleProfiles.map((profile) => ({ value: profile.value, label: profile.label }))}
            selectedSourceValue={selectedPreviewSourceValue}
            sourceOptions={previewDocumentOptions}
            renderedHtml={renderedPreviewHtml}
            isBusy={isBusy}
            canPreview={canRenderTemplatePreview}
            canSave={canSave}
            onModeChange={handleTemplatePreviewModeChange}
            onSampleProfileChange={handleTemplatePreviewSampleProfileChange}
            onSourceChange={handlePreviewSourceChange}
            onPreview={handleRenderTemplatePreview}
          />
        )}
      </form>
    </section>
  );
}
