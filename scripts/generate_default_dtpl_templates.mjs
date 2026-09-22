#!/usr/bin/env node
// Generates the six built-in .dtpl templates from the canonical V3 shapes.
// The generator mirrors the Rust container contract for release assets.
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";

const root = path.resolve(process.argv[2] ?? ".");
const MAGIC = Buffer.from("EXPORTDOCDT", "ascii");
const VERSION = 1;
const DOCUMENT = "document.v3.json";

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function zipDeflate(name, raw) {
  const compressed = zlib.deflateRawSync(raw, { level: 9 });
  const nameBytes = Buffer.from(name, "utf8");
  const local = Buffer.alloc(30);
  local.writeUInt32LE(0x04034b50, 0);
  local.writeUInt16LE(20, 4);
  local.writeUInt16LE(8, 8);
  local.writeUInt32LE(crc32(raw), 14);
  local.writeUInt32LE(compressed.length, 18);
  local.writeUInt32LE(raw.length, 22);
  local.writeUInt16LE(nameBytes.length, 26);
  const central = Buffer.alloc(46);
  central.writeUInt32LE(0x02014b50, 0);
  central.writeUInt16LE(20, 4);
  central.writeUInt16LE(20, 6);
  central.writeUInt16LE(8, 10);
  central.writeUInt32LE(crc32(raw), 16);
  central.writeUInt32LE(compressed.length, 20);
  central.writeUInt32LE(raw.length, 24);
  central.writeUInt16LE(nameBytes.length, 28);
  central.writeUInt32LE(0o600 << 16, 38);
  const centralOffset = local.length + nameBytes.length + compressed.length;
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(1, 8);
  end.writeUInt16LE(1, 10);
  end.writeUInt32LE(central.length + nameBytes.length, 12);
  end.writeUInt32LE(centralOffset, 16);
  return Buffer.concat([local, nameBytes, compressed, central, nameBytes, end]);
}

function encode(design) {
  const payload = zipDeflate(DOCUMENT, Buffer.from(JSON.stringify(design), "utf8"));
  const header = Buffer.alloc(MAGIC.length + 2 + 4 + 32);
  MAGIC.copy(header, 0);
  header.writeUInt16LE(VERSION, MAGIC.length);
  header.writeUInt32LE(payload.length, MAGIC.length + 2);
  crypto.createHash("sha256").update(payload).digest().copy(header, MAGIC.length + 6);
  return Buffer.concat([header, payload]);
}

