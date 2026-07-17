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
  type DesignerMode,
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
  designerMode,
  designerDraftContent,
  content,
  loadedContent,
  contentTemplatePath,
  selectedTemplatePath,
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
  newTemplateFileName,
  newUserTemplateName,
  renameTemplateFileName,
  desktopAvailable,
  packageExportPath,
  packageImportPath,
  templateContentFetching,
}: {
  reportType: ReportTypeOption;
  designerMode: DesignerMode;
  designerDraftContent: string;
  content: string;
  loadedContent: string;
  contentTemplatePath: string;
  selectedTemplatePath: string;
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
  newTemplateFileName: string;
  newUserTemplateName: string;
  renameTemplateFileName: string;
  desktopAvailable: boolean;
  packageExportPath: string;
  packageImportPath: string;
  templateContentFetching: boolean;
}) {
  const isUserTemplate = currentUserTemplate !== null;
  const previewDocumentOptions =
    reportType === "PaymentVoucher"
      ? buildPaymentPreviewOptions(previewPayments, previewPaymentId)
      : buildInvoicePreviewOptions(previewInvoices, previewInvoiceId);
  const selectedPreviewSourceId = reportType === "PaymentVoucher" ? previewPaymentId : previewInvoiceId;
  const selectedPreviewSourceValue = selectedPreviewSourceId > 0 ? String(selectedPreviewSourceId) : "";
  const selectedPreviewSourceLabel =
    previewDocumentOptions.find((option) => option.value === selectedPreviewSourceValue)?.label ?? "";
  const previewContent = designerMode === "new" && designerDraftContent.trim() ? designerDraftContent : content;
  const isLocalSamplePreview =
    templatePreviewMode === "sample" && isLocalReportDesignerPreviewSample(templatePreviewSampleProfile);
  const localSamplePreviewHtml =
    isLocalSamplePreview && previewContent.trim()
      ? renderReportDesignerLocalPreviewSample(previewContent, templatePreviewSampleProfile)
      : "";
  const renderedPreviewHtml = localSamplePreviewHtml || previewHtml || buildRawPreviewHtml(previewContent);
  const selectedTemplateContentLoaded =
    isUserTemplate ||
    (Boolean(selectedTemplatePath) && matchesTemplatePath(selectedContentTemplatePath, selectedTemplatePath));
  const selectedTemplateContentActive =
    selectedTemplateContentLoaded &&
    (isUserTemplate
      ? contentTemplatePath === buildUserTemplateKey(currentUserTemplate.id)
      : matchesTemplatePath(contentTemplatePath, selectedTemplatePath));
  const isBusy = busyFlags.some(Boolean);
  const hasChanges = content !== loadedContent;
  const canPreviewRendered =
    Boolean(selectedTemplatePath) && (reportType === "PaymentVoucher" ? previewPaymentId > 0 : previewInvoiceId > 0);
  const canRenderTemplatePreview =
    templatePreviewMode === "savedSource"
      ? canPreviewRendered && !isBusy
      : isLocalSamplePreview
        ? Boolean(previewContent.trim()) && !isBusy
        : Boolean(previewContent.trim()) && Boolean(selectedTemplatePath) && !isBusy;
  const canCreateTemplate = canManageTemplates && Boolean(newTemplateFileName.trim()) && !isBusy;
  const canCreateUserTemplate =
    canDesignTemplates && Boolean(newUserTemplateName.trim()) && Boolean(selectedTemplatePath) && !isBusy;
  const canRenameTemplate =
    (isUserTemplate ? currentUserTemplate.canEdit && canDesignTemplates : canManageTemplates) &&
    Boolean(selectedTemplatePath) &&
    Boolean(renameTemplateFileName.trim()) &&
    renameTemplateFileName.trim() !==
      (isUserTemplate ? currentUserTemplate.name : fileNameFromPath(selectedTemplatePath)) &&
    !isBusy;
  const canDeleteTemplate = isUserTemplate
    ? currentUserTemplate.canEdit && canDesignTemplates && !isBusy
    : canManageTemplates && Boolean(selectedTemplatePath) && !isBusy;
  const canSave =
    Boolean(selectedTemplatePath) &&
    hasChanges &&
    !isBusy &&
    (isUserTemplate ? currentUserTemplate.canEdit && canDesignTemplates : canManageTemplates);

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
    canRenderTemplatePreview,
    canCreateTemplate,
    canCreateUserTemplate,
    canRenameTemplate,
    canDeleteTemplate,
    canExportPackage: canManageTemplates && (desktopAvailable || Boolean(packageExportPath.trim())) && !isBusy,
    canExportPackageByPath: canManageTemplates && Boolean(packageExportPath.trim()) && !isBusy,
    canDownloadPackage: canManageTemplates && !desktopAvailable && !isBusy,
    canImportPackage: canManageTemplates && (desktopAvailable || Boolean(packageImportPath.trim())) && !isBusy,
    canImportPackageByPath: canManageTemplates && Boolean(packageImportPath.trim()) && !isBusy,
    canUploadPackage: canManageTemplates && !desktopAvailable && !isBusy,
    canSave,
    canFormatSource: Boolean(selectedTemplatePath) && Boolean(content.trim()) && !templateContentFetching,
  };
}
