import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

export type ReportDesignerPreviewSampleProfile =
  | "apiSample"
  | "exportStandard"
  | "exportLongItems"
  | "paymentVoucher";

type PreviewSampleData = {
  globals: Record<string, string | boolean>;
  Invoice: Record<string, string>;
  Customer: Record<string, string>;
  Exporter: Record<string, string>;
  Payment: Record<string, string>;
  items: Array<Record<string, string>>;
};

export function getReportDesignerPreviewSampleProfiles(reportType: ReportDesignerReportType) {
  if (reportType === "PaymentVoucher") {
    return [
      { value: "apiSample" as const, label: "后端样例" },
      { value: "paymentVoucher" as const, label: "付款票据样例" },
    ];
  }

  return [
    { value: "apiSample" as const, label: "后端样例" },
    { value: "exportStandard" as const, label: "常规发票样例" },
    { value: "exportLongItems" as const, label: "长明细分页样例" },
  ];
}

export function isLocalReportDesignerPreviewSample(
  profile: ReportDesignerPreviewSampleProfile,
): profile is Exclude<ReportDesignerPreviewSampleProfile, "apiSample"> {
  return profile !== "apiSample";
}

export function renderReportDesignerLocalPreviewSample(
  sourceHtml: string,
  profile: Exclude<ReportDesignerPreviewSampleProfile, "apiSample">,
) {
  const data = createPreviewSampleData(profile);
  const expandedLoops = expandInvoiceItemLoops(sourceHtml, data);
  const evaluatedConditionals = evaluateSimpleScribanConditionals(expandedLoops, data, {});
  const withFields = replaceScribanValues(evaluatedConditionals, data, {});
  return removeScribanControlLines(withFields);
}

function createPreviewSampleData(profile: Exclude<ReportDesignerPreviewSampleProfile, "apiSample">): PreviewSampleData {
  if (profile === "paymentVoucher") {
    return {
      globals: {
        cny_amount_upper: "人民币壹万贰仟叁佰肆拾伍元陆角柒分",
        doc_seal_path: "",
        customs_seal_path: "",
        shipping_marks_image_data: "",
        ShowSeal: false,
      },
      Invoice: {},
      Customer: {},
      Exporter: {},
      Payment: {
        Department: "外贸业务部",
        InvoiceNo: "PAY-2026-0707",
        PaymentDate: "2026-07-07",
        PayeeName: "宁波样例供应商有限公司",
        PaymentMethod: "电汇",
        BankName: "中国银行宁波分行",
        BankAccount: "6222 0200 0000 0000",
        Amount: "12345.67",
        Currency: "CNY",
        Purpose: "样品采购及报关杂费",
        Remark: "用于新版报表设计器付款票据样例预览",
      },
      items: [],
    };
  }

  const itemCount = profile === "exportLongItems" ? 72 : 8;
  return {
    globals: {
      cny_amount_upper: "美元壹万贰仟叁佰肆拾伍元陆角柒分",
      doc_seal_path: "",
      customs_seal_path: "",
      shipping_marks_image_data: "",
      ShowSeal: true,
    },
    Invoice: {
      InvoiceNo: profile === "exportLongItems" ? "INV-LONG-2026-0707" : "INV-STD-2026-0707",
      InvoiceDate: "2026-07-07",
      ContractNo: "BRG-CT-2026-0707",
      LoadingPort: "NINGBO, CHINA",
      DestinationPort: "LE HAVRE, FRANCE",
      TradeTerm: "FOB NINGBO",
      PaymentTerm: "T/T 30 DAYS",
      ShippingMarks: "N/M\nORDER SAMPLE\nMADE IN CHINA",
      SpecialTerms: profile === "exportLongItems" ? "Partial shipment allowed" : "",
      TotalQuantity: String(itemCount * 12),
      TotalCartons: String(itemCount * 3),
      TotalGrossWeight: `${(itemCount * 18.4).toFixed(2)} KGS`,
      TotalNetWeight: `${(itemCount * 15.2).toFixed(2)} KGS`,
      TotalAmount: "12345.67",
    },
    Customer: {
      CustomerNameEN: "EURO DISNEY ASSOCIES S.A.S",
      AddressEN: "1 rond-point d'Isigny, 77700 Chessy, France",
    },
    Exporter: {
      ExporterNameEN: "NINGBO BRIDGE IMP & EXP CO.,LTD",
      AddressEN: "Ningbo, Zhejiang, China",
    },
    Payment: {},
    items: Array.from({ length: itemCount }, (_, index) => {
      const number = index + 1;
      return {
        ProductNameEN: `Sample product ${String(number).padStart(2, "0")} with controlled wrapping`,
        ProductNameCN: `样例商品${number}`,
        Sku: `SKU-${String(number).padStart(3, "0")}-${profile === "exportLongItems" ? "LONG-CODE-" + "X".repeat(18) : "STD"}`,
        Specification: `Spec ${number}`,
        Quantity: String(10 + number),
        Unit: "PCS",
        Cartons: String(2 + (number % 5)),
        UnitPrice: (5.8 + number / 10).toFixed(2),
        TotalPrice: (128.5 + number * 3.25).toFixed(2),
        GrossWeight: (18 + number / 10).toFixed(2),
        NetWeight: (15 + number / 10).toFixed(2),
        Volume: (0.08 + number / 100).toFixed(3),
      };
    }),
  };
}

