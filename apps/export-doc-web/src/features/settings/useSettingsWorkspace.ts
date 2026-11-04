import { type FormEvent, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocation } from "react-router-dom";
import type { ApiSettingsResponse, ApiSettingsValidationResponse, AppSettings, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isConcurrencyConflict, readApiError } from "../../ui/formUtils.ts";
import { readSettingString } from "./SettingsFieldControls.tsx";
import { singleWindowCustomsCooAplAddPath, singleWindowCustomsCooFetchPlacePath, singleWindowCustomsCooOrgCodePath } from "./settingsConfigurationPaths.ts";
import { cloneSettings, normalizeSettingText, setNestedValue } from "./settingsValueUtils.ts";
import type { SettingPatch, SettingsRecord } from "./settingsTypes.ts";
import { isDesktopBridgeAvailable, selectDirectory } from "../../desktop/desktopBridge.ts";
import { filterSettingsCategories, settingsCategories, allSettingsCategories, type SettingsCategoryKey } from "./settingsCategoryCatalog.ts";
import { readSettingsCategoryFromSearch } from "./settingsNavigationModel.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { useSettingsMaintenanceActions } from "./useSettingsMaintenanceActions.ts";
import { useSettingsDraftSync } from "./useSettingsDraftSync.ts";
import { systemDefaultPatches } from "./settingsDefaults.ts";
import { findIssuingAuthority, parseIssuingAuthorityCode } from "./settingsIssuingAuthority.ts";
import { changedSettingsCategories, settingsCategoryForPath } from "./settingsDraftModel.ts";

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

