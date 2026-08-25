import { Dispatch, SetStateAction, useEffect } from "react";
import {
  ApiReportTemplateContentDto,
  ApiReportTemplateDto,
  ApiUserReportTemplateDto,
} from "../../api/index.ts";
import {
  fileNameFromPath,
  matchesTemplatePath,
  readPreferredPreviewSampleProfile,
  readUserTemplateIdFromKey,
  resolveDefaultTemplatePath,
  resolvePreviewSourceId,
  type ReportTypeOption,
} from "./reportTemplateDesignerModel.ts";
import { type ReportDesignerPreviewSampleProfile } from "../report-designer/reportDesignerPreviewSamples.ts";

export function useReportTemplateSelectionSync({
  requestedReportType,
  availableReportTypeOptions,
  reportType,
  setReportType,
  previewSampleProfiles,
  previewSampleProfile,
  setPreviewSampleProfile,
  requestedPreviewSourceId,
  previewInvoiceIds,
  previewPaymentIds,
  setPreviewInvoiceId,
  setPreviewPaymentId,
  templates,
  templatesLoaded,
  requestedTemplateFileName,
  configuredTemplatePath,
  requestedUserTemplateId,
  selectedTemplatePath,
  setSelectedTemplatePath,
  selectedUserTemplateId,
  setSelectedUserTemplateId,
  userTemplates,
  userTemplatesLoaded,
  templateContent,
  onSelectionChanged,
  onUserTemplateLoaded,
  onDefaultTemplateLoaded,
  onDefaultMetadataLoaded,
}: {
  requestedReportType: ReportTypeOption | null;
  availableReportTypeOptions: Array<{ value: ReportTypeOption; label: string }>;
  reportType: ReportTypeOption;
  setReportType: Dispatch<SetStateAction<ReportTypeOption>>;
  previewSampleProfiles: Array<{ value: ReportDesignerPreviewSampleProfile }>;
  previewSampleProfile: ReportDesignerPreviewSampleProfile;
  setPreviewSampleProfile: Dispatch<SetStateAction<ReportDesignerPreviewSampleProfile>>;
  requestedPreviewSourceId: number;
  previewInvoiceIds: number[];
  previewPaymentIds: number[];
  setPreviewInvoiceId: Dispatch<SetStateAction<number>>;
  setPreviewPaymentId: Dispatch<SetStateAction<number>>;
  templates: ApiReportTemplateDto[];
  templatesLoaded: boolean;
  requestedTemplateFileName: string;
  configuredTemplatePath: string;
  requestedUserTemplateId: number;
  selectedTemplatePath: string;
  setSelectedTemplatePath: Dispatch<SetStateAction<string>>;
  selectedUserTemplateId: number;
  setSelectedUserTemplateId: Dispatch<SetStateAction<number>>;
  userTemplates: ApiUserReportTemplateDto[];
  userTemplatesLoaded: boolean;
  templateContent: ApiReportTemplateContentDto | null;
  onSelectionChanged: () => void;
  onUserTemplateLoaded: (template: ApiUserReportTemplateDto) => void;
  onDefaultTemplateLoaded: (template: ApiReportTemplateContentDto) => void;
  onDefaultMetadataLoaded: (fileName: string, displayName: string) => void;
}) {
  useEffect(() => {
    if (requestedReportType && availableReportTypeOptions.some((option) => option.value === requestedReportType)) {
      setReportType((current) => (current === requestedReportType ? current : requestedReportType));
    }
  }, [availableReportTypeOptions, requestedReportType, setReportType]);

  useEffect(() => {
    if (!previewSampleProfiles.some((profile) => profile.value === previewSampleProfile)) {
      setPreviewSampleProfile(readPreferredPreviewSampleProfile(reportType));
    }
  }, [previewSampleProfile, previewSampleProfiles, reportType, setPreviewSampleProfile]);

  useEffect(() => {
    if (!requestedReportType || requestedPreviewSourceId <= 0) {
      return;
    }

    if (requestedReportType === "PaymentVoucher") {
      setPreviewPaymentId(requestedPreviewSourceId);
    } else {
      setPreviewInvoiceId(requestedPreviewSourceId);
    }
  }, [requestedPreviewSourceId, requestedReportType, setPreviewInvoiceId, setPreviewPaymentId]);

  useEffect(() => {
    if (reportType === "ExportDocument") {
      setPreviewInvoiceId((current) => resolvePreviewSourceId(current, previewInvoiceIds));
    } else {
      setPreviewPaymentId((current) => resolvePreviewSourceId(current, previewPaymentIds));
    }
  }, [previewInvoiceIds, previewPaymentIds, reportType, setPreviewInvoiceId, setPreviewPaymentId]);

  useEffect(() => {
    onSelectionChanged();
  }, [onSelectionChanged, reportType, selectedTemplatePath]);

  useEffect(() => {
    if (!templatesLoaded) {
      return;
    }

    setSelectedTemplatePath((current) =>
      resolveDefaultTemplatePath({
        templates,
        reportType,
        requestedTemplateFileName,
        configuredTemplatePath,
        currentTemplatePath: current,
        userTemplateSelected: selectedUserTemplateId > 0,
      }),
    );
  }, [configuredTemplatePath, reportType, requestedTemplateFileName, selectedUserTemplateId, setSelectedTemplatePath, templates, templatesLoaded]);

  useEffect(() => {
    if (!templatesLoaded || !userTemplatesLoaded || selectedUserTemplateId > 0) {
      return;
    }

    const configuredUserTemplateId = readUserTemplateIdFromKey(configuredTemplatePath);
    const selectedUserTemplateKey = readUserTemplateIdFromKey(selectedTemplatePath);
    const targetId = selectedUserTemplateKey || (!selectedTemplatePath ? configuredUserTemplateId : 0);
    if (userTemplates.some((template) => template.id === targetId && template.isActive)) {
      setSelectedUserTemplateId(targetId);
    }
  }, [configuredTemplatePath, selectedTemplatePath, selectedUserTemplateId, setSelectedUserTemplateId, templatesLoaded, userTemplates, userTemplatesLoaded]);

  useEffect(() => {
    if (requestedUserTemplateId <= 0 || !userTemplatesLoaded) {
      return;
    }

    setSelectedUserTemplateId(
      userTemplates.some((template) => template.id === requestedUserTemplateId) ? requestedUserTemplateId : 0,
    );
  }, [requestedUserTemplateId, setSelectedUserTemplateId, userTemplates, userTemplatesLoaded]);

  useEffect(() => {
    if (selectedUserTemplateId <= 0 || !userTemplatesLoaded) {
      return;
    }

    const selected = userTemplates.find((template) => template.id === selectedUserTemplateId);
    if (!selected) {
      setSelectedUserTemplateId(0);
      return;
    }

    onUserTemplateLoaded(selected);
  }, [onUserTemplateLoaded, selectedUserTemplateId, setSelectedUserTemplateId, userTemplates, userTemplatesLoaded]);

  useEffect(() => {
    if (
      selectedUserTemplateId <= 0 &&
      templateContent &&
      matchesTemplatePath(templateContent.templatePath, selectedTemplatePath)
    ) {
      onDefaultTemplateLoaded(templateContent);
    }
  }, [onDefaultTemplateLoaded, selectedTemplatePath, selectedUserTemplateId, templateContent]);

  useEffect(() => {
    if (selectedUserTemplateId <= 0) {
      const fileName = fileNameFromPath(selectedTemplatePath);
      const selected = templates.find((template) => matchesTemplatePath(template.templatePath, selectedTemplatePath));
      onDefaultMetadataLoaded(fileName, selected?.displayName || fileName);
    }
  }, [onDefaultMetadataLoaded, selectedTemplatePath, selectedUserTemplateId, templates]);
}
