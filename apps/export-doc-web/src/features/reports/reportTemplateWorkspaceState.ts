import {
  ApiInvoiceListItemDto,
  ApiPaymentDto,
  ApiUserReportTemplateDto,
} from "../../api/index.ts";
import {
  isLocalReportDesignerPreviewSample,
  renderReportDesignerLocalPreviewSample,
  type ReportDesignerPreviewSampleProfile,
} from "../report-designer/reportDesignerPreviewSamples.ts";
import {
  buildInvoicePreviewOptions,
  buildPaymentPreviewOptions,
  buildRawPreviewHtml,
  buildUserTemplateKey,
  fileNameFromPath,
  matchesTemplatePath,
  readUserTemplateIdFromKey,
  type ReportTypeOption,
  type TemplatePreviewMode,
} from "./reportTemplateDesignerModel.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function deriveReportTemplateFeedback({
  reportType,
  templateListError,
  userTemplateListError,
  templateContentError,
  previewInvoiceError,
  previewPaymentError,
  message,
  messageType,
}: {
  reportType: ReportTypeOption;
  templateListError: unknown | null;
  userTemplateListError: unknown | null;
  templateContentError: unknown | null;
  previewInvoiceError: unknown | null;
  previewPaymentError: unknown | null;
  message: string | null;
  messageType: "success" | "error" | null;
}) {
  const activeError =
    templateListError ??
    userTemplateListError ??
    templateContentError ??
    (reportType === "ExportDocument" ? previewInvoiceError : previewPaymentError);
  return {
    effectiveMessage: activeError ? readApiError(activeError) : message,
    effectiveMessageType: activeError ? ("error" as const) : messageType,
  };
}

