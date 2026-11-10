import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { lazy, Suspense, useEffect } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import "../../styles/settings-center.css";
import { ListChecks, RefreshCw, RotateCcw, Save } from "lucide-react";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
import { ConcurrencyConflictNotice, InlineNotice, PageState } from "../../ui/PageState.tsx";
import { SettingsCategoryNav, SettingsValidationPanel } from "./SettingsPagePanels.tsx";
import { settingsCategoryForPath, changedSettingCounts } from "./settingsDraftModel.ts";
import { systemDefaultPatches } from "./settingsDefaults.ts";
import { readSettingsCategoryFromSearch, readSettingsPanelLabelFromSearch } from "./settingsNavigationModel.ts";
import { useSettingsWorkspace } from "./useSettingsWorkspace.ts";

const LazyRuntimeDatabaseSettingsPanel = lazy(() => import("./RuntimeDatabaseSettingsPanel.tsx"));
const LazyDocumentSettingsPanel = lazy(() => import("./DocumentSettingsPanel.tsx"));
const LazyExcelImportSettingsPanel = lazy(() => import("./ExcelImportSettingsPanel.tsx"));
const LazyExchangeRateSettingsPanel = lazy(() => import("./ExchangeRateSettingsPanel.tsx"));
const LazyCommunicationSettingsPanel = lazy(() => import("./CommunicationSettingsPanel.tsx"));
const LazySingleWindowSettingsPanel = lazy(() => import("./SingleWindowSettingsPanel.tsx"));
const LazyAiSettingsPanel = lazy(() => import("./AiSettingsPanel.tsx"));

