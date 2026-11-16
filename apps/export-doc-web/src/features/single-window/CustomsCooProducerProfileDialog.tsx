import { Check, Pencil, Plus, RefreshCw, Save, Search, Trash2 } from "lucide-react";
import {
  ApiCustomsCooProducerProfileInputDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { Dialog } from "../../ui/primitives/Dialog.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";

import { useProducerProfileWorkspace } from "./hooks/useProducerProfileWorkspace.ts";
import { readProfileValue } from "./producerProfileModel.ts";

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
  const {
    keyword, setKeyword, pageNumber, setPageNumber, pageSize, selectedId, draft, setDraft,
    message, successMessage, profilesQuery, profiles, isBusy, selectedCount,
    handleSearch, beginCreate, selectProfile, handleClose, handleSave, handleDelete,
    handleApplyDraft, applyListProfile,
  } = useProducerProfileWorkspace({ client, currentProfile, rowLabel, onApply, onClose });
  return (
    <Dialog title="生产企业资料" description={`共 ${profilesQuery.data?.totalCount ?? 0} 条资料`} size="full" busy={isBusy} onClose={() => void handleClose()} closeLabel="关闭">
      <div className="producer-profile-dialog">
        <div className="producer-profile-toolbar">
          <form className="producer-profile-search" onSubmit={handleSearch}>
            <input
              aria-label="生产企业资料关键字"
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="代码、名称、款号、发票号"
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
                      aria-disabled={isBusy}
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
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalCount={profilesQuery.data?.totalCount ?? 0} isBusy={isBusy} onPageChange={setPageNumber} />

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

        <footer data-ui="dialog-footer" className="producer-profile-footer">
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
    </Dialog>
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
