import { BatchExportSettingsPanel } from "./DocumentTemplateSettingsPanels.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

export default function DocumentTemplateSettingsCategory({ settings, canManageSettings, isBusy, onChange }: {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  onChange: (path: string[], value: unknown) => void;
}) {
  return (
    <BatchExportSettingsPanel settings={settings} canManageSettings={canManageSettings} isBusy={isBusy} onChange={onChange} />
  );
}