export function useSettingsWorkspace({
  client,
  canManageSettings,
  canUseDocumentWorkspace,
  isDesktopRuntime,
  productName,
  operationMode,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  canManageUsers: boolean;
  canUseDocumentWorkspace: boolean;
  isDesktopRuntime: boolean;
  productName: string;
  operationMode?: "maintenance";
}) {
  const requestConfirmation = useConfirmation();
  const location = useLocation();
  const availableSettingsCategories = filterSettingsCategories({
    canUseDocumentWorkspace,
  });
  const availableSettingsCategoryKeys = availableSettingsCategories.map((category) => category.key);
  const [settings, setSettings] = useState<SettingsRecord | null>(null);
  const [updateSecrets, setUpdateSecrets] = useState(false);
  const [baselineSettings, setBaselineSettings] = useState<SettingsRecord | null>(null);
  const changedCategories = changedSettingsCategories(baselineSettings, settings);
  const hasUnsavedChanges = changedCategories.length > 0;
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [validationResult, setValidationResult] = useState<ApiSettingsValidationResponse | null>(null);
  const [singleWindowAuthorityAutoState, setSingleWindowAuthorityAutoState] = useState<SingleWindowAuthorityAutoState>({
    fetchPlace: "",
    aplAdd: "",
  });
  const { update: updateRoute } = useRouteQuery();
  const activeCategory = operationMode ?? readSettingsCategoryFromSearch(location.search, availableSettingsCategoryKeys);
  const setActiveCategory = async (key: SettingsCategoryKey) => {
    updateRoute({ section: availableSettingsCategories.find((item) => item.key === key)?.section, group: null, archive: null }, false);
  };
  const queryClient = useQueryClient();
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled: operationMode !== "maintenance" || canManageSettings,
  });

  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: ({ signal }) => client.getHealth({ signal }),
    enabled: ["runtime", "backup", "maintenance"].includes(activeCategory),
  });

  const issuingAuthoritiesQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooIssuingAuthorities(),
    queryFn: ({ signal }) => client.getCustomsCooIssuingAuthorities({ signal }),
    enabled: activeCategory === "documents",
    staleTime: 10 * 60 * 1000,
  });

  useSettingsDraftSync({
    response: settingsQuery.data,
    hasUnsavedChanges,
    setSettings,
    setMessage,
    setUpdateSecrets,
    setBaselineSettings,
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

  const saveMutation = useMutation({
    mutationFn: (body: SettingsRecord) =>
      client.updateSettings({
        body: {
          settings: body as unknown as AppSettings,
          updateSecrets,
        },
      }),
    onSuccess: async (response) => {
      setConcurrencyMessage(null);
      setSettings(response.settings as unknown as SettingsRecord);
      setMessage(null);
      setSuccessMessage(response.requiresRestart ? `${response.message} 需要重启后生效。` : response.message || "设置已保存。");
      setUpdateSecrets(false);
      setBaselineSettings(response.settings as unknown as SettingsRecord);
      setValidationResult(null);
      queryClient.setQueryData<ApiSettingsResponse>(queryKeys.settings(), {
        secrets: response.secrets,
        settings: response.settings,
        storagePolicy: settingsQuery.data?.storagePolicy ?? "",
      });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.settings() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.exchangeRatesRoot() }),
        ...["ExportDocument", "PaymentVoucher"].map((type) => queryClient.invalidateQueries({ queryKey: queryKeys.reportTemplateFields(type) })),
      ]);
    },
    onError: (error) => {
      setConcurrencyMessage(isConcurrencyConflict(error) ? readApiError(error) : null);
      setMessage(isConcurrencyConflict(error) ? null : readApiError(error));
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

  const maintenanceActions = useSettingsMaintenanceActions({ client, refetchHealth: healthQuery.refetch, setMessage, setSuccessMessage });
  const cleanupSystemLogsMutation = maintenanceActions.cleanupMutation;

  const isBusy =
    settingsQuery.isFetching ||
    saveMutation.isPending ||
    validateSettingsMutation.isPending ||
    testEmailMutation.isPending ||
    inferEmailServerMutation.isPending ||
    cleanupSystemLogsMutation.isPending;
  const secrets = settingsQuery.data?.secrets ?? null;
  const databaseProvider = healthQuery.isError ? null : healthQuery.data?.databaseProviderKey ?? null;
  const issuingAuthorityOptions = issuingAuthoritiesQuery.data?.options ?? [];
  const canSelectDesktopDirectory = isDesktopBridgeAvailable();
  const emailAddressCandidate =
    settings
      ? readSettingString(settings, ["email", "fromAddress"]).trim() || readSettingString(settings, ["email", "userName"]).trim()
      : "";
  const currentCategory = operationMode ?? (availableSettingsCategories.some((category) => category.key === activeCategory)
    ? activeCategory
    : "runtime");
  const activeCategoryConfig =
    allSettingsCategories.find((category) => category.key === currentCategory) ?? settingsCategories[0];

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
      title: "恢复本分类默认设置",
      description: `确定要把“${activeCategoryConfig.label}”的设置草稿恢复为默认值吗？`,
      details: ["此操作只修改当前页面草稿。", "点击保存后才会写入正式配置。", "受保护的密码和密钥不会被直接清空。"],
      confirmLabel: "恢复默认值",
    })) {
      return;
    }

    patchSettings([
      { path: ["system", "appName"], value: productName },
      ...systemDefaultPatches,
    ].filter(({ path }) => settingsCategoryForPath(path) === currentCategory
      && path[1] !== "databaseProvider"
      && (path[1] !== "updaterEndpoint" || isDesktopRuntime)
      && (path[1] !== "sqliteDatabaseFileName" || databaseProvider === "sqlite")
      && (!path[1]?.startsWith("postgreSql") || databaseProvider === "postgresql")));
    setSingleWindowAuthorityAutoState({ fetchPlace: "", aplAdd: "" });
    setMessage(null);
    setSuccessMessage(`已恢复“${activeCategoryConfig.label}”的默认值，请检查后保存。`);
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
      if (hasUnsavedChanges) return;
      await cleanupSystemLogsMutation.mutateAsync();
    } catch {
      // Mutation handlers surface the user-facing error.
    }
  }

  async function handleReloadSettings() {
    if (!await confirmDiscardChanges("重新加载系统设置")) return;
    const response = await settingsQuery.refetch();
    if (!response.data || response.isError) return;
    setSettings(response.data.settings as unknown as SettingsRecord);
    setBaselineSettings(response.data.settings as unknown as SettingsRecord);
    setConcurrencyMessage(null);
    setUpdateSecrets(false);
    setValidationResult(null);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settings || !canManageSettings || isBusy) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    saveMutation.mutate(settings);
  }

  return { setSuccessMessage, message, concurrencyMessage, successMessage, settings, isBusy, settingsQuery, handleReloadSettings, handleSubmit, activeCategoryConfig, hasUnsavedChanges, changedCategories, currentCategory, databaseProvider, updateSecrets, setUpdateSecrets, handleRestoreSystemDefaults, handleValidateSettings, availableSettingsCategories, setActiveCategory, healthQuery, patchSetting, secrets, canSelectDesktopDirectory, handleSelectDefaultExportDirectory, issuingAuthorityOptions, handleSingleWindowOrgCodeChange, handleSingleWindowFetchPlaceChange, handleSingleWindowAplAddChange, patchSettings, emailAddressCandidate, handleInferEmailServerConfig, handleTestEmailConnection, setMessage, handleCleanupSystemLogs, validationResult, handleApplyValidationFix, location, baselineSettings, save: () => { if(settings && canManageSettings && !isBusy) saveMutation.mutate(settings); } };
}
