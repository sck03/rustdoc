/** Synthetic editor examples; business previews and PDFs are rendered by Rust. */
export function designerSampleValue(path: string, index: number, labels: ReadonlyMap<string, string>): string {
  const row = index + 1;
  const values: Record<string, string> = {
    "item.StyleName": index === 0 ? "60% COTTON 40% POLYESTER LADIES' KNITTED PANTS" : "100% COTTON MEN'S WOVEN SHIRT",
    "item.StyleNameCN": index === 0 ? "棉混纺女式针织长裤" : "纯棉男式梭织衬衫",
    "item.StyleNo": `ST-${100 + row}`, "item.PoNumber": `PO-${200 + row}`,
    "item.RowNumber": String(row), "item.Cartons": String(100 * row), "item.Quantity": String(600 * row),
    "item.CtnUnitEN": "CTNS", "item.UnitEN": "PCS", "item.UnitCN": "件",
    "item.UnitPrice": "5.60", "item.TotalPrice": (3360 * row).toFixed(2),
    "item.GWTotal": String(120 * row), "item.NWTotal": String(100 * row), "item.Volume": (0.8 * row).toFixed(3),
    "Invoice.Currency": "USD", "Invoice.TradeTerms": "FOB", "Invoice.ShippingMarks": "N/M",
    "Invoice.TotalAmount": "10080.00", "Invoice.AdjustedTotalAmount": "10080.00",
    "total_by_ctn_unit": "300CTNS", "total_by_qty_unit": "1800PCS",
  };
  return values[path] ?? labels.get(path) ?? "示例内容";
}
