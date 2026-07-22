import { FormEvent, lazy, Suspense, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Database,
  Coins,
  FileCog,
  FileSpreadsheet,
  ListChecks,
  Network,
  RefreshCw,
  RotateCcw,
  Save,
  Trash2,
  Wrench,
  type LucideIcon,
} from "lucide-react";
import { useLocation } from "react-router-dom";
import {
  ApiSettingsResponse,
  ApiSettingsValidationResponse,
  ApiSingleWindowIssuingAuthorityOptionDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { SecretToggle, readSettingString } from "./SettingsFieldControls.tsx";
import { exchangeRateAllSupportedCurrenciesPath, exchangeRateLastCurrencyListUpdateTimePath, singleWindowCustomsCooAplAddPath, singleWindowCustomsCooFetchPlacePath, singleWindowCustomsCooOrgCodePath } from "./settingsConfigurationPaths.ts";
import { cloneSettings, normalizeCurrencyList, normalizeSettingText, setNestedValue } from "./settingsValueUtils.ts";
import type { SettingPatch, SettingsRecord } from "./settingsTypes.ts";
import { isDesktopBridgeAvailable, selectDirectory } from "../../desktop/desktopBridge.ts";
import { filterSettingsCategories, settingsCategories, type SettingsCategoryConfig, type SettingsCategoryKey } from "./settingsCategoryCatalog.ts";
import { readSettingsCategoryFromSearch, readSettingsPanelLabelFromSearch } from "./settingsNavigationModel.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { useSettingsMaintenanceActions } from "./useSettingsMaintenanceActions.ts";
import { useSettingsDraftSync } from "./useSettingsDraftSync.ts";

const LazyMaintenanceSettingsPanels = lazy(() => import("./MaintenanceSettingsPanels.tsx"));
const LazyRuntimeDatabaseSettingsPanel = lazy(() => import("./RuntimeDatabaseSettingsPanel.tsx"));
const LazyDocumentTemplateSettingsCategory = lazy(() => import("./DocumentTemplateSettingsCategory.tsx"));
const LazyExcelImportSettingsPanel = lazy(() => import("./ExcelImportSettingsPanel.tsx"));
const LazyExchangeRateSettingsPanel = lazy(() => import("./ExchangeRateSettingsPanel.tsx"));
const LazyCommunicationSettingsPanel = lazy(() => import("./CommunicationSettingsPanel.tsx"));
const LazySingleWindowSettingsPanel = lazy(() => import("./SingleWindowSettingsPanel.tsx"));

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

const systemDefaultPatches: SettingPatch[] = [
  { path: ["system", "appName"], value: "出口单证管理系统" },
  { path: ["system", "defaultTemplateExporterNameCn"], value: "" },
  { path: ["system", "backupRetentionDays"], value: 0 },
  { path: ["system", "itemEntryBlankRowCount"], value: 20 },
  { path: ["system", "auditLogRetentionDays"], value: 180 },
  { path: ["system", "logRetentionDays"], value: 30 },
  { path: ["system", "logRetainedFileCount"], value: 14 },
  { path: ["system", "logFileSizeLimitMB"], value: 20 },
  { path: ["system", "defaultExportDirectory"], value: "" },
  { path: ["system", "databaseProvider"], value: "Sqlite" },
  { path: ["system", "sqliteDatabaseFileName"], value: "data.db" },
  { path: ["system", "postgreSqlAutoBackupEnabled"], value: false },
  { path: ["system", "postgreSqlAutoBackupSchedule"], value: "Daily" },
  { path: ["system", "postgreSqlAutoBackupTime"], value: "02:00" },
  { path: ["system", "postgreSqlAutoBackupDayOfWeek"], value: 1 },
  { path: ["system", "postgreSqlAutoBackupRetentionCount"], value: 14 },
  { path: ["system", "postgreSqlHost"], value: "" },
  { path: ["system", "postgreSqlPort"], value: 5432 },
  { path: ["system", "postgreSqlDatabase"], value: "" },
  { path: ["system", "postgreSqlUsername"], value: "" },
  { path: ["system", "postgreSqlPassword"], value: "" },
  { path: ["system", "postgreSqlAdditionalOptions"], value: "" },
  { path: ["singleWindow", "customsCooDefaults", "applName"], value: "" },
  { path: ["singleWindow", "customsCooDefaults", "applicant"], value: "" },
  { path: ["singleWindow", "customsCooDefaults", "applTel"], value: "" },
  { path: singleWindowCustomsCooOrgCodePath, value: "" },
  { path: singleWindowCustomsCooFetchPlacePath, value: "" },
  { path: singleWindowCustomsCooAplAddPath, value: "" },
];

export function SettingsPage({
  client,
  canManageSettings,
  canManageUsers,
  canUseDocumentWorkspace,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  canManageUsers: boolean;
  canUseDocumentWorkspace: boolean;
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
    queryFn: () => client.getSettings(),
  });

  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: () => client.getHealth(),
    enabled: activeCategory === "maintenance",
  });

  const exportTemplatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates("ExportDocument"),
    queryFn: () => client.listReportTemplates({ reportType: "ExportDocument" }),
    enabled: activeCategory === "document-templates",
    staleTime: 5 * 60 * 1000,
  });

  const paymentTemplatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates("PaymentVoucher"),
    queryFn: () => client.listReportTemplates({ reportType: "PaymentVoucher" }),
    enabled: activeCategory === "document-templates",
    staleTime: 5 * 60 * 1000,
  });

  const issuingAuthoritiesQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooIssuingAuthorities(),
    queryFn: () => client.getCustomsCooIssuingAuthorities(),
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

  useEffect(() => {
    if (settingsQuery.isError) {
      setMessage(readApiError(settingsQuery.error));
      setSuccessMessage(null);
    }
  }, [settingsQuery.error, settingsQuery.isError]);

  useEffect(() => {
    setActiveCategory(readSettingsCategoryFromSearch(location.search, availableSettingsCategoryKeys));
  }, [canUseDocumentWorkspace, location.search]);

  useEffect(() => {
    if (!settings) {
      return;
    }

    const panelLabel = readSettingsPanelLabelFromSearch(location.search);
    if (!panelLabel) {
      return;
    }

    const timerId = window.setTimeout(() => {
      const panel = Array.from(document.querySelectorAll<HTMLElement>("[aria-label]")).find(
        (element) => element.getAttribute("aria-label") === panelLabel,
      );
      panel?.scrollIntoView({ block: "start", behavior: "auto" });
    }, 0);

    return () => window.clearTimeout(timerId);
  }, [activeCategory, location.search, settings]);

  const saveMutation = useMutation({
    mutationFn: (body: SettingsRecord) =>
      client.updateSettings({
        body: {
          settings: body,
          updateSecrets,
        },
      }),
    onSuccess: async (response) => {
      setSettings(response.settings);
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
          settings: body,
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

    patchSettings(systemDefaultPatches);
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

    setSettings(validationResult.normalizedSettings as SettingsRecord);
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
              <h2>{activeCategoryConfig.label}</h2>
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
              {currentCategory === "document-templates" ? (
                <LazyDocumentTemplateSettingsCategory
                  settings={settings}
                  canManageSettings={canManageSettings}
                  isBusy={isBusy}
                  exportTemplates={exportTemplatesQuery.data ?? []}
                  exportTemplatesLoading={exportTemplatesQuery.isFetching}
                  exportTemplateError={exportTemplatesQuery.isError ? exportTemplatesQuery.error : null}
                  paymentTemplates={paymentTemplatesQuery.data ?? []}
                  paymentTemplatesLoading={paymentTemplatesQuery.isFetching}
                  paymentTemplateError={paymentTemplatesQuery.isError ? paymentTemplatesQuery.error : null}
                  onChange={patchSetting}
                  onActionError={(error) => {
                    setMessage(readApiError(error));
                    setSuccessMessage(null);
                  }}
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

function SettingsCategoryNav({
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

function SettingsValidationPanel({
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

function parseIssuingAuthorityCode(value: string, options: ApiSingleWindowIssuingAuthorityOptionDto[]) {
  const trimmed = value.trim();
  if (!trimmed) {
    return "";
  }

  const codeMatch = trimmed.match(/(?:^|\D)(\d{4})(?:\D|$)/);
  if (codeMatch) {
    return codeMatch[1];
  }

  const normalized = normalizeAuthorityLookupText(trimmed);
  const matched = options.find((option) => {
    const normalizedCode = normalizeAuthorityLookupText(option.code);
    const normalizedLabel = normalizeAuthorityLookupText(option.label);
    return normalizedCode === normalized || normalizedLabel === normalized || (normalized.length >= 2 && normalizedLabel.includes(normalized));
  });

  return matched?.code || trimmed;
}

function findIssuingAuthority(code: string, options: ApiSingleWindowIssuingAuthorityOptionDto[]) {
  const normalizedCode = normalizeAuthorityLookupText(code);
  return options.find((option) => normalizeAuthorityLookupText(option.code) === normalizedCode) ?? null;
}

function normalizeAuthorityLookupText(value: string) {
  return value
    .trim()
    .replace(/[\s:：]/g, "")
    .toUpperCase();
}
