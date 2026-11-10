import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiSingleWindowClientProfileDto, ApiSingleWindowClientProfileSaveRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { writeClipboardText } from "../../ui/clipboard.ts";
import { emptyClientDirectories } from "./SingleWindowClientDirectories.tsx";

export const NEW_PROFILE_KEY = "__new__";
function draftFrom(profile?: ApiSingleWindowClientProfileDto): ApiSingleWindowClientProfileSaveRequest {
  return {
    profileKey: profile?.profileKey ?? "",
    expectedVersion: profile?.versionNumber ?? 0,
    profileName: profile?.profileName ?? "",
    companyScope: profile?.companyScope ?? "",
    cardIdentifier: profile?.cardIdentifier ?? "",
    customsCooDirectories: profile?.customsCooDirectories ?? emptyClientDirectories(),
    agentConsignmentDirectories: profile?.agentConsignmentDirectories ?? emptyClientDirectories(),
    canSubmitCustomsCoo: profile?.canSubmitCustomsCoo ?? true,
    canSubmitAgentConsignment: profile?.canSubmitAgentConsignment ?? true,
  };
}

/** Profile identity, draft baseline and commands share one cancellable workspace. */
export function useSingleWindowProfiles(client: ExportDocManagerApiClient, canOperate: boolean) {
  const cache = useQueryClient();
  const [selectedProfileKey, setSelectedProfileKey] = useState("");
  const [draft, setDraft] = useState(draftFrom);
  const [baseline, setBaseline] = useState(draftFrom);
  const [message, setMessage] = useState<string | null>(null);
  const [messageKind, setMessageKind] = useState<"success" | "error">("success");
  const loaded = useRef({ key: "", client });
  const scopeKey = (key: string) => `${canOperate}:${key}`;
  const operation = useScopedSave(scopeKey(selectedProfileKey), client);
  const profilesQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfiles(),
    queryFn: ({ signal }) => client.getSingleWindowClientProfiles({ signal }),
    staleTime: 60_000,
  });
  const profiles = profilesQuery.data?.profiles ?? [];
  const activeProfile = profiles.find(profile => profile.isActive) ?? null;
  const selectedProfile = profiles.find(profile => profile.profileKey === selectedProfileKey) ?? null;
  const isDirty = JSON.stringify(draft) !== JSON.stringify(baseline);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty, message: "当前持卡机公司与操作卡档案有未保存的修改。",
  });
  function load(profile?: ApiSingleWindowClientProfileDto) {
    const next = draftFrom(profile);
    setDraft(next);
    setBaseline(next);
  }
  useEffect(() => {
    if (!profilesQuery.data) return;
    if (!selectedProfileKey) {
      setSelectedProfileKey(activeProfile?.profileKey ?? profiles[0]?.profileKey ?? NEW_PROFILE_KEY);
      return;
    }
    if (loaded.current.key !== selectedProfileKey || loaded.current.client !== client) {
      loaded.current = { key: selectedProfileKey, client };
      load(selectedProfile ?? undefined);
      setMessage(null);
    } else if (!isDirty && selectedProfile && selectedProfile.versionNumber !== baseline.expectedVersion) {
      load(selectedProfile);
    }
  }, [client, profilesQuery.data, selectedProfileKey, selectedProfile, activeProfile, isDirty, baseline.expectedVersion]);

  function showError(error: unknown) {
    if (isAbortError(error)) return;
    setMessage(readApiError(error));
    setMessageKind("error");
  }
  async function run(action: Parameters<typeof operation.run>[0]) {
    try { await operation.run(action); } catch (error) { showError(error); }
  }
  function accept(response: Awaited<ReturnType<typeof client.getSingleWindowClientProfiles>>, profile?: ApiSingleWindowClientProfileDto) {
    cache.setQueryData(queryKeys.singleWindowClientProfiles(), response);
    const key = profile?.profileKey ?? NEW_PROFILE_KEY;
    loaded.current = { key, client };
    setSelectedProfileKey(key);
    load(profile);
  }
  const save = () => run(async ({ signal, checkCurrent, continueInScope }) => {
    if (!canOperate) return;
    const response = await client.saveSingleWindowClientProfile({ body: {
      ...draft, profileName: draft.profileName.trim(), companyScope: draft.companyScope.trim(), cardIdentifier: draft.cardIdentifier.trim(),
    } }, { signal });
    checkCurrent();
    const saved = response.profiles.find(profile => profile.profileKey === response.savedProfileKey);
    if (!saved) throw new Error("保存响应缺少操作档案，请刷新后核对。");
    continueInScope(scopeKey(saved.profileKey));
    accept(response, saved);
    setMessage("操作档案已保存。");
    setMessageKind("success");
  });
  const changeSelectedProfile = (key: string) => run(async ({ checkCurrent, continueInScope }) => {
    if (key === selectedProfileKey) return;
    if (isDirty && !await confirmDiscardChanges("切换操作档案")) return;
    checkCurrent();
    continueInScope(scopeKey(key));
    setSelectedProfileKey(key);
    setMessage(null);
  });
  const refreshProfiles = () => run(async ({ signal, checkCurrent, continueInScope }) => {
    if (isDirty && !await confirmDiscardChanges("刷新操作档案")) return;
    checkCurrent();
    const response = await client.getSingleWindowClientProfiles({ signal });
    checkCurrent();
    const selected = selectedProfileKey === NEW_PROFILE_KEY ? undefined :
      response.profiles.find(profile => profile.profileKey === selectedProfileKey) ??
      response.profiles.find(profile => profile.isActive) ?? response.profiles[0];
    continueInScope(scopeKey(selected?.profileKey ?? NEW_PROFILE_KEY));
    accept(response, selected);
    setMessage(null);
  });
  const activateProfile = (profileKey: string) => run(async ({ signal, checkCurrent, continueInScope }) => {
    if (!canOperate) return;
    if (isDirty && !await confirmDiscardChanges("切换当前操作卡")) return;
    checkCurrent();
    const response = await client.activateSingleWindowClientProfile({ profileKey }, { signal });
    checkCurrent();
    const selected = response.profiles.find(profile => profile.profileKey === response.activeProfileKey);
    if (!selected) throw new Error("启用响应缺少当前操作档案，请刷新后核对。");
    continueInScope(scopeKey(selected.profileKey));
    accept(response, selected);
    setMessage("当前操作档案已切换。");
    setMessageKind("success");
  });
  const copyAssignmentCode = () => run(async ({ checkCurrent }) => {
    const code = selectedProfile?.stationAssignmentCode?.trim();
    if (!code) throw new Error("请先保存操作档案。");
    const copied = await writeClipboardText(code);
    checkCurrent();
    setMessage(copied ? "授权码已复制，请只交给负责制单的办公室系统。" : "浏览器未允许写入剪贴板，请手动选择授权码复制。");
    setMessageKind(copied ? "success" : "error");
  });
  return {
    draft, profiles, profilesQuery, selectedProfile, selectedProfileKey, activeProfile, message, messageKind,
    isBusy: profilesQuery.isFetching || operation.isSaving,
    identityLocked: Boolean(selectedProfile),
    canSaveProfile: Boolean(draft.profileName.trim() && draft.companyScope.trim() && draft.cardIdentifier.trim() &&
      (draft.canSubmitCustomsCoo || draft.canSubmitAgentConsignment)),
    change: <K extends keyof typeof draft>(key: K, value: typeof draft[K]) => setDraft(current => ({ ...current, [key]: value })),
    save, changeSelectedProfile, refreshProfiles, activateProfile, copyAssignmentCode,
    startNewProfile: () => changeSelectedProfile(NEW_PROFILE_KEY),
  };
}
