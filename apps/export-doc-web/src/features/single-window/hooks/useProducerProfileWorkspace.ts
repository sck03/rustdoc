import { useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiCustomsCooProducerProfileDto, ApiCustomsCooProducerProfileInputDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../../ui/ConfirmationProvider.tsx";
import { useServerPageNumber } from "../../../ui/useServerPageNumber.ts";
import { useScopedMutation } from "../../../ui/useScopedMutation.ts";
import { toDraft, normalizeProfileInput, producerProfileDraftEquals, hasProducerIdentity, buildProfileDisplayName, type ProducerProfileDraft } from "../producerProfileModel.ts";

type Command = { type: "save" | "delete" | "create" | "close" | "apply-draft" }
  | { type: "select" | "apply-list"; profile: ApiCustomsCooProducerProfileDto };

export function useProducerProfileWorkspace({ client, currentProfile, rowLabel, onApply, onClose }: {
  client: ExportDocManagerApiClient;
  currentProfile: ApiCustomsCooProducerProfileInputDto;
  rowLabel: string;
  onApply(profile: ApiCustomsCooProducerProfileInputDto): void;
  onClose(): void;
}) {
  const requestConfirmation = useConfirmation();
  const [keyword, setKeyword] = useState("");
  const [committedKeyword, setCommittedKeyword] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const pageSize = 50;
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [draft, setDraft] = useState<ProducerProfileDraft>(() => toDraft(currentProfile));
  const [cleanDraft, setCleanDraft] = useState<ProducerProfileDraft>(() => toDraft(currentProfile));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const profilesQuery = useQuery({
    queryKey: [...queryKeys.singleWindowCustomsCooProducerProfiles(committedKeyword.trim()), pageNumber, pageSize],
    queryFn: ({ signal }) => client.listCustomsCooProducerProfiles({
      keyword: committedKeyword.trim() || undefined, pageNumber, pageSize,
    }, { signal }),
  });
  useServerPageNumber(profilesQuery, pageNumber, setPageNumber, pageSize);
  const isDraftDirty = !producerProfileDraftEquals(draft, cleanDraft);
  useUnsavedChangesGuard({ isDirty: isDraftDirty, message: "生产企业资料有未保存的修改。" });

  function loadProfile(profile: ApiCustomsCooProducerProfileInputDto | ApiCustomsCooProducerProfileDto) {
    const next = toDraft(profile);
    setSelectedId(next.id || null); setDraft(next); setCleanDraft(next);
    setMessage(null); setSuccessMessage(null);
  }
  const commands = useScopedMutation<boolean, Command>(JSON.stringify([rowLabel, currentProfile]), client, {
    mutationFn: async (command, signal, { checkCurrent }) => {
      const confirmInScope = async (options: Parameters<typeof requestConfirmation>[0]) => {
        const accepted = await requestConfirmation(options);
        checkCurrent();
        return accepted;
      };
      const discard = (label: string) => !isDraftDirty || confirmInScope({
        title: "放弃未保存修改", description: `继续${label}会丢失生产企业资料的未保存修改。`,
        confirmLabel: `继续${label}`,
      });
      if (command.type === "save") {
        if (!hasProducerIdentity(draft)) throw new Error("生产企业代码或生产企业名称至少填写一个。");
        const body = { profile: normalizeProfileInput(draft), expectedVersion: draft.id > 0 ? draft.versionNumber : undefined };
        const result = draft.id > 0
          ? await client.updateCustomsCooProducerProfile({ id: draft.id, body }, { signal })
          : await client.createCustomsCooProducerProfile({ body }, { signal });
        checkCurrent(); loadProfile(result.profile);
        setSuccessMessage(result.message || "生产企业资料已保存。");
        return true;
      }
      if (command.type === "delete") {
        if (!selectedId || !await discard("删除资料")) return false;
        if (!await confirmInScope({ title: "删除生产企业资料", description: `确定要删除生产企业资料“${buildProfileDisplayName(draft)}”吗？`, confirmLabel: "确认删除", tone: "danger" })) return false;
        await client.deleteCustomsCooProducerProfile({ id: draft.id, expectedVersion: draft.versionNumber }, { signal });
        checkCurrent(); loadProfile(currentProfile); setSuccessMessage("生产企业资料已删除。");
        return true;
      }
      if (command.type === "apply-draft") {
        if (!hasProducerIdentity(draft) && !draft.producer.trim()) throw new Error("当前编辑区没有可回填的生产企业资料。");
        if (isDraftDirty && !await confirmInScope({ title: "套用未保存资料", description: "生产企业资料有未保存的修改。", details: ["继续套用只会回填当前货项，不会保存到资料库。"], confirmLabel: "继续套用" })) return false;
        checkCurrent(); onApply(normalizeProfileInput(draft));
        return false;
      }
      const labels = { create: "新增资料", close: "关闭弹窗", select: "切换资料", "apply-list": "套用列表资料" };
      if (!await discard(labels[command.type])) return false;
      checkCurrent();
      if (command.type === "close") onClose();
      else if (command.type === "apply-list") onApply(normalizeProfileInput(command.profile));
      else loadProfile(command.type === "select" ? command.profile : currentProfile);
      return false;
    },
    refreshKeys: changed => changed ? [queryKeys.singleWindowCustomsCooProducerProfilesRoot()] : [],
    onError: error => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });
  const isBusy = profilesQuery.isFetching || commands.isPending;
  const run = (command: Command) => { if (!profilesQuery.isFetching) commands.mutate(command); };
  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isBusy) return;
    setCommittedKeyword(keyword.trim()); setPageNumber(1);
  }
  return {
    keyword, setKeyword, pageNumber, setPageNumber, pageSize, selectedId, draft, setDraft,
    message, successMessage, profilesQuery, profiles: profilesQuery.data?.items ?? [],
    isBusy, selectedCount: selectedId ? 1 : 0, handleSearch,
    beginCreate: () => run({ type: "create" }),
    selectProfile: (profile: ApiCustomsCooProducerProfileDto) => run({ type: "select", profile }),
    handleClose: () => run({ type: "close" }),
    handleSave: (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); run({ type: "save" }); },
    handleDelete: () => run({ type: "delete" }),
    handleApplyDraft: () => run({ type: "apply-draft" }),
    applyListProfile: (profile: ApiCustomsCooProducerProfileDto) => run({ type: "apply-list", profile }),
  };
}
