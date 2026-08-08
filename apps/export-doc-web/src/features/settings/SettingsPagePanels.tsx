import { RotateCcw } from "lucide-react";
import type { ApiSettingsValidationResponse } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import type { SettingsCategoryConfig, SettingsCategoryKey } from "./settingsCategoryCatalog.ts";

export function SettingsCategoryNav({
  categories,
  activeCategory,
  onSelect,
}: {
  categories: SettingsCategoryConfig[];
  activeCategory: SettingsCategoryKey;
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
          </button>
        );
      })}
    </nav>
  );
}

export function SettingsValidationPanel({
  result,
  disabled,
  onApplyAutoFix,
}: {
  result: ApiSettingsValidationResponse;
  disabled: boolean;
  onApplyAutoFix: () => void;
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
        <button
          className="command-button secondary"
          type="button"
          disabled={disabled || !result.canAutoFix}
          onClick={onApplyAutoFix}
        >
          <RotateCcw size={17} aria-hidden="true" />
          <span>应用自动修复</span>
        </button>
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
                <th>修复</th>
              </tr>
            </thead>
            <tbody>
              {messages.map((item, index) => (
                <tr key={`${item.propertyName}-${index}`}>
                  <td>{settingsValidationLevelLabel(item.level)}</td>
                  <td>{item.propertyName || "-"}</td>
                  <td>{item.message || "-"}</td>
                  <td>{item.isAutoFixable ? "可自动修复" : "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </ResponsiveTableFrame>
      ) : null}
    </div>
  );
}

function settingsValidationLevelLabel(value?: string) {
  switch (value) {
    case "error":
      return "错误";
    case "warning":
      return "警告";
    case "info":
      return "信息";
    default:
      return value?.trim() || "-";
  }
}
