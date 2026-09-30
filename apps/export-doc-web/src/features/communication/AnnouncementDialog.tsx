import type { Announcement, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { AttachmentDraftFields } from "../../ui/AttachmentDraftFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useAnnouncementDraft } from "./useAnnouncementDraft.ts";

export function AnnouncementDialog({ client, user, record, onClose, onSaved }: { client: ExportDocManagerApiClient; user: ApiUserDto; record?: Announcement; onClose: () => void; onSaved: (row: Announcement) => void }) {
  const m = useAnnouncementDraft(client, user, record, onSaved);
  return <OfficeDialog title={record ? "编辑公告" : "新建公告"} onClose={onClose} {...m.operation} protectChanges hasChanges={m.dirty}>
    <p className="office-muted">保存草稿后，核对内容并发布。有效期按 {user.businessTimeZone} 填写。</p>
    {m.operation.error && m.attachments.saved && <p role="status">草稿已保存，未成功上传的附件仍保留，可再次保存继续上传。</p>}
    <form onSubmit={e => { e.preventDefault(); m.save(); }}><fieldset className="office-form-grid" disabled={m.operation.busy}>
      <OfficeField label="公告标题" wide><input required maxLength={200} value={m.draft.title} onChange={e => m.change({ title: e.target.value })} /></OfficeField>
      <OfficeField label="公告正文" wide><textarea required rows={8} maxLength={20000} value={m.draft.body} onChange={e => m.change({ body: e.target.value })} /></OfficeField>
      <OfficeField label="可见范围"><select disabled={m.departments.isPending || m.departments.isError} value={m.draft.audienceDepartment} onChange={e => m.change({ audienceDepartment: e.target.value })}><option value="">本公司全体</option>{m.departments.data?.map(d => <option key={d.code} value={d.code}>{d.name}</option>)}</select></OfficeField>
      <label className="checkbox-field"><input type="checkbox" checked={m.draft.isPinned} onChange={e => m.change({ isPinned: e.target.checked })} />置顶公告</label>
      {m.departments.isError && <InlineNotice tone="error">{readApiError(m.departments.error)}</InlineNotice>}
      <OfficeField label="生效时间"><input type="datetime-local" required value={m.draft.startsAt} onChange={e => m.change({ startsAt: e.target.value })} /></OfficeField>
      <OfficeField label="失效时间"><input type="datetime-local" required value={m.draft.expiresAt} onChange={e => m.change({ expiresAt: e.target.value })} /></OfficeField>
      <AttachmentDraftFields draft={m.attachments} busy={m.operation.busy} onChange={() => m.setDirty(true)} />
    </fieldset><OfficeSubmit busy={m.operation.busy} label="保存草稿" disabled={m.departments.isPending || m.departments.isError} /></form>
  </OfficeDialog>;
}
