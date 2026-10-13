import type { ApiSettingsSecretsDto } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { DirectorySetting, NumberSetting, TextSetting } from "./SettingsFieldControls.tsx";
import { systemUpdaterEndpointPath } from "./settingsConfigurationPaths.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

export default function RuntimeDatabaseSettingsPanel({ settings, secrets, canManageSettings, updateSecrets, isBusy, isDesktopRuntime, databaseProvider, canSelectDesktopDirectory, onChange, onSelectDefaultExportDirectory }: {
  settings: SettingsRecord;
  secrets: ApiSettingsSecretsDto | null;
  canManageSettings: boolean;
  updateSecrets: boolean;
  isBusy: boolean;
  isDesktopRuntime: boolean;
  databaseProvider: string | null;
  canSelectDesktopDirectory: boolean;
  onChange: (path: string[], value: unknown) => void;
  onSelectDefaultExportDirectory: () => void;
}) {
  const disabled = !canManageSettings || isBusy;
  return <>
    <section className="form-section" aria-label="常规与目录">
      <div className="section-header"><h2>常规与目录</h2></div>
      <fieldset className="settings-fieldset" disabled={disabled}>
        <div className="field-grid">
          <TextSetting settings={settings} path={["system", "appName"]} label="软件名称" onChange={onChange} />
          <DirectorySetting settings={settings} path={["system", "defaultExportDirectory"]} label="默认导出目录" disabled={disabled} canSelectDirectory={canSelectDesktopDirectory} onChange={onChange} onSelectDirectory={onSelectDefaultExportDirectory} />
        </div>
      </fieldset>
    </section>
    {databaseProvider && <section className="form-section" aria-label="数据库连接">
      <div className="section-header"><h2>数据库连接</h2><span>当前使用 {databaseProvider === "sqlite" ? "SQLite" : "PostgreSQL"}</span></div>
      <p className="form-field-description">当前数据库由启动配置决定。这里保存的参数不会切换正在使用的连接；服务器地址、账号和文件路径须由部署管理员在启动配置中维护。</p>
      <fieldset className="settings-fieldset" disabled={disabled}>
        <div className="field-grid">
          {databaseProvider === "sqlite" && <TextSetting settings={settings} path={["system", "sqliteDatabaseFileName"]} label="SQLite 文件名" onChange={onChange} />}
          {databaseProvider === "postgresql" && <>
            <TextSetting settings={settings} path={["system", "postgreSqlHost"]} label="服务器" onChange={onChange} />
            <NumberSetting settings={settings} path={["system", "postgreSqlPort"]} label="端口" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlDatabase"]} label="数据库名" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlUsername"]} label="账号" onChange={onChange} />
            <TextSetting disabled={!updateSecrets} placeholder={secrets?.postgreSqlPasswordSet ? "已保存，勾选后可更新" : ""} settings={settings} path={["system", "postgreSqlPassword"]} label="密码" onChange={onChange} />
            <TextSetting settings={settings} path={["system", "postgreSqlAdditionalOptions"]} label="附加参数" onChange={onChange} />
          </>}
        </div>
      </fieldset>
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
