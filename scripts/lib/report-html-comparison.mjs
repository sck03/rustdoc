// Independent conversions intentionally use distinct document namespaces.
// Preserve page indices, SVG resource suffixes and every layout/content byte.
export function normalizeReportScope(html) {
  return html.replace(/\breport\d+-(\d+)/g, 'report-$1');
}
