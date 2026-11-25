import {
  singleWindowCustomsCooAplAddPath,
  singleWindowCustomsCooFetchPlacePath,
  singleWindowCustomsCooOrgCodePath,
  systemUpdaterEndpointPath,
} from "./settingsConfigurationPaths.ts";
import { settingsCategoryForPath } from "./settingsDraftModel.ts";
import type { SettingsCategoryKey } from "./settingsCategoryCatalog.ts";
import type { SettingPatch } from "./settingsTypes.ts";

export const systemDefaultPatches: SettingPatch[] = [
  { path: systemUpdaterEndpointPath, value: "" },
  { path: ["system", "defaultTemplateExporterNameCn"], value: "" },
  { path: ["system", "itemEntryBlankRowCount"], value: 20 },
  { path: ["system", "itemEntrySpareColumnCount"], value: 0 },
  { path: ["system", "documentFieldLabels"], value: { invoice: {}, item: {}, payment: {} } },
  { path: ["system", "logRetentionDays"], value: 30 },
  { path: ["system", "logRetainedFileCount"], value: 14 },
  { path: ["system", "defaultExportDirectory"], value: "" },
  { path: ["singleWindow", "customsCooDefaults", "applName"], value: "" },
  { path: ["singleWindow", "customsCooDefaults", "applicant"], value: "" },
  { path: ["singleWindow", "customsCooDefaults", "applTel"], value: "" },
  { path: singleWindowCustomsCooOrgCodePath, value: "" },
  { path: singleWindowCustomsCooFetchPlacePath, value: "" },
  { path: singleWindowCustomsCooAplAddPath, value: "" },
];

export function defaultPatchesForCategory(category: SettingsCategoryKey, desktop: boolean) {
  return systemDefaultPatches.filter(({ path }) => settingsCategoryForPath(path) === category
    && (desktop || !["updaterEndpoint", "defaultExportDirectory"].includes(path[1] ?? "")));
}
