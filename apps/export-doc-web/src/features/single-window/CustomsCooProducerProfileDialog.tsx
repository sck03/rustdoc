import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Pencil, Plus, RefreshCw, Save, Search, Trash2, X } from "lucide-react";
import {
  ApiCustomsCooProducerProfileDto,
  ApiCustomsCooProducerProfileInputDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";

type ProducerProfileDraft = ApiCustomsCooProducerProfileInputDto & {
  id: number;
};

export function CustomsCooProducerProfileDialog({
  client,
  currentProfile,
  rowLabel,
  onApply,
  onClose,
}: {
  client: ExportDocManagerApiClient;
  currentProfile: ApiCustomsCooProducerProfileInputDto;
  rowLabel: string;
  onApply: (profile: ApiCustomsCooProducerProfileInputDto) => void;
  onClose: () => void;
}) {
  const requestConfirmation = useConfirmation();
  const queryClient = useQueryClient();
  const [keyword, setKeyword] = useState("");
  const [committedKeyword, setCommittedKeyword] = useState("");
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [draft, setDraft] = useState<ProducerProfileDraft>(() => toDraft(currentProfile));
  const [cleanDraft, setCleanDraft] = useState<ProducerProfileDraft>(() => toDraft(currentProfile));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const profilesQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooProducerProfiles(committedKeyword.trim()),
    queryFn: () =>
      client.listCustomsCooProducerProfiles({
        keyword: committedKeyword.trim() || undefined,
      }),
  });

  const saveMutation = useMutation({
    mutationFn: (nextDraft: ProducerProfileDraft) => {
      const body = { profile: normalizeProfileInput(nextDraft) };
      return nextDraft.id > 0
        ? client.updateCustomsCooProducerProfile({ id: nextDraft.id, body })
        : client.createCustomsCooProducerProfile({ body });
    },
    onSuccess: async (response) => {
      const savedDraft = toDraft(response.profile);
      setSelectedId(response.id);
      setDraft(savedDraft);
      setCleanDraft(savedDraft);
      setMessage(null);
      setSuccessMessage(response.message || "生产企业资料已保存。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowCustomsCooProducerProfilesRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => client.deleteCustomsCooProducerProfile({ id }),
    onSuccess: async () => {
      const nextDraft = toDraft(currentProfile);
      setSelectedId(null);
      setDraft(nextDraft);
      setCleanDraft(nextDraft);
      setMessage(null);
      setSuccessMessage("生产企业资料已删除。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowCustomsCooProducerProfilesRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const profiles = profilesQuery.data?.items ?? [];
  const isBusy = profilesQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;
  const selectedCount = selectedId ? 1 : 0;
  const isDraftDirty = !producerProfileDraftEquals(draft, cleanDraft);

  useUnsavedChangesGuard({
    isDirty: isDraftDirty,
    message: "生产企业资料有未保存的修改。",
  });

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCommittedKeyword(keyword.trim());
  }

  async function beginCreate() {
    if (!await confirmDiscardDraft("新增资料")) {
      return;
    }

    const nextDraft = toDraft(currentProfile);
    setSelectedId(null);
    setDraft(nextDraft);
    setCleanDraft(nextDraft);
    setMessage(null);
    setSuccessMessage(null);
  }

  async function selectProfile(profile: ApiCustomsCooProducerProfileDto) {
    const isSameProfile = selectedId === profile.id && draft.id === profile.id;
    const actionLabel = isSameProfile ? "重新载入当前资料" : "切换资料";
    if (!await confirmDiscardDraft(actionLabel)) {
      return;
    }

    loadProfile(profile);
  }

  function loadProfile(profile: ApiCustomsCooProducerProfileDto) {
    const nextDraft = toDraft(profile);
    setSelectedId(profile.id);
    setDraft(nextDraft);
    setCleanDraft(nextDraft);
    setMessage(null);
    setSuccessMessage(null);
  }

  async function handleClose() {
    if (await confirmDiscardDraft("关闭弹窗")) {
      onClose();
    }
  }

  function handleSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!hasProducerIdentity(draft)) {
      setMessage("生产企业代码或生产企业名称至少填写一个。");
      setSuccessMessage(null);
      return;
    }

    saveMutation.mutate(draft);
  }

  async function handleDelete() {
    if (!selectedId || selectedId <= 0 || isBusy) {
      return;
    }

    if (!await confirmDiscardDraft("删除资料")) {
      return;
    }

    const displayName = buildProfileDisplayName(draft);
    if (!await requestConfirmation({ title: "删除生产企业资料", description: `确定要删除生产企业资料“${displayName}”吗？`, confirmLabel: "确认删除", tone: "danger" })) {
      return;
    }

    deleteMutation.mutate(selectedId);
  }

  async function handleApplyDraft() {
    if (!hasProducerIdentity(draft) && !draft.producer.trim()) {
      setMessage("当前编辑区没有可回填的生产企业资料。");
      setSuccessMessage(null);
      return;
    }

    if (isDraftDirty && !await requestConfirmation({ title: "套用未保存资料", description: "生产企业资料有未保存的修改。", details: ["继续套用只会回填当前货项，不会保存到资料库。"], confirmLabel: "继续套用" })) {
      return;
    }

    onApply(normalizeProfileInput(draft));
  }

  async function applyListProfile(profile: ApiCustomsCooProducerProfileDto) {
    if (!await confirmDiscardDraft("套用列表资料")) {
      return;
    }

    onApply(normalizeProfileInput(toDraft(profile)));
  }

  async function confirmDiscardDraft(actionLabel: string) {
    if (!isDraftDirty) {
      return true;
    }

    return requestConfirmation({ title: "放弃未保存修改", description: `继续${actionLabel}会丢失生产企业资料的未保存修改。`, confirmLabel: `继续${actionLabel}` });
  }

  return (
    <div className="single-window-lock-backdrop" role="presentation">
      <div
        className="single-window-lock-dialog producer-profile-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="producer-profile-title"
      >
        <header className="single-window-lock-header producer-profile-header">
          <div className="single-window-lock-title">
            <Pencil size={18} aria-hidden="true" />
            <h2 id="producer-profile-title">生产企业资料</h2>
            <span>{profiles.length}</span>
          </div>
          <button className="icon-button" type="button" title="关闭" aria-label="关闭" onClick={handleClose} disabled={isBusy}>
            <X size={18} aria-hidden="true" />
          </button>
        </header>

        <div className="producer-profile-toolbar">
          <form className="producer-profile-search" onSubmit={handleSearch}>
            <input
              aria-label="生产企业资料关键字"
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="代码、名称、联系人、电话、款号"
            />
            <button className="command-button secondary" type="submit" disabled={isBusy}>
              <Search size={16} aria-hidden="true" />
              <span>搜索</span>
            </button>
            <button
              className="icon-button"
              type="button"
              title="刷新" aria-label="刷新"
              disabled={isBusy}
              onClick={() => void profilesQuery.refetch()}
            >
              <RefreshCw size={17} aria-hidden="true" />
            </button>
          </form>
          <div className="producer-profile-target">
            <span>当前货项</span>
            <strong>{rowLabel}</strong>
          </div>
        </div>

        {profilesQuery.isError || message ? <InlineNotice tone="error" title="生产企业资料操作失败">{profilesQuery.isError ? readApiError(profilesQuery.error) : message}</InlineNotice> : null}
        {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}

        <div className="producer-profile-layout">
          <ResponsiveTableFrame className="producer-profile-table-frame" label="生产企业资料">
            <table className="producer-profile-table">
              <thead>
                <tr>
                  <th>操作</th>
                  <th>代码</th>
                  <th>名称</th>
                  <th>联系人</th>
                  <th>电话</th>
                  <th>最近发票</th>
                  <th>最近款号</th>
                </tr>
              </thead>
              <tbody>
                {profiles.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="empty-cell small-empty">
                      暂无生产企业资料
                    </td>
                  </tr>
                ) : (
                  profiles.map((profile) => (
                    <tr
                      key={profile.id}
                      className={selectedId === profile.id ? "producer-profile-row-selected" : undefined}
                      onClick={() => selectProfile(profile)}
                    >
                      <td className="item-action-cell">
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="套用到当前货项" aria-label="套用到当前货项"
                          disabled={isBusy}
                          onClick={(event) => {
                            event.stopPropagation();
                            applyListProfile(profile);
                          }}
                        >
                          <Check size={16} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="编辑资料" aria-label="编辑资料"
                          disabled={isBusy}
                          onClick={(event) => {
                            event.stopPropagation();
                            selectProfile(profile);
                          }}
                        >
                          <Pencil size={16} aria-hidden="true" />
                        </button>
                      </td>
                      <td className="strong-cell">{readProfileValue(profile.ciqRegNo)}</td>
                      <td className="message-cell" title={profile.prdcEtpsName}>
                        {readProfileValue(profile.prdcEtpsName)}
                      </td>
                      <td>{readProfileValue(profile.prdcEtpsConcEr)}</td>
                      <td>{readProfileValue(profile.prdcEtpsTel)}</td>
                      <td>{readProfileValue(profile.lastInvoiceNo)}</td>
                      <td>{readProfileValue(profile.lastSourceStyleNo)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </ResponsiveTableFrame>

          <form className="producer-profile-editor" id="producer-profile-form" onSubmit={handleSave} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
            <div className="producer-profile-editor-header">
              <strong>{selectedId ? `编辑 #${selectedId}` : "新资料"}</strong>
              <span>已选 {selectedCount}</span>
            </div>
            <div className="producer-profile-editor-grid">
              <ProfileTextInput label="生产企业代码" value={draft.ciqRegNo} disabled={isBusy} onChange={(value) => setDraft({ ...draft, ciqRegNo: value })} />
              <ProfileTextInput label="生产企业名称" value={draft.prdcEtpsName} disabled={isBusy} onChange={(value) => setDraft({ ...draft, prdcEtpsName: value })} />
              <ProfileTextInput label="联系人" value={draft.prdcEtpsConcEr} disabled={isBusy} onChange={(value) => setDraft({ ...draft, prdcEtpsConcEr: value })} />
              <ProfileTextInput label="联系电话" value={draft.prdcEtpsTel} disabled={isBusy} onChange={(value) => setDraft({ ...draft, prdcEtpsTel: value })} />
              <ProfileTextarea label="生产商描述" value={draft.producer} disabled={isBusy} onChange={(value) => setDraft({ ...draft, producer: value })} />
              <ProfileTextInput label="生产商电话" value={draft.producerTel} disabled={isBusy} onChange={(value) => setDraft({ ...draft, producerTel: value })} />
              <ProfileTextInput label="生产商传真" value={draft.producerFax} disabled={isBusy} onChange={(value) => setDraft({ ...draft, producerFax: value })} />
              <ProfileTextInput label="生产商邮箱" value={draft.producerEmail} disabled={isBusy} onChange={(value) => setDraft({ ...draft, producerEmail: value })} />
              <ProfileSelect label="生产商保密" value={draft.producerSertFlag} disabled={isBusy} onChange={(value) => setDraft({ ...draft, producerSertFlag: value })} />
              <ProfileTextInput label="最近发票号" value={draft.lastInvoiceNo} disabled={isBusy} onChange={(value) => setDraft({ ...draft, lastInvoiceNo: value })} />
              <ProfileTextInput label="最近合同号" value={draft.lastContractNo} disabled={isBusy} onChange={(value) => setDraft({ ...draft, lastContractNo: value })} />
              <ProfileTextInput label="最近款号" value={draft.lastSourceStyleNo} disabled={isBusy} onChange={(value) => setDraft({ ...draft, lastSourceStyleNo: value })} />
            </div>
          </form>
        </div>

        <footer className="single-window-lock-footer producer-profile-footer">
          <button className="command-button secondary" type="button" onClick={handleClose} disabled={isBusy}>
            <span>关闭</span>
          </button>
          <button className="command-button secondary" type="button" onClick={beginCreate} disabled={isBusy}>
            <Plus size={17} aria-hidden="true" />
            <span>新增</span>
          </button>
          <button className="command-button secondary" type="button" onClick={handleDelete} disabled={isBusy || !selectedId}>
            <Trash2 size={17} aria-hidden="true" />
            <span>删除</span>
          </button>
          <button className="command-button secondary" type="submit" form="producer-profile-form" disabled={isBusy}>
            <Save size={17} aria-hidden="true" />
            <span>保存资料</span>
          </button>
          <button className="command-button" type="button" onClick={handleApplyDraft} disabled={isBusy}>
            <Check size={17} aria-hidden="true" />
            <span>套用</span>
          </button>
        </footer>
      </div>
    </div>
  );
}

function ProfileTextInput({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label>
      <span>{label}</span>
      <input value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function ProfileTextarea({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label className="producer-profile-textarea">
      <span>{label}</span>
      <textarea value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function ProfileSelect({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <label>
      <span>{label}</span>
      <select value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        <option value="">未选择</option>
        <option value="N">N：否</option>
        <option value="Y">Y：是</option>
      </select>
    </label>
  );
}

function toDraft(profile: ApiCustomsCooProducerProfileInputDto | ApiCustomsCooProducerProfileDto): ProducerProfileDraft {
  return {
    id: "id" in profile ? profile.id : 0,
    ciqRegNo: profile.ciqRegNo ?? "",
    prdcEtpsName: profile.prdcEtpsName ?? "",
    prdcEtpsConcEr: profile.prdcEtpsConcEr ?? "",
    prdcEtpsTel: profile.prdcEtpsTel ?? "",
    producer: profile.producer ?? "",
    producerTel: profile.producerTel ?? "",
    producerFax: profile.producerFax ?? "",
    producerEmail: profile.producerEmail ?? "",
    producerSertFlag: profile.producerSertFlag ?? "",
    lastInvoiceNo: profile.lastInvoiceNo ?? "",
    lastContractNo: profile.lastContractNo ?? "",
    lastSourceStyleNo: profile.lastSourceStyleNo ?? "",
  };
}

function normalizeProfileInput(profile: ApiCustomsCooProducerProfileInputDto): ApiCustomsCooProducerProfileInputDto {
  return {
    ciqRegNo: normalizeUpper(profile.ciqRegNo),
    prdcEtpsName: normalizeText(profile.prdcEtpsName),
    prdcEtpsConcEr: normalizeText(profile.prdcEtpsConcEr),
    prdcEtpsTel: normalizeText(profile.prdcEtpsTel),
    producer: normalizeText(profile.producer),
    producerTel: normalizeText(profile.producerTel),
    producerFax: normalizeText(profile.producerFax),
    producerEmail: normalizeText(profile.producerEmail),
    producerSertFlag: normalizeUpper(profile.producerSertFlag),
    lastInvoiceNo: normalizeText(profile.lastInvoiceNo),
    lastContractNo: normalizeText(profile.lastContractNo),
    lastSourceStyleNo: normalizeText(profile.lastSourceStyleNo),
  };
}

function producerProfileDraftEquals(left: ProducerProfileDraft, right: ProducerProfileDraft) {
  return JSON.stringify(toComparableProducerProfileDraft(left)) === JSON.stringify(toComparableProducerProfileDraft(right));
}

function toComparableProducerProfileDraft(profile: ProducerProfileDraft) {
  return {
    id: profile.id || 0,
    ...normalizeProfileInput(profile),
  };
}

function hasProducerIdentity(profile: ApiCustomsCooProducerProfileInputDto) {
  return Boolean(profile.ciqRegNo?.trim() || profile.prdcEtpsName?.trim());
}

function buildProfileDisplayName(profile: ApiCustomsCooProducerProfileInputDto) {
  const code = profile.ciqRegNo?.trim() ?? "";
  const name = profile.prdcEtpsName?.trim() ?? "";
  return code && name ? `${code} - ${name}` : name || code || "未命名";
}

function readProfileValue(value?: string) {
  return value?.trim() ? value : "-";
}

function normalizeText(value?: string) {
  return value?.trim() ?? "";
}

function normalizeUpper(value?: string) {
  return normalizeText(value).toUpperCase();
}
