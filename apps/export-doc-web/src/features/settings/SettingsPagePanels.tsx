import type { ApiSettingsValidationMessageDto, ApiSettingsValidationResponse } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import type { SettingsCategoryConfig, SettingsCategoryKey } from "./settingsCategoryCatalog.ts";

export function SettingsCategoryNav({
  categories,
  activeCategory,
  changedCategories = [],
  onSelect,
}: {
  categories: SettingsCategoryConfig[];
  activeCategory: SettingsCategoryKey;
  changedCategories?: SettingsCategoryKey[];
  onSelect: (category: SettingsCategoryKey) => void;
}) {
  return (
    <nav className="settings-category-nav" aria-label="设置分类">
      {categories.map((category) => {
        const Icon = category.icon;
        const isActive = category.key === activeCategory;
        return (
          <button
            key={category.key}
            className={isActive ? "settings-category-item settings-category-item-active" : "settings-category-item"}
            type="button"
            aria-current={isActive ? "page" : undefined}
            onClick={() => onSelect(category.key)}
          >
            <Icon size={17} aria-hidden="true" />
            <span>{category.label}</span>
            {changedCategories.includes(category.key) && <small className="settings-dirty-marker">未保存</small>}
          </button>
        );
      })}
    </nav>
  );
}

export function SettingsValidationPanel({
  result,
}: {
  result: ApiSettingsValidationResponse;
}) {
  const messages = Array.isArray(result.messages) ? result.messages : [];
  const errorCount = messages.filter((item) => item.level === "error").length;
  const warningCount = messages.filter((item) => item.level === "warning").length;

  return (
    <div className="settings-validation-panel" aria-label="设置校验结果">
      <div className="section-header">
        <div>
          <h2>设置校验结果</h2>
          <span>{result.isValid ? "可保存" : "需处理"}</span>
        </div>
      </div>
      <InlineNotice tone={result.isValid ? "success" : "error"}>
        {messages.length === 0
          ? "未发现需要处理的设置项。"
          : `错误 ${errorCount} 项，警告 ${warningCount} 项。`}
      </InlineNotice>
      {messages.length > 0 ? (
        <ResponsiveTableFrame className="backup-table-frame" label="设置校验消息">
          <table className="backup-table" aria-label="设置校验消息">
            <thead>
              <tr>
                <th>级别</th>
                <th>字段</th>
                <th>信息</th>
              </tr>
            </thead>
            <tbody>
              {messages.map((item, index) => (
                <tr key={`${item.propertyName}-${index}`}>
                  <td>{settingsValidationLevelLabel(item.level)}</td>
                  <td>{item.propertyName || "-"}</td>
                  <td>{item.message || "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </ResponsiveTableFrame>
      ) : null}
    </div>
  );
}

function settingsValidationLevelLabel(value: ApiSettingsValidationMessageDto["level"]) {
  switch (value) {
    case "error":
      return "错误";
    case "warning":
      return "警告";
    case "info":
      return "信息";
  }
}
