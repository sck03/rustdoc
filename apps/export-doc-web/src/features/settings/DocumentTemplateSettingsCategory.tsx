import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { BatchExportSettingsPanel, PaymentTemplateSettingsPanel } from "./DocumentTemplateSettingsPanels.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

export default function DocumentTemplateSettingsCategory({ settings, canManageSettings, isBusy, exportTemplates, exportTemplatesLoading, exportTemplateError, paymentTemplates, paymentTemplatesLoading, paymentTemplateError, onChange, onActionError }: {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  exportTemplates: Awaited<ReturnType<ExportDocManagerApiClient["listReportTemplates"]>>;
  exportTemplatesLoading: boolean;
  exportTemplateError: unknown;
  paymentTemplates: Awaited<ReturnType<ExportDocManagerApiClient["listReportTemplates"]>>;
  paymentTemplatesLoading: boolean;
  paymentTemplateError: unknown;
  onChange: (path: string[], value: unknown) => void;
  onActionError: (error: unknown) => void;
}) {
  return (
    <>
      <BatchExportSettingsPanel settings={settings} canManageSettings={canManageSettings} isBusy={isBusy} templates={exportTemplates} templatesLoading={exportTemplatesLoading} templateErrorMessage={exportTemplateError ? readApiError(exportTemplateError) : null} onChange={onChange} onActionError={onActionError} />
      <PaymentTemplateSettingsPanel settings={settings} canManageSettings={canManageSettings} isBusy={isBusy} templates={paymentTemplates} templatesLoading={paymentTemplatesLoading} templateErrorMessage={paymentTemplateError ? readApiError(paymentTemplateError) : null} onChange={onChange} onActionError={onActionError} />
    </>
  );
}