function expandInvoiceItemLoops(sourceHtml: string, data: PreviewSampleData) {
  const loopOpenPattern = /{{\s*for\s+item\s+in\s+Invoice\.Items\s*}}/g;
  let result = "";
  let cursor = 0;
  let match: RegExpExecArray | null;

  while ((match = loopOpenPattern.exec(sourceHtml)) !== null) {
    const loopStart = match.index;
    const contentStart = loopOpenPattern.lastIndex;
    const loopEnd = findMatchingScribanEnd(sourceHtml, contentStart);
    if (loopEnd < 0) {
      break;
    }

    result += sourceHtml.slice(cursor, loopStart);
    const rowTemplate = sourceHtml.slice(contentStart, loopEnd);
    result += data.items.map((item) => renderLoopItem(rowTemplate, data, item)).join("");
    cursor = loopEnd + "{{ end }}".length;
    loopOpenPattern.lastIndex = cursor;
  }

  return result + sourceHtml.slice(cursor);
}

function findMatchingScribanEnd(sourceHtml: string, startIndex: number) {
  const tagPattern = /{{\s*(for\s+item\s+in\s+Invoice\.Items|if\b[^}]*|end)\s*}}/g;
  tagPattern.lastIndex = startIndex;
  let depth = 1;
  let match: RegExpExecArray | null;

  while ((match = tagPattern.exec(sourceHtml)) !== null) {
    const tag = match[1].trim();
    if (tag.startsWith("for ") || tag.startsWith("if ")) {
      depth += 1;
    } else if (tag === "end") {
      depth -= 1;
      if (depth === 0) {
        return match.index;
      }
    }
  }

  return -1;
}

function renderLoopItem(template: string, data: PreviewSampleData, item: Record<string, string>) {
  const evaluatedConditionals = evaluateSimpleScribanConditionals(template, data, item);
  const withFields = replaceScribanValues(evaluatedConditionals, data, item);
  return removeScribanControlLines(withFields);
}

function evaluateSimpleScribanConditionals(sourceHtml: string, data: PreviewSampleData, item: Record<string, string>) {
  let result = sourceHtml;
  const conditionalPattern = /{{\s*if\s+([^{}]+?)\s*}}([\s\S]*?)(?:{{\s*else\s*}}([\s\S]*?))?{{\s*end\s*}}/g;

  for (let guard = 0; guard < 8; guard += 1) {
    const next = result.replace(conditionalPattern, (_match, expression: string, whenTrue: string, whenFalse = "") =>
      evaluateScribanCondition(expression, data, item) ? whenTrue : whenFalse,
    );
    if (next === result) {
      return result;
    }

    result = next;
  }

  return result;
}

function evaluateScribanCondition(expression: string, data: PreviewSampleData, item: Record<string, string>) {
  const normalized = expression.trim();
  const equality = normalized.match(/^([A-Za-z_][A-Za-z0-9_.]*)\s*(==|!=)\s*"([^"]*)"$/);
  if (equality) {
    const actual = String(readSampleValue(equality[1], data, item) ?? "");
    return equality[2] === "==" ? actual === equality[3] : actual !== equality[3];
  }

  const falseCheck = normalized.match(/^([A-Za-z_][A-Za-z0-9_.]*)\s*==\s*false$/);
  if (falseCheck) {
    return !readSampleValue(falseCheck[1], data, item);
  }

  return Boolean(readSampleValue(normalized, data, item));
}

function replaceScribanValues(sourceHtml: string, data: PreviewSampleData, item: Record<string, string>) {
  return sourceHtml.replace(/{{\s*([A-Za-z_][A-Za-z0-9_.]*)\s*}}/g, (_match, path: string) => {
    const value = readSampleValue(path, data, item);
    return escapeHtml(value === undefined ? "" : String(value));
  });
}

function readSampleValue(path: string, data: PreviewSampleData, item: Record<string, string>) {
  const normalized = path.trim();
  if (normalized.startsWith("item.")) {
    return item[normalized.slice("item.".length)];
  }

  if (normalized.startsWith("Invoice.Items.")) {
    return item[normalized.slice("Invoice.Items.".length)];
  }

  if (normalized.startsWith("Invoice.")) {
    return data.Invoice[normalized.slice("Invoice.".length)];
  }

  if (normalized.startsWith("Customer.")) {
    return data.Customer[normalized.slice("Customer.".length)];
  }

  if (normalized.startsWith("Exporter.")) {
    return data.Exporter[normalized.slice("Exporter.".length)];
  }

  if (normalized.startsWith("Payment.")) {
    return data.Payment[normalized.slice("Payment.".length)];
  }

  return data.globals[normalized];
}

function removeScribanControlLines(sourceHtml: string) {
  return sourceHtml.replace(/{{\s*[^{}]+\s*}}/g, "");
}

function escapeHtml(value: string) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
