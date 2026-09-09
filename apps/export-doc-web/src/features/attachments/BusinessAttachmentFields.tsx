import type { BusinessAttachmentCategoryRecord } from "../../api/index.ts";
import type { AttachmentMetadata } from "./attachmentModel.ts";

export function BusinessAttachmentFields({ value, categories, disabled, onChange }: {
  value: AttachmentMetadata; categories: BusinessAttachmentCategoryRecord[]; disabled: boolean;
  onChange: (value: AttachmentMetadata) => void;
}) {
  return <fieldset disabled={disabled}>
    <label>资料名称<input value={value.title} onChange={(event) => onChange({ ...value, title: event.target.value })} maxLength={200} required /></label>
    <label>分类<select required value={value.categoryId || ""} onChange={(event) => onChange({ ...value, categoryId: Number(event.target.value) })}>
      <option value="" disabled>请选择分类</option>
      {categories.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
    </select></label>
    {!categories.length && <p className="business-records-muted">暂无资料分类，请由管理员在“管理分类”中添加。</p>}
    <label>客户 PO（选填）<input value={value.poNumber} maxLength={100} onChange={(event) => onChange({ ...value, poNumber: event.target.value })} /></label>
    <label>款号（选填）<input value={value.styleNo} maxLength={200} onChange={(event) => onChange({ ...value, styleNo: event.target.value })} /></label>
  </fieldset>;
}
