export function buildInvoiceReportCheckUrl(webUrl, invoiceId) {
  const url = new URL(webUrl);
  url.searchParams.set("smokeInvoiceReport", String(invoiceId));
  url.hash = `/invoices/${invoiceId}`;
  return url.toString();
}

export function buildDashboardCheckUrl(webUrl) {
  const url = new URL(webUrl);
  url.searchParams.set("smokeDashboard", "1");
  url.hash = "/dashboard";
  return url.toString();
}