const style = (size, bold = false, align = "Left") => ({
  fontFamily: "Noto Sans CJK SC", fontSizePt: size, bold, color: "#173f3b",
  backgroundColor: "#ffffff", align, verticalAlign: "Top", borderColor: "#c5d3cf",
  borderWidthPx: 0, borderStyle: "None", paddingHundredthMm: 100,
});
const base = (reportType, orientation = "Portrait", font = "Noto Sans CJK SC") => ({
  version: 3, astKind: "ReportDocument", coordinateUnit: "hundredth-mm", contractVersion: "3.0",
  reportType,
  page: {
    size: "A4", orientation,
    widthHundredthMm: orientation === "Landscape" ? 29700 : 21000,
    heightHundredthMm: orientation === "Landscape" ? 21000 : 29700,
    marginTopHundredthMm: 800, marginRightHundredthMm: 800, marginBottomHundredthMm: 800, marginLeftHundredthMm: 800,
    fontFamily: font, fontSizePt: 9,
  },
  grid: { enabled: true, snap: true, sizeHundredthMm: 500 },
  layers: ["Header", "Body", "Footer", "Overlay"].map((role) => ({
    id: role.toLowerCase(),
    name: { Header: "页眉", Body: "主体", Footer: "页脚", Overlay: "覆盖层" }[role],
    role, visible: true, locked: false,
    print: { repeatOnEveryPage: role === "Header" || role === "Footer", keepTogether: role !== "Body", pinToPageBottom: role === "Footer", minHeightHundredthMm: 0 },
    elements: [],
  })),
});
const text = (id, label, value, x, y, w, h, size, bold = false, align = "Left") => ({
  id, label, xHundredthMm: x, yHundredthMm: y, widthHundredthMm: w, heightHundredthMm: h,
  rotationDeg: 0, zIndex: 0, visible: true, locked: false, outputEnabled: true,
  style: style(size, bold, align), type: "Text", text: value,
});
const field = (id, label, path, x, y, w, h, size, bold = false, align = "Left") => ({
  id, label, xHundredthMm: x, yHundredthMm: y, widthHundredthMm: w, heightHundredthMm: h,
  rotationDeg: 0, zIndex: 0, visible: true, locked: false, outputEnabled: true,
  style: style(size, bold, align), type: "Field", fieldPath: path, fallbackText: "",
});
const controlledImage = (id, label, path, x, y, w, h) => ({
  id, label, xHundredthMm: x, yHundredthMm: y, widthHundredthMm: w, heightHundredthMm: h,
  rotationDeg: 0, zIndex: 0, visible: true, locked: false, outputEnabled: true,
  style: style(8), type: "Image", sourceKind: "Field", purpose: "Image",
  fieldPath: path, resourceId: "", altText: "", hideWhenSourceEmpty: true,
});
function detail(id, columns, y, x = 800, width = 19400) {
  return {
    id, label: "明细表", xHundredthMm: x, yHundredthMm: y, widthHundredthMm: width, heightHundredthMm: 15000,
    rotationDeg: 0, zIndex: 0, visible: true, locked: false, outputEnabled: true,
    style: { ...style(8), borderWidthPx: 0.2, borderStyle: "Solid" }, type: "Flow", flowKind: "DetailTable",
    block: {
      id, type: "DetailTable", title: "商品明细", sourcePath: "Invoice.Items", repeatMode: "ScribanFor",
      columns, print: { repeatHeaderOnPageBreak: true, keepRowsTogether: true },
      headerStyle: { fontSizePt: 8, bold: true, align: "Center" }, bodyStyle: { fontSizePt: 8 },
      border: { color: "#333333", widthPx: 0.2, style: "Solid", top: true, right: true, bottom: true, left: true },
    },
  };
}
const detailColumns = [
  ["item-0", "款号", "item.StyleNo", 22, "Left"],
  ["item-1", "品名", "item.StyleName", 72, "Left"],
  ["item-2", "数量", "item.Quantity", 22, "Right"],
  ["item-3", "单位", "item.UnitEN", 18, "Center"],
  ["item-4", "单价", "item.UnitPrice", 25, "Right"],
  ["item-5", "金额", "item.TotalPrice", 25, "Right"],
].map(([id, title, fieldPath, widthMm, align]) => ({ id, title, fieldPath, contentKind: "Field", widthMm, align }));