function SettingsPanelDeepLink({ label }: { label: string | null }) {
  useEffect(() => {
    if (!label) return;

    const container = document.querySelector(".settings-category-panel");
    if (!container) return;
    const scrollToPanel = () => {
      const panel = Array.from(container.querySelectorAll<HTMLElement>("[aria-label]")).find(
        (element) => element.getAttribute("aria-label") === label,
      );
      panel?.scrollIntoView({ block: "start", behavior: "auto" });
      return Boolean(panel);
    };
    if (scrollToPanel()) return;
    const observer = new MutationObserver(() => { if (scrollToPanel()) observer.disconnect(); });
    observer.observe(container, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, [label]);

  return null;
}


export function SettingsPage({
  client,
  canManageSettings,
  canManageUsers,
  canUseDocumentWorkspace,
  isDesktopRuntime,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  canManageUsers: boolean;
  canUseDocumentWorkspace: boolean;
  isDesktopRuntime: boolean;
}) {
  const model = useSettingsWorkspace({client,canManageSettings,canManageUsers,canUseDocumentWorkspace,isDesktopRuntime});
  const { message, concurrencyMessage, successMessage, settings, isBusy, settingsQuery, handleReloadSettings, handleSubmit, activeCategoryConfig, hasUnsavedChanges, changedCategories, currentCategory, databaseProvider, updateSecrets, setUpdateSecrets, handleRestoreSystemDefaults, handleValidateSettings, availableSettingsCategories, setActiveCategory, healthQuery, patchSetting, secrets, canSelectDesktopDirectory, handleSelectDefaultExportDirectory, issuingAuthorityOptions, handleSingleWindowOrgCodeChange, handleSingleWindowFetchPlaceChange, handleSingleWindowAplAddChange, patchSettings, emailAddressCandidate, handleInferEmailServerConfig, handleTestEmailConnection, validationResult, handleApplyValidationFix, location } = model;
  const counts = changedSettingCounts(model.settings, model.baselineSettings);
  return (
    <EditorWorkspace className="editor-surface settings-surface" aria-label="设置">
      {message ? <InlineNotice tone="error" title="设置未保存">{message}</InlineNotice> : null}
      {concurrencyMessage && <ConcurrencyConflictNotice message={concurrencyMessage} isBusy={settingsQuery.isFetching} onReload={() => void handleReloadSettings()} />}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!settings && isBusy ? <PageState tone="loading" title="正在加载系统设置" description="请稍候，系统正在读取运行目录、数据库和业务配置。" /> : null}

      {settings ? (
        <form className="entity-form settings-center-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <div className="settings-command-strip">
            <PageHeader title={activeCategoryConfig.label} actions={<>
              <details className="settings-more-actions" onKeyDown={event => { if (event.key === "Escape") event.currentTarget.open = false; }}>
                <summary className="command-button secondary">更多</summary><div>
              <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={() => void handleReloadSettings()}>
                <RefreshCw size={18} aria-hidden="true" />
              </button>
              {systemDefaultPatches.some(({ path }) => settingsCategoryForPath(path) === currentCategory) && <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={handleRestoreSystemDefaults}>
                <RotateCcw size={17} aria-hidden="true" />
                <span>恢复本分类默认</span>
              </button>}
              <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={handleValidateSettings}>
                <ListChecks size={17} aria-hidden="true" />
                <span>校验设置</span>
              </button>
                </div>
              </details>
              <button className="command-button" type="submit" disabled={isBusy || !canManageSettings}>
                <Save size={17} aria-hidden="true" />
                <span>保存全部修改</span>
              </button>
            </>}>
              <span className="settings-save-status" data-dirty={hasUnsavedChanges} role="status">{hasUnsavedChanges ? `本分类 ${counts[currentCategory] ?? 0} 项未保存 · 其他分类 ${Object.entries(counts).filter(([key]) => key !== currentCategory).reduce((total,[,count]) => total+(count ?? 0),0)} 项` : "所有修改已保存"}</span>
            </PageHeader>
          </div>
          <div className="settings-center-layout">
            <SettingsCategoryNav
              categories={availableSettingsCategories}
              activeCategory={currentCategory}
              changedCategories={changedCategories.map((category) => category.key)}
              onSelect={setActiveCategory}
            />
            <div className="settings-category-panel">
              {["runtime", "backup"].includes(currentCategory) && !databaseProvider && <PageState
                tone={healthQuery.isError ? "error" : "loading"}
                title={healthQuery.isError ? "无法读取当前数据库类型" : "正在读取运行模式"}
                description={healthQuery.isError ? readApiError(healthQuery.error) : undefined}
                action={healthQuery.isError ? <button className="command-button secondary" type="button" onClick={() => void healthQuery.refetch()}>重试运行状态</button> : undefined} />}
              {validationResult && <SettingsValidationPanel result={validationResult} disabled={isBusy || !canManageSettings} onApplyAutoFix={handleApplyValidationFix} />}
              <Suspense fallback={<PageState tone="loading" title="正在加载设置分类" />}>
              {currentCategory === "runtime" ? (
                <LazyRuntimeDatabaseSettingsPanel
                  settings={settings}
                  secrets={secrets}
                  canManageSettings={canManageSettings}
                  updateSecrets={updateSecrets}
                  onUpdateSecrets={setUpdateSecrets}
                  isBusy={isBusy}
                  canSelectDesktopDirectory={canSelectDesktopDirectory}
                  isDesktopRuntime={isDesktopRuntime}
                  databaseProvider={databaseProvider}
                  onChange={patchSetting}
                  onSelectDefaultExportDirectory={() => void handleSelectDefaultExportDirectory()}
                />
              ) : null}
              {currentCategory === "documents" && <LazyDocumentSettingsPanel settings={settings} disabled={isBusy || !canManageSettings} search={location.search} onChange={patchSetting}
                declarationDefaults={<LazySingleWindowSettingsPanel settings={settings} issuingAuthorityOptions={issuingAuthorityOptions}
                  canManageSettings={canManageSettings && !isBusy} onChange={patchSetting} onOrgCodeChange={handleSingleWindowOrgCodeChange}
                  onFetchPlaceChange={handleSingleWindowFetchPlaceChange} onAplAddChange={handleSingleWindowAplAddChange} />} />}
              {currentCategory === "excel-import" ? (
                <LazyExcelImportSettingsPanel
                  settings={settings}
                  canManageSettings={canManageSettings}
                  isBusy={isBusy}
                  onChange={patchSetting}
                />
              ) : null}

              {currentCategory === "exchange-rate" ? (
                <LazyExchangeRateSettingsPanel
                  client={client}
                  settings={settings}
                  canManageSettings={canManageSettings}
                  isBusy={isBusy}
                  onChange={patchSetting}
                  onPatchSettings={patchSettings}
                  onBlocked={(text) => {
                    model.setMessage(text);
                    model.setSuccessMessage(null);
                  }}
                />
              ) : null}
              {currentCategory === "communication" ? (
                <LazyCommunicationSettingsPanel
                  settings={settings}
                  secrets={secrets}
                  canManageSettings={canManageSettings}
                  updateSecrets={updateSecrets}
                  onUpdateSecrets={setUpdateSecrets}
                  isBusy={isBusy}
                  emailAddressCandidate={emailAddressCandidate}
                  onChange={patchSetting}
                  onInferEmailServerConfig={handleInferEmailServerConfig}
                  onTestEmailConnection={handleTestEmailConnection}
                />
              ) : null}
              {currentCategory === "ai" && <LazyAiSettingsPanel settings={settings} secrets={secrets}
                disabled={isBusy || !canManageSettings} updateSecrets={updateSecrets} onUpdateSecrets={setUpdateSecrets} onChange={patchSetting} />}
              <SettingsPanelDeepLink label={readSettingsCategoryFromSearch(location.search) === currentCategory ? readSettingsPanelLabelFromSearch(location.search) : null} />
              </Suspense>
            </div>
          </div>
        </form>
      ) : null}
    </EditorWorkspace>
  );
}
