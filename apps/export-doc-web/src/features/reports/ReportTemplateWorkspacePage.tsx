import { useCallback, useEffect, useMemo, useState } from "react";
import "../../styles/routes/reports.css";
import { useLocation } from "react-router-dom";
import { ApiReportTemplatePreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
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
import { hasValidReportDesignerV3Schema } from "../report-designer/reportDesignerV3TemplateParser.ts";
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
import { useReportTemplateDocumentAccess } from "./useReportTemplateDocumentAccess.ts";
import { useReportTemplateSaveMutations } from "./useReportTemplateSaveMutations.ts";
import { useUserReportTemplateLifecycleMutations } from "./useUserReportTemplateLifecycleMutations.ts";
import { useDefaultReportTemplateLifecycleMutations } from "./useDefaultReportTemplateLifecycleMutations.ts";
import { useReportTemplatePackageWorkspace } from "./useReportTemplatePackageWorkspace.ts";
import { useReportTemplateFileWorkspace } from "./useReportTemplateFileWorkspace.ts";
import { useReportTemplatePreviewMutations } from "./useReportTemplatePreviewMutations.ts";
import {
  buildNewTemplateFileName,
  buildUserTemplateKey,
  fileNameFromPath,
  readPreferredPreviewSampleProfile,
  readPreviewSourceIdFromSearch,
  readReportTypeFromSearch,
  readSearchFromHash,
  readTemplateFileNameFromSearch,
  readUserTemplateIdFromSearch,
  type DesignerMode,
  type ReportTypeOption,
  type ReportTemplatePermissionAccess,
  type TemplatePreviewMode,
  type TemplateWorkspaceMode,
} from "./reportTemplateDesignerModel.ts";
import { useReportTemplateEditingActions } from "./useReportTemplateEditingActions.ts";
import { readDefaultReportTemplatePath } from "./reportTemplateSelectionModel.ts";
import { readReportTemplateReturnTarget } from "./reportTemplateReturnNavigation.ts";
import { useReportExportDefaults } from "./useReportExportDefaults.ts";
import { createReportTemplatePageActions } from "./reportTemplatePageActions.ts";
export function ReportTemplateWorkspacePage({
  client,
  templateAccess,
  canManageSettings,
  view = "designer",
}: {
  client: ExportDocManagerApiClient;
  templateAccess: ReportTemplatePermissionAccess;
  canManageSettings: boolean;
  view?: "designer" | "management";
}) {
  const canManageTemplates = templateAccess.publish;
  const canDesignTemplates = templateAccess.design;
  const canCloneTemplates = templateAccess.clone;
  const workspaceDeviceProfile = useWorkspaceDeviceProfile();
  const workspaceDeviceMode = workspaceDeviceProfile.mode;
  const workspaceDeviceCapabilities = workspaceDeviceProfile.capabilities;
  const isLimitedReportView = !workspaceDeviceCapabilities.canUseDenseWorkbench;
  const requestConfirmation = useConfirmation();
  const { availableReportTypeOptions, invoicePreviewPermission, paymentPreviewPermission } =
    useReportTemplateDocumentAccess(templateAccess.view);
  const location = useLocation();
  const returnTarget = readReportTemplateReturnTarget(location.state);
  const routeSearch = location.search || readSearchFromHash();
  const requestedReportType = useMemo(() => readReportTypeFromSearch(routeSearch), [routeSearch]);
  const initialReportType: ReportTypeOption = requestedReportType ?? availableReportTypeOptions[0]?.value ?? "ExportDocument";
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
  const [designerMode, setDesignerMode] = useState<DesignerMode>("v3");
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
  const [renameTemplateFileName, setRenameTemplateFileName] = useState("");
  const [currentTemplateDisplayName, setCurrentTemplateDisplayName] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error" | null>(null);
  const desktopAvailable = isDesktopBridgeAvailable();
  const canUseCurrentReportType = availableReportTypeOptions.some((option) => option.value === reportType);
  const canPreviewSavedSource = reportType === "PaymentVoucher"
    ? paymentPreviewPermission.allowed
    : invoicePreviewPermission.allowed;

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
    canPreviewInvoiceSource: invoicePreviewPermission.allowed,
    canPreviewPaymentSource: paymentPreviewPermission.allowed,
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
    setDesignerMode("v3");
    setPreview(null);
  }, []);

  const handleUserTemplateLoaded = useCallback((selected: (typeof userTemplates)[number]) => {
    const syntheticPath = buildUserTemplateKey(selected.id);
    setSelectedTemplatePath(syntheticPath);
    applyLoadedContent(syntheticPath, selected.contentHtml);
    setRenameTemplateFileName(selected.name);
    setCurrentTemplateDisplayName(selected.name);
    setDesignerDraftContent("");
    setDesignerMode(hasValidReportDesignerV3Schema(selected.contentHtml) ? "v3" : "advancedHtml");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, [applyLoadedContent]);

  const handleDefaultTemplateLoaded = useCallback((template: NonNullable<typeof templateContentQuery.data>) => {
    applyLoadedContent(template.templatePath, template.content);
    setDesignerDraftContent("");
    setDesignerMode(hasValidReportDesignerV3Schema(template.content) ? "v3" : "advancedHtml");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, [applyLoadedContent]);

  const handleDefaultMetadataLoaded = useCallback((fileName: string, displayName: string) => {
    setRenameTemplateFileName(fileName);
    setCurrentTemplateDisplayName(displayName);
  }, []);

  const handleTemplateFileImported = useCallback((template: NonNullable<typeof templateContentQuery.data>) => {
    setSelectedTemplatePath(template.templatePath);
    applyLoadedContent(template.templatePath, template.content);
    setRenameTemplateFileName(fileNameFromPath(template.templatePath));
    setCurrentTemplateDisplayName(template.displayName);
    setDesignerDraftContent("");
    setDesignerMode(hasValidReportDesignerV3Schema(template.content) ? "v3" : "advancedHtml");
    setPreview(null);
  }, [applyLoadedContent]);

  const fileWorkspace = useReportTemplateFileWorkspace({
    client,
    reportType,
    selectedTemplatePath,
    defaultExportDirectory,
    requestConfirmation,
    onImported: handleTemplateFileImported,
    showMessage: (nextMessage, nextType) => { setMessage(nextMessage); setMessageType(nextType); },
  });

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
  const hasUnappliedDesignerChanges = Boolean(designerDraftContent.trim()) && designerDraftContent !== content;
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
      showFeedback("草稿已保存；发布和共享状态已按草稿规则重置。", "success");
    },
    onError: (error) => showFeedback(readApiError(error), "error"),
  });

  const {
    createBlankUserTemplateMutation,
    cloneUserTemplateMutation,
    archiveUserTemplateMutation,
    restoreUserTemplateVersionMutation,
    updateUserTemplateStatusMutation,
  } = useUserReportTemplateLifecycleMutations({
    client,
    reportType,
    selectedTemplatePath,
    selectedUserTemplateId,
    currentUserTemplate,
    newTemplateName: newUserTemplateName,
    onCreated: (created) => {
      setSelectedUserTemplateId(created.id);
      setSelectedTemplatePath(buildUserTemplateKey(created.id));
      applyLoadedContent(buildUserTemplateKey(created.id), created.contentHtml);
      setRenameTemplateFileName(created.name);
      setCurrentTemplateDisplayName(created.name);
      setNewUserTemplateName("");
      showFeedback("私有草稿已创建。", "success");
    },
    onArchived: async () => {
      setSelectedUserTemplateId(0);
      setSelectedTemplatePath("");
      clearLoadedTemplateContent();
      setRenameTemplateFileName("");
      setCurrentTemplateDisplayName("");
      showFeedback("模板已归档，可从归档列表恢复为草稿。", "success");
      await templatesQuery.refetch();
    },
    onRestored: (saved) => {
      applyLoadedContent(buildUserTemplateKey(saved.id), saved.contentHtml);
      setRenameTemplateFileName(saved.name);
      setCurrentTemplateDisplayName(saved.name);
      showFeedback(`已恢复到版本 ${saved.versionNumber}，请检查后继续编辑。`, "success");
    },
    onStatusUpdated: (saved, action) => {
      applyLoadedContent(buildUserTemplateKey(saved.id), saved.contentHtml);
      setCurrentTemplateDisplayName(saved.name);
      const nextMessage = action.kind === "publish"
        ? "模板已发布，可用于正式输出。"
        : action.kind === "disable"
          ? "模板已停用，不再用于正式输出。"
          : action.kind === "restore"
            ? saved.status === "Draft" ? "归档模板已恢复为私有草稿。" : "模板已恢复发布。"
            : `共享范围已更新：${reportTemplateShareScopeLabel(saved.shareScope)}`;
      showFeedback(nextMessage, "success");
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
    previewContent,
    isLocalSamplePreview,
    renderedPreviewHtml,
    selectedTemplateContentActive,
    isBusy,
    hasUnappliedDesignerChanges: workspaceHasUnappliedDesignerChanges,
    hasUnsavedChanges,
    canRenderTemplatePreview,
    canCreateTemplate,
    canCreateBlankUserTemplate,
    canCloneUserTemplate,
    canRenameTemplate,
    canDeleteTemplate,
    canExportPackage,
    canExportPackageByPath,
    canDownloadPackage,
    canImportPackage,
    canImportPackageByPath,
    canUploadPackage,
    canExportTemplateFile,
    canExportTemplateFileByPath,
    canImportTemplateFile,
    canImportTemplateFileByPath,
    canUploadTemplateFile,
    canSave,
    canUpdateDisplayName,
    canSetDefault,
    canFormatSource,
  } = deriveReportTemplateWorkspaceState({
    reportType,
    designerDraftContent,
    content,
    loadedContent,
    contentTemplatePath,
    selectedTemplatePath,
    currentTemplateDisplayName,
    persistedDisplayName,
    defaultTemplatePath,
    canUseAdvancedTools: workspaceDeviceCapabilities.canUseAdvancedTools,
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
      fileWorkspace.exportFileMutation.isPending,
      fileWorkspace.downloadFileMutation.isPending,
      fileWorkspace.importFileMutation.isPending,
      fileWorkspace.uploadFileMutation.isPending,
      saveMutation.isPending,
      saveUserTemplateMutation.isPending,
      createBlankUserTemplateMutation.isPending,
      cloneUserTemplateMutation.isPending,
      archiveUserTemplateMutation.isPending,
      restoreUserTemplateVersionMutation.isPending,
      updateUserTemplateStatusMutation.isPending,
      exportDefaults.isBusy,
      samplePreviewMutation.isPending,
      invoicePreviewMutation.isPending,
      paymentPreviewMutation.isPending,
    ],
    canManageTemplates,
    canDesignTemplates,
    canCloneTemplates,
    canArchiveTemplates: templateAccess.archive,
    canImportTemplates: templateAccess.import,
    canExportTemplates: templateAccess.export,
    canPreviewSavedSource,
    newTemplateFileName,
    newUserTemplateName,
    renameTemplateFileName,
    desktopAvailable,
    packageExportPath: packageWorkspace.exportPath,
    packageImportPath: packageWorkspace.importPath,
    fileExportPath: fileWorkspace.exportPath,
    fileImportPath: fileWorkspace.importPath,
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

  const {
    handleCreateTemplate,
    handleCreateBlankUserTemplate,
    handleCloneUserTemplate,
    handleUserTemplateLifecycleAction,
    handleRestoreUserTemplateVersion,
    handleRenameTemplate,
    handleUpdateDisplayName,
    handleSetDefaultTemplate,
    handleExportSettingsChange,
    handleSaveExportSettings,
    handleDeleteTemplate,
    handleSave,
  } = createReportTemplatePageActions({
    canCreateTemplate,
    createTemplate: () => createTemplateMutation.mutate(),
    canCreateBlankUserTemplate,
    createBlankUserTemplate: () => createBlankUserTemplateMutation.mutate(),
    canCloneUserTemplate,
    cloneUserTemplate: () => cloneUserTemplateMutation.mutate(),
    currentUserTemplate,
    requestConfirmation,
    runUserTemplateLifecycleAction: (value) => updateUserTemplateStatusMutation.mutate(value),
    restoreUserTemplateVersion: (version) => restoreUserTemplateVersionMutation.mutate(version),
    canRenameTemplate,
    isUserTemplate,
    hasUnsavedChanges,
    renameTemplate: () => renameTemplateMutation.mutate(),
    canUpdateDisplayName,
    updateDisplayName: () => isUserTemplate ? saveUserTemplateMutation.mutate(undefined) : updateDisplayNameMutation.mutate(),
    canSetDefault,
    setDefaultTemplate: () => setDefaultTemplateMutation.mutate(),
    canDeleteTemplate,
    deleteUserTemplate: () => {
      if (currentUserTemplate) archiveUserTemplateMutation.mutate(currentUserTemplate);
    },
    deleteTemplate: () => deleteTemplateMutation.mutate(),
    canSave,
    designerMode,
    workspaceHasUnappliedDesignerChanges,
    previewContent,
    saveNewDesignerContent: handleSaveNewReportDesignerContent,
    saveUserTemplate: () => saveUserTemplateMutation.mutate(undefined),
    saveDefaultTemplate: () => saveMutation.mutate(undefined),
    exportDefaults,
    clearFeedback: () => { setMessage(null); setMessageType(null); },
  });

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
      userPanel={Object.values(templateAccess).some(Boolean) ? {
        currentTemplate: currentUserTemplate,
        versions: userTemplateVersionsQuery.data ?? [],
        versionsLoading: userTemplateVersionsQuery.isFetching,
        newTemplateName: newUserTemplateName,
        isBusy,
        allowCreateBlank: canDesignTemplates,
        allowClone: canCloneTemplates,
        canCreateBlank: canCreateBlankUserTemplate,
        canClone: canCloneUserTemplate,
        onNewTemplateNameChange: setNewUserTemplateName,
        onCreateBlank: handleCreateBlankUserTemplate,
        onClone: handleCloneUserTemplate,
        onShareScopeChange: (shareScope) => void handleUserTemplateLifecycleAction({ kind: "share", shareScope }),
        onPublish: () => void handleUserTemplateLifecycleAction({ kind: "publish" }),
        onDisable: () => void handleUserTemplateLifecycleAction({ kind: "disable" }),
        onRestore: () => void handleUserTemplateLifecycleAction({ kind: "restore" }),
        onArchive: handleDeleteTemplate,
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
        canDelete: !isUserTemplate && canDeleteTemplate,
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
        canExportTemplates: templateAccess.export,
        canImportTemplates: templateAccess.import,
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
      filePanel={{
        desktopAvailable: fileWorkspace.desktopAvailable,
        canExportTemplates: templateAccess.export,
        canImportTemplates: templateAccess.import,
        isBusy,
        exportPath: fileWorkspace.exportPath,
        importPath: fileWorkspace.importPath,
        uploadInputRef: fileWorkspace.uploadInputRef,
        canExport: canExportTemplateFile,
        canImport: canImportTemplateFile,
        canUpload: canUploadTemplateFile,
        canExportByPath: canExportTemplateFileByPath,
        canImportByPath: canImportTemplateFileByPath,
        onExport: () => fileWorkspace.exportFile(canExportTemplateFile),
        onImport: () => void fileWorkspace.importFile(canImportTemplateFile, hasUnsavedChanges),
        onUpload: () => fileWorkspace.chooseUpload(canUploadTemplateFile),
        onUploadFileChange: (event) => void fileWorkspace.uploadFile(event, canUploadTemplateFile, hasUnsavedChanges),
        onExportByPath: () => fileWorkspace.exportFile(canExportTemplateFileByPath),
        onImportByPath: () => void fileWorkspace.importFile(canImportTemplateFileByPath, hasUnsavedChanges),
        onExportPathChange: fileWorkspace.setExportPath,
        onImportPathChange: fileWorkspace.setImportPath,
        onChooseExportPath: fileWorkspace.chooseExportPath,
        onChooseImportPath: fileWorkspace.chooseImportPath,
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
          v3Disabled={designerMode === "advancedHtml"}
          onBackToManagement={() => void handleBackToManagement()}
          onDesignerModeChange={handleDesignerModeChange}
          onPreview={handleRenderTemplatePreview}
        />

        <WorkspaceDeviceNotice
          mode={workspaceDeviceMode}
          phone="当前设备提供模板预览；返回模板管理可切换模板，完整设计请使用桌面端。"
          tablet={workspaceDeviceCapabilities.canUseAdvancedTools
            ? "可预览、使用 V3 可视化设计或高级 HTML；复杂版式建议继续使用高级 HTML。"
            : "当前设备提供模板预览；连接鼠标或触控板后可使用 V3 可视化设计或高级 HTML。"}
        />

        <ReportTemplateFeedback message={effectiveMessage} type={effectiveMessageType} />

        {workspaceMode === "design" ? (
          <div className="report-template-grid report-template-grid-design">
            <ReportTemplateDesignWorkspace
              client={client}
              editable={workspaceDeviceCapabilities.canUseAdvancedTools && (
                isUserTemplate
                  ? currentUserTemplate?.canEdit === true && canDesignTemplates
                  : canManageTemplates
              )}
              designerMode={designerMode}
              reportType={reportType}
              displayName={currentUserTemplate?.name ?? currentTemplate?.displayName ?? ""}
              content={content}
              fieldCatalog={fieldCatalogQuery.data}
              canFormatSource={canFormatSource}
              sourceDisabled={!canFormatSource}
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
