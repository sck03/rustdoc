import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { useLocation } from "react-router-dom";
import { ApiReportTemplatePreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getWorkspaceDeviceCapabilities, useWorkspaceDeviceMode } from "../../app/workspaceDevice.ts";
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
import { hasReportDesignerSchema } from "../report-designer/reportDesignerTemplateParser.ts";
import {
  getReportDesignerPreviewSampleProfiles,
  type ReportDesignerPreviewSampleProfile,
} from "../report-designer/reportDesignerPreviewSamples.ts";
import { formatReportTemplateSource } from "./reportTemplateFormatter.ts";
import { ReportTemplatePackagePanel } from "./ReportTemplatePackagePanel.tsx";
import { ReportTemplatePreviewWorkspace } from "./ReportTemplatePreviewWorkspace.tsx";
import { ReportTemplateWorkspaceHeader } from "./ReportTemplateWorkspaceHeader.tsx";
import { ReportTemplateAdminPanel } from "./ReportTemplateAdminPanel.tsx";
import { ReportTemplateSelectionPanel } from "./ReportTemplateSelectionPanel.tsx";
import { useReportTemplateSelectionSync } from "./useReportTemplateSelectionSync.ts";
import { deriveReportTemplateFeedback, deriveReportTemplateWorkspaceState } from "./reportTemplateWorkspaceState.ts";
import { ReportTemplateDesignWorkspace } from "./ReportTemplateDesignWorkspace.tsx";
import { useReportTemplateSelectionActions } from "./useReportTemplateSelectionActions.ts";
import { ReportTemplateFeedback } from "./ReportTemplateFeedback.tsx";
import { ReportTemplateUserPanel, reportTemplateShareScopeLabel } from "./ReportTemplateUserPanel.tsx";
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
  normalizePreviewSampleProfile,
  readPreferredPreviewSampleProfile,
  readPreviewSourceIdFromSearch,
  readReportTypeFromSearch,
  readSearchFromHash,
  readTemplateFileNameFromSearch,
  reportTypeOptions,
  type DesignerMode,
  type ReportTypeOption,
  type TemplatePreviewMode,
  type TemplateWorkspaceMode,
} from "./reportTemplateDesignerModel.ts";

