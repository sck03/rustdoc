import {
  authorizedHeaders,
  authorizedJsonHeaders,
  ensureTrailingSlash,
} from "./web-runtime-smoke-common.mjs";

export async function createSmokeInvoice(options, accessToken, tokenType) {
  const timestamp = Date.now();
  const invoiceNo = `SMOKE-INV-${timestamp}`;
  const invoiceDate = `${new Date().toISOString().slice(0, 10)}T00:00:00`;
  const unitPrice = 12.34;
  const quantity = 10;
  const purchasePrice = 11.3;
  const body = {
    id: 0,
    invoiceNo,
    contractNo: `SMOKE-CON-${timestamp}`,
    invoiceDate,
    shipmentDate: invoiceDate,
    customerNameEN: `Smoke Customer ${timestamp}`,
    customerAddressEN: "Smoke Customer Address",
    exporterNameEN: "Smoke Exporter Ltd.",
    exporterNameCN: "Smoke Exporter",
    exporterAddressEN: "Smoke Exporter Address",
    currency: "USD",
    status: "Draft",
    paymentTerms: "T/T",
    portOfLoading: "NINGBO",
    portOfDestination: "LOS ANGELES",
    destinationCountry: "USA",
    tradeTerms: "FOB",
    transportMode: "BY SEA",
    shippingMarksType: "Text",
    shippingMarks: "N/M",
    exchangeRate: 7,
    totalAmount: unitPrice * quantity,
    totalPurchaseAmount: purchasePrice * quantity,
    totalTaxRefundAmount: 13,
    totalQuantity: quantity,
    totalCartons: 1,
    totalGrossWeight: 11,
    totalNetWeight: 10,
    totalVolume: 0.12,
    items: [
      {
        id: 0,
        invoiceId: 0,
        styleNo: `SMOKE-STYLE-${timestamp}`,
        styleName: "Smoke Sample Goods",
        hsCode: "6109100090",
        origin: "CHINA",
        quantity,
        unitEN: "PCS",
        unitCN: "件",
        cartons: 1,
        ctnUnitEN: "CTNS",
        ctnUnitCN: "箱",
        gwTotal: 11,
        nwTotal: 10,
        volume: 0.12,
        unitPrice,
        totalPrice: unitPrice * quantity,
        purchasePrice,
        purchaseTotal: purchasePrice * quantity,
        taxRebateRate: 13,
      },
    ],
  };

  const response = await fetch(new URL("/api/invoices", ensureTrailingSlash(options.apiBaseUrl)), {
    method: "POST",
    headers: authorizedJsonHeaders(options, accessToken, tokenType),
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Invoice smoke create failed with HTTP ${response.status}: ${await response.text()}`);
  }

  const payload = await response.json();
  if (!payload?.id || !payload?.invoice) {
    throw new Error(`Invoice smoke create response did not include id/invoice: ${JSON.stringify(payload)}`);
  }

  return payload.invoice;
}

export async function createSmokeProduct(options, accessToken, tokenType) {
  const timestamp = Date.now();
  const body = {
    id: 0,
    productCode: `SMOKE-PRODUCT-${timestamp}`,
    nameEN: "Smoke Library Goods",
    nameCN: "Smoke Library Goods CN",
    description: "",
    hsCode: "6217109000",
    elements: "",
    supervisionConditions: "",
    inspectionCategory: "",
    taxRebateRate: 13,
    material: "Cotton",
    brand: "SMOKE",
    origin: "CHINA",
    unitEN: "PCS",
    unitCN: "件",
    length: 10,
    width: 20,
    height: 30,
    gwPerCtn: 2,
    nwPerCtn: 1.8,
    pcsPerCtn: 12,
    packageUnitEN: "CTNS",
    packageUnitCN: "箱",
    defaultPrice: 19.88,
  };

  const response = await fetch(new URL("/api/master-data/products", ensureTrailingSlash(options.apiBaseUrl)), {
    method: "POST",
    headers: authorizedJsonHeaders(options, accessToken, tokenType),
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Product smoke create failed with HTTP ${response.status}: ${await response.text()}`);
  }

  const payload = await response.json();
  if (!payload?.id || !payload?.productCode) {
    throw new Error(`Product smoke create response did not include id/productCode: ${JSON.stringify(payload)}`);
  }

  return payload;
}

export async function deleteSmokeProduct(options, accessToken, tokenType, productId) {
  if (!productId) {
    return false;
  }

  const response = await fetch(new URL(`/api/master-data/products/${productId}`, ensureTrailingSlash(options.apiBaseUrl)), {
    method: "DELETE",
    headers: authorizedHeaders(options, accessToken, tokenType),
  });
  return response.ok || response.status === 404;
}

export async function deleteSmokeInvoice(options, accessToken, tokenType, invoiceId) {
  const response = await fetch(new URL(`/api/invoices/${invoiceId}`, ensureTrailingSlash(options.apiBaseUrl)), {
    method: "DELETE",
    headers: authorizedHeaders(options, accessToken, tokenType),
  });
  return response.ok;
}

export async function getApiSettings(options, accessToken, tokenType) {
  const response = await fetch(new URL("/api/settings", ensureTrailingSlash(options.apiBaseUrl)), {
    headers: authorizedHeaders(options, accessToken, tokenType),
  });
  if (!response.ok) {
    throw new Error(`Settings read failed with HTTP ${response.status}: ${await response.text()}`);
  }

  return response.json();
}

export async function getReportTemplates(options, accessToken, tokenType, reportType) {
  const url = new URL("/api/reports/templates", ensureTrailingSlash(options.apiBaseUrl));
  url.searchParams.set("reportType", reportType);
  const response = await fetch(url, {
    headers: authorizedHeaders(options, accessToken, tokenType),
  });
  if (!response.ok) {
    throw new Error(`Report template list failed with HTTP ${response.status}: ${await response.text()}`);
  }

  return response.json();
}

export async function saveApiSettings(options, accessToken, tokenType, settings) {
  const response = await fetch(new URL("/api/settings", ensureTrailingSlash(options.apiBaseUrl)), {
    method: "PUT",
    headers: authorizedJsonHeaders(options, accessToken, tokenType),
    body: JSON.stringify({ settings, updateSecrets: false }),
  });
  if (!response.ok) {
    throw new Error(`Settings save failed with HTTP ${response.status}: ${await response.text()}`);
  }

  return response.json();
}
