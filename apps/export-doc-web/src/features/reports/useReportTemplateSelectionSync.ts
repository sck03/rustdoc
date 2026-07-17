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
  onDefaultRenamePath,
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
  onDefaultRenamePath: (fileName: string) => void;
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
        currentTemplatePath: current,
        userTemplateSelected: selectedUserTemplateId > 0,
      }),
    );
  }, [reportType, requestedTemplateFileName, selectedUserTemplateId, setSelectedTemplatePath, templates, templatesLoaded]);

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
      onDefaultRenamePath(fileNameFromPath(selectedTemplatePath));
    }
  }, [onDefaultRenamePath, selectedTemplatePath, selectedUserTemplateId]);
}
