import { useState, type FormEvent } from "react";
import type { BusinessAttachmentCategoryRecord, BusinessAttachmentMetadataUpdate, BusinessAttachmentRecord } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { attachmentMetadata } from "./attachmentModel.ts";
import { BusinessAttachmentFields } from "./BusinessAttachmentFields.tsx";

export function BusinessAttachmentMetadataForm({ item, categories, busy, onSave, onClose }: {
  item: BusinessAttachmentRecord; categories: BusinessAttachmentCategoryRecord[]; busy: boolean;
  onSave: (request: BusinessAttachmentMetadataUpdate) => Promise<boolean>; onClose: () => void;
}) {
  const [value, setValue] = useState(() => attachmentMetadata(item));
  const [expectedVersion] = useState(item.versionNumber);
  const [note, setNote] = useState("");
  const [error, setError] = useState("");
  const [dirty, setDirty] = useState(false);
  const guard = useUnsavedChangesGuard({ isDirty: dirty, message: "资料信息修正尚未保存。" });
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!categories.some((category) => category.id === value.categoryId)) { setError("请选择有效的资料分类。"); return; }
    setError("");
    if (await onSave({ ...value, expectedVersion, note })) { setDirty(false); onClose(); }
  }
  return <form className="business-records-form" aria-label="修正资料信息" onChangeCapture={() => setDirty(true)} onSubmit={(event) => void submit(event)}>
    <h3>修正资料信息</h3>
    {error && <InlineNotice tone="error">{error}</InlineNotice>}
    <BusinessAttachmentFields value={value} categories={categories} disabled={busy} onChange={setValue} />
    <label>修正说明<input required maxLength={500} value={note} disabled={busy} onChange={(event) => setNote(event.target.value)} /></label>
    <p className="business-records-muted">修改名称、分类、PO 或款号，原文件和有效版本保持原样。</p>
    <div className="business-records-actions">
      <button type="submit" className="command-button" disabled={busy || !dirty}>保存资料信息</button>
      <button type="button" className="command-button secondary" disabled={busy} onClick={() => void guard.confirmDiscardChanges("取消修正").then((yes) => { if (yes) onClose(); })}>取消修正</button>
    </div>
  </form>;
}
