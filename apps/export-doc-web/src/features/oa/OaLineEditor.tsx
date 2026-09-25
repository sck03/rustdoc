import { useState, type Dispatch, type SetStateAction } from "react";
import type { OaExpenseLine, OaRequestSave } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { OfficeField } from "../office/OfficeUi.tsx";

export const expenseCategories = { Travel: "差旅", Transport: "交通", Meals: "餐饮", Office: "办公", Other: "其他" } as const;
export function OaLineEditor({ draft, setDraft, purchase, businessDate }: {
  draft: OaRequestSave; setDraft: Dispatch<SetStateAction<OaRequestSave>>; purchase: boolean; businessDate: string;
}) {
  const [keys, setKeys] = useState(() => (purchase ? draft.purchaseLines ?? [] : draft.lines ?? []).map(createRequestKey));
  function add() {
    setKeys((values) => [...values, createRequestKey()]);
    setDraft((value) => purchase ? { ...value, purchaseLines: [...value.purchaseLines ?? [], { name: "", specification: "", unit: "件", quantity: "1", unitPrice: "" }] }
      : { ...value, lines: [...value.lines ?? [], { category: "Travel", spentOn: businessDate, description: "", amount: "" }] });
  }
  function remove(index: number) {
    setKeys((values) => values.filter((_, position) => position !== index));
    setDraft((value) => purchase ? { ...value, purchaseLines: value.purchaseLines?.filter((_, position) => position !== index) }
      : { ...value, lines: value.lines?.filter((_, position) => position !== index) });
  }
  return <section className="office-field-wide oa-lines" aria-label={purchase ? "采购明细" : "费用明细"}>
    <h3>{purchase ? "采购明细与预算" : "费用明细"}</h3>
    {keys.map((key, index) => <fieldset className="office-form-grid oa-line" key={key}>
      <legend>第 {index + 1} 项</legend>
      {purchase ? (() => {
        const line = draft.purchaseLines![index];
        const set = (field: keyof typeof line, value: string) => setDraft((current) => ({ ...current, purchaseLines: current.purchaseLines?.map((item, position) => position === index ? { ...item, [field]: value } : item) }));
        return <>
          <OfficeField label="物品名称" wide><input required maxLength={200} value={line.name} onChange={(event) => set("name", event.target.value)} /></OfficeField>
          <OfficeField label="数量"><input required inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,3})?" value={line.quantity} onChange={(event) => set("quantity", event.target.value)} /></OfficeField>
          <OfficeField label="单位"><input required maxLength={20} value={line.unit} onChange={(event) => set("unit", event.target.value)} /></OfficeField>
          <OfficeField label="预算单价"><input required inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,2})?" value={line.unitPrice} onChange={(event) => set("unitPrice", event.target.value)} /></OfficeField>
          <details className="office-field-wide"><summary>规格说明（选填）</summary><OfficeField label="规格"><input maxLength={200} value={line.specification ?? ""} onChange={(event) => set("specification", event.target.value)} /></OfficeField></details>
        </>;
      })() : (() => {
        const line = draft.lines![index];
        const set = (value: Partial<OaExpenseLine>) => setDraft((current) => ({ ...current, lines: current.lines?.map((item, position) => position === index ? { ...item, ...value } : item) }));
        return <>
          <OfficeField label="费用类别"><select value={line.category} onChange={(event) => set({ category: event.target.value as OaExpenseLine["category"] })}>{Object.entries(expenseCategories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></OfficeField>
          <OfficeField label="发生日期"><input type="date" required max={businessDate} value={line.spentOn} onChange={(event) => set({ spentOn: event.target.value })} /></OfficeField>
          <OfficeField label="费用说明" wide><input required maxLength={200} value={line.description} onChange={(event) => set({ description: event.target.value })} /></OfficeField>
          <OfficeField label="金额"><input required inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,2})?" value={line.amount} onChange={(event) => set({ amount: event.target.value })} /></OfficeField>
        </>;
      })()}
      <button className="command-button secondary" type="button" disabled={keys.length <= 1} onClick={() => remove(index)}>删除第 {index + 1} 项</button>
    </fieldset>)}
    <button className="command-button secondary" type="button" disabled={keys.length >= 100} onClick={add}>添加明细</button>
    <p className="office-muted">金额保留两位小数，保存后显示核算合计。{purchase && "采购预算按每项数量乘单价、四舍五入到分后合计。"}</p>
  </section>;
}
