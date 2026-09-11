import type { ApiSingleWindowIssuingAuthorityOptionDto } from "../../api/index.ts";
import { TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";
import { singleWindowCustomsCooAplAddPath, singleWindowCustomsCooFetchPlacePath, singleWindowCustomsCooOrgCodePath } from "./settingsConfigurationPaths.ts";

export function SingleWindowSettingsPanel({ settings, issuingAuthorityOptions, canManageSettings, onChange, onOrgCodeChange, onFetchPlaceChange, onAplAddChange }: {
  settings: SettingsRecord;
  issuingAuthorityOptions: ApiSingleWindowIssuingAuthorityOptionDto[];
  canManageSettings: boolean;
  onChange: (path: string[], value: unknown) => void;
  onOrgCodeChange: (value: string) => void;
  onFetchPlaceChange: (value: string) => void;
  onAplAddChange: (value: string) => void;
}) {
  return (
      <section className="form-section settings-single-window-defaults-section" aria-label="申报默认值">
        <div className="section-header"><h2>申报默认值</h2></div>
        <fieldset className="settings-fieldset" disabled={!canManageSettings}>
          <div className="field-grid settings-single-window-grid">
            <TextSetting settings={settings} path={["singleWindow", "customsCooDefaults", "applName"]} label="申报员姓名" onChange={onChange} />
            <TextSetting settings={settings} path={["singleWindow", "customsCooDefaults", "applicant"]} label="申报员身份证号" onChange={onChange} />
            <TextSetting settings={settings} path={["singleWindow", "customsCooDefaults", "applTel"]} label="申报员电话" onChange={onChange} />
            <TextSetting settings={settings} path={singleWindowCustomsCooOrgCodePath} label="签证机构代码(4位)" list="customs-coo-issuing-authority-options" onChange={(_, value) => onOrgCodeChange(value)} />
            <TextSetting settings={settings} path={singleWindowCustomsCooFetchPlacePath} label="领证机构代码(4位)" list="customs-coo-issuing-authority-options" onChange={(_, value) => onFetchPlaceChange(value)} />
            <TextSetting settings={settings} path={singleWindowCustomsCooAplAddPath} label="申请地址(机构所在地)" onChange={(_, value) => onAplAddChange(value)} />
            <datalist id="customs-coo-issuing-authority-options">
              {issuingAuthorityOptions.map((option) => <option key={option.code || option.label} value={option.code} label={option.label} />)}
            </datalist>
          </div>
        </fieldset>
      </section>
  );
}

export default SingleWindowSettingsPanel;
