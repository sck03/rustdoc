import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2, Copy, CreditCard, FolderOpen, Plus, RefreshCw, Save, ShieldCheck } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import type { ApiSingleWindowClientProfileDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectDirectory } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { writeClipboardText } from "../../ui/clipboard.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";

const NEW_PROFILE_KEY = "__new__";

export function SingleWindowStationProfilePanel({
  client,
  canOperate,
}: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
}) {
  const queryClient = useQueryClient();
  const [selectedProfileKey, setSelectedProfileKey] = useState("");
  const [profileName, setProfileName] = useState("");
  const [companyScope, setCompanyScope] = useState("");
  const [cardIdentifier, setCardIdentifier] = useState("");
  const [customsCooClientRootPath, setCustomsCooClientRootPath] = useState("");
  const [agentConsignmentClientRootPath, setAgentConsignmentClientRootPath] = useState("");
  const [canSubmitCustomsCoo, setCanSubmitCustomsCoo] = useState(true);
  const [canSubmitAgentConsignment, setCanSubmitAgentConsignment] = useState(true);
  const [message, setMessage] = useState<string | null>(null);
  const [messageKind, setMessageKind] = useState<"success" | "error">("success");
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);
  const loadedProfileKeyRef = useRef("");

  const profilesQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfiles(),
    queryFn: () => client.getSingleWindowClientProfiles(),
    staleTime: 60_000,
  });

  const profiles = profilesQuery.data?.profiles ?? [];
  const activeProfile = profiles.find((profile) => profile.isActive)
    ?? profiles.find((profile) => profile.profileKey === profilesQuery.data?.activeProfileKey)
    ?? null;
  const selectedProfile = useMemo(
    () => profiles.find((profile) => profile.profileKey === selectedProfileKey) ?? null,
    [profiles, selectedProfileKey],
  );
  const currentDraftSnapshot = JSON.stringify({
    profileName,
    companyScope,
    cardIdentifier,
    customsCooClientRootPath,
    agentConsignmentClientRootPath,
    canSubmitCustomsCoo,
    canSubmitAgentConsignment,
  });
  const persistedDraftSnapshot = JSON.stringify(selectedProfile ? {
    profileName: selectedProfile.profileName ?? "",
    companyScope: selectedProfile.companyScope ?? "",
    cardIdentifier: selectedProfile.cardIdentifier ?? "",
    customsCooClientRootPath: selectedProfile.customsCooClientRootPath ?? "",
    agentConsignmentClientRootPath: selectedProfile.agentConsignmentClientRootPath ?? "",
    canSubmitCustomsCoo: selectedProfile.canSubmitCustomsCoo,
    canSubmitAgentConsignment: selectedProfile.canSubmitAgentConsignment,
  } : {
    profileName: "",
    companyScope: "",
    cardIdentifier: "",
    customsCooClientRootPath: "",
    agentConsignmentClientRootPath: "",
    canSubmitCustomsCoo: true,
    canSubmitAgentConsignment: true,
  });
  const hasUnsavedProfileChanges = canOperate && currentDraftSnapshot !== persistedDraftSnapshot;
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedProfileChanges,
    message: "当前持卡机公司与操作卡档案有未保存的修改。",
  });

  useEffect(() => {
    if (!profilesQuery.data) return;
    if (profiles.length === 0) {
      if (selectedProfileKey !== NEW_PROFILE_KEY) setSelectedProfileKey(NEW_PROFILE_KEY);
      return;
    }
    if (selectedProfileKey === NEW_PROFILE_KEY) return;
    if (!selectedProfileKey || !profiles.some((profile) => profile.profileKey === selectedProfileKey)) {
      setSelectedProfileKey(activeProfile?.profileKey ?? profiles[0]?.profileKey ?? NEW_PROFILE_KEY);
    }
  }, [activeProfile?.profileKey, profiles, profiles.length, profilesQuery.data, selectedProfileKey]);

  useEffect(() => {
    const draftKey = selectedProfile?.profileKey ?? (selectedProfileKey === NEW_PROFILE_KEY ? NEW_PROFILE_KEY : "");
    if (draftKey && loadedProfileKeyRef.current !== draftKey) {
      loadedProfileKeyRef.current = draftKey;
      if (selectedProfile) loadProfile(selectedProfile);
      else resetDraft();
      return;
    }
    if (hasUnsavedProfileChanges) return;
    if (!selectedProfile) {
      if (selectedProfileKey === NEW_PROFILE_KEY) resetDraft();
      return;
    }

    loadProfile(selectedProfile);
  }, [hasUnsavedProfileChanges, selectedProfile, selectedProfileKey]);

  const saveMutation = useMutation({
    mutationFn: () => client.saveSingleWindowClientProfile({
      body: {
        profileKey: selectedProfile?.profileKey ?? "",
        profileName: profileName.trim(),
        companyScope: companyScope.trim(),
        cardIdentifier: cardIdentifier.trim(),
        customsCooClientRootPath: customsCooClientRootPath.trim(),
        agentConsignmentClientRootPath: agentConsignmentClientRootPath.trim(),
        canSubmitCustomsCoo,
        canSubmitAgentConsignment,
      },
    }),
    onSuccess: async (response) => {
      queryClient.setQueryData(queryKeys.singleWindowClientProfiles(), response);
      const savedProfile = response.profiles.find((profile) => profile.profileKey === response.activeProfileKey)
        ?? response.profiles[0]
        ?? null;
      const nextProfileKey = savedProfile?.profileKey ?? NEW_PROFILE_KEY;
      loadedProfileKeyRef.current = nextProfileKey;
      setSelectedProfileKey(nextProfileKey);
      if (savedProfile) loadProfile(savedProfile);
      else resetDraft();
      setMessage(response.message || "操作档案已保存并启用。");
      setMessageKind("success");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowClientProfiles() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageKind("error");
    },
  });

  const activateMutation = useMutation({
    mutationFn: (profileKey: string) => client.activateSingleWindowClientProfile({ profileKey }),
    onSuccess: async (response) => {
      queryClient.setQueryData(queryKeys.singleWindowClientProfiles(), response);
      const activatedProfile = response.profiles.find((profile) => profile.profileKey === response.activeProfileKey) ?? null;
      loadedProfileKeyRef.current = activatedProfile?.profileKey ?? response.activeProfileKey;
      setSelectedProfileKey(response.activeProfileKey);
      if (activatedProfile) loadProfile(activatedProfile);
      setMessage(response.message || "当前操作档案已切换。");
      setMessageKind("success");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowClientProfiles() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageKind("error");
    },
  });

  const isBusy = profilesQuery.isFetching || saveMutation.isPending || activateMutation.isPending;
  const identityLocked = Boolean(selectedProfile);
  const canSaveProfile = Boolean(
    profileName.trim() &&
    companyScope.trim() &&
    cardIdentifier.trim() &&
    (canSubmitCustomsCoo || canSubmitAgentConsignment),
  );

  function resetDraft() {
    setProfileName("");
    setCompanyScope("");
    setCardIdentifier("");
    setCustomsCooClientRootPath("");
    setAgentConsignmentClientRootPath("");
    setCanSubmitCustomsCoo(true);
    setCanSubmitAgentConsignment(true);
  }

  function loadProfile(profile: ApiSingleWindowClientProfileDto) {
    setProfileName(profile.profileName ?? "");
    setCompanyScope(profile.companyScope ?? "");
    setCardIdentifier(profile.cardIdentifier ?? "");
    setCustomsCooClientRootPath(profile.customsCooClientRootPath ?? "");
    setAgentConsignmentClientRootPath(profile.agentConsignmentClientRootPath ?? "");
    setCanSubmitCustomsCoo(profile.canSubmitCustomsCoo);
    setCanSubmitAgentConsignment(profile.canSubmitAgentConsignment);
  }

  async function chooseRoot(kind: "coo" | "acd") {
    if (!canOperate) return;
    try {
      const current = kind === "coo" ? customsCooClientRootPath : agentConsignmentClientRootPath;
      const selected = await selectDirectory(current);
      if (!selected) return;
      if (kind === "coo") setCustomsCooClientRootPath(selected);
      else setAgentConsignmentClientRootPath(selected);
      setDesktopMessage(null);
      setMessage(null);
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  async function changeSelectedProfile(nextProfileKey: string) {
    if (nextProfileKey === selectedProfileKey) return;
    if (hasUnsavedProfileChanges && !await confirmDiscardChanges("切换操作档案")) return;
    setSelectedProfileKey(nextProfileKey);
    setMessage(null);
    setDesktopMessage(null);
  }

  async function startNewProfile() {
    if (hasUnsavedProfileChanges && !await confirmDiscardChanges("新建操作档案")) return;
    setSelectedProfileKey(NEW_PROFILE_KEY);
    resetDraft();
    setMessage(null);
  }

  async function refreshProfiles() {
    if (hasUnsavedProfileChanges && !await confirmDiscardChanges("刷新操作档案")) return;
    if (selectedProfile) loadProfile(selectedProfile);
    else resetDraft();

    const refreshed = await profilesQuery.refetch();
    const refreshedProfiles = refreshed.data?.profiles ?? [];
    const refreshedProfile = selectedProfileKey === NEW_PROFILE_KEY
      ? null
      : refreshedProfiles.find((profile) => profile.profileKey === selectedProfileKey)
        ?? refreshedProfiles.find((profile) => profile.isActive)
        ?? refreshedProfiles.find((profile) => profile.profileKey === refreshed.data?.activeProfileKey)
        ?? refreshedProfiles[0]
        ?? null;
    const nextProfileKey = refreshedProfile?.profileKey ?? NEW_PROFILE_KEY;
    loadedProfileKeyRef.current = nextProfileKey;
    setSelectedProfileKey(nextProfileKey);
    if (refreshedProfile) loadProfile(refreshedProfile);
    else resetDraft();
  }

  async function activateProfile(profileKey: string) {
    if (hasUnsavedProfileChanges && !await confirmDiscardChanges("切换当前操作卡")) return;
    activateMutation.mutate(profileKey);
  }

  async function copyAssignmentCode() {
    const code = selectedProfile?.stationAssignmentCode?.trim() ?? "";
    if (!code) {
      setMessage("当前档案尚未生成持卡机授权码，请先保存档案。");
      setMessageKind("error");
      return;
    }

    if (await writeClipboardText(code)) {
      setMessage("持卡机授权码已复制。请只交给负责为该档案制单的办公室系统。");
      setMessageKind("success");
    } else {
      setMessage("浏览器未允许写入剪贴板，请手动选择授权码复制。");
      setMessageKind("error");
    }
  }

  return (
    <section className="form-section single-window-station-profile" aria-label="本机持卡机操作档案">
      <div className="section-header">
        <div>
          <h2>公司与操作卡档案</h2>
          <span>同一台持卡机可维护任意多个抬头和操作卡；换卡前先切换当前档案</span>
        </div>
        <div className="toolbar-actions">
          <span className={activeProfile ? "status-pill status-success" : "status-pill status-warning"}>
            {activeProfile ? "当前档案已启用" : "尚未配置"}
          </span>
          <button
            className="icon-button"
            type="button"
            title="刷新操作档案"
            aria-label="刷新操作档案"
            disabled={isBusy}
            onClick={() => void refreshProfiles()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canOperate || isBusy}
            onClick={() => void startNewProfile()}
          >
            <Plus size={17} aria-hidden="true" />
            <span>新增档案</span>
          </button>
          <button
            className="command-button"
            type="button"
            disabled={!canOperate || isBusy || !canSaveProfile}
            onClick={() => saveMutation.mutate()}
          >
            <Save size={17} aria-hidden="true" />
            <span>保存并启用</span>
          </button>
        </div>
      </div>

      {!canOperate ? <PermissionNotice>当前权限仅允许查看操作档案，不能修改公司、操作卡或目录。</PermissionNotice> : null}
      {profilesQuery.isError ? <InlineNotice tone="error" title="操作档案加载失败">{readApiError(profilesQuery.error)}</InlineNotice> : null}
      {message ? <InlineNotice tone={messageKind}>{message}</InlineNotice> : null}
      {desktopMessage ? <InlineNotice tone="error">{desktopMessage}</InlineNotice> : null}

      <div className="single-window-station-summary">
        <span><ShieldCheck size={16} aria-hidden="true" />本机独立运行</span>
        <span><Building2 size={16} aria-hidden="true" />{activeProfile?.companyScope || "未选择公司"}</span>
        <span><CreditCard size={16} aria-hidden="true" />{activeProfile?.cardIdentifier || "未选择操作卡"}</span>
        <span>{profiles.length} 个档案</span>
      </div>

      <div className="field-grid single-window-station-fields">
        <label className="form-field">
          <span className="form-field-label"><span>正在编辑</span></span>
          <select
            aria-label="选择操作档案"
            value={selectedProfileKey}
            disabled={isBusy}
            onChange={(event) => void changeSelectedProfile(event.target.value)}
          >
            <option value={NEW_PROFILE_KEY}>新建操作档案</option>
            {profiles.map((profile) => (
              <option key={profile.profileKey} value={profile.profileKey}>
                {profile.isActive ? "当前 · " : ""}{profile.profileName} · {profile.companyScope}
              </option>
            ))}
          </select>
        </label>
        <TextField label="档案名称" value={profileName} required disabled={!canOperate || isBusy} onChange={setProfileName} />
        <TextField label="公司抬头" value={companyScope} required disabled={!canOperate || isBusy || identityLocked} onChange={setCompanyScope} />
        <TextField
          label="操作卡标识"
          value={cardIdentifier}
          required
          disabled={!canOperate || isBusy || identityLocked}
          description="填写卡片编号、公司简称或内部资产编号；系统不保存卡密码。"
          onChange={setCardIdentifier}
        />
      </div>

      {identityLocked ? (
        <InlineNotice tone="info" title="档案身份保持固定">
          已有档案的公司抬头和操作卡标识不可原地更换。换公司、换卡或同机操作另一抬头时，请使用“新增档案”，以免历史批次归属混淆。
        </InlineNotice>
      ) : null}

      {selectedProfile && !selectedProfile.isActive ? (
        <div className="toolbar-actions single-window-profile-switch-actions">
          <button
            className="command-button secondary"
            type="button"
            disabled={!canOperate || isBusy}
            onClick={() => void activateProfile(selectedProfile.profileKey)}
          >
            <CreditCard size={17} aria-hidden="true" />
            <span>切换为当前操作卡</span>
          </button>
        </div>
      ) : null}

      {selectedProfile ? (
        <section className="single-window-assignment-card" aria-label="持卡机预分派授权码">
          <div className="section-header compact-section-header">
            <div>
              <h3>办公室预分派授权码</h3>
              <span>用于把提交包锁定到本机当前公司、操作档案和操作卡</span>
            </div>
            <button
              className="command-button secondary"
              type="button"
              disabled={!canOperate || isBusy || !selectedProfile.stationAssignmentCode}
              onClick={() => void copyAssignmentCode()}
            >
              <Copy size={16} aria-hidden="true" />
              <span>复制授权码</span>
            </button>
          </div>
          <label className="form-field">
            <span className="form-field-label"><span>授权码</span></span>
            <textarea
              rows={3}
              readOnly
              spellCheck={false}
              value={selectedProfile.stationAssignmentCode ?? ""}
              aria-label="当前操作档案的持卡机预分派授权码"
            />
            <small>此码包含交接认证凭据，作用类似钥匙。换卡或换公司请创建新档案并使用新授权码，不要发到公共群聊或写入普通说明文档。</small>
          </label>
          <div className="detail-grid single-window-assignment-details">
            <div className="detail-item"><span>持卡机</span><strong>{selectedProfile.stationKey}</strong></div>
            <div className="detail-item"><span>操作档案</span><strong>{selectedProfile.profileName}</strong></div>
            <div className="detail-item"><span>公司抬头</span><strong>{selectedProfile.companyScope}</strong></div>
            <div className="detail-item"><span>操作卡</span><strong>{selectedProfile.cardIdentifier}</strong></div>
          </div>
        </section>
      ) : null}

      <div className="single-window-capability-grid">
        <label className="single-window-capability-card">
          <input type="checkbox" checked={canSubmitCustomsCoo} disabled={!canOperate || isBusy} onChange={(event) => setCanSubmitCustomsCoo(event.target.checked)} />
          <span><strong>海关原产地证</strong><small>COO 提交与回执目录</small></span>
        </label>
        <PathField
          label="COO 交接目录（官方客户端或手工导入）"
          value={customsCooClientRootPath}
          disabled={!canOperate || isBusy || !canSubmitCustomsCoo}
          description="留空时在业务数据目录中为该档案创建交接目录；官方客户端不会自动导入，请按客户端要求完成操作。"
          actions={<>
            <DesktopIconButton title="选择 COO 交接目录" disabled={!canOperate || isBusy || !canSubmitCustomsCoo} onClick={() => void chooseRoot("coo")}><FolderOpen size={17} aria-hidden="true" /></DesktopIconButton>
            {renderOpenPathAction(customsCooClientRootPath, "打开 COO 交接目录", setDesktopMessage)}
          </>}
          onChange={setCustomsCooClientRootPath}
        />
        <label className="single-window-capability-card">
          <input type="checkbox" checked={canSubmitAgentConsignment} disabled={!canOperate || isBusy} onChange={(event) => setCanSubmitAgentConsignment(event.target.checked)} />
          <span><strong>报关代理委托</strong><small>ACD 提交与回执目录</small></span>
        </label>
        <PathField
          label="ACD 交接目录（官方客户端或手工导入）"
          value={agentConsignmentClientRootPath}
          disabled={!canOperate || isBusy || !canSubmitAgentConsignment}
          description="与 COO 及其他公司档案目录必须完全分开；留空会创建受管交接目录，仍需人工确认官方客户端导入。"
          actions={<>
            <DesktopIconButton title="选择 ACD 交接目录" disabled={!canOperate || isBusy || !canSubmitAgentConsignment} onClick={() => void chooseRoot("acd")}><FolderOpen size={17} aria-hidden="true" /></DesktopIconButton>
            {renderOpenPathAction(agentConsignmentClientRootPath, "打开 ACD 交接目录", setDesktopMessage)}
          </>}
          onChange={setAgentConsignmentClientRootPath}
        />
      </div>
    </section>
  );
}