function commercial(reportType, title, detailId) {
  const design = base(reportType);
  design.layers[0].elements = [
    text("title", "标题", title, 800, 700, 19400, 1000, 16, true, "Center"),
    field("exporter", "出口商", "Exporter.ExporterNameEN", 800, 1900, 12000, 700, 10, true),
    field("exporter-address", "出口商地址", "Exporter.AddressEN", 800, 2600, 12000, 700, 8),
    field("customer", "客户", "Customer.CustomerNameEN", 13000, 1900, 7200, 700, 10, true),
    field("customer-address", "客户地址", "Customer.AddressEN", 13000, 2600, 7200, 700, 8),
    field("invoice-no", "发票号", "Invoice.InvoiceNo", 800, 3500, 6000, 650, 10, true),
    field("invoice-date", "日期", "Invoice.InvoiceDate", 7000, 3500, 6000, 650, 9),
    field("contract-no", "合同号", "Invoice.ContractNo", 13000, 3500, 7200, 650, 9),
    controlledImage("shipping-marks", "唛头", "Invoice.ShippingMarks", 16000, 4100, 4200, 2600),
    controlledImage("doc-seal", "印章", "doc_seal_path", 16000, 7000, 4200, 2600),
  ];
  const table = detail(detailId, detailColumns, 4500);
  table.block.print.firstPageRows = 12;
  table.block.print.continuationPageRows = 12;
  design.layers[1].elements = [table];
  design.layers[2].elements = [
    text("total-label", "合计", "TOTAL:", 14500, 26800, 2500, 800, 12, true, "Right"),
    field("total", "合计", "Invoice.TotalAmount", 17000, 26800, 3200, 800, 12, true, "Right"),
    field("terms", "付款条款", "Invoice.PaymentTerms", 800, 27800, 19400, 700, 8),
    text("signature", "签章", "Authorized signature / 授权签章", 800, 28600, 19400, 500, 8, false, "Right"),
  ];
  return design;
}
function packing() {
  const design = commercial("ExportDocument", "PACKING LIST / 装箱单", "packing-details");
  const columns = [
    ["pack-0", "款号", "item.StyleNo", 20, "Left"], ["pack-1", "品名", "item.StyleName", 62, "Left"],
    ["pack-2", "数量", "item.Quantity", 22, "Right"], ["pack-3", "箱数", "item.Cartons", 20, "Right"],
    ["pack-4", "毛重", "item.GWTotal", 24, "Right"], ["pack-5", "净重", "item.NWTotal", 24, "Right"],
    ["pack-6", "体积", "item.Volume", 22, "Right"],
  ].map(([id, title, fieldPath, widthMm, align]) => ({ id, title, fieldPath, contentKind: "Field", widthMm, align }));
  const table = detail("packing-details", columns, 4500);
  table.block.print.firstPageRows = 12;
  table.block.print.continuationPageRows = 12;
  design.layers[1].elements = [table];
  design.layers[2].elements = [
    field("total-cartons", "总箱数", "Invoice.TotalCartons", 11000, 26800, 3000, 700, 10, true, "Right"),
    field("total-weight", "总毛重", "Invoice.TotalGrossWeight", 14000, 26800, 3000, 700, 10, true, "Right"),
    field("total-volume", "总体积", "Invoice.TotalVolume", 17000, 26800, 3200, 700, 10, true, "Right"),
  ];
  return design;
}
function contract() {
  const design = commercial("ExportDocument", "SALES CONTRACT / 售货合同", "contract-details");
  design.layers[0].elements.push(field("marks", "唛头", "Invoice.ShippingMarks", 800, 4200, 19400, 1500, 8));
  design.layers[1].elements = [detail("contract-details", detailColumns, 6100)];
  design.layers[2].elements = [
    field("clauses", "条款", "Invoice.SpecialTerms", 800, 22000, 19400, 5200, 8),
    text("signature", "签章", "买方 / 卖方签字盖章", 800, 27600, 19400, 700, 9, true, "Center"),
  ];
  return design;
}
function customs() {
  const design = base("ExportDocument", "Landscape", "Noto Serif CJK SC");
  const columns = [
    ["cus-0", "项号", "item.StyleNo", 18, "Center"], ["cus-1", "商品编号", "item.HsCode", 28, "Left"],
    ["cus-2", "品名规格", "item.StyleName", 72, "Left"], ["cus-3", "数量单位", "item.Quantity", 28, "Right"],
    ["cus-4", "单价总价", "item.TotalPrice", 30, "Right"], ["cus-5", "原产国", "item.Description", 24, "Center"],
    ["cus-6", "目的国", "Invoice.DestinationCountry", 24, "Center"], ["cus-7", "征免", "item.PoNumber", 20, "Center"],
  ].map(([id, title, fieldPath, widthMm, align]) => ({ id, title, fieldPath, contentKind: "Field", widthMm, align }));
  design.layers[0].elements = [
    text("title", "标题", "中华人民共和国海关出口货物报关单", 800, 500, 28100, 900, 15, true, "Center"),
    field("pre-no", "预录入编号", "Invoice.InvoiceNo", 800, 1500, 12000, 600, 8, true),
    field("exporter", "境内发货人", "Exporter.ExporterNameCN", 800, 2200, 15000, 700, 8, true),
    field("customer", "境外收货人", "Customer.CustomerNameEN", 16000, 2200, 12900, 700, 8),
    field("transport", "运输方式", "Invoice.TransportMode", 800, 3000, 8000, 600, 8),
    field("contract", "合同协议号", "Invoice.ContractNo", 9000, 3000, 9000, 600, 8),
    field("declaration-date", "申报日期", "Invoice.ShipmentDate", 18000, 3000, 9000, 600, 8),
    controlledImage("customs-seal", "海关印章", "customs_seal_path", 25000, 1500, 3600, 2000),
  ];
  const table = detail("customs-details", columns, 4500, 800, 28100);
  table.block.print.firstPageRows = 6;
  table.block.print.continuationPageRows = 15;
  design.layers[1].elements = [table];
  design.layers[2].elements = [
    field("total-cartons", "件数", "Invoice.TotalCartons", 800, 19300, 7000, 700, 9, true),
    field("total-weight", "毛重", "Invoice.TotalGrossWeight", 8000, 19300, 8000, 700, 9, true),
    field("total-net", "净重", "Invoice.TotalNetWeight", 16000, 19300, 8000, 700, 9, true),
    text("signature", "签章", "申报单位(签章)", 24000, 19300, 4900, 700, 9, true, "Right"),
    text("brand-note", "品牌说明", "境外品牌", 800, 20100, 28100, 500, 7),
  ];
  return design;
}
function payment(reportType, title, expense) {
  const design = base(reportType, "Portrait", "Noto Serif CJK SC");
  design.layers[0].elements = [
    field("payer", "付款单位", "Payment.PayerName", 800, 700, 19400, 800, 14, true, "Center"),
    text("title", "标题", title, 800, 1600, 19400, 900, 15, true, "Center"),
    field("department", "部门", "Payment.Department", 800, 2700, 9000, 600, 9),
    field("date", "日期", "Payment.PaymentDate", 11000, 2700, 9200, 600, 9, false, "Right"),
  ];
  if (expense) {
    const fields = ["TravelExpense","BusinessEntertainmentExpense","TelephoneExpense","OfficeExpense","RepairExpense","FreightMiscExpense","InspectionExpense","OtherExpense"];
    const names = ["差旅费", "业务招待费", "电话费", "办公费", "修理费", "运杂费", "检验费", "其他"];
    design.layers[1].elements = [detail("expense-details", names.map((name, index) => ({ id: `exp-${index}`, title: name, fieldPath: `Payment.${fields[index]}`, contentKind: "Field", widthMm: 22, align: "Right" })), 3600)];
  } else {
    const columns = [
      ["pay-0", "项目", "Payment.Project", 28, "Left"], ["pay-1", "出口发票号", "Payment.InvoiceNo", 42, "Left"],
      ["pay-2", "出货日期", "Payment.ShipmentDate", 34, "Center"], ["pay-3", "美元", "Payment.USDAmount", 30, "Right"],
      ["pay-4", "人民币", "Payment.CNYAmount", 34, "Right"], ["pay-5", "大写金额", "cny_amount_upper", 48, "Left"],
    ].map(([id, head, fieldPath, widthMm, align]) => ({ id, title: head, fieldPath, contentKind: "Field", widthMm, align }));
    design.layers[1].elements = [detail("payment-details", columns, 3600)];
  }
  design.layers[2].elements = [
    field("payee", "收款单位", "Payment.PayeeName", 800, 23000, 9000, 700, 9, true),
    field("bank", "开户行", "Payment.BankName", 10000, 23000, 10200, 700, 9),
    field("account", "账号", "Payment.AccountNo", 800, 23900, 9000, 700, 9),
    field("method", "支付方式", "Payment.PaymentMethod", 10000, 23900, 10200, 700, 9),
    field("notes", "备注", "Payment.Notes", 800, 25000, 19400, 3300, 8),
    text("signature", "签字", expense ? "审批签字:" : "复核:", 800, 28400, 19400, 600, 9, true, "Right"),
  ];
  return design;
}

const templates = [
  ["Templates/Export/invoice_template.dtpl", commercial("ExportDocument", "COMMERCIAL INVOICE / 商业发票", "invoice-details")],
  ["Templates/Export/packing_list_template.dtpl", packing()],
  ["Templates/Export/contract_template.dtpl", contract()],
  ["Templates/Export/customs_declaration_template.dtpl", customs()],
  ["Templates/Internal/payment_voucher_template.dtpl", payment("PaymentVoucher", "付款单(费用支付专用)", false)],
  ["Templates/Internal/expense_reimbursement_template.dtpl", payment("PaymentVoucher", "费用报销明细单", true)],
];
for (const [relative, design] of templates) {
  const target = path.join(root, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, encode(design));
  console.log(`${relative} ${fs.statSync(target).size} bytes`);
}
