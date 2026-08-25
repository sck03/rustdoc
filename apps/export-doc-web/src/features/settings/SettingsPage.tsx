import { FormEvent, lazy, Suspense, useEffect, useState } from "react";
import "../../styles/runtime-diagnostics.css";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ListChecks,
  RefreshCw,
  RotateCcw,
  Save,
  Trash2,
} from "lucide-react";
import { useLocation } from "react-router-dom";
import {
  ApiSettingsResponse,
  ApiSettingsValidationResponse,
  AppSettings,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { SecretToggle, readSettingString } from "./SettingsFieldControls.tsx";
import { singleWindowCustomsCooAplAddPath, singleWindowCustomsCooFetchPlacePath, singleWindowCustomsCooOrgCodePath } from "./settingsConfigurationPaths.ts";
import { cloneSettings, normalizeCurrencyList, normalizeSettingText, setNestedValue } from "./settingsValueUtils.ts";
import type { SettingPatch, SettingsRecord } from "./settingsTypes.ts";
import { isDesktopBridgeAvailable, selectDirectory } from "../../desktop/desktopBridge.ts";
import { filterSettingsCategories, settingsCategories, type SettingsCategoryKey } from "./settingsCategoryCatalog.ts";
import { readSettingsCategoryFromSearch, readSettingsPanelLabelFromSearch } from "./settingsNavigationModel.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useSettingsMaintenanceActions } from "./useSettingsMaintenanceActions.ts";
import { useSettingsDraftSync } from "./useSettingsDraftSync.ts";
import { systemDefaultPatches } from "./settingsDefaults.ts";
import { findIssuingAuthority, parseIssuingAuthorityCode } from "./settingsIssuingAuthority.ts";
import { SettingsCategoryNav, SettingsValidationPanel } from "./SettingsPagePanels.tsx";

const LazyMaintenanceSettingsPanels = lazy(() => import("./MaintenanceSettingsPanels.tsx"));
const LazyRuntimeDatabaseSettingsPanel = lazy(() => import("./RuntimeDatabaseSettingsPanel.tsx"));
const LazyExcelImportSettingsPanel = lazy(() => import("./ExcelImportSettingsPanel.tsx"));
const LazyExchangeRateSettingsPanel = lazy(() => import("./ExchangeRateSettingsPanel.tsx"));
const LazyCommunicationSettingsPanel = lazy(() => import("./CommunicationSettingsPanel.tsx"));
const LazySingleWindowSettingsPanel = lazy(() => import("./SingleWindowSettingsPanel.tsx"));

function SettingsPanelDeepLink({ label }: { label: string | null }) {
  useEffect(() => {
    if (!label) return;

    const panel = Array.from(document.querySelectorAll<HTMLElement>("[aria-label]")).find(
      (element) => element.getAttribute("aria-label") === label,
    );
    panel?.scrollIntoView({ block: "start", behavior: "auto" });
  }, [label]);

  return null;
}

type EmailServerSuggestionDraft = {
  emailAddress: string;
  hadFromAddress: boolean;
  hadUserName: boolean;
  hadSmtpHost: boolean;
};

type SingleWindowAuthorityAutoState = {
  fetchPlace: string;
  aplAdd: string;
};

export function SettingsPage({
  client,
  canManageSettings,
  canManageUsers,
  canUseDocumentWorkspace,
  productName,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  canManageUsers: boolean;
  canUseDocumentWorkspace: boolean;
  productName: string;
}) {
  const requestConfirmation = useConfirmation();
  const location = useLocation();
  const availableSettingsCategories = filterSettingsCategories({
    canUseDocumentWorkspace,
  });
  const availableSettingsCategoryKeys = availableSettingsCategories.map((category) => category.key);
  const [settings, setSettings] = useState<SettingsRecord | null>(null);
  const [updateSecrets, setUpdateSecrets] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [validationResult, setValidationResult] = useState<ApiSettingsValidationResponse | null>(null);
  const [singleWindowAuthorityAutoState, setSingleWindowAuthorityAutoState] = useState<SingleWindowAuthorityAutoState>({
    fetchPlace: "",
    aplAdd: "",
  });
  const [activeCategory, setActiveCategory] = useState<SettingsCategoryKey>(() =>
    readSettingsCategoryFromSearch(location.search, availableSettingsCategoryKeys),
  );
  const queryClient = useQueryClient();

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
  });

  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: ({ signal }) => client.getHealth({ signal }),
    enabled: activeCategory === "maintenance",
  });

  const issuingAuthoritiesQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooIssuingAuthorities(),
    queryFn: ({ signal }) => client.getCustomsCooIssuingAuthorities({ signal }),
    enabled: activeCategory === "single-window",
    staleTime: 10 * 60 * 1000,
  });

  useSettingsDraftSync({
    response: settingsQuery.data,
    hasUnsavedChanges,
    setSettings,
    setMessage,
    setUpdateSecrets,
    setHasUnsavedChanges,
    setValidationResult,
    setSingleWindowAuthorityAutoState,
  });

  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedChanges,
    message: "系统设置页面有未保存修改，离开后这些配置将丢失。",
  });

  useEffect(() => {
    if (settingsQuery.isError) {
      setMessage(readApiError(settingsQuery.error));
      setSuccessMessage(null);
    }
  }, [settingsQuery.error, settingsQuery.isError]);

  useEffect(() => {
    setActiveCategory(readSettingsCategoryFromSearch(location.search, availableSettingsCategoryKeys));
  }, [canUseDocumentWorkspace, location.search]);

  const saveMutation = useMutation({
    mutationFn: (body: SettingsRecord) =>
      client.updateSettings({
        body: {
          settings: body as unknown as AppSettings,
          updateSecrets,
        },
      }),
    onSuccess: async (response) => {
      setSettings(response.settings as unknown as SettingsRecord);
      setMessage(null);
      setSuccessMessage(response.requiresRestart ? `${response.message} 需要重启后生效。` : response.message || "设置已保存。");
      setUpdateSecrets(false);
      setHasUnsavedChanges(false);
      setValidationResult(null);
      queryClient.setQueryData<ApiSettingsResponse>(queryKeys.settings(), {
        secrets: response.secrets,
        settings: response.settings,
        storagePolicy: settingsQuery.data?.storagePolicy ?? "",
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.settings() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const validateSettingsMutation = useMutation({
    mutationFn: (body: SettingsRecord) =>
      client.validateSettings({
        body: {
          settings: body as unknown as AppSettings,
          updateSecrets,
        },
      }),
    onSuccess: (response) => {
      setValidationResult(response);
      setActiveCategory("maintenance");
      setMessage(response.isValid ? null : "设置校验发现需要处理的问题。");
      setSuccessMessage(
        response.isValid
          ? response.hasWarnings
            ? "设置校验完成，有警告项可复核。"
            : "设置校验通过。"
          : null,
      );
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const testEmailMutation = useMutation({
    mutationFn: () => client.testEmailConnection(),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "邮件连接测试成功。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.emailStatus() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const testWebDavMutation = useMutation({
    mutationFn: () => client.testCloudBackupConnection(),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(response.message || "WebDAV 连接测试成功。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.cloudBackupStatus() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const inferEmailServerMutation = useMutation({
    mutationFn: (request: EmailServerSuggestionDraft) =>
      client.suggestEmailServerConfig({
        body: {
          emailAddress: request.emailAddress,
        },
      }),
    onSuccess: (response, request) => {
      const draftEmailAddress = response.emailAddress || request.emailAddress;
      const patches: SettingPatch[] = [];

      if (!request.hadFromAddress && draftEmailAddress) {
        patches.push({ path: ["email", "fromAddress"], value: draftEmailAddress });
      }

      if (!request.hadUserName && draftEmailAddress) {
        patches.push({ path: ["email", "userName"], value: draftEmailAddress });
      }

      if (!request.hadSmtpHost) {
        patches.push({ path: ["email", "smtpHost"], value: response.smtpHost });
        patches.push({ path: ["email", "smtpPort"], value: response.smtpPort });
        patches.push({ path: ["email", "enableSsl"], value: response.enableSsl });
      }

      if (patches.length > 0) {
        patchSettings(patches);
      }

      setMessage(null);
      setSuccessMessage(
        response.message || `已根据 ${draftEmailAddress} 推断 SMTP 配置。`,
      );
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const maintenanceActions = useSettingsMaintenanceActions({ client, patchSettings, refetchHealth: healthQuery.refetch, setMessage, setSuccessMessage });
  const cleanupSystemLogsMutation = maintenanceActions.cleanupMutation;
  const refreshExchangeCurrenciesMutation = maintenanceActions.refreshCurrenciesMutation;

  const isBusy =
    settingsQuery.isFetching ||
    saveMutation.isPending ||
    validateSettingsMutation.isPending ||
    testEmailMutation.isPending ||
    testWebDavMutation.isPending ||
    inferEmailServerMutation.isPending ||
    cleanupSystemLogsMutation.isPending ||
    refreshExchangeCurrenciesMutation.isPending;
  const secrets = settingsQuery.data?.secrets ?? null;
  const issuingAuthorityOptions = issuingAuthoritiesQuery.data?.options ?? [];
  const canSelectDesktopDirectory = isDesktopBridgeAvailable();
  const emailAddressCandidate =
    settings
      ? readSettingString(settings, ["email", "fromAddress"]).trim() || readSettingString(settings, ["email", "userName"]).trim()
      : "";
  const currentCategory = availableSettingsCategories.some((category) => category.key === activeCategory)
    ? activeCategory
    : "runtime";
  const activeCategoryConfig =
    availableSettingsCategories.find((category) => category.key === currentCategory) ?? settingsCategories[0];

  function patchSetting(path: string[], value: unknown) {
    if (!canManageSettings) {
      return;
    }

    patchSettings([{ path, value }]);
  }

  function patchSettings(patches: SettingPatch[]) {
    if (!canManageSettings || patches.length === 0) {
      return;
    }

    setSettings((current) => {
      const next = cloneSettings(current ?? {});
      for (const patch of patches) {
        setNestedValue(next, patch.path, patch.value);
      }
      return next;
    });
    setHasUnsavedChanges(true);
    setValidationResult(null);
    setSuccessMessage(null);
  }

  function handleSingleWindowOrgCodeChange(value: string) {
    if (!settings) {
      return;
    }

    const orgCode = parseIssuingAuthorityCode(value, issuingAuthorityOptions);
    const authority = findIssuingAuthority(orgCode, issuingAuthorityOptions);
    const patches: SettingPatch[] = [
      { path: singleWindowCustomsCooOrgCodePath, value: orgCode },
    ];
    const nextAutoState = { ...singleWindowAuthorityAutoState };

    if (authority) {
      const currentFetchPlace = readSettingString(settings, singleWindowCustomsCooFetchPlacePath);
      if (
        !currentFetchPlace.trim() ||
        normalizeSettingText(currentFetchPlace) === normalizeSettingText(singleWindowAuthorityAutoState.fetchPlace)
      ) {
        patches.push({ path: singleWindowCustomsCooFetchPlacePath, value: authority.code });
        nextAutoState.fetchPlace = authority.code;
      }

      const currentAplAdd = readSettingString(settings, singleWindowCustomsCooAplAddPath);
      if (
        authority.applicationAddress &&
        (!currentAplAdd.trim() ||
          normalizeSettingText(currentAplAdd) === normalizeSettingText(singleWindowAuthorityAutoState.aplAdd))
      ) {
        patches.push({ path: singleWindowCustomsCooAplAddPath, value: authority.applicationAddress });
        nextAutoState.aplAdd = authority.applicationAddress;
      }
    }

    setSingleWindowAuthorityAutoState(nextAutoState);
    patchSettings(patches);
  }

  function handleSingleWindowFetchPlaceChange(value: string) {
    const fetchPlace = parseIssuingAuthorityCode(value, issuingAuthorityOptions);
    if (
      singleWindowAuthorityAutoState.fetchPlace &&
      normalizeSettingText(fetchPlace) !== normalizeSettingText(singleWindowAuthorityAutoState.fetchPlace)
    ) {
      setSingleWindowAuthorityAutoState((current) => ({ ...current, fetchPlace: "" }));
    }

    patchSetting(singleWindowCustomsCooFetchPlacePath, fetchPlace);
  }

  function handleSingleWindowAplAddChange(value: string) {
    if (
      singleWindowAuthorityAutoState.aplAdd &&
      normalizeSettingText(value) !== normalizeSettingText(singleWindowAuthorityAutoState.aplAdd)
    ) {
      setSingleWindowAuthorityAutoState((current) => ({ ...current, aplAdd: "" }));
    }

    patchSetting(singleWindowCustomsCooAplAddPath, value);
  }

  function handleTestEmailConnection() {
    if (!canManageSettings || isBusy) {
      return;
    }

    if (hasUnsavedChanges) {
      setMessage("请先保存当前邮件设置，再测试已保存的 SMTP 配置。");
      setSuccessMessage(null);
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    testEmailMutation.mutate();
  }

  function handleTestWebDavConnection() {
    if (!canManageSettings || isBusy) {
      return;
    }

    if (hasUnsavedChanges) {
      setMessage("请先保存当前 WebDAV 设置，再测试已保存的连接配置。");
      setSuccessMessage(null);
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    testWebDavMutation.mutate();
  }

  function handleInferEmailServerConfig() {
    if (!canManageSettings || isBusy) {
      return;
    }

    if (!emailAddressCandidate) {
      setMessage("请先填写发件人地址或邮箱账号。");
      setSuccessMessage(null);
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    inferEmailServerMutation.mutate({
      emailAddress: emailAddressCandidate,
      hadFromAddress: Boolean(readSettingString(settings ?? {}, ["email", "fromAddress"]).trim()),
      hadUserName: Boolean(readSettingString(settings ?? {}, ["email", "userName"]).trim()),
      hadSmtpHost: Boolean(readSettingString(settings ?? {}, ["email", "smtpHost"]).trim()),
    });
  }

  async function handleSelectDefaultExportDirectory() {
    if (!canManageSettings || isBusy) {
      return;
    }

    try {
      const selectedPath = await selectDirectory();
      if (selectedPath) {
        patchSetting(["system", "defaultExportDirectory"], selectedPath);
      }
    } catch (error) {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    }
  }

  async function handleRestoreSystemDefaults() {
    if (!canManageSettings || isBusy) {
      return;
    }

    if (!await requestConfirmation({
      title: "恢复系统默认设置",
      description: "确定要把当前系统设置草稿恢复为默认值吗？",
      details: ["此操作只修改当前页面草稿。", "点击保存后才会写入正式配置。", "受保护的密码和密钥不会被直接清空。"],
      confirmLabel: "恢复默认值",
    })) {
      return;
    }

    patchSettings([
      { path: ["system", "appName"], value: productName },
      ...systemDefaultPatches,
    ]);
    setSingleWindowAuthorityAutoState({ fetchPlace: "", aplAdd: "" });
    setMessage(null);
    setSuccessMessage("已恢复系统设置默认值，请检查后保存。受保护的密码/密钥字段仍按“更新敏感字段”开关处理。");
  }

  function handleValidateSettings() {
    if (!settings || !canManageSettings || isBusy) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    validateSettingsMutation.mutate(settings);
  }

  function handleApplyValidationFix() {
    if (!validationResult?.canAutoFix || !validationResult.normalizedSettings || !canManageSettings) {
      return;
    }

    setSettings(validationResult.normalizedSettings as unknown as SettingsRecord);
    setHasUnsavedChanges(true);
    setMessage(null);
    setSuccessMessage("已把自动修复结果应用到当前草稿，请检查后保存。");
    setValidationResult({
      ...validationResult,
      canAutoFix: false,
    });
  }

  async function handleCleanupSystemLogs() {
    if (!settings || !canManageSettings || isBusy) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    try {
      if (hasUnsavedChanges) {
        await saveMutation.mutateAsync(settings);
      }

      await cleanupSystemLogsMutation.mutateAsync();
    } catch {
      // Mutation handlers surface the user-facing error.
    }
  }

  function handleRefreshExchangeCurrencies() {
    if (!canManageSettings || isBusy) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    refreshExchangeCurrenciesMutation.mutate();
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settings || !canManageSettings) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    saveMutation.mutate(settings);
  }

  return (
    <section className="editor-surface settings-surface" aria-label="设置">
      {message ? <InlineNotice tone="error" title="设置未保存">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!settings && isBusy ? <PageState tone="loading" title="正在加载系统设置" description="请稍候，系统正在读取运行目录、数据库和业务配置。" /> : null}

      {settings ? (
        <form className="entity-form settings-center-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <div className="settings-command-strip">
            <div className="settings-command-heading">
              <div className="settings-command-heading-row">
                <h2>{activeCategoryConfig.label}</h2>
              </div>
              {hasUnsavedChanges ? <span>有未保存修改</span> : null}
            </div>
            <div className="toolbar-actions settings-command-actions">
              <SecretToggle checked={updateSecrets} disabled={!canManageSettings} onChange={setUpdateSecrets} />
              <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={() => void settingsQuery.refetch()}>
                <RefreshCw size={18} aria-hidden="true" />
              </button>
              <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={handleRestoreSystemDefaults}>
                <RotateCcw size={17} aria-hidden="true" />
                <span>恢复默认</span>
              </button>
              <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={handleValidateSettings}>
                <ListChecks size={17} aria-hidden="true" />
                <span>校验设置</span>
              </button>
              <button className="command-button" type="button" disabled={isBusy || !canManageSettings} onClick={handleCleanupSystemLogs}>
                <Trash2 size={17} aria-hidden="true" />
                <span>清理旧日志</span>
              </button>
              <button className="command-button" type="submit" disabled={isBusy || !canManageSettings}>
                <Save size={17} aria-hidden="true" />
                <span>保存</span>
              </button>
            </div>
          </div>
          <div className="settings-center-layout">
            <SettingsCategoryNav
              categories={availableSettingsCategories}
              activeCategory={currentCategory}
              onSelect={setActiveCategory}
            />
            <div className="settings-category-panel">
              <Suspense fallback={<PageState tone="loading" title="正在加载设置分类" />}>
              {currentCategory === "runtime" ? (
                <LazyRuntimeDatabaseSettingsPanel
                  settings={settings}
                  secrets={secrets}
                  canManageSettings={canManageSettings}
                  updateSecrets={updateSecrets}
                  isBusy={isBusy}
                  canSelectDesktopDirectory={canSelectDesktopDirectory}
                  onChange={patchSetting}
                  onSelectDefaultExportDirectory={() => void handleSelectDefaultExportDirectory()}
                />
              ) : null}
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
                  settings={settings}
                  canManageSettings={canManageSettings}
                  isBusy={isBusy}
                  onChange={patchSetting}
                  onPatchSettings={patchSettings}
                  onBlocked={(text) => {
                    setMessage(text);
                    setSuccessMessage(null);
                  }}
                  onRefreshCurrencies={handleRefreshExchangeCurrencies}
                />
              ) : null}
              {currentCategory === "communication" ? (
                <LazyCommunicationSettingsPanel
                  client={client}
                  settings={settings}
                  secrets={secrets}
                  canManageSettings={canManageSettings}
                  updateSecrets={updateSecrets}
                  isBusy={isBusy}
                  emailAddressCandidate={emailAddressCandidate}
                  onChange={patchSetting}
                  onInferEmailServerConfig={handleInferEmailServerConfig}
                  onTestEmailConnection={handleTestEmailConnection}
                  onTestWebDavConnection={handleTestWebDavConnection}
                  onPathError={setMessage}
                />
              ) : null}
              {currentCategory === "single-window" ? (
                <LazySingleWindowSettingsPanel
                  settings={settings}
                  secrets={secrets}
                  issuingAuthorityOptions={issuingAuthorityOptions}
                  canManageSettings={canManageSettings}
                  updateSecrets={updateSecrets}
                  onChange={patchSetting}
                  onOrgCodeChange={handleSingleWindowOrgCodeChange}
                  onFetchPlaceChange={handleSingleWindowFetchPlaceChange}
                  onAplAddChange={handleSingleWindowAplAddChange}
                />
              ) : null}
              {currentCategory === "maintenance" ? (
                <>
                  <Suspense fallback={<PageState tone="loading" title="正在加载维护工具" />}>
                    <LazyMaintenanceSettingsPanels
                      client={client}
                      canManageSettings={canManageSettings}
                      canManageUsers={canManageUsers}
                      health={healthQuery.data ?? null}
                      healthIsBusy={healthQuery.isFetching}
                      healthErrorMessage={healthQuery.isError ? readApiError(healthQuery.error) : null}
                      initialPanelLabel={readSettingsPanelLabelFromSearch(location.search) ?? ""}
                      onRefreshHealth={() => void healthQuery.refetch()}
                      onPathError={setMessage}
                    />
                  </Suspense>
                  {validationResult ? (
                    <SettingsValidationPanel
                      result={validationResult}
                      disabled={isBusy || !canManageSettings}
                      onApplyAutoFix={handleApplyValidationFix}
                    />
                  ) : null}
                </>
              ) : null}
              <SettingsPanelDeepLink label={readSettingsPanelLabelFromSearch(location.search)} />
              </Suspense>
            </div>
          </div>
        </form>
      ) : null}
    </section>
  );
}

export function getSettingsTitle() {
  return "设置";
}
