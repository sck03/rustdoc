import { readFileSync } from "node:fs";

const reports = process.argv.slice(2);
if (!reports.length) throw new Error("Specify the cargo-audit JSON reports to summarize.");
for (const file of reports) {
  const report = JSON.parse(readFileSync(file, "utf8"));
  if (!report.database || !Array.isArray(report.vulnerabilities?.list)) throw new Error(`Invalid RustSec report: ${file}`);
  console.log(`${file}: ${report.vulnerabilities.count} vulnerabilities; advisory database ${report.database["last-commit"] || "local cache"}.`);
  for (const [kind, findings] of [["vulnerability", report.vulnerabilities.list], ...Object.entries(report.warnings ?? {})]) for (const finding of findings) {
    const advisory = finding.advisory ? `: ${finding.advisory.id}: ${finding.advisory.title}; patched ${finding.versions.patched.join(", ") || "none"}` : "";
    console.log(`  ${kind}: ${finding.package.name} ${finding.package.version}${advisory}.`);
  }
}
