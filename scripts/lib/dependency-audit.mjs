import { createRequire } from "node:module";

const require = createRequire(new URL("../../apps/export-doc-web/package.json", import.meta.url));
const parseSpdx = require("spdx-expression-parse");

// Every offered license must remain reviewed; approval is not legal advice.
export function isApprovedLicenseExpression(expression, approved) {
  try {
    const visit = node => node.conjunction
      ? visit(node.left) && visit(node.right)
      : approved.has(node.license) && !node.plus
        && (!node.exception || (node.license === "Apache-2.0" && node.exception === "LLVM-exception"));
    return visit(parseSpdx(expression));
  } catch { return false; }
}

export function classifyNpmAudit({ stdout, status, error, signal }) {
  if (error || signal || status === null) throw new Error(`npm audit could not complete: ${error?.message || signal || "no exit status"}`);
  const text = String(stdout || "").trim();
  const start = text.indexOf("{");
  if (start < 0) throw new Error("npm audit did not return JSON.");
  const report = JSON.parse(text.slice(start));
  if (report.error || !report.metadata?.vulnerabilities || !report.vulnerabilities) {
    throw new Error(`npm audit returned an invalid report: ${JSON.stringify(report.error || report)}`);
  }
  const counts = report.metadata.vulnerabilities;
  const severities = ["info", "low", "moderate", "high", "critical"];
  if (severities.some(key => !Number.isSafeInteger(counts[key]) || counts[key] < 0)) throw new Error("npm audit returned invalid vulnerability counts.");
  const blocked = counts.high + counts.critical > 0;
  if (status !== (blocked ? 1 : 0)) throw new Error(`npm audit exit status ${status} does not match its report.`);
  return { report, state: blocked ? "blocked" : severities.some(key => counts[key] > 0) ? "warning" : "passed", exitCode: blocked ? 1 : 0 };
}
