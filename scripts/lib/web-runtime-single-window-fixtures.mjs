import { authorizedHeaders, ensureTrailingSlash } from "./web-runtime-smoke-common.mjs";

export async function getSingleWindowBatchDetail(options, accessToken, tokenType, batchId) {
  const response = await fetch(
    new URL(
      `/api/single-window/operation-center/${encodeURIComponent(String(batchId))}`,
      ensureTrailingSlash(options.apiBaseUrl),
    ),
    { headers: authorizedHeaders(options, accessToken, tokenType) },
  );
  if (!response.ok) {
    throw new Error(`Single Window operation center detail failed with HTTP ${response.status}: ${await response.text()}`);
  }

  return response.json();
}

export function buildSmokeCustomsCooReceiptXml(referenceNo) {
  const now = new Date().toISOString();
  return [
    `<?xml version="1.0" encoding="UTF-8"?>`,
    `<Receipt>`,
    `  <CertNo>${escapeXmlText(referenceNo)}</CertNo>`,
    `  <ReceiveTime>${escapeXmlText(now)}</ReceiveTime>`,
    `  <Channel>6</Channel>`,
    `  <Note>Smoke operation center receipt</Note>`,
    `  <SendTime>${escapeXmlText(now)}</SendTime>`,
    `  <CusRespData>`,
    `    <RepType>5</RepType>`,
    `    <RepCode>0000</RepCode>`,
    `    <RepAddMsg>Smoke approved</RepAddMsg>`,
    `  </CusRespData>`,
    `</Receipt>`,
  ].join("\n");
}

export function buildSmokeAgentConsignmentReceiptXml(referenceNo) {
  return [
    `<?xml version="1.0" encoding="UTF-8"?>`,
    `<ImportAgrResponse xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">`,
    `  <ResponseInfo>`,
    `    <ResponseCode>0</ResponseCode>`,
    `    <ResponseMessage>Smoke ACD accepted</ResponseMessage>`,
    `  </ResponseInfo>`,
    `  <ConsignNo>${escapeXmlText(referenceNo)}</ConsignNo>`,
    `</ImportAgrResponse>`,
  ].join("\n");
}

export function escapeXmlText(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}
