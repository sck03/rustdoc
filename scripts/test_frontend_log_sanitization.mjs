import assert from "node:assert/strict";
import {
  sanitizeFrontendLogPayload,
  sanitizeFrontendLogText,
  sanitizeFrontendLogUrl,
} from "../apps/export-doc-web/src/desktop/desktopLogSanitizer.ts";

const sanitized = sanitizeFrontendLogPayload({
  message: "Authorization: Bearer abcdefghijklmnopqrstuvwxyz password=plain-secret operator@example.com",
  source: "C:\\Users\\bridge\\workspace\\app.ts",
  stack: "request https://example.test/downloads/jobs/0123456789abcdef0123456789abcdef?token=query-secret",
  url: "https://user:pass@example.test/invoices/0123456789abcdef0123456789abcdef?token=query-secret#details",
});

for (const secret of ["abcdefghijklmnopqrstuvwxyz", "plain-secret", "operator@example.com", "bridge", "query-secret", "user:pass"]) {
  assert.equal(JSON.stringify(sanitized).includes(secret), false, `sanitized payload leaked ${secret}`);
}
assert.match(sanitized.message, /\[REDACTED\]/u);
assert.match(sanitized.message, /\[REDACTED_EMAIL\]/u);
assert.equal(sanitizeFrontendLogUrl("file:///Users/bridge/private.log"), "[REDACTED_URL]");
assert.equal(sanitizeFrontendLogText("token=abc123456789"), "token=[REDACTED]");

console.log("Frontend diagnostic log sanitization passed.");
