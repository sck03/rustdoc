import { Building2, Copy, CreditCard, Plus, RefreshCw, Save, ShieldCheck } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { FieldShell, TextField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { SingleWindowClientDirectories } from "./SingleWindowClientDirectories.tsx";
import { NEW_PROFILE_KEY, useSingleWindowProfiles } from "./useSingleWindowProfiles.ts";

export function SingleWindowStationProfilePanel({ client, canOperate }: {
  client: ExportDocManagerApiClient; canOperate: boolean;
}) {
  const { draft, change, profiles, profilesQuery, selectedProfile, selectedProfileKey, activeProfile,
    message, messageKind, isBusy, identityLocked, canSaveProfile, save, changeSelectedProfile,
    refreshProfiles, activateProfile, copyAssignmentCode, startNewProfile } = useSingleWindowProfiles(client, canOperate);
  const { profileName, companyScope, cardIdentifier, customsCooDirectories, agentConsignmentDirectories,
    canSubmitCustomsCoo, canSubmitAgentConsignment } = draft;
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
            onClick={() => void save()}
          >
            <Save size={17} aria-hidden="true" />
            <span>保存档案</span>
          </button>
        </div>
      </div>

      {!canOperate ? <PermissionNotice>当前权限仅允许查看操作档案，不能修改公司、操作卡或目录。</PermissionNotice> : null}
      {profilesQuery.isError ? <InlineNotice tone="error" title="操作档案加载失败">{readApiError(profilesQuery.error)}</InlineNotice> : null}
      {message ? <InlineNotice tone={messageKind}>{message}</InlineNotice> : null}

      <div className="single-window-station-summary">
        <span><ShieldCheck size={16} aria-hidden="true" />本机独立运行</span>
        <span><Building2 size={16} aria-hidden="true" />{activeProfile?.companyScope || "未选择公司"}</span>
        <span><CreditCard size={16} aria-hidden="true" />{activeProfile?.cardIdentifier || "未选择操作卡"}</span>
        <span>{profiles.length} 个档案</span>
      </div>

      <div className="field-grid single-window-station-fields">
        <FieldShell label="正在编辑" disabled={isBusy}>
          {() => (
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
          )}
        </FieldShell>
        <TextField label="档案名称" value={profileName} required disabled={!canOperate || isBusy} onChange={value => change("profileName", value)} />
        <TextField label="公司抬头" value={companyScope} required disabled={!canOperate || isBusy || identityLocked} onChange={value => change("companyScope", value)} />
        <TextField
          label="操作卡标识"
          value={cardIdentifier}
          required
          disabled={!canOperate || isBusy || identityLocked}
          description="填写卡片编号、公司简称或内部资产编号；系统不保存卡密码。"
          onChange={value => change("cardIdentifier", value)}
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
          <input type="checkbox" checked={canSubmitCustomsCoo} disabled={!canOperate || isBusy} onChange={(event) => change("canSubmitCustomsCoo", event.target.checked)} />
          <span><strong>海关原产地证</strong><small>COO 提交与回执目录</small></span>
        </label>
        <label className="single-window-capability-card">
          <input type="checkbox" checked={canSubmitAgentConsignment} disabled={!canOperate || isBusy} onChange={(event) => change("canSubmitAgentConsignment", event.target.checked)} />
          <span><strong>报关代理委托</strong><small>ACD 提交与回执目录</small></span>
        </label>
      </div>
      <SingleWindowClientDirectories key={selectedProfileKey} value={{ customsCooDirectories, agentConsignmentDirectories }}
        disabled={!canOperate || isBusy} autoDetect={selectedProfileKey === NEW_PROFILE_KEY && !isBusy && canOperate}
        cooEnabled={canSubmitCustomsCoo} acdEnabled={canSubmitAgentConsignment}
        onChange={next => { change("customsCooDirectories", next.customsCooDirectories); change("agentConsignmentDirectories", next.agentConsignmentDirectories); }} />
    </section>
  );
}
