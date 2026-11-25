import { InlineNotice } from "../../ui/PageState.tsx";
import { DirectorySetting, TextSetting, readSettingString } from "./SettingsFieldControls.tsx";
import { systemUpdaterEndpointPath } from "./settingsConfigurationPaths.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

export default function RuntimeDatabaseSettingsPanel({ settings, canManageSettings, isBusy, isDesktopRuntime, databaseProvider, canSelectDesktopDirectory, onChange, onSelectDefaultExportDirectory }: {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  isDesktopRuntime: boolean;
  databaseProvider: string | null;
  canSelectDesktopDirectory: boolean;
  onChange: (path: string[], value: unknown) => void;
  onSelectDefaultExportDirectory: () => void;
}) {
  const disabled = !canManageSettings || isBusy;
  return <>
    {isDesktopRuntime && <section className="form-section" aria-label="常规与目录">
      <div className="section-header"><h2>常规与目录</h2></div>
      <fieldset className="settings-fieldset" disabled={disabled}>
        <div className="field-grid">
          <DirectorySetting settings={settings} path={["system", "defaultExportDirectory"]} label="默认导出目录" disabled={disabled} canSelectDirectory={canSelectDesktopDirectory} onChange={onChange} onSelectDirectory={onSelectDefaultExportDirectory} />
        </div>
      </fieldset>
      <p className="form-field-description">选择运行数据根目录内的子目录，配置保存相对路径。</p>
    </section>}
    {databaseProvider && <section className="form-section" aria-label="数据库连接">
      <div className="section-header"><h2>数据库连接</h2></div>
      <div className="detail-grid runtime-detail-grid">
        <div className="detail-item"><span>当前数据库</span><strong>{databaseProvider === "sqlite" ? "SQLite" : "PostgreSQL"}</strong></div>
        {databaseProvider === "sqlite" && <div className="detail-item"><span>SQLite 文件名</span><strong>{readSettingString(settings, ["system", "sqliteDatabaseFileName"])}</strong></div>}
      </div>
      <p className="form-field-description">数据库连接由启动配置管理。</p>
    </section>}
    {isDesktopRuntime && <section className="form-section" aria-label="软件更新">
      <div className="section-header"><h2>更新来源</h2></div>
      <InlineNotice tone="info" title="更新来源受保护">
        留空时使用软件默认更新地址；只有系统管理员或软件服务商提供专用地址时才需要修改。公网地址应使用 HTTPS，HTTP 地址只适用于受控公司内网或 VPN。
      </InlineNotice>
      <fieldset className="settings-fieldset" disabled={disabled}>
        <div className="field-grid">
          <TextSetting settings={settings} path={systemUpdaterEndpointPath} label="软件更新地址"
            placeholder="例如 https://.../latest.json 或 http://内网服务器/.../latest.json"
            maxLength={2048} className="form-field-wide" onChange={onChange} />
        </div>
      </fieldset>
    </section>}
  </>;
}
