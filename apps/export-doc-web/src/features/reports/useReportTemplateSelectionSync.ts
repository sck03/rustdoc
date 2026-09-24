import { Dispatch, SetStateAction, useEffect, useRef } from "react";
import {
  ApiReportTemplateContentDto,
  ApiReportTemplateDto,
  ApiUserReportTemplateDto,
} from "../../api/index.ts";
import {
  matchesTemplatePath,
  matchesTemplateFileName,
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
  userTemplateContent,
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
  userTemplateContent: ApiUserReportTemplateDto | null;
  templateContent: ApiReportTemplateContentDto | null;
  preserveSelection: boolean;
  onUserTemplateLoaded: (template: ApiUserReportTemplateDto) => void;
  onDefaultTemplateLoaded: (template: ApiReportTemplateContentDto) => void;
}) {
  const appliedRequest = useRef("");
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

    const requestKey = `${requestedReportType ?? ""}|${requestedTemplateFileName}|${requestedUserTemplateId}`;
    const requestChanged = appliedRequest.current !== requestKey;
    if (requestChanged && requestedReportType && requestedReportType !== reportType) return;
    appliedRequest.current = requestKey;
    if (requestChanged) setSelectedUserTemplateId(requestedUserTemplateId);
    setSelectedTemplatePath((current) => {
      if (current && (!requestChanged || matchesTemplateFileName(current, requestedTemplateFileName))) return current;
      return resolveDefaultTemplatePath({
        templates,
        reportType,
        requestedTemplateFileName,
        configuredTemplatePath,
        currentTemplatePath: requestChanged ? "" : current,
        userTemplateSelected: (requestChanged ? requestedUserTemplateId : selectedUserTemplateId) > 0,
      });
    });
  }, [configuredTemplatePath, preserveSelection, reportType, requestedReportType, requestedTemplateFileName, requestedUserTemplateId, selectedUserTemplateId, setSelectedTemplatePath, setSelectedUserTemplateId, templates, templatesLoaded]);

  useEffect(() => {
    if (!templatesLoaded || selectedUserTemplateId > 0) {
      return;
    }

    const configuredUserTemplateId = readUserTemplateIdFromKey(configuredTemplatePath);
    const selectedUserTemplateKey = readUserTemplateIdFromKey(selectedTemplatePath);
    const targetId = selectedUserTemplateKey || (!selectedTemplatePath ? configuredUserTemplateId : 0);
    if (targetId > 0) {
      setSelectedUserTemplateId(targetId);
    }
  }, [configuredTemplatePath, selectedTemplatePath, selectedUserTemplateId, setSelectedUserTemplateId, templatesLoaded]);

  useEffect(() => {
    if (selectedUserTemplateId <= 0 || userTemplateContent?.id !== selectedUserTemplateId) {
      return;
    }

    onUserTemplateLoaded(userTemplateContent);
  }, [onUserTemplateLoaded, selectedUserTemplateId, userTemplateContent]);

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
