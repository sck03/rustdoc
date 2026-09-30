import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { Announcement, AnnouncementSave, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { useAttachmentDraft } from "../../ui/useAttachmentDraft.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";

export function useAnnouncementDraft(client: ExportDocManagerApiClient, user: ApiUserDto, record: Announcement | undefined, onSaved: (row: Announcement) => void) {
  const operation = useOfficeOperation();
  const attachments = useAttachmentDraft(record);
  const [dirty, setDirty] = useState(false);
  const [draft, setDraft] = useState<AnnouncementSave>(() => ({ requestKey: record?.requestKey ?? createRequestKey(), title: record?.title ?? "", body: record?.body ?? "", audienceDepartment: record?.audienceDepartment ?? "", isPinned: record?.isPinned ?? false,
    startsAt: toBusinessDateTimeLocalInput(record?.startsAt ?? new Date().toISOString(), user.businessTimeZone),
    expiresAt: toBusinessDateTimeLocalInput(record?.expiresAt ?? new Date(Date.now() + 30 * 86400000).toISOString(), user.businessTimeZone) }));
  const departments = useQuery({ queryKey: ["office", "announcement-departments", user.id, user.companyScope], queryFn: ({ signal }) => client.listAnnouncementDepartments({ signal }) });
  useUnsavedChangesGuard({ isDirty: dirty, message: "公告有未保存的修改。" });
  function save() {
    void operation.run(signal => {
      const startsAt = businessDateTimeLocalInputToIso(draft.startsAt, user.businessTimeZone);
      const expiresAt = businessDateTimeLocalInputToIso(draft.expiresAt, user.businessTimeZone);
      if (!startsAt || !expiresAt) throw new Error("请填写完整的有效期。");
      const body = { ...draft, startsAt, expiresAt };
      return attachments.save(JSON.stringify(body), signal,
        (current, signal) => current ? client.updateAnnouncement({ id: current.id, body: { ...body, expectedVersion: current.versionNumber } }, { signal }) : client.createAnnouncement({ body }, { signal }),
        (current, entry, signal) => { const form = new FormData(); form.set("file", entry.file); form.set("expectedVersion", String(current.versionNumber)); return client.uploadAnnouncementAttachment({ id: current.id, body: form }, { signal }); });
    }, row => { setDirty(false); onSaved(row); });
  }
  return { operation, attachments, draft, departments, dirty, setDirty, save, change: (value: Partial<AnnouncementSave>) => { setDirty(true); setDraft(current => ({ ...current, ...value })); } };
}