export function ReportTemplateDesignerPage({
  apiBaseUrl: _apiBaseUrl,
  client,
  canManageTemplates,
  canDesignTemplates,
}: {
  apiBaseUrl: string;
  client: ExportDocManagerApiClient;
  canManageTemplates: boolean;
  canDesignTemplates: boolean;
}) {
  const workspaceDeviceMode = useWorkspaceDeviceMode();
  const workspaceDeviceCapabilities = getWorkspaceDeviceCapabilities(workspaceDeviceMode);
  const isLimitedReportView = !workspaceDeviceCapabilities.canUseDenseWorkbench;
  const requestConfirmation = useConfirmation();
  const invoiceOutputPermission = useModulePermission("document.invoice-reports");
  const paymentOutputPermission = useModulePermission("document.payment-reports");
  const location = useLocation();
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
  const requestedPreviewSourceId = useMemo(
    () => readPreviewSourceIdFromSearch(routeSearch, requestedReportType ?? initialReportType),
    [initialReportType, requestedReportType, routeSearch],
  );
  const [reportType, setReportType] = useState<ReportTypeOption>(() => initialReportType);
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [selectedUserTemplateId, setSelectedUserTemplateId] = useState(0);
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
    setContent(selected.contentHtml);
    setContentTemplatePath(syntheticPath);
    setLoadedContent(selected.contentHtml);
    setRenameTemplateFileName(selected.name);
    setDesignerDraftContent("");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, []);

  const handleDefaultTemplateLoaded = useCallback((template: NonNullable<typeof templateContentQuery.data>) => {
    setContent(template.content);
    setContentTemplatePath(template.templatePath);
    setLoadedContent(template.content);
    setDesignerDraftContent("");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, []);

  const handleDefaultRenamePath = useCallback((fileName: string) => setRenameTemplateFileName(fileName), []);

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
    onDefaultRenamePath: handleDefaultRenamePath,
  });

  const clearSelectionFeedback = useCallback(() => {
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }, []);

  useEffect(() => {
    if (isLimitedReportView) {
      setWorkspaceMode("preview");
    }
  }, [isLimitedReportView]);
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
    renameTemplateFileName,
    onDefaultTemplateSaved: (saved) => {
      setContent(saved.content);
      setContentTemplatePath(saved.templatePath);
      setLoadedContent(saved.content);
      setMessage("模板已保存。");
      setMessageType("success");
    },
    onUserTemplateSaved: (saved) => {
      const syntheticPath = buildUserTemplateKey(saved.id);
      setContent(saved.contentHtml);
      setContentTemplatePath(syntheticPath);
      setLoadedContent(saved.contentHtml);
      setRenameTemplateFileName(saved.name);
      setMessage(saved.isShared ? "模板已保存并保持团队共享。" : "我的模板已保存。");
      setMessageType("success");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageType("error");
    },
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
      setContent(created.contentHtml);
      setContentTemplatePath(buildUserTemplateKey(created.id));
      setLoadedContent(created.contentHtml);
      setRenameTemplateFileName(created.name);
      setNewUserTemplateName("");
      setNewUserTemplateShareScope("Private");
      setMessage(created.isShared ? "团队共享模板已创建。" : "我的私有模板已创建。");
      setMessageType("success");
    },
    onDeleted: async () => {
      setSelectedUserTemplateId(0);
      setSelectedTemplatePath("");
      clearLoadedTemplateContent();
      setRenameTemplateFileName("");
      setMessage("我的模板已删除。");
      setMessageType("success");
      await templatesQuery.refetch();
    },
    onRestored: (saved) => {
      setContent(saved.contentHtml);
      setContentTemplatePath(buildUserTemplateKey(saved.id));
      setLoadedContent(saved.contentHtml);
      setRenameTemplateFileName(saved.name);
      setMessage(`已恢复到版本 ${saved.versionNumber}，请检查后继续编辑。`);
      setMessageType("success");
    },
    onStatusUpdated: (saved) => {
      setContent(saved.contentHtml);
      setContentTemplatePath(buildUserTemplateKey(saved.id));
      setLoadedContent(saved.contentHtml);
      setMessage(!saved.isActive ? "模板已停用，不再用于预览和正式输出。" : `共享范围已更新：${reportTemplateShareScopeLabel(saved.shareScope)}`);
      setMessageType("success");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageType("error");
    },
  });

  const { createTemplateMutation, renameTemplateMutation, deleteTemplateMutation } =
    useDefaultReportTemplateLifecycleMutations({
      client,
      reportType,
      selectedTemplatePath,
      newTemplateFileName,
      newTemplateDisplayName,
      renameTemplateFileName,
      onCreated: (created) => {
        setSelectedTemplatePath(created.templatePath);
        setContent(created.content);
        setContentTemplatePath(created.templatePath);
        setLoadedContent(created.content);
        setRenameTemplateFileName(fileNameFromPath(created.templatePath));
        setNewTemplateFileName(buildNewTemplateFileName(reportType));
        setNewTemplateDisplayName("");
        setPreview(null);
        setMessage("模板已新建。");
        setMessageType("success");
      },
      onRenamed: (renamed) => {
        setSelectedTemplatePath(renamed.templatePath);
        setContent(renamed.content);
        setContentTemplatePath(renamed.templatePath);
        setLoadedContent(renamed.content);
        setRenameTemplateFileName(fileNameFromPath(renamed.templatePath));
        setPreview(null);
        setMessage("模板已重命名。");
        setMessageType("success");
      },
      onDeleted: () => {
        setSelectedTemplatePath("");
        setContent("");
        setContentTemplatePath("");
        setLoadedContent("");
        setRenameTemplateFileName("");
        setPreview(null);
        setMessage("模板已删除。");
        setMessageType("success");
      },
      onError: (error) => {
        setMessage(readApiError(error));
        setMessageType("error");
      },
    });

  const { samplePreviewMutation, invoicePreviewMutation, paymentPreviewMutation } = useReportTemplatePreviewMutations({
    client,
    reportType,
    selectedTemplatePath,
    content,
    withSeal: currentTemplate?.withSealDefault ?? true,
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
    hasChanges,
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
      templateContentQuery.isFetching,
      createTemplateMutation.isPending,
      renameTemplateMutation.isPending,
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

  async function handleRenameTemplate() {
    if (!canRenameTemplate) {
      return;
    }

    if (hasUnsavedChanges && !await requestConfirmation({ title: "重命名模板", description: "当前模板有未保存修改，确定继续重命名吗？", details: ["用户模板会先保存当前内容；内置文件模板将按现有重命名规则处理。"], confirmLabel: "继续重命名" })) {
      return;
    }

    if (isUserTemplate) {
      saveUserTemplateMutation.mutate(previewContent);
    } else {
      renameTemplateMutation.mutate();
    }
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

  function handleDesignerModeChange(mode: DesignerMode) {
    if (isLimitedReportView) {
      return;
    }
    if (mode === designerMode) {
      return;
    }
    if (designerMode === "new" && workspaceHasUnappliedDesignerChanges) {
      setContent(designerDraftContent);
      setContentTemplatePath(selectedTemplatePath);
      setPreview(null);
    }
    setWorkspaceMode("design");
    setDesignerMode(mode);
  }

  async function handleRefreshTemplates() {
    if (!await confirmDiscardChanges("刷新报表模板")) {
      return;
    }
    await Promise.all([templatesQuery.refetch(), userTemplatesQuery.refetch()]);
  }

  async function handleApplyNewReportDesignerContent(nextContent: string) {
    if (!selectedTemplatePath || !selectedTemplateContentActive || (isUserTemplate && !currentUserTemplate?.canEdit)) {
      return;
    }

    if (!await confirmStructuredTemplateOverwrite()) {
      return;
    }

    setContent(nextContent);
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage("新版设计器内容已应用到模板源码，保存后写入模板文件。");
    setMessageType("success");
  }

  async function handleSaveNewReportDesignerContent(nextContent: string) {
    if (!selectedTemplatePath || !selectedTemplateContentActive) {
      return;
    }

    if (isUserTemplate ? !canDesignTemplates || !currentUserTemplate?.canEdit : !canManageTemplates) {
      setMessage("当前账号没有保存模板权限。");
      setMessageType("error");
      return;
    }

    if (!await confirmStructuredTemplateOverwrite()) {
      return;
    }

    setContent(nextContent);
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage(null);
    setMessageType(null);
    if (isUserTemplate) {
      saveUserTemplateMutation.mutate(nextContent);
    } else {
      saveMutation.mutate(nextContent);
    }
  }

  async function confirmStructuredTemplateOverwrite() {
    if (!content.trim() || hasReportDesignerSchema(content)) {
      return true;
    }

    return requestConfirmation({ title: "转换为新版设计器结构", description: "当前模板未包含新版设计器结构。继续后将使用结构化模板 HTML 覆盖当前旧 HTML。", details: ["建议在转换前导出模板包备份。"], confirmLabel: "确认转换" });
  }

  function handleFormatSource() {
    if (!canFormatSource) {
      return;
    }

    const formatted = formatReportTemplateSource(content);
    setContent(formatted);
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage("源码已格式化，保存后写入模板文件。");
    setMessageType("success");
  }

  function handleTemplatePreviewModeChange(nextMode: TemplatePreviewMode) {
    setTemplatePreviewMode(nextMode);
    setWorkspaceMode("preview");
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }

  function handleTemplatePreviewSampleProfileChange(value: string) {
    setTemplatePreviewSampleProfile(normalizePreviewSampleProfile(value, reportType));
    setPreview(null);
    setMessage(null);
    setMessageType(null);
  }

  function handleRenderTemplatePreview() {
    if (!canRenderTemplatePreview) {
      return;
    }

    setWorkspaceMode("preview");

    if (templatePreviewMode === "sample") {
      if (isLocalSamplePreview) {
        setPreview(null);
        setMessage(null);
        setMessageType(null);
        return;
      }

      samplePreviewMutation.mutate(previewContent);
      return;
    }

    if (reportType === "PaymentVoucher") {
      paymentPreviewMutation.mutate();
    } else {
      invoicePreviewMutation.mutate();
    }
  }

  return (
    <section className="editor-surface report-template-surface" aria-label="报表模板设计">
      <form className="report-template-layout" onSubmit={handleSave} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
        <ReportTemplateWorkspaceHeader
          title={currentUserTemplate?.name || currentTemplate?.displayName || "报表模板"}
          designerMode={designerMode}
          workspaceMode={workspaceMode}
          isBusy={isBusy}
          canPreview={canRenderTemplatePreview}
          canSave={canSave}
          designDisabled={isLimitedReportView}
          onDesignerModeChange={handleDesignerModeChange}
          onWorkspaceModeChange={setWorkspaceMode}
          onRefresh={() => void handleRefreshTemplates()}
          onPreview={handleRenderTemplatePreview}
        />

        <WorkspaceDeviceNotice
          mode={workspaceDeviceMode}
          phone="可选择模板、查看预览和进行轻量确认；结构化拖拽、源码编辑、模板保存和模板包导入导出请使用桌面端。"
          tablet="可选择模板、查看预览和进行现场确认；完整报表设计、源码编辑、模板保存和模板包导入导出请使用桌面端。"
        />

        {isLimitedReportView ? (
          <div className="report-template-mobile-selection">
            <ReportTemplateSelectionPanel
              reportType={reportType}
              reportTypeOptions={availableReportTypeOptions}
              templates={templates}
              userTemplates={userTemplates}
              selectedTemplatePath={selectedTemplatePath}
              selectedUserTemplateId={selectedUserTemplateId}
              isBusy={isBusy}
              onReportTypeChange={handleReportTypeChange}
              onTemplateChange={handleTemplateChange}
              onUserTemplateChange={handleUserTemplateChange}
            />
          </div>
        ) : null}

        <ReportTemplateFeedback message={effectiveMessage} type={effectiveMessageType} />

        {workspaceMode === "design" ? (
        <div className={`report-template-grid report-template-grid-design report-template-grid-${designerMode}`}>
          <aside className="report-template-sidebar">
            <ReportTemplateSelectionPanel
              reportType={reportType}
              reportTypeOptions={availableReportTypeOptions}
              templates={templates}
              userTemplates={userTemplates}
              selectedTemplatePath={selectedTemplatePath}
              selectedUserTemplateId={selectedUserTemplateId}
              isBusy={isBusy}
              onReportTypeChange={handleReportTypeChange}
              onTemplateChange={handleTemplateChange}
              onUserTemplateChange={handleUserTemplateChange}
            />
            {canDesignTemplates ? (
              <ReportTemplateUserPanel
                currentTemplate={currentUserTemplate}
                versions={userTemplateVersionsQuery.data ?? []}
                versionsLoading={userTemplateVersionsQuery.isFetching}
                newTemplateName={newUserTemplateName}
                newTemplateShareScope={newUserTemplateShareScope}
                isBusy={isBusy}
                canCreate={canCreateUserTemplate}
                isUserTemplate={isUserTemplate}
                onNewTemplateNameChange={setNewUserTemplateName}
                onNewTemplateShareScopeChange={setNewUserTemplateShareScope}
                onCreate={handleCreateUserTemplate}
                onShareScopeChange={(shareScope) => updateUserTemplateStatusMutation.mutate({ shareScope })}
                onToggleActive={async () => {
                  if (!currentUserTemplate) {
                    return;
                  }

                  const action = currentUserTemplate.isActive ? "停用" : "重新启用";
                  if (await requestConfirmation({ title: `${action}模板`, description: `确定${action}“${currentUserTemplate.name}”吗？`, confirmLabel: `确认${action}` })) {
                    updateUserTemplateStatusMutation.mutate({ isActive: !currentUserTemplate.isActive });
                  }
                }}
                onRestoreVersion={async (versionNumber) => {
                  if (await requestConfirmation({ title: `恢复到 V${versionNumber}`, description: `确定恢复到 V${versionNumber} 吗？`, details: ["当前未保存修改将被替换。", "现有历史版本仍会保留。"], confirmLabel: "确认恢复" })) {
                    restoreUserTemplateVersionMutation.mutate(versionNumber);
                  }
                }}
              />
            ) : null}
            <ReportTemplateAdminPanel
              currentTemplateLabel={
                currentUserTemplate?.name || (selectedTemplatePath ? fileNameFromPath(selectedTemplatePath) : "未选择模板")
              }
              newTemplateFileName={newTemplateFileName}
              newTemplateDisplayName={newTemplateDisplayName}
              renameTemplateFileName={renameTemplateFileName}
              renameLabel={isUserTemplate ? "模板名称" : "新文件名"}
              canManageTemplates={canManageTemplates}
              canCreate={canCreateTemplate}
              canRename={canRenameTemplate}
              canDelete={canDeleteTemplate}
              canEditRename={
                isUserTemplate
                  ? Boolean(currentUserTemplate?.canEdit) && canDesignTemplates && !isBusy
                  : canManageTemplates && Boolean(selectedTemplatePath) && !isBusy
              }
              isBusy={isBusy}
              onNewTemplateFileNameChange={setNewTemplateFileName}
              onNewTemplateDisplayNameChange={setNewTemplateDisplayName}
              onRenameTemplateFileNameChange={setRenameTemplateFileName}
              onCreate={handleCreateTemplate}
              onRename={handleRenameTemplate}
              onDelete={handleDeleteTemplate}
            />
            <ReportTemplatePackagePanel
              desktopAvailable={packageWorkspace.desktopAvailable}
              canManageTemplates={canManageTemplates}
              isBusy={isBusy}
              importStrategy={packageWorkspace.importStrategy}
              exportPath={packageWorkspace.exportPath}
              importPath={packageWorkspace.importPath}
              uploadInputRef={packageWorkspace.uploadInputRef}
              canExport={canExportPackage}
              canExportByPath={canExportPackageByPath}
              canDownload={canDownloadPackage}
              canImport={canImportPackage}
              canImportByPath={canImportPackageByPath}
              canUpload={canUploadPackage}
              onImportStrategyChange={packageWorkspace.setImportStrategy}
              onExport={() => packageWorkspace.exportPackage(canExportPackage)}
              onExportByPath={() => packageWorkspace.exportByPath(canExportPackageByPath)}
              onDownload={() => packageWorkspace.downloadPackage(canDownloadPackage)}
              onImport={() => packageWorkspace.importPackage(canImportPackage, hasUnsavedChanges)}
              onImportByPath={() => packageWorkspace.importByPath(canImportPackageByPath, hasUnsavedChanges)}
              onUpload={() => packageWorkspace.chooseUpload(canUploadPackage)}
              onUploadFileChange={(event) => packageWorkspace.uploadFile(event, canUploadPackage, hasUnsavedChanges)}
              onExportPathChange={packageWorkspace.setExportPath}
              onImportPathChange={packageWorkspace.setImportPath}
              onChooseExportPath={packageWorkspace.chooseExportPath}
              onChooseImportPath={packageWorkspace.chooseImportPath}
            />
          </aside>

          <ReportTemplateDesignWorkspace
            designerMode={designerMode}
            reportType={reportType}
            displayName={currentUserTemplate?.name ?? currentTemplate?.displayName ?? ""}
            content={content}
            fieldCatalog={fieldCatalogQuery.data}
            canApplyTemplateContent={
              Boolean(selectedTemplatePath) &&
              selectedTemplateContentActive &&
              !isBusy &&
              (!isUserTemplate || Boolean(currentUserTemplate?.canEdit))
            }
            canSaveTemplateContent={
              Boolean(selectedTemplatePath) &&
              selectedTemplateContentActive &&
              !isBusy &&
              (isUserTemplate ? Boolean(currentUserTemplate?.canEdit) && canDesignTemplates : canManageTemplates)
            }
            hasTemplateChanges={hasChanges}
            canFormatSource={canFormatSource}
            sourceDisabled={
              !selectedTemplatePath ||
              templateContentQuery.isFetching ||
              (isUserTemplate ? !currentUserTemplate?.canEdit : !canManageTemplates)
            }
            onApplyTemplateContent={handleApplyNewReportDesignerContent}
            onSaveTemplateContent={handleSaveNewReportDesignerContent}
            onDesignerDraftContentChange={setDesignerDraftContent}
            onOpenSource={() => setDesignerMode("source")}
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
