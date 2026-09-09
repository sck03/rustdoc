import { useState, type FormEvent } from "react";
import type { BusinessAttachmentCategoryRecord } from "../../api/index.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";

export function BusinessAttachmentCategoryManager({ items, busy, onSave, onDelete, onClose }: {
  items: BusinessAttachmentCategoryRecord[]; busy: boolean;
  onSave: (item: BusinessAttachmentCategoryRecord | null, name: string) => Promise<boolean>;
  onDelete: (item: BusinessAttachmentCategoryRecord) => Promise<boolean>; onClose: () => void;
}) {
  const [editing, setEditing] = useState<BusinessAttachmentCategoryRecord | null>(null);
  const [name, setName] = useState("");
  const confirmAction = useConfirmation();
  const guard = useUnsavedChangesGuard({ isDirty: name !== (editing?.name ?? ""), message: "分类名称尚未保存。" });
  async function edit(item: BusinessAttachmentCategoryRecord | null) {
    if (await guard.confirmDiscardChanges("切换分类")) { setEditing(item); setName(item?.name ?? ""); }
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (await onSave(editing, name)) { setEditing(null); setName(""); }
  }
  async function remove(item: BusinessAttachmentCategoryRecord) {
    if (await confirmAction({ title: "删除资料分类", description: `删除“${item.name}”？仅允许删除未被资料使用的分类。`, confirmLabel: "删除分类", tone: "danger" })) {
      if (await onDelete(item) && editing?.id === item.id) { setEditing(null); setName(""); }
    }
  }
  return <section className="business-records-form" aria-label="资料分类管理">
    <header className="business-records-heading"><h3>管理分类</h3>
      <button className="command-button secondary" type="button" disabled={busy} onClick={() => void guard.confirmDiscardChanges("关闭分类管理").then((yes) => { if (yes) onClose(); })}>关闭分类管理</button></header>
    <p className="business-records-muted">分类供本单所属公司共用；改名会同步所有资料，已使用的分类须先重新分类才能删除。</p>
    <form className="business-records-actions" onSubmit={(event) => void save(event)}>
      <label>{editing ? "修改分类名称" : "新增分类名称"}<input required maxLength={80} disabled={busy} value={name} onChange={(event) => setName(event.target.value)} placeholder="例如：产品图纸、客户确认、质检报告" /></label>
      <button className="command-button" type="submit" disabled={busy || !name.trim()}>{editing ? "保存分类名称" : "新增分类"}</button>
      {editing && <button className="command-button secondary" type="button" disabled={busy} onClick={() => void edit(null)}>取消改名</button>}
    </form>
    <ul className="business-records-list">{items.map((item) => <li className="business-records-card" key={item.id}>
      <div><strong>{item.name}</strong><p className="business-records-muted">{item.attachmentCount} 份资料使用</p></div>
      <div className="business-records-actions"><button className="command-button secondary" type="button" disabled={busy} onClick={() => void edit(item)} aria-label={`修改分类${item.name}`}>改名</button>
        <button className="command-button danger" type="button" disabled={busy || item.attachmentCount > 0} onClick={() => void remove(item)} aria-label={`删除分类${item.name}`}>删除</button></div>
    </li>)}</ul>
  </section>;
}