export function deriveReportTemplateWorkspaceState({
  reportType,
  designerDraftContent,
  designerDraftDirty,
  designerDraftValid,
  content,
  loadedContent,
  contentTemplatePath,
  selectedTemplatePath,
  currentTemplateDisplayName,
  persistedDisplayName,
  defaultTemplatePath,
  canUseAdvancedTools,
  selectedContentTemplatePath,
  currentUserTemplate,
  templatePreviewMode,
  templatePreviewSampleProfile,
  previewHtml,
  previewInvoices,
  previewPayments,
  previewInvoiceId,
  previewPaymentId,
  busyFlags,
  canManageTemplates,
  canDesignTemplates,
  canCloneTemplates,
  canArchiveTemplates,
  canImportTemplates,
  canExportTemplates,
  canPreviewSavedSource,
  newTemplateFileName,
  newUserTemplateName,
  renameTemplateFileName,
  desktopAvailable,
  packageExportPath,
  packageImportPath,
  fileExportPath,
  fileImportPath,
}: {
  reportType: ReportTypeOption;
  designerDraftContent: string;
  designerDraftDirty: boolean;
  designerDraftValid: boolean;
  content: string;
  loadedContent: string;
  contentTemplatePath: string;
  selectedTemplatePath: string;
  currentTemplateDisplayName: string;
  persistedDisplayName: string;
  defaultTemplatePath: string;
  canUseAdvancedTools: boolean;
  selectedContentTemplatePath: string;
  currentUserTemplate: ApiUserReportTemplateDto | null;
  templatePreviewMode: TemplatePreviewMode;
  templatePreviewSampleProfile: ReportDesignerPreviewSampleProfile;
  previewHtml: string;
  previewInvoices: ApiInvoiceListItemDto[];
  previewPayments: ApiPaymentDto[];
  previewInvoiceId: number;
  previewPaymentId: number;
  busyFlags: boolean[];
  canManageTemplates: boolean;
  canDesignTemplates: boolean;
  canCloneTemplates: boolean;
  canArchiveTemplates: boolean;
  canImportTemplates: boolean;
  canExportTemplates: boolean;
  canPreviewSavedSource: boolean;
  newTemplateFileName: string;
  newUserTemplateName: string;
  renameTemplateFileName: string;
  desktopAvailable: boolean;
  packageExportPath: string;
  packageImportPath: string;
  fileExportPath: string;
  fileImportPath: string;
}) {
  const userTemplateId = currentUserTemplate?.id ?? readUserTemplateIdFromKey(selectedTemplatePath);
  const isUserTemplate = userTemplateId > 0;
  const previewDocumentOptions =
    reportType === "PaymentVoucher"
      ? buildPaymentPreviewOptions(previewPayments, previewPaymentId)
      : buildInvoicePreviewOptions(previewInvoices, previewInvoiceId);
  const selectedPreviewSourceId = reportType === "PaymentVoucher" ? previewPaymentId : previewInvoiceId;
  const selectedPreviewSourceValue = selectedPreviewSourceId > 0 ? String(selectedPreviewSourceId) : "";
  const selectedPreviewSourceLabel =
    previewDocumentOptions.find((option) => option.value === selectedPreviewSourceValue)?.label ?? "";
  const previewContent = designerDraftContent.trim() ? designerDraftContent : content;
  const isLocalSamplePreview =
    templatePreviewMode === "sample" && isLocalReportDesignerPreviewSample(templatePreviewSampleProfile);
  const localSamplePreviewHtml =
    isLocalSamplePreview && previewContent.trim()
      ? renderReportDesignerLocalPreviewSample(previewContent, templatePreviewSampleProfile)
      : "";
  const renderedPreviewHtml = localSamplePreviewHtml || previewHtml || buildRawPreviewHtml(previewContent);
  const selectedTemplateContentLoaded =
    isUserTemplate ||
    (Boolean(selectedTemplatePath) && (matchesTemplatePath(selectedContentTemplatePath, selectedTemplatePath) ||
      matchesTemplatePath(contentTemplatePath, selectedTemplatePath)));
  const selectedTemplateContentActive =
    selectedTemplateContentLoaded &&
    (isUserTemplate
      ? contentTemplatePath === buildUserTemplateKey(userTemplateId)
      : matchesTemplatePath(contentTemplatePath, selectedTemplatePath));
  const isBusy = busyFlags.some(Boolean);
  const hasChanges = content !== loadedContent;
  const hasUnappliedDesignerChanges = designerDraftDirty;
  const hasUnsavedChanges = hasChanges || hasUnappliedDesignerChanges || currentTemplateDisplayName !== persistedDisplayName;
  const canPreviewRendered =
    Boolean(selectedTemplatePath) && (reportType === "PaymentVoucher" ? previewPaymentId > 0 : previewInvoiceId > 0);
  const canRenderTemplatePreview = designerDraftValid && (
    templatePreviewMode === "savedSource"
      ? canPreviewSavedSource && canPreviewRendered && !isBusy
      : isLocalSamplePreview
        ? Boolean(previewContent.trim()) && !isBusy
        : canDesignTemplates && Boolean(previewContent.trim()) && Boolean(selectedTemplatePath) && !isBusy);
  const canCreateTemplate = canManageTemplates && Boolean(newTemplateFileName.trim()) && !isBusy;
  const canCreateBlankUserTemplate =
    canDesignTemplates && Boolean(newUserTemplateName.trim()) && !isBusy;
  const canCloneUserTemplate =
    canCloneTemplates && Boolean(newUserTemplateName.trim()) && Boolean(selectedTemplatePath) && !isBusy;
  const canRenameTemplate =
    !isUserTemplate && canManageTemplates &&
    Boolean(selectedTemplatePath) &&
    Boolean(renameTemplateFileName.trim()) &&
    renameTemplateFileName.trim() !== fileNameFromPath(selectedTemplatePath) &&
    !isBusy;
  const canDeleteTemplate = isUserTemplate
    ? currentUserTemplate?.canArchive === true && !isBusy
    : canArchiveTemplates && Boolean(selectedTemplatePath) && !isBusy;
  const canEditCurrentTemplate =
    Boolean(selectedTemplatePath) &&
    !isBusy &&
    (isUserTemplate ? currentUserTemplate?.canEdit === true && canDesignTemplates : canManageTemplates);

  return {
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
    hasUnappliedDesignerChanges,
    hasUnsavedChanges,
    canRenderTemplatePreview,
    canCreateTemplate,
    canCreateBlankUserTemplate,
    canCloneUserTemplate,
    canRenameTemplate,
    canDeleteTemplate,
    canExportPackage: canExportTemplates && (desktopAvailable || Boolean(packageExportPath.trim())) && !isBusy,
    canExportPackageByPath: canExportTemplates && Boolean(packageExportPath.trim()) && !isBusy,
    canDownloadPackage: canExportTemplates && !desktopAvailable && !isBusy,
    canImportPackage: canImportTemplates && (desktopAvailable || Boolean(packageImportPath.trim())) && !isBusy,
    canImportPackageByPath: canImportTemplates && Boolean(packageImportPath.trim()) && !isBusy,
    canUploadPackage: canImportTemplates && !desktopAvailable && !isBusy,
    canExportTemplateFile: !isUserTemplate && canExportTemplates && Boolean(selectedTemplatePath) &&
      (desktopAvailable || Boolean(fileExportPath.trim())) && !isBusy,
    canExportTemplateFileByPath: !isUserTemplate && canExportTemplates && Boolean(selectedTemplatePath) &&
      Boolean(fileExportPath.trim()) && !isBusy,
    canDownloadTemplateFile: !isUserTemplate && canExportTemplates && Boolean(selectedTemplatePath) && !isBusy,
    canImportTemplateFile: !isUserTemplate && canImportTemplates && Boolean(selectedTemplatePath) &&
      (desktopAvailable || Boolean(fileImportPath.trim())) && !isBusy,
    canImportTemplateFileByPath: !isUserTemplate && canImportTemplates && Boolean(selectedTemplatePath) &&
      Boolean(fileImportPath.trim()) && !isBusy,
    canUploadTemplateFile: !isUserTemplate && canImportTemplates && Boolean(selectedTemplatePath) && !desktopAvailable && !isBusy,
    canSave: selectedTemplateContentActive && canEditCurrentTemplate && hasUnsavedChanges && designerDraftValid,
    canUpdateDisplayName: selectedTemplateContentActive && designerDraftValid && canEditCurrentTemplate && Boolean(currentTemplateDisplayName.trim()) &&
      currentTemplateDisplayName.trim() !== persistedDisplayName,
    canSetDefault: Boolean(selectedTemplatePath) && canManageTemplates && !isBusy &&
      !matchesTemplatePath(selectedTemplatePath, defaultTemplatePath),
    canFormatSource: canEditCurrentTemplate && canUseAdvancedTools,
  };
}
