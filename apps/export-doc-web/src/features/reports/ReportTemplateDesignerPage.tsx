import { ChangeEvent, FormEvent, useCallback, useMemo, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { ApiReportTemplatePreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  isDesktopBridgeAvailable,
  selectReportTemplatePackageFile,
  selectSaveReportTemplatePackagePath,
} from "../../desktop/desktopBridge.ts";
import { readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
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
import { useReportTemplatePackageMutations } from "./useReportTemplatePackageMutations.ts";
import { useReportTemplatePreviewMutations } from "./useReportTemplatePreviewMutations.ts";
import {
  buildNewTemplateFileName,
  buildTemplatePackageFileName,
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
  type TemplateImportStrategyOption,
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
  const packageUploadInputRef = useRef<HTMLInputElement | null>(null);
  const [reportType, setReportType] = useState<ReportTypeOption>(() => initialReportType);
  const [selectedTemplatePath, setSelectedTemplatePath] = useState("");
  const [selectedUserTemplateId, setSelectedUserTemplateId] = useState(0);
  const [content, setContent] = useState("");
  const [contentTemplatePath, setContentTemplatePath] = useState("");
  const [loadedContent, setLoadedContent] = useState("");
  const [workspaceMode, setWorkspaceMode] = useState<TemplateWorkspaceMode>("design");
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
  const [packageExportPath, setPackageExportPath] = useState(() => buildTemplatePackageFileName());
  const [packageImportPath, setPackageImportPath] = useState("");
  const [packageImportStrategy, setPackageImportStrategy] = useState<TemplateImportStrategyOption>("Merge");
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

  const { exportPackageMutation, downloadPackageMutation, importPackageMutation, uploadPackageMutation } =
    useReportTemplatePackageMutations({
    client,
    reportType,
    selectedTemplatePath,
    packageExportPath,
    importStrategy: packageImportStrategy,
    onExported: (response) => {
      setPackageExportPath(response.packagePath);
      setPackageImportPath(response.packagePath);
      setMessage(`模板包已导出：${response.packagePath}`);
      setMessageType("success");
    },
    onDownloaded: () => {
      setMessage("模板包已下载。");
      setMessageType("success");
    },
    onImported: (response, source) => {
      setPreview(null);
      setMessage(
        source === "upload"
          ? `模板包已上传并导入：${response.templateCount} 个模板配置。`
          : `模板包已导入：${response.templateCount} 个模板配置。`,
      );
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
      exportPackageMutation.isPending,
      downloadPackageMutation.isPending,
      importPackageMutation.isPending,
      uploadPackageMutation.isPending,
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
    packageExportPath,
    packageImportPath,
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

  function handleRenameTemplate() {
    if (!canRenameTemplate) {
      return;
    }

    if (hasChanges && !window.confirm("当前模板有未保存修改，确定继续重命名？")) {
      return;
    }

    if (isUserTemplate) {
      saveUserTemplateMutation.mutate(content);
    } else {
      renameTemplateMutation.mutate();
    }
  }

  function handleDeleteTemplate() {
    if (!canDeleteTemplate) {
      return;
    }

    const suffix = hasChanges ? "当前模板有未保存修改，" : "";
    if (!window.confirm(`${suffix}确定删除当前模板？`)) {
      return;
    }

    if (currentUserTemplate) {
      deleteUserTemplateMutation.mutate(currentUserTemplate.id);
    } else {
      deleteTemplateMutation.mutate();
    }
  }

  async function choosePackageExportPath() {
    try {
      const selectedPath = await requestPackageExportPath();
      if (selectedPath) {
        setPackageExportPath(selectedPath);
        setMessage(null);
        setMessageType(null);
      }
    } catch (error) {
      setMessage(readDesktopError(error));
      setMessageType("error");
    }
  }

  async function requestPackageExportPath() {
    const defaultFileName = fileNameFromPath(packageExportPath.trim()) || buildTemplatePackageFileName();
    return selectSaveReportTemplatePackagePath(defaultFileName, defaultExportDirectory);
  }

  async function choosePackageImportPath() {
    try {
      const selectedPath = await selectReportTemplatePackageFile();
      if (selectedPath) {
        setPackageImportPath(selectedPath);
        setMessage(null);
        setMessageType(null);
      }
    } catch (error) {
      setMessage(readDesktopError(error));
      setMessageType("error");
    }
  }

  async function handleExportPackage() {
    if (!canExportPackage) {
      return;
    }

    if (desktopAvailable) {
      try {
        const selectedPath = await requestPackageExportPath();
        if (!selectedPath) {
          return;
        }

        setPackageExportPath(selectedPath);
        exportPackageMutation.mutate(selectedPath);
      } catch (error) {
        setMessage(readDesktopError(error));
        setMessageType("error");
      }
      return;
    }

    const packagePath = packageExportPath.trim();
    if (packagePath) {
      exportPackageMutation.mutate(packagePath);
    }
  }

  function handleDownloadPackage() {
    if (canDownloadPackage) {
      downloadPackageMutation.mutate();
    }
  }

  async function handleImportPackage() {
    if (!canImportPackage) {
      return;
    }

    if (hasChanges && !window.confirm("当前模板有未保存修改，确定继续导入模板包？")) {
      return;
    }

    if (desktopAvailable) {
      try {
        const selectedPath = await selectReportTemplatePackageFile();
        if (!selectedPath) {
          return;
        }

        setPackageImportPath(selectedPath);
        importPackageMutation.mutate(selectedPath);
      } catch (error) {
        setMessage(readDesktopError(error));
        setMessageType("error");
      }
      return;
    }

    const packagePath = packageImportPath.trim();
    if (packagePath) {
      importPackageMutation.mutate(packagePath);
    }
  }

  function handleExportPackageByPath() {
    const packagePath = packageExportPath.trim();
    if (canExportPackageByPath && packagePath) {
      exportPackageMutation.mutate(packagePath);
    }
  }

  function handleImportPackageByPath() {
    const packagePath = packageImportPath.trim();
    if (!canImportPackageByPath || !packagePath) {
      return;
    }

    if (hasChanges && !window.confirm("当前模板有未保存修改，确定继续导入模板包？")) {
      return;
    }

    importPackageMutation.mutate(packagePath);
  }

  function choosePackageUploadFile() {
    if (canUploadPackage) {
      packageUploadInputRef.current?.click();
    }
  }

  function handlePackageUploadFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.currentTarget.files?.[0];
    event.currentTarget.value = "";
    if (!file || !canUploadPackage) {
      return;
    }

    if (hasChanges && !window.confirm("当前模板有未保存修改，确定继续上传并导入模板包？")) {
      return;
    }

    uploadPackageMutation.mutate(file);
  }

  function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (canSave) {
      if (isUserTemplate) {
        saveUserTemplateMutation.mutate(undefined);
      } else {
        saveMutation.mutate(undefined);
      }
    }
  }

  function handleApplyNewReportDesignerContent(nextContent: string) {
    if (!selectedTemplatePath || !selectedTemplateContentActive || (isUserTemplate && !currentUserTemplate?.canEdit)) {
      return;
    }

    if (!confirmStructuredTemplateOverwrite()) {
      return;
    }

    setContent(nextContent);
    setContentTemplatePath(selectedTemplatePath);
    setPreview(null);
    setMessage("新版设计器内容已应用到模板源码，保存后写入模板文件。");
    setMessageType("success");
  }

  function handleSaveNewReportDesignerContent(nextContent: string) {
    if (!selectedTemplatePath || !selectedTemplateContentActive) {
      return;
    }

    if (isUserTemplate ? !canDesignTemplates || !currentUserTemplate?.canEdit : !canManageTemplates) {
      setMessage("当前账号没有保存模板权限。");
      setMessageType("error");
      return;
    }

    if (!confirmStructuredTemplateOverwrite()) {
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

  function confirmStructuredTemplateOverwrite() {
    if (!content.trim() || hasReportDesignerSchema(content)) {
      return true;
    }

    return window.confirm("当前模板未包含新版设计器结构。继续会用结构化模板 HTML 覆盖当前旧 HTML，确定继续？");
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
          onDesignerModeChange={(mode) => {
            setWorkspaceMode("design");
            setDesignerMode(mode);
          }}
          onWorkspaceModeChange={setWorkspaceMode}
          onRefresh={() => {
            void templatesQuery.refetch();
            void userTemplatesQuery.refetch();
          }}
          onPreview={handleRenderTemplatePreview}
        />

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
                onToggleActive={() => {
                  if (!currentUserTemplate) {
                    return;
                  }

                  const action = currentUserTemplate.isActive ? "停用" : "重新启用";
                  if (window.confirm(`确定${action}“${currentUserTemplate.name}”？`)) {
                    updateUserTemplateStatusMutation.mutate({ isActive: !currentUserTemplate.isActive });
                  }
                }}
                onRestoreVersion={(versionNumber) => {
                  if (window.confirm(`确定恢复到 V${versionNumber}？当前未保存修改将被替换。`)) {
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
              desktopAvailable={desktopAvailable}
              canManageTemplates={canManageTemplates}
              isBusy={isBusy}
              importStrategy={packageImportStrategy}
              exportPath={packageExportPath}
              importPath={packageImportPath}
              uploadInputRef={packageUploadInputRef}
              canExport={canExportPackage}
              canExportByPath={canExportPackageByPath}
              canDownload={canDownloadPackage}
              canImport={canImportPackage}
              canImportByPath={canImportPackageByPath}
              canUpload={canUploadPackage}
              onImportStrategyChange={setPackageImportStrategy}
              onExport={handleExportPackage}
              onExportByPath={handleExportPackageByPath}
              onDownload={handleDownloadPackage}
              onImport={handleImportPackage}
              onImportByPath={handleImportPackageByPath}
              onUpload={choosePackageUploadFile}
              onUploadFileChange={handlePackageUploadFile}
              onExportPathChange={setPackageExportPath}
              onImportPathChange={setPackageImportPath}
              onChooseExportPath={choosePackageExportPath}
              onChooseImportPath={choosePackageImportPath}
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
