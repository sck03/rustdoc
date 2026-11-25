import { Plus, Trash2 } from "lucide-react";
import type { ApiPaymentDto, ApiPaymentGoodsItem } from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { paymentGoodsItems, paymentGoodsPatch } from "./paymentModel.ts";
import "./paymentGoodsItems.css";

const emptyItem = (): ApiPaymentGoodsItem => ({ goodsName: "", quantity: "", quantityUnit: "" });

export function PaymentGoodsItemsEditor({ payment, onChange }: {
  payment: ApiPaymentDto; onChange: (patch: Partial<ApiPaymentDto>) => void;
}) {
  const goods = paymentGoodsItems(payment);
  const rows = goods.length ? goods : [emptyItem()];
  const change = (index: number, field: keyof ApiPaymentGoodsItem, value: string) =>
    onChange(paymentGoodsPatch(rows.map((item, i) => i === index ? { ...item, [field]: value } : item)));
  return <div className="payment-goods-items">
    <div className="section-header">
      <h3>货物明细</h3>
      <button className="command-button secondary" type="button" disabled={rows.length >= 4}
        onClick={() => onChange(paymentGoodsPatch([...rows, emptyItem()]))}>
        <Plus size={17} aria-hidden="true" /><span>新增货物</span>
      </button>
    </div>
    <ResponsiveTableFrame label="付款货物明细（最多 4 项）">
      <table className="payment-goods-table" aria-label="付款货物明细">
        <thead><tr><th scope="col">序号</th><th scope="col">货物品名</th><th scope="col">数量</th><th scope="col">数量单位</th><th scope="col">操作</th></tr></thead>
        <tbody>{rows.map((item, index) => <tr key={index}>
          <th scope="row">{index + 1}</th>
          {([ ["goodsName", "货物品名", 500], ["quantity", "数量", 100], ["quantityUnit", "数量单位", 20] ] as const).map(([field, label, limit]) =>
            <td key={field}><input className="item-cell-input" aria-label={`第 ${index + 1} 项${label}`} value={item[field]} maxLength={limit}
              onChange={event => change(index, field, event.target.value)} /></td>)}
          <td><button className="icon-button danger" type="button" aria-label={`删除第 ${index + 1} 项货物`}
            onClick={() => onChange(paymentGoodsPatch(rows.filter((_, i) => i !== index)))}><Trash2 size={17} aria-hidden="true" /></button></td>
        </tr>)}</tbody>
      </table>
    </ResponsiveTableFrame>
  </div>;
}
