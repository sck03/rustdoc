import { Dispatch, SetStateAction, useEffect } from "react";
import {
  ApiReportTemplateContentDto,
  ApiReportTemplateDto,
  ApiUserReportTemplateDto,
} from "../../api/index.ts";
import {
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
  preserveSelection,
  onUserTemplateLoaded,
  onDefaultTemplateLoaded,
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
  preserveSelection: boolean;
  onUserTemplateLoaded: (template: ApiUserReportTemplateDto) => void;
  onDefaultTemplateLoaded: (template: ApiReportTemplateContentDto) => void;
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
    if (!templatesLoaded || preserveSelection) {
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
  }, [configuredTemplatePath, preserveSelection, reportType, requestedTemplateFileName, selectedUserTemplateId, setSelectedTemplatePath, templates, templatesLoaded]);

  useEffect(() => {
    if (!templatesLoaded || !userTemplatesLoaded || selectedUserTemplateId > 0) {
      return;
    }

    const configuredUserTemplateId = readUserTemplateIdFromKey(configuredTemplatePath);
    const selectedUserTemplateKey = readUserTemplateIdFromKey(selectedTemplatePath);
    const targetId = selectedUserTemplateKey || (!selectedTemplatePath ? configuredUserTemplateId : 0);
    if (userTemplates.some((template) => template.id === targetId && template.status === "Published")) {
      setSelectedUserTemplateId(targetId);
    }
  }, [configuredTemplatePath, selectedTemplatePath, selectedUserTemplateId, setSelectedUserTemplateId, templatesLoaded, userTemplates, userTemplatesLoaded]);

  useEffect(() => {
    if (requestedUserTemplateId <= 0 || !userTemplatesLoaded || preserveSelection) {
      return;
    }

    setSelectedUserTemplateId(
      userTemplates.some((template) => template.id === requestedUserTemplateId) ? requestedUserTemplateId : 0,
    );
  }, [preserveSelection, requestedUserTemplateId, setSelectedUserTemplateId, userTemplates, userTemplatesLoaded]);

  useEffect(() => {
    if (selectedUserTemplateId <= 0 || !userTemplatesLoaded) {
      return;
    }

    const selected = userTemplates.find((template) => template.id === selectedUserTemplateId);
    if (!selected) {
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

}
