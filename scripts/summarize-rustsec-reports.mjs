import { readFileSync } from "node:fs";

const reports = process.argv.slice(2);
if (!reports.length) throw new Error("Specify the cargo-audit JSON reports to summarize.");
for (const file of reports) {
  const report = JSON.parse(readFileSync(file, "utf8"));
  if (!report.database || !Array.isArray(report.vulnerabilities?.list)) throw new Error(`Invalid RustSec report: ${file}`);
  console.log(`${file}: ${report.vulnerabilities.count} vulnerabilities; advisory database ${report.database["last-commit"] || "local cache"}.`);
  for (const finding of report.vulnerabilities.list) {
    console.log(`  ${finding.advisory.id}: ${finding.package.name} ${finding.package.version}: ${finding.advisory.title}; patched ${finding.versions.patched.join(", ") || "none"}.`);
  }
  for (const finding of report.warnings?.unsound || []) console.log(`  unsound: ${finding.advisory.id}: ${finding.package.name} ${finding.package.version}.`);
}
